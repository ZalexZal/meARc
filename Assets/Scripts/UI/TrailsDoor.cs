using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// My trails: the card the reader picks a trail from, over the blurred map.
///
/// It has four states and only one of them is the list. A reader who has just
/// installed the app, on a bus, with one bar of signal, will see the other three,
/// and each has to say something true rather than show an empty box: waiting,
/// nothing joined yet, or the server not answering.
/// </summary>
public class TrailsDoor : MonoBehaviour
{
    [Header("Rows")]
    public RectTransform listParent;
    public TrailRowView rowPrefab;

    [Header("States")]
    [Tooltip("Shown while the membership request is still in flight.")]
    public GameObject waitingState;

    [Tooltip("Shown when the reader belongs to no trails at all.")]
    public GameObject emptyState;

    [Tooltip("Shown when the last request failed.")]
    public GameObject failedState;

    public TextMeshProUGUI failedLabel;

    [Header("Controls")]
    public Button joinButton;

    [Tooltip("The card's own subtitle. Hidden when the list is not a list, " +
             "because the state note underneath already says what is going on, " +
             "and saying it twice reads as a layout accident.")]
    public TextMeshProUGUI subtitle;

    [Tooltip("Sizes the list to what is in it, up to a limit. A fixed height " +
             "leaves a quarter of the card empty under two trails.")]
    public LayoutElement listSize;

    [Header("Near you")]
    [Tooltip("The library: public trails near the reader, joined without a code.")]
    public GameObject nearGroup;
    public RectTransform nearParent;

    [Header("Daylight v2")]
    [Tooltip("What SizeList measures: the whole scrolling list, not only the rows.")]
    public RectTransform measure;

    [Tooltip("The inset group of joined trails, off when there are none.")]
    public GameObject mineGroup;
    public TextMeshProUGUI nearNote;

