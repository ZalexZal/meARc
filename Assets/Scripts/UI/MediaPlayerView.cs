using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays one piece of a marker: a video, or a recording.
///
/// Both go through VideoPlayer, including the audio-only ones. The obvious way to
/// play a recording is an AudioSource with UnityWebRequestMultimedia, and it is
/// the wrong one here: that path wants the whole file in memory before a sound
/// comes out, and it only understands mp3, ogg and wav. VideoPlayer hands the URL
/// to the platform's own media stack, which streams it and knows the formats a
/// phone actually records in. For an audio piece there is simply no picture, and
/// the page does not show one.
///
/// Nothing here autoplays. Sound starting by itself in a street is startling, and
/// it spends someone's data without asking - which is also why the size is shown
/// before the first tap when the reader is on mobile data.
/// </summary>
public class MediaPlayerView : MonoBehaviour
{
    const string DataAgreedKey = "media_data_agreed";

    [Header("Picture")]
    [Tooltip("Where a video is drawn. Hidden for a recording, which has no picture.")]
    public RawImage screen;

    public GameObject screenHolder;

    [Header("Transport")]
    public Button playButton;
    public Image playIcon;
    public Sprite playSprite;
    public Sprite pauseSprite;

    [Tooltip("Fills left to right as it plays. Not a waveform - a waveform would " +
             "have to be drawn from samples nobody has decoded yet.")]
    public RectTransform progressFill;

    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI timeLabel;

    [Header("States")]
    [Tooltip("Shown while the file is being prepared, and when it will not play.")]
    public GameObject noticeHolder;

    public TextMeshProUGUI noticeLabel;

    [Tooltip("The mobile-data question. Shown instead of playing, once.")]
    public GameObject dataHolder;

    public TextMeshProUGUI dataLabel;
    public Button dataPlayButton;

    VideoPlayer _player;
    AudioSource _audio;
    RenderTexture _target;
    MarkerMedia _media;
    bool _prepared;
    bool _failed;

    void Awake()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;

        _player = gameObject.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.isLooping = false;
        _player.source = VideoSource.Url;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _player.SetTargetAudioSource(0, _audio);

        // Ask for as little as the platform will give. This is a phone in a street,
        // and every frame decoded ahead is a frame of battery.
        _player.skipOnDrop = true;

        _player.prepareCompleted += HandlePrepared;
        _player.errorReceived += HandleError;
        _player.loopPointReached += HandleFinished;

        if (playButton != null)
            playButton.onClick.AddListener(Toggle);

