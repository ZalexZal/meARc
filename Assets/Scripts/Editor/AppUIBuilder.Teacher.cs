using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The teacher tools: the "Учителски пулт" panel (TeacherPanelView) and the
/// students' waiting screen (WaitingView), under one holder at the Canvas root.
///
/// The holder carries both components and stays active, so the panel can be
/// opened from anywhere (TeacherPanelView.Open) and the waiting screen can
/// listen for the trail's start; the two screens are children it switches.
/// </summary>
public static partial class AppUIBuilder
{
    /// <summary>The holder's name at the Canvas root. Add it to GeneratedNames.</summary>
    public const string TeacherToolsName = "TeacherTools";

    /// <summary>
    /// Builds both screens. Call once from BuildAll, after the tab panels and
    /// the nav bar (so the screens cover them) and before the doors and sheets
    /// (so a door - the team card, the trail list - draws over the waiting
    /// screen). Returns the holder.
    /// </summary>
    public static GameObject BuildTeacherTools(Transform root)
    {
        RectTransform holder = UIFactory.Rect(TeacherToolsName, root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        WaitingView waiting = holder.gameObject.AddComponent<WaitingView>();
        TeacherPanelView teacher = holder.gameObject.AddComponent<TeacherPanelView>();

        // Waiting first, so the panel draws over it if both were ever up.
        BuildWaitingScreen(holder, waiting);
        BuildTeacherPanel(holder, teacher);

        EditorUtility.SetDirty(waiting);
        EditorUtility.SetDirty(teacher);
        return holder.gameObject;
    }

    // ------------------------------------------------------------ the panel

    static void BuildTeacherPanel(RectTransform holder, TeacherPanelView view)
    {
        RectTransform panel = UIFactory.Rect("TeacherPanel", holder);
        UIFactory.Stretch(panel);
        UIFactory.Fill(panel, ThemeRole.Background).raycastTarget = true;
        AddFade(panel.gameObject);
        view.panel = panel.gameObject;

        RectTransform safe = SafeContent(panel, padBottom: true);

        RectTransform column = UIFactory.Rect("Column", safe);
        UIFactory.Stretch(column);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(column, 28f, false);
        stack.padding = new RectOffset((int)Margin, (int)Margin, 22, 22);

        // Back, the panel's name, and room on the right so the name sits centred.
        RectTransform head = UIFactory.Rect("Head", column);
        UIFactory.Size(head.gameObject, 133f);
        HorizontalLayoutGroup headRow = UIFactory.HorizontalStack(head, 22f);
        headRow.childAlignment = TextAnchor.MiddleCenter;

        RectTransform backRect = UIFactory.Rect("Back", head);
        UIFactory.Size(backRect.gameObject, 122f, 122f).flexibleWidth = 0f;
        Button back = UIFactory.Button(backRect, ThemeRole.Fill, 1f, UIShape.Circle);
        Icon(backRect, AppIcon.ChevronLeft, 55f, ThemeRole.TextPrimary);
        back.gameObject.AddComponent<WayOut>();
        Wire(back.onClick, view.Close, "Close the teacher panel");
        view.closeButton = back;

        TextMeshProUGUI eyebrow = UIFactory.Text("Eyebrow", head, Body, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Center, "teacher.eyebrow", FontStyles.Bold);
        UIFactory.Flexible(eyebrow.gameObject);
        eyebrow.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform spacer = UIFactory.Rect("Spacer", head);
        UIFactory.Size(spacer.gameObject, 122f, 122f).flexibleWidth = 0f;

        view.titleLabel = UIFactory.Text("TrailName", column, Display, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        view.titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.titleLabel.overflowMode = TextOverflowModes.Ellipsis;
        UIFactory.Size(view.titleLabel.gameObject, 100f);

        view.stateLabel = UIFactory.Text("State", column, Secondary, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.stateLabel);

        view.readyLabel = UIFactory.Text("Ready", column, Secondary, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.readyLabel);

        view.startButton = Pill(column, "Start", "teacher.start", ThemeRole.Accent, ThemeRole.OnAccent, Btn, Body);

        BuildTabs(column, view);

        // The two lists, one shown at a time, taking the rest of the height.
        view.lobbyContent = TeacherList(column, "Lobby", out GameObject lobbyRoot);
        view.lobbyList = lobbyRoot;

        GameObject lobbyEmpty = StateNote(view.lobbyContent, "Empty", "teacher.lobbyEmpty",
                                          "teacher.lobbyEmptyBody", out TextMeshProUGUI lobbyTitle);
        DropLocalizer(lobbyTitle);
        view.lobbyEmpty = lobbyEmpty;
        view.lobbyEmptyTitle = lobbyTitle;
        Transform lobbyBody = lobbyEmpty.transform.Find("Body");
        view.lobbyEmptyBody = lobbyBody != null ? lobbyBody.gameObject : null;

        view.boardContent = TeacherList(column, "Board", out GameObject boardRoot);
        view.boardList = boardRoot;
        view.boardEmpty = StateNote(view.boardContent, "Empty", "teacher.boardEmpty", "teacher.boardEmptyBody");
        boardRoot.SetActive(false);

        // Templates, cloned at run time.
        RectTransform templates = UIFactory.Rect("Templates", panel);
        UIFactory.Stretch(templates);
        view.teamTemplate = TeamTemplate(templates);
        view.playerTemplate = PlayerTemplate(templates);
        view.boardTemplate = BoardTemplate(templates);
        templates.gameObject.SetActive(false);

        BuildConfirm(panel, view);

        panel.gameObject.SetActive(false);
    }

    static void BuildTabs(Transform parent, TeacherPanelView view)
    {
        RectTransform track = UIFactory.Rect("Tabs", parent);
        UIFactory.Size(track.gameObject, 122f);
        UIFactory.Fill(track, ThemeRole.Fill, 1f, UIShape.Pill).raycastTarget = false;
        HorizontalLayoutGroup row = UIFactory.HorizontalStack(track, 0f, true);
        row.padding = new RectOffset(8, 8, 8, 8);
        row.childForceExpandHeight = true;

        view.lobbyTab = TabHalf(track, "LobbyTab", out view.lobbyTabFace, out view.lobbyTabLabel);
        view.boardTab = TabHalf(track, "BoardTab", out view.boardTabFace, out view.boardTabLabel);
    }

    static Button TabHalf(Transform parent, string name, out Image face, out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Flexible(rt.gameObject);

        // Coloured by the view (selected: the surface; the other: clear).
        face = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        UIFactory.Feedback(button);

        label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextPrimary, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Unthemed(label.gameObject);
        return button;
    }

    static RectTransform TeacherList(Transform parent, string name, out GameObject root)
    {
        RectTransform content = UIFactory.ScrollList(name, parent, 33f);
        content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 11, (int)(Margin * 2f));

        root = content.parent.parent.gameObject;
        LayoutElement grow = root.AddComponent<LayoutElement>();
        grow.flexibleHeight = 1f;
        grow.minHeight = 0f;
        return content;
    }

    /// <summary>A capsule button that fits its label, wired at run time by its view.</summary>
    static Button Pill(Transform parent, string name, string key, ThemeRole fill, ThemeRole ink,
                       float height, float size)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, height);
        Button button = UIFactory.Button(rt, fill, 1f, UIShape.Pill);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, size, ink, TextAlignmentOptions.Center, key);
        UIFactory.Stretch(label.rectTransform);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return button;
    }

    /// <summary>A small capsule that sizes to its words: Kick, Ban, Disqualify.</summary>
    static Button MiniPill(Transform parent, string name, string key, ThemeRole ink)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, 122f).flexibleWidth = 0f;
        Button button = UIFactory.Button(rt, ThemeRole.Fill, 1f, UIShape.Pill);

        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(rt, 0f);
        layout.padding = new RectOffset(36, 36, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Secondary, ink, TextAlignmentOptions.Center, key);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        LayoutElement size = UIFactory.Size(label.gameObject, 122f);
        size.preferredWidth = -1f;
        return button;
    }

    static GameObject TeamTemplate(Transform parent)
    {
        RectTransform card = UIFactory.Rect("TeamCard", parent);
        UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 0f, false);
        stack.padding = new RectOffset(44, 44, 11, 22);

        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.Size(head.gameObject, 166f);
        HorizontalLayoutGroup row = UIFactory.HorizontalStack(head, 22f);
        row.childAlignment = TextAnchor.MiddleLeft;

        RectTransform text = UIFactory.Rect("Text", head);
        UIFactory.VerticalStack(text, 4f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        TextMeshProUGUI name = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(name.gameObject, 58f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;

        // Recoloured by the view: muted, or red for a disqualified team.
        TextMeshProUGUI meta = UIFactory.Text("Meta", text, Secondary, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Left);
        UIFactory.Size(meta.gameObject, 50f);
        meta.textWrappingMode = TextWrappingModes.NoWrap;
        meta.overflowMode = TextOverflowModes.Ellipsis;
        Unthemed(meta.gameObject);

        MiniPill(head, "Disqualify", "teacher.dq", ThemeRole.StatusPoor);

        RectTransform members = UIFactory.Rect("Members", card);
        UIFactory.VerticalStack(members, 0f, false);

        return card.gameObject;
    }

    static GameObject PlayerTemplate(Transform parent)
    {
        RectTransform row = UIFactory.Rect("PlayerRow", parent);
        UIFactory.Size(row.gameObject, 144f);

        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);

        HorizontalLayoutGroup line = UIFactory.HorizontalStack(row, 22f);
        line.childAlignment = TextAnchor.MiddleLeft;

        // A plain disc the view colours: green when the phone said GPS works.
        Disc(row, 30f, ThemeRole.FillStrong, UIShape.Circle, out RectTransform _);

        TextMeshProUGUI name = UIFactory.Text("Name", row, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Left);
        UIFactory.Flexible(name.gameObject);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;

        MiniPill(row, "Kick", "teacher.kick", ThemeRole.TextPrimary);
        MiniPill(row, "Ban", "teacher.ban", ThemeRole.StatusPoor);

        return row.gameObject;
    }

    static GameObject BoardTemplate(Transform parent)
    {
        RectTransform row = UIFactory.Rect("BoardRow", parent);
        UIFactory.Size(row.gameObject, 177f);
        UIFactory.Fill(row, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;
        HorizontalLayoutGroup line = UIFactory.HorizontalStack(row, 33f);
        line.padding = new RectOffset(44, 44, 0, 0);
        line.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI rank = UIFactory.Text("Rank", row, Title, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Size(rank.gameObject, 100f, 90f).flexibleWidth = 0f;

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 4f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        TextMeshProUGUI name = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(name.gameObject, 58f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI meta = UIFactory.Text("Meta", text, Secondary, ThemeRole.TextMuted, TextAlignmentOptions.Left);
        UIFactory.Size(meta.gameObject, 50f);

        TextMeshProUGUI points = UIFactory.Text("Points", row, Body, ThemeRole.AccentText,
                                                TextAlignmentOptions.Right, null, FontStyles.Bold);
        UIFactory.Size(points.gameObject, 100f, 240f).flexibleWidth = 0f;
        points.textWrappingMode = TextWrappingModes.NoWrap;

        return row.gameObject;
    }

    /// <summary>
    /// The one confirmation every destructive action goes through: a title
    /// naming who or what, what it does, a red button, and Cancel (which the
    /// back button and a tap outside also press).
    /// </summary>
    static void BuildConfirm(RectTransform panel, TeacherPanelView view)
    {
        RectTransform group = UIFactory.Rect("Confirm", panel);
        UIFactory.Stretch(group);
        view.confirmGroup = group.gameObject;

        RectTransform scrim = UIFactory.Rect("Scrim", group);
        UIFactory.Stretch(scrim);
        Image dim = scrim.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.4f);
        Button outside = scrim.gameObject.AddComponent<Button>();
        outside.targetGraphic = dim;
        outside.transition = Selectable.Transition.None;
        Wire(outside.onClick, view.ConfirmNo, "Tap outside the confirmation");

        RectTransform card = UIFactory.Rect("Card", group);
        card.anchorMin = new Vector2(0f, 0.5f);
        card.anchorMax = new Vector2(1f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.offsetMin = new Vector2(Margin, 0f);
        card.offsetMax = new Vector2(-Margin, 0f);
        UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 33f);
        stack.padding = new RectOffset((int)SheetPad, (int)SheetPad, (int)SheetPad, 22);

        view.confirmTitle = UIFactory.Text("Title", card, Title, ThemeRole.TextPrimary,
                                           TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.confirmTitle);

        view.confirmBody = UIFactory.Text("Body", card, Body, ThemeRole.TextSecondary, TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.confirmBody);

        RectTransform actionRect = UIFactory.Rect("Action", card);
        UIFactory.Size(actionRect.gameObject, Btn);
        Button action = UIFactory.Button(actionRect, ThemeRole.BadDot, 1f, UIShape.Pill);
        Wire(action.onClick, view.ConfirmYes, "Confirm the teacher action");
        view.confirmAction = action;
        view.confirmActionLabel = UIFactory.Text("Label", actionRect, Body, ThemeRole.OnAccent,
                                                 TextAlignmentOptions.Center);
        UIFactory.Stretch(view.confirmActionLabel.rectTransform);
        view.confirmActionLabel.textWrappingMode = TextWrappingModes.NoWrap;

        Button cancel = QuietAction(card, "Cancel", "teacher.cancel", view.ConfirmNo, "Cancel the teacher action");
        cancel.gameObject.AddComponent<WayOut>();
        view.confirmCancel = cancel;

        group.gameObject.SetActive(false);
    }

    // ----------------------------------------------------- the waiting screen

    static void BuildWaitingScreen(RectTransform holder, WaitingView view)
    {
        RectTransform panel = UIFactory.Rect("WaitingScreen", holder);
        UIFactory.Stretch(panel);
        UIFactory.Fill(panel, ThemeRole.Background).raycastTarget = true;
        AddFade(panel.gameObject);
        view.panel = panel.gameObject;

        RectTransform safe = SafeContent(panel, padBottom: true);

        RectTransform column = UIFactory.Rect("Column", safe);
        UIFactory.Stretch(column);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(column, 44f, false);
        stack.padding = new RectOffset((int)(Margin * 1.5f), (int)(Margin * 1.5f), 120, (int)Margin);
        stack.childAlignment = TextAnchor.MiddleCenter;

        Image clock = Icon(column, AppIcon.Clock, 150f, ThemeRole.AccentText);
        clock.preserveAspect = true;

        TextMeshProUGUI title = UIFactory.Text("Title", column, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, "wait.title", FontStyles.Bold);
        UIFactory.FitsItsText(title);

        TextMeshProUGUI body = UIFactory.Text("Body", column, Body, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.Center, "wait.body");
        UIFactory.FitsItsText(body);

        RectTransform team = Group(column, "Team");
        team.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(44, 44, 22, 22);

        view.teamLabel = UIFactory.Text("TeamName", team, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.teamLabel.gameObject, 122f);
        view.teamLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.teamLabel.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI none = UIFactory.Text("NoTeam", team, Secondary, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Left, "wait.noTeamBody");
        UIFactory.FitsItsText(none);
        view.noTeamNote = none.gameObject;

        view.memberRows = new GameObject[WaitingView.MaxMembers];
        view.memberNames = new TextMeshProUGUI[WaitingView.MaxMembers];
        view.memberDots = new Image[WaitingView.MaxMembers];

        for (int i = 0; i < WaitingView.MaxMembers; i++)
        {
            RectTransform row = UIFactory.Rect("Member" + i, team);
            UIFactory.Size(row.gameObject, 122f);
            HorizontalLayoutGroup line = UIFactory.HorizontalStack(row, 22f);
            line.childAlignment = TextAnchor.MiddleLeft;

            view.memberDots[i] = Disc(row, 30f, ThemeRole.FillStrong, UIShape.Circle, out RectTransform _);

            TextMeshProUGUI name = UIFactory.Text("Name", row, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Left);
            UIFactory.Flexible(name.gameObject);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            view.memberNames[i] = name;

            view.memberRows[i] = row.gameObject;
            row.gameObject.SetActive(false);
        }

        view.teamButton = Pill(column, "TeamButton", null, ThemeRole.Accent, ThemeRole.OnAccent, Btn, Body);
        view.teamButtonLabel = view.teamButton.GetComponentInChildren<TextMeshProUGUI>(true);
        view.teamButtonLabel.text = AppStrings.Get("wait.makeTeam");

        RectTransform laterRect = UIFactory.Rect("Later", column);
        UIFactory.Size(laterRect.gameObject, Tap);
        Button later = UIFactory.Button(laterRect, ThemeRole.Surface, 0f);
        TextMeshProUGUI laterLabel = UIFactory.Text("Label", laterRect, Body, ThemeRole.AccentText,
                                                    TextAlignmentOptions.Center, "wait.later");
        UIFactory.Stretch(laterLabel.rectTransform);
        later.gameObject.AddComponent<WayOut>();
        view.laterButton = later;

        panel.gameObject.SetActive(false);
    }
}