    readonly List<TrailRowView> _rows = new List<TrailRowView>();
    readonly List<TrailRowView> _nearRows = new List<TrailRowView>();
    bool _joining;

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Refresh;
        Refresh();
        AskNear();
    }

    /// <summary>Once per opening. The list is short and the position rarely
    /// moves far while this card is up.</summary>
    void AskNear()
    {
        TrailService service = TrailService.Instance;
        UserLocationService location = UserLocationService.Instance;

        if (service == null || location == null || !location.HasValidPose)
            return;

        StartCoroutine(service.LoadNear(location.Latitude, location.Longitude, Refresh));
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Refresh;
    }

    public void Refresh()
    {
        TrailService service = TrailService.Instance;

        if (service != null)
            service.RetryIfFailed();

        bool loaded = service != null && service.HasLoaded;
        bool failed = service != null && !string.IsNullOrEmpty(service.LastError);
        List<TrailService.Trail> mine = service != null
            ? service.Mine
            : new List<TrailService.Trail>();

        // A failure with trails already in hand is not a failure worth a screen:
        // the reader can still open any of them. It only takes over when the list
        // is empty and therefore useless.
        bool showFailed = failed && mine.Count == 0;
        bool showWaiting = !loaded && !showFailed && mine.Count == 0;
        bool showEmpty = loaded && !showFailed && mine.Count == 0;

        Set(waitingState, showWaiting);
        Set(emptyState, showEmpty);
        Set(failedState, showFailed);

        if (failedLabel != null)
            failedLabel.text = AppStrings.Get("trails.failed");

        // Only over an actual list. "Pick one to walk it" above the words "No
        // trails yet" is the app describing a list the reader cannot see, and the
        // state note underneath is already a heading and a sentence of its own.
        if (subtitle != null)
        {
            subtitle.text = AppStrings.Get("trails.subtitle");
            subtitle.gameObject.SetActive(mine.Count > 0);
        }

        if (mineGroup != null)
            mineGroup.SetActive(mine.Count > 0);

        Fill(mine, service != null ? service.SelectedId : null);
        FillNear(service);
        SizeList();
    }

    /// <summary>
    /// Gives the list exactly the height of what is in it, up to the point where
    /// it starts to scroll instead of growing - so the card hugs two trails, hugs
    /// a two-line note, and can never grow taller than the screen it rests on.
    ///
    /// Public because the preview harness sets the list's state directly rather
    /// than through Refresh, and without a way to call this it photographs the
    /// height the holder was built with instead of the height the app gives it.
    /// </summary>
    public void SizeList()
    {
        RectTransform list = measure != null ? measure : listParent;

        if (listSize == null || list == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(list);

        float wanted = Mathf.Min(LayoutUtility.GetPreferredHeight(list), 1100f);

        // Both, or the minimum wins. UIFactory.Size sets minHeight alongside
        // preferredHeight, and a layout group takes the larger of the two.
        listSize.minHeight = wanted;
        listSize.preferredHeight = wanted;
    }

    void Fill(List<TrailService.Trail> mine, string selectedId)
    {
        if (listParent == null || rowPrefab == null)
            return;

        // Rows are reused rather than destroyed and remade. The list is short, but
        // this screen refreshes on every membership change and on every language
        // switch, and rebuilding it each time throws away the scroll position.
        while (_rows.Count < mine.Count)
        {
            TrailRowView row = Instantiate(rowPrefab, listParent);
            _rows.Add(row);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            TrailRowView row = _rows[i];

            if (i >= mine.Count)
            {
                row.gameObject.SetActive(false);
                continue;
            }

            TrailService.Trail trail = mine[i];

            row.gameObject.SetActive(true);
            row.Bind(trail, trail.id == selectedId);
            row.SetFirst(i == 0);

            if (row.button != null)
            {
                string id = trail.id;   // captured per row, not per loop variable
                row.button.onClick.RemoveAllListeners();
                row.button.onClick.AddListener(() => Open(id));
            }

            row.onLeave = Leave;
        }
    }

    void FillNear(TrailService service)
    {
        if (nearGroup == null || nearParent == null || rowPrefab == null)
            return;

        // Last in the list, under the reader's own trails.
        nearGroup.transform.SetAsLastSibling();

        List<TrailService.Listed> near = service != null
            ? service.Near
            : new List<TrailService.Listed>();

        UserLocationService location = UserLocationService.Instance;
        bool located = location != null && location.HasValidPose;

        string note = !located ? "near.noLocation"
                    : service == null || !service.NearLoaded ? "near.looking"
                    : near.Count == 0 && service.NearJoined > 0 ? "near.allJoined"
                    : near.Count == 0 ? "near.none"
                    : null;

        if (nearNote != null)
        {
            nearNote.gameObject.SetActive(note != null);
            if (note != null)
                nearNote.text = AppStrings.Get(note);
        }

        // No rows, no box: an empty grey group under the note read as a list
        // that had failed to load.
        nearParent.gameObject.SetActive(near.Count > 0);

        while (_nearRows.Count < near.Count)
            _nearRows.Add(Instantiate(rowPrefab, nearParent));

        for (int i = 0; i < _nearRows.Count; i++)
        {
            TrailRowView row = _nearRows[i];
            bool used = i < near.Count;
            row.gameObject.SetActive(used);

            if (!used)
                continue;

            row.BindListed(near[i]);
            row.SetFirst(i == 0);

            if (row.button != null)
            {
                string id = near[i].id;
                row.button.onClick.RemoveAllListeners();
                row.button.onClick.AddListener(() => JoinListed(id));
            }
        }
    }

    void JoinListed(string worldId)
    {
        TrailService service = TrailService.Instance;

        if (service == null || _joining)
            return;

        _joining = true;

        StartCoroutine(service.JoinListed(worldId, ok =>
        {
            _joining = false;

            if (ok)
            {
                if (DoorFlow.Instance != null)
                    DoorFlow.Instance.Entered();
                return;
            }

            if (nearNote != null)
            {
                nearNote.gameObject.SetActive(true);
                nearNote.text = AppStrings.Get("near.failed");
            }
        }));
    }

    /// <summary>
    /// The row has asked twice and been answered twice.
    ///
    /// The list refreshes itself from the server rather than removing the row on
    /// the spot: if the delete did not happen, the trail should still be there,
    /// and a row that vanishes optimistically is a row that lies.
    /// </summary>
    void Leave(TrailService.Trail trail)
    {
        TrailService service = TrailService.Instance;

        if (service == null || trail == null)
            return;

        StartCoroutine(service.Leave(trail.id, _ => Refresh()));
    }

    void Open(string worldId)
    {
        TrailService service = TrailService.Instance;
        if (service != null)
            service.Select(worldId);

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.Entered();
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