        if (dataPlayButton != null)
            dataPlayButton.onClick.AddListener(AgreeToData);
    }

    void OnDestroy()
    {
        if (_player != null)
        {
            _player.prepareCompleted -= HandlePrepared;
            _player.errorReceived -= HandleError;
            _player.loopPointReached -= HandleFinished;
        }

        ReleaseTarget();
    }

    void OnDisable()
    {
        Stop();
    }

    // -------------------------------------------------------------------- open

    public void Show(MarkerMedia media)
    {
        Stop();

        _media = media;
        _prepared = false;
        _failed = false;

        if (media == null)
            return;

        bool still = media.Kind == MarkerMediaKind.Image;
        bool video = media.Kind == MarkerMediaKind.Video || still;

        if (screenHolder != null)
            screenHolder.SetActive(video);

        // A recording has no picture, and the frame from the last video is not it.
        if (screen != null && !video)
            screen.enabled = false;

        // A still has a picture and no transport: nothing to play, pause, seek or
        // count down. Leaving the controls up would offer three buttons that do
        // nothing to a photograph.
        if (playButton != null)
            playButton.gameObject.SetActive(!still);

        if (progressFill != null)
            progressFill.transform.parent.gameObject.SetActive(!still);

        if (timeLabel != null)
            timeLabel.gameObject.SetActive(!still);

        if (still)
        {
            ShowStill(media);
            return;
        }

        if (titleLabel != null)
            titleLabel.text = media.Title;

        SetProgress(0f);
        UpdateTime(0d);
        SetPlayIcon(false);

        string url = MarkerMediaUrl.For(media);

        if (string.IsNullOrEmpty(url))
        {
            Fail("media.failed");
            return;
        }

        // Text is written to disk and works with no signal. A 30 MB video is not
        // something to quietly keep, so it is honest about needing a connection
        // rather than sitting on a spinner until it times out.
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Fail("media.needsConnection");
            return;
        }

        if (NeedsDataWarning(media))
        {
            ShowDataQuestion(media);
            return;
        }

        Prepare(url);
    }

    void Prepare(string url)
    {
        SetNotice("media.loading");
        SetDataQuestion(false);

        _player.url = url;

        if (_media != null && _media.Kind == MarkerMediaKind.Video)
            EnsureTarget();

        _player.Prepare();
    }

    // ------------------------------------------------------------- mobile data

    bool NeedsDataWarning(MarkerMedia media)
    {
        if (PlayerPrefs.GetInt(DataAgreedKey, 0) == 1)
            return false;

        if (Application.internetReachability != NetworkReachability.ReachableViaCarrierDataNetwork)
            return false;

        // No size recorded means no honest warning to give. Better to say nothing
        // than to invent a number.
        return media != null && media.bytes > 0L;
    }

    void ShowDataQuestion(MarkerMedia media)
    {
        SetNotice(null);
        SetDataQuestion(true);

        if (dataLabel != null)
            dataLabel.text = string.Format(AppStrings.Get("media.dataWarning"), media.Size);
    }

    void AgreeToData()
    {
        PlayerPrefs.SetInt(DataAgreedKey, 1);
        PlayerPrefs.Save();

        SetDataQuestion(false);

        string url = MarkerMediaUrl.For(_media);
        if (!string.IsNullOrEmpty(url))
            Prepare(url);
    }

    // ---------------------------------------------------------------- playback

    /// <summary>
    /// Downloads a photograph and puts it on the same surface the video renders
    /// to, so a marker's pieces list does not need a second kind of row.
    ///
    /// UnityWebRequestTexture rather than VideoPlayer: VideoPlayer will happily
    /// be handed a JPEG and will report a failure that says nothing useful.
    /// </summary>
    void ShowStill(MarkerMedia media)
    {
        string url = MarkerMediaUrl.For(media);

        if (string.IsNullOrEmpty(url))
        {
            Fail("media.failed");
            return;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Fail("media.needsConnection");
            return;
        }

        StartCoroutine(LoadStill(url, media));
    }

    IEnumerator LoadStill(string url, MarkerMedia media)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            // The reader may have moved on while this was in the air; painting it
            // now would put one marker's photograph on another's sheet.
            if (_media != media)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Media] Image failed (" + request.responseCode + "): " +
                                 request.error);
                Fail("media.failed");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);

            if (screen != null)
            {
                screen.enabled = true;
                screen.texture = texture;
            }

            _prepared = true;
        }
    }

    public void Toggle()
    {
        if (_failed || _player == null)
            return;

        if (_player.isPlaying)
        {
            _player.Pause();
            SetPlayIcon(false);
            return;
        }

        if (!_prepared)
            return;   // still loading; the notice already says so

        _player.Play();
        SetPlayIcon(true);
    }

    public void Stop()
    {
        if (_player != null && _player.isPlaying)
            _player.Pause();

        SetPlayIcon(false);
    }

    void HandlePrepared(VideoPlayer source)
    {
        _prepared = true;
        SetNotice(null);
        UpdateTime(0d);

        // Prepared, not playing. The reader asked to open the piece, not to be
        // played at.
        SetPlayIcon(false);
    }

    void HandleError(VideoPlayer source, string message)
    {
        Debug.LogWarning("[Media] " + message);
        Fail("media.failed");
    }

    void HandleFinished(VideoPlayer source)
    {
        SetPlayIcon(false);
        SetProgress(1f);
    }

    void Fail(string key)
    {
        _failed = true;
        _prepared = false;
        SetNotice(key);
        SetDataQuestion(false);
        SetPlayIcon(false);
    }

    void Update()
    {
        if (_player == null || !_prepared)
            return;

        double length = _player.length;
        if (length <= 0d)
            return;

        SetProgress((float)(_player.time / length));
        UpdateTime(_player.time);
    }

    // ------------------------------------------------------------------ screen

    /// <summary>
    /// A render texture the size of the rect it is drawn into, made once per piece.
    /// Sized from the rect rather than from the video, so a portrait clip and a
    /// landscape one both fill the same frame instead of resizing the page.
    /// </summary>
    void EnsureTarget()
    {
        if (screen == null)
            return;

        Rect rect = screen.rectTransform.rect;
        int width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 16, 1920);
        int height = Mathf.Clamp(Mathf.RoundToInt(rect.height), 16, 1080);

        if (_target != null && _target.width == width && _target.height == height)
            return;

        ReleaseTarget();

        _target = new RenderTexture(width, height, 0);
        _target.Create();

        _player.targetTexture = _target;
        screen.texture = _target;

        // Only now is there anything to draw. Enabled before this, it is a white
        // rectangle the size of the frame.
        screen.enabled = true;
    }

    void ReleaseTarget()
    {
        if (_target == null)
            return;

        if (screen != null && screen.texture == _target)
        {
            screen.texture = null;
            screen.enabled = false;
        }

        if (_player != null && _player.targetTexture == _target)
            _player.targetTexture = null;

        _target.Release();
        Destroy(_target);
        _target = null;
    }

    // ------------------------------------------------------------------ chrome

    void SetProgress(float fraction)
    {
        if (progressFill == null)
            return;

        // anchorMax rather than a width, so it stays right at any screen size and
        // costs no layout pass.
        Vector2 max = progressFill.anchorMax;
        max.x = Mathf.Clamp01(fraction);
        progressFill.anchorMax = max;
    }

    void UpdateTime(double seconds)
    {
        if (timeLabel == null)
            return;

        double length = _player != null ? _player.length : 0d;

        // Falls back to the length the portal recorded, so the reader sees how long
        // a piece is before it has been prepared.
        if (length <= 0d && _media != null)
            length = _media.seconds;

        timeLabel.text = Clock(seconds) + " / " + Clock(length);
    }

    static string Clock(double seconds)
    {
        int whole = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    void SetPlayIcon(bool playing)
    {
        if (playIcon == null)
            return;

        Sprite wanted = playing ? pauseSprite : playSprite;
        if (wanted != null)
            playIcon.sprite = wanted;
    }

    void SetNotice(string key)
    {
        bool on = !string.IsNullOrEmpty(key);

        if (noticeHolder != null)
            noticeHolder.SetActive(on);

        if (on && noticeLabel != null)
            noticeLabel.text = AppStrings.Get(key);
    }

    void SetDataQuestion(bool on)
    {
        if (dataHolder != null)
            dataHolder.SetActive(on);
    }
}
