using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pieces inside a marker's sheet: the list of them, and the page one opens
/// into.
///
/// This lives in the same sheet as the marker's text rather than in a screen of
/// its own, which is the whole reason the sheet treatment was chosen: the reader
/// never leaves the map. Opening a piece swaps what the sheet is showing and
/// leaves everything else - where they are, what is around them, the way back -
/// exactly where it was.
///
/// A marker with no media and no question never shows any of this. Most markers on
/// a guided trail are text, and a list header reading "0 pieces" over an empty box
/// is worse than the plain sheet it replaced.
/// </summary>
public class MarkerPieceController : MonoBehaviour
{
    [Header("On the overview")]
    [Tooltip("The whole pieces section. Off for a marker that is only text.")]
    public GameObject section;

    public GameObject countRow;
    public TextMeshProUGUI countLabel;

    [Tooltip("Why a stop will not open. Its own line - the count chip is a fixed pill.")]
    public TextMeshProUGUI tooFarLabel;
    public RectTransform listParent;
    public PieceRowView rowPrefab;

    [Header("The page a piece opens into")]
    [Tooltip("Covers the overview while a piece is open.")]
    public GameObject page;

    public TextMeshProUGUI pageTitle;

    [Tooltip("The sheet's close mark. Taken away while a page is open, because " +
             "Back is the way out of a page and two ways out of one screen is " +
             "one of them for the reader to wonder about.")]
    public GameObject closeMark;
    public Button backButton;

    public MediaPlayerView media;
    public MarkerQuestionView question;

    [Header("Icons")]
    public Sprite videoIcon;
    public Sprite audioIcon;
    public Sprite photoIcon;
    public Sprite questionIcon;

    [Tooltip("Hidden while a piece is open, so the sheet shows one thing at a time.")]
    public GameObject overview;

    [Header("Daylight v2")]
    [Tooltip("The one row a stop shows from afar: what is here, shut until you arrive.")]
    public PieceRowView lockedRow;
    public Sprite lockIcon;

    [Tooltip("The sheet header's back button, faded out on the overview so the " +
             "title stays centred.")]
    public CanvasGroup backGroup;

    float _tick;

    readonly List<PieceRowView> _rows = new List<PieceRowView>();
    readonly HashSet<string> _played = new HashSet<string>();

    BuildingData _marker;
    List<MarkerMedia> _media = new List<MarkerMedia>();

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(Back);

        if (question != null)
            question.onDone = Back;

