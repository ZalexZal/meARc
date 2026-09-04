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

    public TextMeshProUGUI countLabel;
    public RectTransform listParent;
    public PieceRowView rowPrefab;

    [Header("The page a piece opens into")]
    [Tooltip("Covers the overview while a piece is open.")]
    public GameObject page;

    public TextMeshProUGUI pageTitle;
    public Button backButton;

    public MediaPlayerView media;
    public MarkerQuestionView question;

    [Header("Icons")]
    public Sprite videoIcon;
    public Sprite audioIcon;
    public Sprite questionIcon;

    [Tooltip("Hidden while a piece is open, so the sheet shows one thing at a time.")]
    public GameObject overview;

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

        int count = marker != null ? marker.PieceCount : 0;

        Set(section, count > 0);

        if (count == 0)
            return;

        if (countLabel != null)
        {
            countLabel.text = count == 1
                ? AppStrings.Get("piece.one")
                : string.Format(AppStrings.Get("piece.count"), count);
        }

        Fill();
    }

    /// <summary>Called when the sheet closes. Stops anything still playing.</summary>
    public void Close()
    {
        if (media != null)
            media.Stop();

        Set(page, false);
        Set(overview, true);
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

            if (i < _media.Count)
            {
                MarkerMedia piece = _media[i];
                bool audio = piece.Kind == MarkerMediaKind.Audio;

                _rows[i].Bind(audio ? audioIcon : videoIcon,
                              AppStrings.Get(Verb(piece)),
                              piece.Title,
                              _played.Contains(Key(piece)));
            }
            else
            {
                // The question is always last: it is the thing the media is there
                // to make answerable.
                MarkerTaskService service = MarkerTaskService.Instance;
                bool solved = service != null &&
                              service.Progress(_marker.buildingId).IsFinished;

                _rows[i].Bind(questionIcon,
                              AppStrings.Get("piece.answer"),
                              _marker.task.Prompt,
                              solved);
            }
        }
    }

    // ------------------------------------------------------------------- pages

    public void Open(int index)
    {
        if (_marker == null)
            return;

        bool isQuestion = index >= _media.Count;

        Set(overview, false);
        Set(page, true);

        Set(media != null ? media.gameObject : null, !isQuestion);
        Set(question != null ? question.gameObject : null, isQuestion);

        if (isQuestion)
        {
            if (pageTitle != null)
                pageTitle.text = AppStrings.Get("piece.answer");

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

        // Redrawn on the way back, so a tick or a solved question appears without
        // the reader closing the sheet and opening it again.
        Fill();
    }

    string Key(MarkerMedia piece)
    {
        return (_marker != null ? _marker.buildingId : string.Empty) + "|" + piece.path;
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
