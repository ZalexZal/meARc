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

        Render(guest, data, auth != null ? auth.DisplayName : null, auth != null ? auth.Email : null);
    }

    /// <summary>
    /// Draws a given state. Refresh passes the live one; the preview harness
    /// passes a sample, because in the editor nobody is signed in and every
    /// picture would otherwise be of a guest.
    /// </summary>
    public void Render(bool guest, ProfileService.ProfileData data, string displayName, string email)
    {
        DrawWho(guest, data, displayName, email);

        if (statsGroup != null)
            statsGroup.SetActive(!guest && data != null);

        if (!guest && data != null)
        {
            Set(trailsValue, data.trails_walked.ToString());
            Set(stopsValue, data.stops_found.ToString());

            int total = data.badges != null ? data.badges.Length : Badges.Keys.Length;
            Set(badgesValue, data.Earned + " / " + total);
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

    void DrawMedal(int slot, string key, bool earned, int tier)
    {
        if (slot >= medalSlots.Length || medalSlots[slot] == null)
            return;

        medalSlots[slot].SetActive(true);
        Color colour = Badges.Colour(key, earned, tier);

        if (slot < medalRings.Length && medalRings[slot] != null)
            medalRings[slot].color = Badges.Wash(colour, earned);

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