        Set(page, false);
    }

    // ------------------------------------------------------------------- list

    /// <summary>Called when the sheet opens on a marker.</summary>
    /// <summary>
    /// What a reader is being asked to do with this piece.
    ///
    /// A photograph labelled "Watch" is a photograph that looks like a video that
    /// will not start. Uploaded stills have been possible in the builder from the
    /// beginning and the app had no word for one.
    /// </summary>
    static string Verb(MarkerMedia piece)
    {
        if (piece.Kind == MarkerMediaKind.Audio)
            return "piece.listen";

        if (piece.Kind == MarkerMediaKind.Image)
            return "piece.look";

        return "piece.watch";
    }

    public void Bind(BuildingData marker)
    {
        _marker = marker;
        _media = marker != null ? marker.UsableMedia : new List<MarkerMedia>();

        Set(page, false);
        Set(overview, true);
        Set(closeMark, true);
        PageChrome(false);

        int count = marker != null ? marker.PieceCount : 0;

        Set(section, count > 0);
        Set(countRow, false);

        if (tooFarLabel != null)
            Set(tooFarLabel.gameObject, false);

        if (lockedRow != null)
            Set(lockedRow.gameObject, false);

        if (count == 0)
            return;

        Fill();
    }

    /// <summary>
    /// The stop is there, but the reader is not - so it names itself and keeps
    /// its contents.
    ///
    /// The question and the media are the trail. Read from a bench on the other
    /// side of town they are the route given away, and on a scored trail the
    /// answer with it. Tapping a pin on the map used to open everything, which
    /// made the whole AR half of the app optional.
    ///
    /// The section still shows, carrying the reason. An empty sheet with no
    /// explanation reads as a bug; "come closer" reads as a rule.
    /// </summary>
    public void BindLocked(BuildingData marker)
    {
        _marker = marker;
        _media = new List<MarkerMedia>();

        Set(page, false);
        Set(overview, true);
        Set(closeMark, true);
        PageChrome(false);
        Set(section, true);

        Set(countRow, false);

        if (tooFarLabel != null)
            Set(tooFarLabel.gameObject, false);

        for (int i = 0; i < _rows.Count; i++)
            _rows[i].gameObject.SetActive(false);

        // One shut row saying what is here and when it opens. A stop with
        // nothing but its words has nothing to promise, so it shows none.
        bool any = marker != null && marker.PieceCount > 0;
        Set(section, any);

        if (lockedRow == null || !any)
            return;

        Set(lockedRow.gameObject, true);
        lockedRow.SetFirst(true);

        // Standing on the stop with a fake-location app on reads as "come
        // closer" otherwise - a sentence that cannot be acted on at 0 m.
        bool mocked = UserLocationService.Instance != null && UserLocationService.Instance.IsMocked;

        lockedRow.Draw(lockIcon,
                       AppStrings.Get(marker.HasTask ? "piece.question" : "piece.more"),
                       AppStrings.Get(mocked ? "piece.mocked" : "piece.unlocks"),
                       PieceRowView.Look.Shut);
    }

    /// <summary>Called when the sheet closes. Stops anything still playing.</summary>
    public void Close()
    {
        if (media != null)
            media.Stop();

        Set(page, false);
        Set(overview, true);
        Set(closeMark, true);
        PageChrome(false);
    }

    void Fill()
    {
        if (listParent == null || rowPrefab == null || _marker == null)
            return;

        int total = _marker.PieceCount;

        while (_rows.Count < total)
        {
            PieceRowView row = Instantiate(rowPrefab, listParent);
            _rows.Add(row);

            int index = _rows.Count - 1;   // captured per row, not per loop variable
            if (row.button != null)
                row.button.onClick.AddListener(() => Open(index));
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            if (i >= total)
            {
                _rows[i].gameObject.SetActive(false);
                continue;
            }

            _rows[i].gameObject.SetActive(true);

            _rows[i].SetFirst(i == 0);

            if (i < _media.Count)
            {
                MarkerMedia piece = _media[i];
                Sprite icon = piece.Kind == MarkerMediaKind.Audio ? audioIcon
                            : piece.Kind == MarkerMediaKind.Image && photoIcon != null ? photoIcon
                            : videoIcon;

                // The thing's own name on top and its kind underneath, as Daylight
                // lists them: "The stage from inside" over "Video".
                string kind = AppStrings.Get(Kind(piece));
                string title = piece.Title;

                // A video link has no file name to fall back on, so Title comes
                // back as the kind word. Where it plays instead - "youtu.be" - is
                // a name; the kind word twice is not.
                if (string.IsNullOrWhiteSpace(title) ||
                    string.Equals(title, AppStrings.Get(Verb(piece)), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(title, kind, StringComparison.OrdinalIgnoreCase))
                {
                    title = Uri.TryCreate(piece.path ?? string.Empty, UriKind.Absolute, out Uri link)
                        ? link.Host.Replace("www.", string.Empty)
                        : kind;
                }

                _rows[i].Draw(icon, title, kind,
                              _played.Contains(Key(piece)) ? PieceRowView.Look.Done : PieceRowView.Look.Open);
            }
            else
            {
                // The question is always last: it is the thing the media is there
                // to make answerable.
                DrawQuestionRow(_rows[i]);
            }
        }
    }

    /// <summary>
    /// The question's row: what it is worth, and where it stands - answered,
    /// shut for a few minutes, waiting for the creator, or there to be answered.
    /// </summary>
    void DrawQuestionRow(PieceRowView row)
    {
        MarkerTaskService service = MarkerTaskService.Instance;
        MarkerTaskService.MarkerProgress progress = service != null
            ? service.Progress(_marker.buildingId)
            : new MarkerTaskService.MarkerProgress();

        TrailService trails = TrailService.Instance;
        bool scored = trails != null && trails.SelectedIsGame;
        int worth = _marker.task != null ? _marker.task.points : 0;

        string title = scored && worth > 0
            ? string.Format(AppStrings.Get("piece.questionFor"), worth)
            : AppStrings.Get("piece.question");

        if (progress.solved)
        {
            string said = scored && progress.points > 0
                ? string.Format(AppStrings.Get("piece.rightPts"), progress.points)
                : AppStrings.Get("piece.right");
            row.Draw(questionIcon, AppStrings.Get("piece.question"), said,
                     PieceRowView.Look.Done);
            return;
        }

        if (progress.skipped)
        {
            row.Draw(questionIcon, AppStrings.Get("piece.question"), AppStrings.Get("piece.skipped"),
                     PieceRowView.Look.Shut);
            return;
        }

        string caption;
        if (progress.IsLocked)
        {
            int left = Mathf.CeilToInt(progress.SecondsLeft);
            caption = string.Format(AppStrings.Get("piece.lockedFor"), (left / 60) + ":" + (left % 60).ToString("00"));
        }
        else if (progress.awaitingReview)
        {
            caption = AppStrings.Get("piece.review");
        }
        else
        {
            caption = AppStrings.Get("piece.answerHere");
        }

        row.Draw(questionIcon, title, caption, PieceRowView.Look.Open);
    }

    /// <summary>"Video", "Audio", "Photo": what a piece is, as a noun.</summary>
    static string Kind(MarkerMedia piece)
    {
        if (piece.Kind == MarkerMediaKind.Audio)
            return "piece.kindAudio";

        if (piece.Kind == MarkerMediaKind.Image)
            return "piece.kindPhoto";

        return "piece.kindVideo";
    }

    /// <summary>Whether this stop has a question that can be opened now.</summary>
    public bool HasQuestion
    {
        get { return _marker != null && _marker.HasTask && _rows.Count > 0 && section != null && section.activeSelf &&
                     (lockedRow == null || !lockedRow.gameObject.activeSelf); }
    }

    /// <summary>Opens the question, for the sheet's own "Answer the question".</summary>
    public void OpenQuestion()
    {
        if (_marker != null && _marker.HasTask)
            Open(_media.Count);
    }

    void Update()
    {
        // A question shut for a few minutes counts down on its row.
        if (_marker == null || !_marker.HasTask || page == null || page.activeSelf)
            return;

        _tick += Time.unscaledDeltaTime;
        if (_tick < 0.5f)
            return;

        _tick = 0f;

        MarkerTaskService service = MarkerTaskService.Instance;
        int index = _media.Count;
        if (service != null && index < _rows.Count && _rows[index].gameObject.activeSelf &&
            service.Progress(_marker.buildingId).IsLocked)
        {
            DrawQuestionRow(_rows[index]);
        }
    }

    // ------------------------------------------------------------------- pages

    static readonly string[] VideoFiles = { ".mp4", ".webm", ".mov", ".m4v", ".m3u8" };

    static bool IsWebPage(MarkerMedia piece)
    {
        if (piece == null || string.IsNullOrWhiteSpace(piece.path) || piece.Kind == MarkerMediaKind.Audio)
            return false;

        if (!Uri.TryCreate(piece.path.Trim(), UriKind.Absolute, out Uri link) ||
            (link.Scheme != "http" && link.Scheme != "https"))
            return false;

        string file = link.AbsolutePath.ToLowerInvariant();

        for (int i = 0; i < VideoFiles.Length; i++)
        {
            if (file.EndsWith(VideoFiles[i]))
                return false;
        }

        // Our own storage holds files; anything uploaded plays in the app.
        return !link.AbsolutePath.Contains("/storage/v1/object/");
    }

    public void Open(int index)
    {
        if (_marker == null)
            return;

        bool isQuestion = index >= _media.Count;

        // A link to a page, not to a file - YouTube, Vimeo. The in-app player can
        // only play a file, and said "Couldn't play that" for the very links the
        // website's editor asks for. Those open where they play.
        if (!isQuestion && IsWebPage(_media[index]))
        {
            _played.Add(Key(_media[index]));
            Application.OpenURL(_media[index].path.Trim());
            return;
        }

        Set(overview, false);
        Set(page, true);
        PageChrome(true);

        Set(media != null ? media.gameObject : null, !isQuestion);
        Set(question != null ? question.gameObject : null, isQuestion);

        // Back is the way out of a page; the mark in the corner is the way out
        // of the sheet. On a page they are the same door twice.
        Set(closeMark, false);

        if (isQuestion)
        {
            // No heading. The rule two comments down - the heading says what kind
            // of thing this is, the page says which one - has no work to do here,
            // because a question has no name apart from itself. It printed
            // "Answer" in the title face and then the question in the title face
            // directly underneath, one line apart, which is the fault that
            // comment was written about.
            if (pageTitle != null)
                pageTitle.text = AppStrings.Get("piece.question");

            if (question != null)
                question.Show(_marker);

            return;
        }

        MarkerMedia piece = _media[index];

        // The heading says what kind of thing this is; the page itself says which
        // one. Putting the name in both places printed it twice, one line apart.
        if (pageTitle != null)
        {
            pageTitle.text = AppStrings.Get(Verb(piece));
        }

        // Marked as played on opening, not on finishing. The tick means "you have
        // been here", which is what a reader glancing down the list is asking; a
        // tick that only appears after every last second of a four-minute recording
        // would be a tick almost nobody ever sees.
        _played.Add(Key(piece));

        if (media != null)
            media.Show(piece);
    }

    public void Back()
    {
        if (media != null)
            media.Stop();

        Set(page, false);
        Set(overview, true);
        Set(closeMark, true);
        PageChrome(false);

        // Redrawn on the way back, so a tick or a solved question appears without
        // the reader closing the sheet and opening it again.
        Fill();
    }

    string Key(MarkerMedia piece)
    {
        return (_marker != null ? _marker.buildingId : string.Empty) + "|" + piece.path;
    }

    /// <summary>Back shows on a page and not on the overview.</summary>
    void PageChrome(bool onPage)
    {
        if (backGroup != null)
        {
            backGroup.alpha = onPage ? 1f : 0f;
            backGroup.interactable = onPage;
            backGroup.blocksRaycasts = onPage;
        }

        if (!onPage && pageTitle != null)
            pageTitle.text = string.Empty;
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
