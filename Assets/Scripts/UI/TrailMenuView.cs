using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The trail chip's pull-down: the chevron promises a menu, so it opens one,
/// hung from the chip, rather than taking over the screen.
///
///   ✓  Кой знае повече?
///      По ред · Отбори и класиране
///      Разходка из стария град
///      Свободен ред
///   ─────────────────────────────
///   +  Join with a code
///   ≡  All trails                 ›
///
/// Switching between trails already joined is one tap; the full trail screen
/// (trails near you, leaving one) is still a row away. With nothing joined
/// there is nothing to list, so the chip opens that screen directly.
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

    public RectTransform rowsParent;
    [Tooltip("One trail's row, built once and switched off; copies of it are the list.")]
    public RectTransform rowTemplate;

    public Button joinButton;
    public Button allButton;

    public int maxRows = 6;
    public float maxWidth = 820f;
    public float scrimAlpha = 0.12f;

    const float Stiffness = 380f;
    const float Damping = 30f;

    readonly List<RectTransform> _rows = new List<RectTransform>();
    Coroutine _move;
    bool _open;

    public bool IsOpen { get { return _open; } }

    void Awake()
    {
        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);

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

        // Nothing joined: the menu would hold two rows that both lead to the
        // trail screen, so go there.
        if (service == null || service.Mine == null || service.Mine.Count == 0 || DemoTrail.Active)
        {
            OpenAll();
            return;
        }

        gameObject.SetActive(true);
        Draw(service);
        Place();

        _open = true;
        Haptics.Tick();

        if (_move != null)
            StopCoroutine(_move);

        _move = StartCoroutine(Grow());
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

    // ------------------------------------------------------------------ rows

    void Draw(TrailService service)
    {
        if (rowsParent == null || rowTemplate == null)
            return;

        int count = Mathf.Min(service.Mine.Count, maxRows);

        while (_rows.Count < count)
        {
            RectTransform row = Instantiate(rowTemplate, rowsParent);
            row.name = "Trail";
            _rows.Add(row);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < count;
            if (_rows[i].gameObject.activeSelf != used)
                _rows[i].gameObject.SetActive(used);

            if (used)
                DrawRow(_rows[i], service.Mine[i], service.Mine[i].id == service.SelectedId, i == 0);
        }
    }

    void DrawRow(RectTransform row, TrailService.Trail trail, bool current, bool first)
    {
        TextMeshProUGUI name = Find<TextMeshProUGUI>(row, "Text/Name");
        TextMeshProUGUI sub = Find<TextMeshProUGUI>(row, "Text/Sub");
        Image check = Find<Image>(row, "Check/Icon");
        Transform rule = row.Find("Rule");

        if (name != null)
        {
            name.text = trail.name;
            name.fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
        }

        if (sub != null)
        {
            string order = AppStrings.Get(TrailService.IsOrdered(trail.mode) ? "chip.inOrder" : "chip.anyOrder");
            sub.text = TrailService.IsGame(trail.mode)
                ? order + " · " + AppStrings.Get("menu.teams")
                : order;
        }

        if (check != null)
            check.enabled = current;

        if (rule != null)
            rule.gameObject.SetActive(!first);

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            string id = trail.id;
            button.onClick.AddListener(() => Choose(id));
        }
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
