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

    [Tooltip("Only there when a trail is already open behind this screen.")]
    public GameObject closeButton;

    readonly List<TrailRowView> _rows = new List<TrailRowView>();

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Refresh;
    }

    public void SetCanClose(bool canClose)
    {
        if (closeButton != null)
            closeButton.SetActive(canClose);
    }

    public void Refresh()
    {
        TrailService service = TrailService.Instance;

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

        Fill(mine, service != null ? service.SelectedId : null);
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

            if (row.button != null)
            {
                string id = trail.id;   // captured per row, not per loop variable
                row.button.onClick.RemoveAllListeners();
                row.button.onClick.AddListener(() => Open(id));
            }
        }
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
