using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The trail chip's pull-down: the chevron promises a menu, so it opens one,
/// hung from the chip, rather than taking over the screen.
///
///   Моите маршрути                       (signed in, owns trails)
///   (◎)  Разходка из Ямбол                ✓
///        Образование · Ямбол
///        Учителски пулт                   (only when it has teams)
///   ─────────────────────────────
///   Твоите
///   (◎)  Кой знае повече?                 ✓
///        Образование · Ямбол · 2 от 5
///   ─────────────────────────────
///   Наблизо
///   (◎)  Ески джамия                  350 м
///        Туризъм · Ямбол
///   ─────────────────────────────
///   ⌕  Търси сред всички маршрути
///   +  Влез с код
///
/// Switching between trails already joined is one tap; the three closest public
/// trails not joined yet open their preview, where they are joined. Nearby is left out while the position is
/// unknown. With nothing joined and no position there is nothing to list, so
/// the chip opens the trails screen directly.
///
/// It grows out of the chip on a spring, the chevron turns over while it is
/// open, and a tap anywhere else (or Back) folds it away again.
/// </summary>
public class TrailMenuView : MonoBehaviour
{
    [Tooltip("The chip it hangs from.")]
    public RectTransform anchor;
    [Tooltip("The chip's chevron, turned over while the menu is open.")]
    public RectTransform chevron;

    public RectTransform card;
    public CanvasGroup cardGroup;
    public Image scrim;
    public Button scrimButton;

    [Header("Yours")]
    public GameObject mineLabel;
    public RectTransform rowsParent;
    [Tooltip("One trail's row, built once and switched off; copies of it are the list.")]
    public RectTransform rowTemplate;

    [Header("Mine as owner")]
    [Tooltip("\"Моите маршрути\": trails this account owns (my_worlds, role = owner).")]
    public GameObject ownedLabel;
    public RectTransform ownedParent;
    [Tooltip("One owned trail: a child \"Row\" shaped like rowTemplate's, and a " +
             "child \"Teacher\" button under it. Built once and switched off.")]
    public RectTransform ownedTemplate;
    [Tooltip("The hairline under the owned trails.")]
    public GameObject ownedDivider;
    public int maxOwned = 3;

    [Header("Nearby")]
    public GameObject nearDivider;
    public GameObject nearLabel;
    public RectTransform nearParent;

    [Header("The way to the rest")]
    public GameObject actionsDivider;
    public Button joinButton;
    [Tooltip("Search all trails: opens the trails screen.")]
    public Button allButton;

    [Tooltip("education, tourism, estate, other - TrailFilter.Categories order.")]
    public Sprite[] categoryIcons;

    public int maxRows = 3;
    public int maxNear = 3;
    public float maxWidth = 1100f;
    public float scrimAlpha = 0.12f;

    const float Stiffness = 380f;
    const float Damping = 30f;

    readonly List<RectTransform> _rows = new List<RectTransform>();
    readonly List<RectTransform> _nearRows = new List<RectTransform>();
    readonly List<RectTransform> _ownedRows = new List<RectTransform>();
    Coroutine _move;
    bool _open;
    string _progress;   // the preview's stand-in for the open trail's progress

    public bool IsOpen { get { return _open; } }

    void Awake()
    {
        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);

        if (ownedTemplate != null)
            ownedTemplate.gameObject.SetActive(false);

        TurnChevron(0f);

        if (scrimButton != null)
            scrimButton.onClick.AddListener(Close);

        if (joinButton != null)
            joinButton.onClick.AddListener(() =>
            {
                CloseNow();
                if (DoorFlow.Instance != null)
                    DoorFlow.Instance.OpenCode();
            });

