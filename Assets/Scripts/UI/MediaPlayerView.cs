using System.Security.Cryptography;
using System.Text;
using System.IO;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays one piece of a marker: a video, or a recording.
///
/// A video goes through VideoPlayer. A recording does not, and used to.
///
/// The reasoning was that VideoPlayer hands the URL to the platform's own media
/// stack, which streams it and knows every format a phone records in, while
/// UnityWebRequestMultimedia wants the whole file in memory and understands only
/// mp3, ogg and wav. All true, and all beside the point: Android's media stack
/// refuses a file with no picture in it. A phone playing a perfectly good mp3
/// logged "AndroidVideoMedia: No video tracks" and the page said "Couldn't play
/// that" - so every recording any creator has uploaded has been unplayable on
/// the thing the app runs on.
///
/// So a recording is downloaded as an AudioClip and played through the
/// AudioSource that was already here for the video's sound. It arrives whole
/// rather than streaming, which for a minute of speech is a second or two, and
/// the format has to be one of three - which is what the bucket accepts anyway.
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

    /// <summary>True while the open piece is a recording, which has its own path.</summary>
    bool _hearing;

    Coroutine _fetching;
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
        _hearing = media != null && media.Kind == MarkerMediaKind.Audio;

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

        // Before the early return below, not after it. Returning first left the
        // label holding the word UIFactory seeds every text object with - the
        // object is called Title, so a photograph was captioned "Title".
        if (titleLabel != null)
            titleLabel.text = media.Title;

        if (still)
        {
            ShowStill(media);
            return;
        }

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

        if (_hearing)
        {
            if (_fetching != null)
                StopCoroutine(_fetching);

            _fetching = StartCoroutine(Hear(url));
            return;
        }

        _player.url = url;

        if (_media != null && _media.Kind == MarkerMediaKind.Video)
            EnsureTarget();

        _player.Prepare();
    }

    /// <summary>
    /// Downloads a recording and hands it to the AudioSource.
    ///
    /// The type is read off the address rather than guessed: Unity will decode an
    /// mp3 as an ogg without complaining and then play silence.
    /// </summary>
    IEnumerator Hear(string url)
    {
        using (UnityWebRequest request =
               UnityWebRequestMultimedia.GetAudioClip(url, TypeOf(url)))
        {
            yield return request.SendWebRequest();

            _fetching = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Media] Recording failed (" + request.responseCode + "): " +
                                 request.error);
                Fail("media.failed");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);

            if (clip == null || clip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogWarning("[Media] Recording did not decode: " + url);
                Fail("media.failed");
                yield break;
            }

            _audio.clip = clip;
            _prepared = true;

            SetNotice(null);
            UpdateTime(0d);

            // Ready, not playing. Sound starting by itself in a street is
            // startling, and the reader asked to open the piece, not be played at.
            SetPlayIcon(false);
        }
    }

    static AudioType TypeOf(string url)
    {
        string lower = url == null ? string.Empty : url.ToLowerInvariant();

        // A signed URL keeps its token in the query string, which is full of
        // letters that would fool a plain EndsWith.
        int query = lower.IndexOf((char)63);
        if (query > 0)
            lower = lower.Substring(0, query);

        if (lower.EndsWith(".ogg"))
            return AudioType.OGGVORBIS;

        if (lower.EndsWith(".wav"))
            return AudioType.WAV;

        // mp3, m4a, and anything unlabelled. MPEG is what a phone records and
        // what a creator uploads.
        return AudioType.MPEG;
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

        // Disk first, and not only as a fallback. A media URL carries the
        // timestamp it was uploaded with, so replacing a picture makes a new
        // URL - a copy of this one can never be the wrong one, and the walker
        // stops paying for the same photograph twice.
        Texture2D kept = FromDisk(url);

        if (kept != null)
        {
            Paint(kept);
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
        // Bytes, not a texture download. Unity reads JPEG and PNG and nothing
        // else, and a creator's WebP came back as "Data Processing Error".
        using (UnityWebRequest request = UnityWebRequest.Get(url))
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

            Texture2D texture = Decode(request.downloadHandler.data, out byte[] readable);

            if (texture == null)
            {
                Debug.LogWarning("[Media] Image could not be decoded (" +
                                 request.downloadHandler.data.Length + " bytes)");
                Fail("media.failed");
                yield break;
            }

            Paint(texture);
            ToDisk(url, readable);
        }
    }

    /// <summary>
    /// JPEG and PNG Unity reads itself. Anything else - WebP, HEIC from a newer
    /// phone - goes through Android's own decoder and comes back as PNG, and the
    /// PNG is what gets kept, so the next look is a plain read from disk.
    /// </summary>
    static Texture2D Decode(byte[] bytes, out byte[] readable)
    {
        readable = bytes;

        if (bytes == null || bytes.Length == 0)
            return null;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (texture.LoadImage(bytes, true))
            return texture;

        Destroy(texture);
        readable = AndroidToPng(bytes);

        if (readable == null)
            return null;

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (texture.LoadImage(readable, true))
            return texture;

        Destroy(texture);
        readable = null;
        return null;
    }

    // ponytail: full-size PNG re-encode on the main thread; a very large photo
    // stalls for a moment the first time. Downsample with inSampleSize if it shows.
    static byte[] AndroidToPng(byte[] bytes)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass factory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            using (AndroidJavaObject bitmap = factory.CallStatic<AndroidJavaObject>(
                       "decodeByteArray", (sbyte[])(Array)bytes, 0, bytes.Length))
            {
                if (bitmap == null)
                    return null;

                using (AndroidJavaClass formats = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat"))
                using (AndroidJavaObject png = formats.GetStatic<AndroidJavaObject>("PNG"))
                using (AndroidJavaObject stream = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
                {
                    bool written = bitmap.Call<bool>("compress", png, 100, stream);
                    bitmap.Call("recycle");

                    return written ? (byte[])(Array)stream.Call<sbyte[]>("toByteArray") : null;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Media] Android could not decode the picture: " + exception.Message);
        }
#endif
        return null;
    }

    void Paint(Texture2D texture)
    {
        if (screen != null)
        {
            screen.enabled = true;
            screen.texture = texture;
        }

        _prepared = true;
    }

    /// <summary>
    /// Where a downloaded picture is kept. Named by a hash of its URL because
    /// the URL is the identity - it holds the trail, the stop and the moment it
    /// was uploaded - and because a URL is far too long to be a filename.
    /// </summary>
    static string PathFor(string url)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            StringBuilder name = new StringBuilder(32);

            for (int i = 0; i < hash.Length; i++)
                name.Append(hash[i].ToString("x2"));

            return Path.Combine(Application.persistentDataPath, "media", name + ".img");
        }
    }

    static Texture2D FromDisk(string url)
    {
        try
        {
            string path = PathFor(url);

            if (!File.Exists(path))
                return null;

            Texture2D texture = new Texture2D(2, 2);

            // LoadImage works out JPEG or PNG for itself and returns false rather
            // than throwing on a file that was half written before the app died.
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(texture);
                return null;
            }

            return texture;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Media] Could not read the kept picture: " + exception.Message);
            return null;
        }
    }

    static void ToDisk(string url, byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return;

        try
        {
            string path = PathFor(url);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception exception)
        {
            // The picture is already on screen. Failing to keep it is not worth
            // telling anybody about.
            Debug.LogWarning("[Media] Could not keep the picture: " + exception.Message);
        }
    }

    public void Toggle()
    {
        if (_failed)
            return;

        if (_hearing)
        {
            if (_audio == null)
                return;

            if (_audio.isPlaying)
            {
                _audio.Pause();
                _audioRunning = false;
                SetPlayIcon(false);
                return;
            }

            if (!_prepared)
                return;   // still loading; the notice already says so

            _audio.Play();
            _audioRunning = true;
            SetPlayIcon(true);
            return;
        }

        if (_player == null)
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

        if (_audio != null && _audio.isPlaying)
            _audio.Pause();

        _audioRunning = false;
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

    bool _audioRunning;

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
        if (!_prepared)
            return;

        double at;
        double length;

        if (_hearing)
        {
            if (_audio == null || _audio.clip == null)
                return;

            // An AudioSource has no "finished" event the way a VideoPlayer has
            // loopPointReached. It just stops and rewinds, and the button stayed
            // on pause over a clip that had ended.
            if (_audioRunning && !_audio.isPlaying && _audio.time <= 0f)
            {
                _audioRunning = false;
                SetPlayIcon(false);
                SetProgress(1f);
                UpdateTime(_audio.clip.length);
                return;
            }

            at = _audio.time;
            length = _audio.clip.length;
        }
        else
        {
            if (_player == null)
                return;

            at = _player.time;
            length = _player.length;
        }

        if (length <= 0d)
            return;

        SetProgress((float)(at / length));
        UpdateTime(at);
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

        // The clip's own length when it is sound. This read the video player
        // only, so every audio piece said "0:00" for how long it was.
        double length = _hearing
            ? (_audio != null && _audio.clip != null ? _audio.clip.length : 0d)
            : (_player != null ? _player.length : 0d);

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
