using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The top of the You tab: who you are, what you have walked, what you have
/// earned.
///
/// Two states, and the second is the one that matters most: a guest still has a
/// You tab, because a guest still needs its settings. For a guest the numbers
/// step aside and the badges sit dimmed, as the reason to make an account rather
/// than as a locked door.
/// </summary>
public class ProfileView : MonoBehaviour
{
    [Header("Who")]
    public Image face;
    public TextMeshProUGUI faceLabel;
    public GameObject faceGuestIcon;
    public TextMeshProUGUI nameLabel;
    public TextMeshProUGUI subLabel;

    [Tooltip("\"Level 6 · Walker I\" under the name (0056).")]
    public TextMeshProUGUI levelLabel;
    [Tooltip("The bar under it: its fill's right anchor is the progress.")]
    public GameObject levelBar;
    public RectTransform levelFill;

    [Header("Numbers")]
    public GameObject statsGroup;
    public TextMeshProUGUI trailsValue;
    public TextMeshProUGUI stopsValue;
    public TextMeshProUGUI badgesValue;

    [Header("Badges")]
    public GameObject badgesGroup;
    public TextMeshProUGUI badgesTitle;
    public Button allBadgesButton;
    public TextMeshProUGUI allBadgesLabel;
    public GameObject[] medalSlots = new GameObject[0];
    public Image[] medalRings = new Image[0];
    public Image[] medalIcons = new Image[0];
    public TextMeshProUGUI[] medalLabels = new TextMeshProUGUI[0];

    [Tooltip("One per badge, in Badges.Keys order.")]
    public Sprite[] badgeSprites = new Sprite[0];

    [Header("Recent trails")]
    public GameObject recentGroup;
    public TextMeshProUGUI[] recentNames = new TextMeshProUGUI[0];
    public TextMeshProUGUI[] recentMetas = new TextMeshProUGUI[0];
    public GameObject[] recentRows = new GameObject[0];

    [Header("Guest")]
    public TextMeshProUGUI guestNote;

    [Header("Daylight v2")]
    [Tooltip("The guest's card: progress lives on this phone, make a profile.")]
    public GameObject guestGroup;

    [Tooltip("The signed-in card: the face, the name, since when.")]
    public GameObject meGroup;

    public Button createButton;
    public Button signInButton;

    [Tooltip("Rows only an account has: the account page, sign out.")]
    public GameObject[] signedInOnly = new GameObject[0];

    [Tooltip("The hairline round a badge not yet earned.")]
    public Image[] medalEdges = new Image[0];

    [Tooltip("One per slot in the grid. Each opens the badge drawn in it.")]
    public Button[] medalButtons = new Button[0];
    public BadgeSheetView badgeSheet;

    // Which badge each slot is showing: the grid is sorted earned-first, so a
    // slot's badge changes with the profile.
    string[] _slotKeys = new string[0];

    void Awake()
    {
        for (int i = 0; i < medalButtons.Length; i++)
        {
            int slot = i;
            if (medalButtons[i] != null)
                medalButtons[i].onClick.AddListener(() => OpenBadge(slot));
        }

        if (createButton != null)
            createButton.onClick.AddListener(OpenAccount);

        if (signInButton != null)
            signInButton.onClick.AddListener(OpenAccount);
    }

