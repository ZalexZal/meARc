using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBuildingData", menuName = "Building Data/Building Info")]
public class BuildingData : ScriptableObject
{
    [Header("Publishing")]
    [Tooltip("Rows marked draft in the sheet are invisible to readers. Turn on " +
             "developer mode to preview them.")]
    public bool isPublished = true;

    [Header("Identity")]
    [Tooltip("Permanent, lowercase, no spaces - e.g. \"bezisten_yambol\". This is the key " +
             "discovery progress is saved under, so it must never change once the app " +
             "has shipped.")]
    public string buildingId;

    // Each field is followed immediately by its Bulgarian counterpart, so both can
    // be written in one pass while the research is in front of you. Leave a
    // Bulgarian field empty and the English one is shown in its place.

    [Tooltip("Which stop this is on an ordered trail: 1, 2, 3. Zero means the trail " +
             "has no order and the app falls back to whatever is nearest and unopened. " +
             "Set by whoever makes the trail, not by the reader.")]
    public int step;

    [Header("Name")]
    public string buildingName;

    [Tooltip("Bulgarian name, e.g. \"Ямболски безистен\".")]
    public string buildingNameBg;

    [Header("Description")]
    [TextArea(4, 8)]
    public string description;

    [TextArea(4, 8)]
    [Tooltip("Bulgarian description.")]
    public string descriptionBg;

    [Header("Details")]
    [Tooltip("Shown to the reader exactly as typed, so \"c. 1850\" and \"Unknown\" are fine.")]
    public string yearBuilt;

    [Tooltip("Bulgarian form, e.g. \"15 век\" or \"Неизвестна\".")]
    public string yearBuiltBg;

    public string architect;

    [Tooltip("Bulgarian form - transliterated name, or \"Неизвестен\".")]
    public string architectBg;

    [Tooltip("Filled in automatically from Year Built. Edit by hand when the year above " +
             "isn't a plain number. 0 means the era is unknown and this building is left " +
             "out of era filters.")]
    public int yearBuiltNumeric;

    [Tooltip("Legacy free-text type. Use Category instead - this is kept only so older " +
             "assets don't lose data.")]
    public string buildingType;

    [Header("Classification")]
    [Tooltip("Drives the type filter in Search, and carries its own Bulgarian label.")]
    public BuildingCategory category;

    [Header("Location")]
    public double latitude;
    public double longitude;

    [Header("Marker")]
    [Tooltip("Optional style key from the sheet's \"marker\" column. Blank means " +
             "use whatever this building's Category uses.")]
    public string markerStyle;

    [Tooltip("Optional hex colour from the sheet's \"marker_colour\" column, e.g. " +
             "#C2483A. Blank means use the Category's colour.")]
    public string markerColourHex;

    [Tooltip("Made on site and not yet filled in on the website. Only the trail's " +
             "owner and editors are ever sent one.")]
    public bool isDraft;

    [Tooltip("The words on a sign-shaped marker (content.<lang>.sign). Blank shows the name.")]
    public string signText;
    public string signTextBg;

    [Tooltip("The stop asks a question (content.<lang>.question). Known from the list, " +
             "before the stop is ever opened, unlike task.")]
    public bool hasQuestion;

    [Header("Content")]
    [Tooltip("Uploaded video and audio, in the order the creator arranged them. A " +
             "marker with several is a small sequence rather than a page.")]
    public MarkerMedia[] media;

    [Tooltip("The question on this marker, if it has one. Never carries the answer - " +
             "see MarkerContent.cs.")]
    public MarkerTask task;

    [Tooltip("Hints, in order, with their reveal state. Fetched with the rest of " +
             "the stop's contents when it is opened.")]
    public MarkerHint[] hints;

    [Tooltip("How close somebody has to be for this to count as arriving at, in " +
             "metres. The creator sets it per stop; 0 means it was never sent " +
             "and the app falls back to its own default.")]
    public int radiusMetres;

    /// <summary>Media the app can actually play, in order. Never null.</summary>
    public System.Collections.Generic.List<MarkerMedia> UsableMedia
    {
        get
        {
            var usable = new System.Collections.Generic.List<MarkerMedia>();

            if (media == null)
                return usable;

            for (int i = 0; i < media.Length; i++)
            {
                if (media[i] != null && media[i].IsUsable)
                    usable.Add(media[i]);
            }

            return usable;
        }
    }

    public bool HasTask
    {
        get { return task != null && task.IsUsable; }
    }

    /// <summary>
    /// How many things there are to do here, counting the question as one. Drives
    /// the "3 pieces" chip, and a marker with nothing but text reads zero.
    /// </summary>
    public int PieceCount
    {
        get { return UsableMedia.Count + (HasTask ? 1 : 0); }
    }

    [Header("External Link")]
    public string externalLinkURL;

    [Tooltip("Optional separate link for Bulgarian readers - the Bulgarian Wikipedia " +
             "article is usually more useful to them than the English one.")]
    public string externalLinkURLBg;

    // ------------------------------------------------------------- localization

    /// <summary>
    /// Falls back to the asset filename so a half-filled entry is still identifiable
    /// in a list rather than rendering as a blank row.
    /// </summary>
    public string DisplayName
    {
        get
        {
            string chosen = Pick(buildingName, buildingNameBg);
            if (!string.IsNullOrWhiteSpace(chosen))
                return chosen;

            // A stop from the server is named by its id, and a creator can leave
            // the title empty: the walker was shown "765d4520-4758-..." in a
            // toast. Its number says more, and nothing at all is better than a
            // database key.
            if (string.IsNullOrWhiteSpace(name) || System.Guid.TryParse(name, out _))
            {
                return step > 0
                    ? string.Format(AppStrings.Get("stop.untitledN"), step)
                    : AppStrings.Get("stop.untitled");
            }

            return name;
        }
    }

    public string Description { get { return Pick(description, descriptionBg); } }
    public string YearBuilt { get { return Pick(yearBuilt, yearBuiltBg); } }
    public string Architect { get { return Pick(architect, architectBg); } }
    public string ExternalLink { get { return Pick(externalLinkURL, externalLinkURLBg); } }

    /// <summary>What a sign-shaped marker says: its own words, or the stop's name.</summary>
    public string SignText
    {
        get
        {
            string words = Pick(signText, signTextBg);
            return string.IsNullOrWhiteSpace(words) ? DisplayName : words;
        }
    }

    /// <summary>
    /// Bulgarian when it is both selected and written; English otherwise. The
    /// fallback matters during content entry, when most Bulgarian fields are still
    /// empty and a blank panel would look like a bug.
    /// </summary>
    static string Pick(string english, string bulgarian)
    {
        if (AppStrings.CurrentLanguage == AppLanguage.Bulgarian &&
            !string.IsNullOrWhiteSpace(bulgarian))
        {
            return bulgarian;
        }

        return english;
    }

    /// <summary>
    /// True if either language's name contains the query, so a Bulgarian reader can
    /// search in Cyrillic and still find a building whose Bulgarian name is missing.
    /// </summary>
    public bool NameMatches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(buildingName, query) ||
               Contains(buildingNameBg, query) ||
               Contains(name, query);
    }

    static bool Contains(string value, string query)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Deriving the number saves typing the year twice for every building, while
        // still leaving it editable for entries the parser can't read.
        if (yearBuiltNumeric == 0 && !string.IsNullOrWhiteSpace(yearBuilt))
        {
            Match match = Regex.Match(yearBuilt, @"\d{3,4}");
            if (match.Success && int.TryParse(match.Value, out int parsed))
                yearBuiltNumeric = parsed;
        }
    }
#endif
}
