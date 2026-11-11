using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round 3, agent M: "Моите маршрути" in the trail menu, and the role seal
/// beside the name. Called from the main build in AppUIBuilder.cs.
/// </summary>
public static partial class AppUIBuilder
{
    // ------------------------------------------------------- Моите маршрути

    /// <summary>
    /// Adds "Моите маршрути" to the top of a built trail menu: a label, the
    /// owned-trail rows, and a hairline under them. Call right after
    /// BuildTrailMenu, with the view it returned.
    ///
    ///   Моите маршрути
    ///   (◎)  Name                               ✓
    ///        Category · City
    ///        👥 Учителски пулт                   (accent, only with teams)
    /// </summary>
    public static void BuildOwnedSection(TrailMenuView view)
    {
        if (view == null || view.card == null || view.rowTemplate == null)
            return;

        RectTransform card = view.card;

        view.ownedLabel = MenuSection(card, "OwnedLabel", "misc.myTrails");
        view.ownedLabel.transform.SetSiblingIndex(OwnedSectionStart(card));

        RectTransform owned = UIFactory.Rect("Owned", card);
        UIFactory.VerticalStack(owned, 0f);
        owned.SetSiblingIndex(view.ownedLabel.transform.GetSiblingIndex() + 1);
        view.ownedParent = owned;

        view.ownedDivider = MenuDivider(card, "OwnedDivider");
        view.ownedDivider.transform.SetSiblingIndex(owned.GetSiblingIndex() + 1);

        // --- one owned trail: the ordinary trail row, and the teacher's way in
        //     under it, indented to the name so it reads as belonging to it.
        RectTransform item = UIFactory.Rect("OwnedItem", owned);
        UIFactory.VerticalStack(item, 0f, false);

        RectTransform row = Object.Instantiate(view.rowTemplate, item);
        row.name = "Row";
        row.gameObject.SetActive(true);

        RectTransform teacher = UIFactory.Rect("Teacher", item);
        UIFactory.Size(teacher.gameObject, OwnedActionHeight);
        UIFactory.Button(teacher, ThemeRole.Fill, 0f);

        Image glyph = Icon(teacher, AppIcon.Users, 42f, ThemeRole.AccentText, out RectTransform glyphRect);
        glyph.gameObject.name = "Glyph";
        UIFactory.Corner(glyphRect, new Vector2(0f, 0.5f), new Vector2(42f, 42f),
                         new Vector2(MenuText, 0f));

        TextMeshProUGUI label = UIFactory.Text("Label", teacher, Secondary, ThemeRole.AccentText,
                                               TextAlignmentOptions.Left, "misc.teacherPanel",
                                               FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(MenuText + 42f + 17f, 0f);
        label.rectTransform.offsetMax = new Vector2(-MenuSide, 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        view.ownedTemplate = item;
        item.gameObject.SetActive(false);

        view.ownedLabel.SetActive(false);
        owned.gameObject.SetActive(false);
        view.ownedDivider.SetActive(false);

        EditorUtility.SetDirty(view);
    }

    const float OwnedActionHeight = 96f;

    /// <summary>Where the first section starts: after the card's own art (face, outline).</summary>
    static int OwnedSectionStart(RectTransform card)
    {
        Transform mine = card.Find("MineLabel");
        return mine != null ? mine.GetSiblingIndex() : card.childCount;
    }

    /// <summary>Stand-ins for the preview: one game with teams, one plain walk.</summary>
    public static List<TrailService.Owned> SampleOwned()
    {
        return new List<TrailService.Owned>
        {
            new TrailService.Owned
            {
                id = "owned-0", name = "Ямбол за 7. клас", mode = "ctf", role = "owner",
                team_count = 4, category = "education",
                city = new TrailService.CityJson { key = "yambol", bg = "Ямбол", en = "Yambol" }
            },
            new TrailService.Owned
            {
                id = "owned-1", name = "Старият център", mode = "guide", role = "owner",
                team_count = 0, category = "tourism",
                city = new TrailService.CityJson { key = "yambol", bg = "Ямбол", en = "Yambol" }
            }
        };
    }

    // ------------------------------------------------------------ the seal

    /// <summary>
    /// The role seal, the website's NameSeal: a rounded square or a circle and
    /// the role's glyph, size x size, as a fixed-width layout child. It follows
    /// the signed-in account by itself (NameSeal.followAccount).
    /// </summary>
    public static NameSeal BuildNameSeal(Transform parent, float size)
    {
        RectTransform holder = UIFactory.Rect("NameSeal", parent);
        UIFactory.Corner(holder, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);
        LayoutElement element = UIFactory.Size(holder.gameObject, size, size);
        element.flexibleWidth = 0f;

        NameSeal seal = holder.gameObject.AddComponent<NameSeal>();

        RectTransform squareRect = UIFactory.Rect("Square", holder);
        UIFactory.Stretch(squareRect);
        seal.square = UIFactory.Plain(squareRect, new Color32(0x1d, 0x1d, 0x1f, 0xff), UIShape.Small);
        seal.square.raycastTarget = false;

        RectTransform roundRect = UIFactory.Rect("Round", holder);
        UIFactory.Stretch(roundRect);
        seal.round = UIFactory.Plain(roundRect, new Color32(0x1d, 0x1d, 0x1f, 0xff), UIShape.Circle);
        seal.round.raycastTarget = false;
        roundRect.gameObject.SetActive(false);

        RectTransform glyphRect = UIFactory.Rect("Glyph", holder);
        UIFactory.Corner(glyphRect, new Vector2(0.5f, 0.5f), new Vector2(size * 0.62f, size * 0.62f),
                         Vector2.zero);
        seal.glyph = UIFactory.Plain(glyphRect, Color.white);
        seal.glyph.preserveAspect = true;
        seal.glyph.raycastTarget = false;

        seal.glyphs = SealGlyphs();
        seal.glyph.sprite = seal.glyphs[seal.glyphs.Length - 1];

        EditorUtility.SetDirty(seal);
        return seal;
    }

    /// <summary>
    /// In NameSeal.GlyphKeys order. The website's own drawings (agent R's
    /// Assets/Art/Stage/icon-*.png, white on clear) where they exist, else the
    /// app's vector icons the role banner uses.
    /// </summary>
    static Sprite[] SealGlyphs()
    {
        AppIcon[] fallback =
        {
            AppIcon.Book,      // teacher
            AppIcon.Landmark,  // guide
            AppIcon.Home,      // broker
            AppIcon.Spark,     // other
            AppIcon.Walk,      // viewer
            AppIcon.Plus       // pencil
        };

        Sprite[] sprites = new Sprite[NameSeal.GlyphKeys.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite art = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Stage/icon-" + NameSeal.GlyphKeys[i] + ".png");
            sprites[i] = art != null ? art : UIIcons.Get(fallback[i]);
        }

        return sprites;
    }
}