    static void OpenAccount()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenAccount();
    }

    void OnEnable()
    {
        ProfileService.OnChanged += Refresh;
        SupabaseAuth.OnSessionChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        ThemeManager.OnThemeChanged += Refresh;

        // Asked every time the tab opens, not only the first time.
        //
        // The numbers were cached for the life of the app: a stop reached on the
        // map a minute ago, or a rule changed on the server, showed up only after
        // a restart - and a phone was seen holding a badge it no longer had.
        // One small request when somebody actually looks at the screen.
        if (ProfileService.Instance != null)
            ProfileService.Instance.Load();

        Refresh();
    }

    void OnDisable()
    {
        ProfileService.OnChanged -= Refresh;
        SupabaseAuth.OnSessionChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        ThemeManager.OnThemeChanged -= Refresh;
    }

    public void Refresh()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool guest = auth == null || auth.IsAnonymous;

        ProfileService service = ProfileService.Instance;
        ProfileService.ProfileData data = service != null ? service.Latest : null;

        Render(guest, data, auth != null ? auth.DisplayName : null, auth != null ? auth.Email : null,
               service != null ? service.Level : null);
    }

    /// <summary>
    /// Draws a given state. Refresh passes the live one; the preview harness
    /// passes a sample, because in the editor nobody is signed in and every
    /// picture would otherwise be of a guest.
    /// </summary>
    public void Render(bool guest, ProfileService.ProfileData data, string displayName, string email,
                       ProfileService.LevelData level = null)
    {
        DrawWho(guest, data, displayName, email);
        DrawLevel(guest, level);

        if (guestGroup != null)
            guestGroup.SetActive(guest);

        if (meGroup != null)
            meGroup.SetActive(!guest);

        for (int i = 0; i < signedInOnly.Length; i++)
        {
            if (signedInOnly[i] != null)
                signedInOnly[i].SetActive(!guest);
        }

        if (statsGroup != null)
            statsGroup.SetActive(!guest && data != null);

        if (!guest && data != null)
        {
            Set(trailsValue, data.trails_walked.ToString());
            Set(stopsValue, data.stops_found.ToString());

            // "1 trail", "2 trails": the word under a number agrees with it.
            Caption(trailsValue, data.trails_walked == 1 ? "profile.trailsOne" : "profile.trails");
            Caption(stopsValue, data.stops_found == 1 ? "profile.stopsOne" : "profile.stops");

            int total = data.badges != null ? data.badges.Length : Badges.Keys.Length;
            Set(badgesValue, string.Format(AppStrings.Get("chip.progress"), data.Earned, total));
        }

        DrawMedals(guest, data);
        DrawRecent(guest, data);

        if (guestNote != null)
        {
            guestNote.gameObject.SetActive(guest);
            guestNote.text = AppStrings.Get("profile.guestNote");
        }
    }

    void DrawWho(bool guest, ProfileService.ProfileData data, string displayName, string email)
    {
        string name = displayName;

        if (string.IsNullOrWhiteSpace(name) && data != null)
            name = data.name;

        if (guest)
        {
            Set(nameLabel, AppStrings.Get("profile.guest"));
            Set(subLabel, AppStrings.Get("profile.guestSub"));
        }
        else
        {
            Set(nameLabel, string.IsNullOrWhiteSpace(name) ? (email ?? string.Empty) : name);
            Set(subLabel, Since(data));
        }

        if (face != null)
            face.color = ThemeManager.Get(guest ? ThemeRole.SurfaceAlt : ThemeRole.Accent);

        if (faceLabel != null)
        {
            faceLabel.gameObject.SetActive(!guest);
            faceLabel.text = Initials(name, email);
            faceLabel.color = ThemeManager.Get(ThemeRole.OnAccent);
        }

        if (faceGuestIcon != null)
            faceGuestIcon.SetActive(guest);
    }

    void DrawLevel(bool guest, ProfileService.LevelData level)
    {
        bool show = !guest && level != null && level.level > 0;

        if (levelLabel != null)
        {
            levelLabel.gameObject.SetActive(show);
            if (show)
                levelLabel.text = string.Format(AppStrings.Get("profile.level"), level.level,
                                                ProfileService.LevelName(level.level));
        }

        if (levelBar != null)
            levelBar.SetActive(show);

        if (show && levelFill != null)
            levelFill.anchorMax = new Vector2(level.Progress, 1f);
    }

    void DrawMedals(bool guest, ProfileService.ProfileData data)
    {
        if (badgesGroup != null)
            badgesGroup.SetActive(guest || data != null);

        Set(badgesTitle, AppStrings.Get(guest ? "profile.waiting" : "profile.badges"));
        Set(allBadgesLabel, string.Format(AppStrings.Get("profile.allBadges"), Badges.Keys.Length));

        if (allBadgesButton != null)
            allBadgesButton.gameObject.SetActive(!guest);

        // Earned first, newest in the list last, so the row leads with what
        // somebody has. A guest sees the first few as a preview.
        ProfileService.BadgeRow[] rows = data != null && data.badges != null
            ? data.badges : new ProfileService.BadgeRow[0];

        int slot = 0;

        if (!guest)
        {
            for (int i = rows.Length - 1; i >= 0 && slot < medalSlots.Length; i--)
            {
                if (rows[i] != null && rows[i].earned)
                    DrawMedal(slot++, rows[i].key, true, rows[i].tier);
            }

            for (int i = 0; i < rows.Length && slot < medalSlots.Length; i++)
            {
                if (rows[i] != null && !rows[i].earned)
                    DrawMedal(slot++, rows[i].key, false, 0);
            }
        }
        else
        {
            for (int i = 0; i < Badges.Keys.Length && slot < medalSlots.Length; i++)
                DrawMedal(slot++, Badges.Keys[i], false, 0);
        }

        for (int i = slot; i < medalSlots.Length; i++)
            if (medalSlots[i] != null)
                medalSlots[i].SetActive(false);
    }

    void OpenBadge(int slot)
    {
        if (badgeSheet != null && slot < _slotKeys.Length)
            badgeSheet.Open(_slotKeys[slot]);
    }

    void DrawMedal(int slot, string key, bool earned, int tier)
    {
        if (slot >= medalSlots.Length || medalSlots[slot] == null)
            return;

        if (_slotKeys.Length != medalSlots.Length)
            _slotKeys = new string[medalSlots.Length];

        _slotKeys[slot] = key;

        medalSlots[slot].SetActive(true);

        // One blue for every earned badge, as Daylight draws them: a tint of it
        // behind the glyph in the full colour. One not yet earned is an outline.
        Color colour = ThemeManager.Get(earned ? ThemeRole.AccentText : ThemeRole.TextMuted);

        if (slot < medalRings.Length && medalRings[slot] != null)
        {
            Color wash = ThemeManager.Get(ThemeRole.AccentTint);
            if (!earned)
                wash.a = 0f;
            medalRings[slot].color = wash;
        }

        if (slot < medalEdges.Length && medalEdges[slot] != null)
            medalEdges[slot].gameObject.SetActive(!earned);

        if (slot < medalIcons.Length && medalIcons[slot] != null)
        {
            int index = Badges.IndexOf(key);
            if (index >= 0 && index < badgeSprites.Length)
                medalIcons[slot].sprite = badgeSprites[index];
            medalIcons[slot].color = colour;
        }

        if (slot < medalLabels.Length && medalLabels[slot] != null)
            medalLabels[slot].text = Badges.Name(key);
    }

    void DrawRecent(bool guest, ProfileService.ProfileData data)
    {
        bool any = !guest && data != null && data.trails != null && data.trails.Length > 0;

        if (recentGroup != null)
            recentGroup.SetActive(any);

        if (!any)
            return;

        for (int i = 0; i < recentRows.Length; i++)
        {
            bool used = i < data.trails.Length;

            if (recentRows[i] != null)
                recentRows[i].SetActive(used);

            if (!used)
                continue;

            ProfileService.TrailRow row = data.trails[i];
            Set(i < recentNames.Length ? recentNames[i] : null, row.name);

            string meta = row.done + " / " + row.total;
            if (!string.IsNullOrEmpty(row.team))
                meta += " · " + string.Format(AppStrings.Get("profile.points"), row.points);
            if (row.finished)
                meta += " · " + AppStrings.Get("profile.finished");

            Set(i < recentMetas.Length ? recentMetas[i] : null, meta);
        }
    }

    static string Since(ProfileService.ProfileData data)
    {
        if (data == null || string.IsNullOrEmpty(data.since))
            return AppStrings.Get("profile.signedIn");

        DateTime when;
        if (!DateTime.TryParse(data.since, null,
                               System.Globalization.DateTimeStyles.RoundtripKind, out when))
            return AppStrings.Get("profile.signedIn");

        string month = AppStrings.Get("month." + when.Month);
        return string.Format(AppStrings.Get("profile.since"), month, when.Year);
    }

    /// <summary>The word under a stat's number, kept through language changes.</summary>
    static void Caption(TextMeshProUGUI value, string key)
    {
        Transform cell = value != null ? value.transform.parent : null;
        Transform found = cell != null ? cell.Find("Caption") : null;
        TextMeshProUGUI caption = found != null ? found.GetComponent<TextMeshProUGUI>() : null;
        if (caption == null)
            return;

        LocalizedLabel localized = caption.GetComponent<LocalizedLabel>();
        if (localized != null)
            localized.key = key;

        caption.text = AppStrings.Get(key);
    }

    public static string Initials(string name, string email)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string first = parts[0].Substring(0, 1);
            string second = parts.Length > 1 ? parts[1].Substring(0, 1) : string.Empty;
            return (first + second).ToUpperInvariant();
        }

        return string.IsNullOrEmpty(email) ? "?" : email.Substring(0, 1).ToUpperInvariant();
    }

    static void Set(TextMeshProUGUI label, string value)
    {
        if (label != null)
            label.text = value ?? string.Empty;
    }
}