        if (allButton != null)
            allButton.onClick.AddListener(OpenAll);
    }

    void OnDisable()
    {
        _open = false;
        if (_move != null)
        {
            StopCoroutine(_move);
            _move = null;
        }

        TurnChevron(0f);
    }

    public void Toggle()
    {
        if (_open)
            Close();
        else
            Open();
    }

    public void Open()
    {
        TrailService service = TrailService.Instance;
        UserLocationService location = UserLocationService.Instance;
        bool located = location != null && location.HasValidPose;
        bool anyMine = service != null && service.Mine != null && service.Mine.Count > 0;

        // Nothing joined and nothing to measure "nearby" from: the menu would hold
        // two rows that both lead to the trail screen, so go there.
        if (service == null || DemoTrail.Active || (!anyMine && !located))
        {
            OpenAll();
            return;
        }

        gameObject.SetActive(true);
        Redraw();
        Place();

        _open = true;
        Haptics.Tick();

        if (_move != null)
            StopCoroutine(_move);

        _move = StartCoroutine(Grow());

        // Asked on the service, so a menu folded away mid-request does not
        // strand the answer; drawn when it comes back if the menu is still up.
        if (located)
            service.StartCoroutine(service.LoadNear(location.Latitude, location.Longitude, () =>
            {
                if (_open && isActiveAndEnabled)
                    Redraw();
            }));

        // The trails this account made. Asked every time the menu opens: one
        // may have been made on the website a minute ago.
        if (TrailService.CanOwn && ownedParent != null)
            service.StartCoroutine(service.LoadOwned(() =>
            {
                if (_open && isActiveAndEnabled)
                    Redraw();
            }));
    }

    public void Close()
    {
        if (!_open || !isActiveAndEnabled)
        {
            CloseNow();
            return;
        }

        _open = false;

        if (_move != null)
            StopCoroutine(_move);

        _move = StartCoroutine(Shrink());
    }

    void CloseNow()
    {
        _open = false;
        gameObject.SetActive(false);
    }

    void OpenAll()
    {
        CloseNow();
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTrails();
    }

    void Choose(string worldId)
    {
        TrailService service = TrailService.Instance;
        bool same = service != null && service.SelectedId == worldId;

        if (service != null && !same)
            service.Select(worldId);

        if (!same && DoorFlow.Instance != null)
            DoorFlow.Instance.Entered();

        Haptics.Tick();
        Close();
    }

    /// <summary>
    /// A nearby trail is not the reader's yet: it opens the preview, where it is
    /// joined (TrailPreviewView), rather than being joined on the spot.
    /// </summary>
    void PreviewNear(string worldId)
    {
        Haptics.Tick();
        CloseNow();

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenPreview(worldId);
    }

    /// <summary>
    /// The teacher panel for an owned trail. The menu folds first, so Back from
    /// the panel lands on the map rather than on a menu left hanging.
    /// </summary>
    void OpenTeacher(string worldId, string trailName)
    {
        Haptics.Tick();
        CloseNow();
        TeacherPanelView.Open(worldId, trailName);
    }

    // ------------------------------------------------------------------ rows

    void Redraw()
    {
        TrailService service = TrailService.Instance;
        UserLocationService location = UserLocationService.Instance;
        bool located = location != null && location.HasValidPose;

        DrawWithOwned(service != null ? service.OwnedNow() : null,
                      service != null ? service.Mine : null,
                      service != null ? service.SelectedId : null,
                      located && service != null ? service.Near : null);
    }

    /// <summary>
    /// Fills the menu from these lists. Public so the preview harness draws it
    /// with the same code the app does. A null or empty near list hides Nearby.
    /// </summary>
    public void Draw(List<TrailService.Trail> mine, string selectedId, List<TrailService.Listed> near,
                     string progress = null)
    {
        DrawWithOwned(null, mine, selectedId, near, progress);
    }

    /// <summary>
    /// As Draw, with "Моите маршрути" on top: the trails this account owns. An
    /// owned trail is not listed again under Твоите. Null or empty hides it.
    /// </summary>
    public void DrawWithOwned(List<TrailService.Owned> owned, List<TrailService.Trail> mine,
                              string selectedId, List<TrailService.Listed> near,
                              string progress = null)
    {
        _progress = progress;

        if (rowsParent == null || rowTemplate == null)
            return;

        List<TrailService.Owned> ownedShown = PickOwned(owned, selectedId);
        bool anyOwned = ownedShown.Count > 0 && ownedParent != null && ownedTemplate != null;

        if (ownedParent != null && ownedTemplate != null)
        {
            Fill(_ownedRows, ownedParent, anyOwned ? ownedShown.Count : 0, ownedTemplate);
            for (int i = 0; anyOwned && i < ownedShown.Count; i++)
                DrawOwned(_ownedRows[i], ownedShown[i], ownedShown[i].id == selectedId, i == 0);
        }

        List<TrailService.Trail> shown = Pick(anyOwned ? WithoutOwned(mine, ownedShown) : mine,
                                              selectedId);
        int nearCount = near == null ? 0 : Mathf.Min(near.Count, maxNear);

        Fill(_rows, rowsParent, shown.Count, rowTemplate);
        for (int i = 0; i < shown.Count; i++)
            DrawMine(_rows[i], shown[i], shown[i].id == selectedId, i == 0);

        if (nearParent != null)
        {
            Fill(_nearRows, nearParent, nearCount, rowTemplate);
            for (int i = 0; i < nearCount; i++)
                DrawNear(_nearRows[i], near[i], i == 0);
        }

        bool anyMine = shown.Count > 0;
        bool anyNear = nearCount > 0;

        Set(mineLabel, anyMine);
        Set(rowsParent.gameObject, anyMine);
        Set(nearLabel, anyNear);
        if (nearParent != null)
            Set(nearParent.gameObject, anyNear);
        Set(nearDivider, anyMine && anyNear);
        Set(actionsDivider, anyOwned || anyMine || anyNear);

        Set(ownedLabel, anyOwned);
        if (ownedParent != null)
            Set(ownedParent.gameObject, anyOwned);
        Set(ownedDivider, anyOwned && (anyMine || anyNear));
    }

    /// <summary>Up to maxOwned, and always the open one if it is owned.</summary>
    List<TrailService.Owned> PickOwned(List<TrailService.Owned> owned, string selectedId)
    {
        List<TrailService.Owned> shown = new List<TrailService.Owned>();

        if (owned == null)
            return shown;

        for (int i = 0; i < owned.Count && shown.Count < maxOwned; i++)
            shown.Add(owned[i]);

        bool hasCurrent = false;
        for (int i = 0; i < shown.Count; i++)
            hasCurrent |= shown[i].id == selectedId;

        if (!hasCurrent && shown.Count > 0)
        {
            for (int i = maxOwned; i < owned.Count; i++)
            {
                if (owned[i].id == selectedId)
                {
                    shown[shown.Count - 1] = owned[i];
                    break;
                }
            }
        }

        return shown;
    }

    /// <summary>The joined trails minus the owned ones already listed above them.</summary>
    static List<TrailService.Trail> WithoutOwned(List<TrailService.Trail> mine,
                                                 List<TrailService.Owned> owned)
    {
        List<TrailService.Trail> rest = new List<TrailService.Trail>();
        if (mine == null)
            return rest;

        for (int i = 0; i < mine.Count; i++)
        {
            bool listed = false;
            for (int j = 0; j < owned.Count && !listed; j++)
                listed = owned[j].id == mine[i].id;

            if (!listed)
                rest.Add(mine[i]);
        }

        return rest;
    }

    /// <summary>Up to maxRows of the joined trails, and always the open one.</summary>
    List<TrailService.Trail> Pick(List<TrailService.Trail> mine, string selectedId)
    {
        List<TrailService.Trail> shown = new List<TrailService.Trail>();

        if (mine == null)
            return shown;

        for (int i = 0; i < mine.Count && shown.Count < maxRows; i++)
            shown.Add(mine[i]);

        bool hasCurrent = false;
        for (int i = 0; i < shown.Count; i++)
            hasCurrent |= shown[i].id == selectedId;

        if (!hasCurrent && shown.Count > 0)
        {
            for (int i = maxRows; i < mine.Count; i++)
            {
                if (mine[i].id == selectedId)
                {
                    shown[shown.Count - 1] = mine[i];
                    break;
                }
            }
        }

        return shown;
    }

    void Fill(List<RectTransform> rows, RectTransform parent, int count, RectTransform template)
    {
        while (rows.Count < count)
        {
            RectTransform row = Instantiate(template, parent);
            row.name = "Trail";
            rows.Add(row);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < count;
            if (rows[i].gameObject.activeSelf != used)
                rows[i].gameObject.SetActive(used);
        }
    }

    void DrawMine(RectTransform row, TrailService.Trail trail, bool current, bool first)
    {
        string sub = TrailService.CategoryAndCity(trail.category, trail.city);

        if (current)
        {
            string progress = _progress ?? Progress();
            if (progress.Length > 0)
                sub = sub.Length == 0 ? progress : sub + " · " + progress;
        }

        DrawRow(row, trail.name, trail.category, sub, current, null, first);

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            string id = trail.id;
            button.onClick.AddListener(() => Choose(id));
        }
    }

    /// <summary>
    /// An owned trail: tapping the row opens it, as under Твоите; "Учителски
    /// пулт" under it opens the teacher panel, and only when there are teams to
    /// run (a scored mode, or teams already made).
    /// </summary>
    void DrawOwned(RectTransform item, TrailService.Owned trail, bool current, bool first)
    {
        Transform rowT = item.Find("Row");
        RectTransform row = rowT != null ? (RectTransform)rowT : item;

        DrawRow(row, trail.name, trail.category,
                TrailService.CategoryAndCity(trail.category, trail.city), current, null, first);

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            string id = trail.id;
            button.onClick.AddListener(() => Choose(id));
        }

        Transform teacherT = item.Find("Teacher");
        if (teacherT == null)
            return;

        bool teams = trail.HasTeams;
        Set(teacherT.gameObject, teams);

        Button teacher = teacherT.GetComponent<Button>();
        if (teacher != null)
        {
            teacher.onClick.RemoveAllListeners();
            if (teams)
            {
                string id = trail.id;
                string title = trail.name;
                teacher.onClick.AddListener(() => OpenTeacher(id, title));
            }
        }
    }

    void DrawNear(RectTransform row, TrailService.Listed trail, bool first)
    {
        DrawRow(row, trail.name, trail.category,
                TrailService.CategoryAndCity(trail.category, trail.city), false,
                AppStrings.FormatDistance(trail.distance_m), first);

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            string id = trail.id;
            button.onClick.AddListener(() => PreviewNear(id));
        }
    }

    void DrawRow(RectTransform row, string title, string category, string subText,
                 bool current, string distanceText, bool first)
    {
        TextMeshProUGUI name = Find<TextMeshProUGUI>(row, "Text/Name");
        TextMeshProUGUI sub = Find<TextMeshProUGUI>(row, "Text/Sub");
        TextMeshProUGUI distance = Find<TextMeshProUGUI>(row, "Distance");
        Image icon = Find<Image>(row, "Chip/Icon");
        Image check = Find<Image>(row, "Check/Icon");
        Transform rule = row.Find("Rule");

        if (name != null)
            name.text = title;

        if (sub != null)
        {
            sub.text = subText;
            sub.gameObject.SetActive(subText.Length > 0);
        }

        if (icon != null && categoryIcons != null && categoryIcons.Length == TrailFilter.Categories.Length)
            icon.sprite = categoryIcons[TrailFilter.CategoryIndex(category)];

        if (check != null)
            check.enabled = current;

        if (distance != null)
        {
            distance.gameObject.SetActive(!string.IsNullOrEmpty(distanceText));
            distance.text = distanceText ?? string.Empty;
        }

        if (rule != null)
            rule.gameObject.SetActive(!first);
    }

    /// <summary>"2 от 5" for the open trail; its stops are the ones on the phone.</summary>
    static string Progress()
    {
        if (DemoTrail.Active)
            return string.Empty;

        var all = BuildingDatabase.AllBuildings;
        if (all == null || all.Count == 0)
            return string.Empty;

        int done = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (NextStopCard.Done(all[i]))
                done++;
        }

        return string.Format(AppStrings.Get("chip.progress"), done, all.Count);
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        Transform t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    // ----------------------------------------------------------- placing it

    /// <summary>Hung under the chip, its right edge on the chip's, where the chevron is.</summary>
    void Place()
    {
        if (card == null || anchor == null)
            return;

        RectTransform root = (RectTransform)transform;
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);

        Vector3 bottomRight = root.InverseTransformPoint(corners[3]);
        Vector3 bottomLeft = root.InverseTransformPoint(corners[0]);
        float width = Mathf.Min(maxWidth, bottomRight.x - bottomLeft.x);

        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(1f, 1f);
        card.sizeDelta = new Vector2(width, card.sizeDelta.y);
        card.anchoredPosition = new Vector2(bottomRight.x, bottomRight.y - 17f);
    }

    /// <summary>Open and still, for a picture of it.</summary>
    public void ShowStill()
    {
        Place();
        Apply(1f);
    }

    // -------------------------------------------------------------- motion

    IEnumerator Grow()
    {
        // From the chevron's corner: a little smaller and clear, to full size
        // with the faintest overshoot, as a menu comes out of its button.
        float x = 0f;
        float v = 0f;

        while (true)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float a = -Stiffness * (x - 1f) - Damping * v;
            v += a * dt;
            x += v * dt;
            Apply(x);

            if (Mathf.Abs(x - 1f) < 0.002f && Mathf.Abs(v) < 0.02f)
                break;

            yield return null;
        }

        Apply(1f);
        _move = null;
    }

    IEnumerator Shrink()
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.14f);
            Apply(1f - t * t);
            yield return null;
        }

        _move = null;
        gameObject.SetActive(false);
    }

    void Apply(float k)
    {
        if (card != null)
        {
            float s = Mathf.LerpUnclamped(0.86f, 1f, k);
            card.localScale = new Vector3(s, s, 1f);
        }

        if (cardGroup != null)
            cardGroup.alpha = Mathf.Clamp01(k * 1.6f);

        if (scrim != null)
        {
            Color c = scrim.color;
            c.a = scrimAlpha * Mathf.Clamp01(k);
            scrim.color = c;
        }

        TurnChevron(Mathf.Clamp01(k));
    }

    void TurnChevron(float k)
    {
        if (chevron != null)
            chevron.localRotation = Quaternion.Euler(0f, 0f, 180f * k);
    }
}
