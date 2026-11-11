using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round 3, agent L: the account door as two screens (sign-in, and the A5
/// sign-up with the living stage over it), the stage itself, and the focused
/// state every text field gets. Called from the main build in AppUIBuilder.cs.
///
/// Sizes are the A5 phone design's CSS pixels times <see cref="Css"/>: the
/// design is 390 wide and the reference canvas 1080.
/// </summary>
public static partial class AppUIBuilder
{
    /// <summary>One CSS pixel of the 390-wide phone design, in reference units.</summary>
    const float Css = 1080f / 390f;

    /// <summary>The stage's height on the sign-up screen: the design's 392px hero.</summary>
    const float SignUpStageHeight = 1086f;

    /// <summary>How far the form's sheet rides up over the bottom of the stage (24 CSS px).</summary>
    const float StageSheetOverlap = 66f;

    /// <summary>Sky left under the scene, so the sheet never covers the ground (34 CSS px).</summary>
    const float StageGround = 94f;

    /// <summary>A sign-up text box: 44 CSS px.</summary>
    const float AuthFieldHeight = 122f;

    const string StageArtFolder = "Assets/Art/Stage";

    /// <summary>The scenes in RoleStage.KeyIndex order.</summary>
    static readonly string[] StageSceneKeys = { "guide", "teacher", "broker", "other", "viewer" };

    // ================================================================ the door

    /// <summary>
    /// The account door: sign-up (the A5 phone layout, stage over a sheet) and
    /// sign-in, two screens under one AccountSectionView. Replaces
    /// BuildAccountDoor. DoorFlow's title, guest line and Later label are the
    /// sign-up screen's, because the guest warning is an offer to make one.
    ///
    /// Which screen opens is AccountSectionView.Next: sign-up unless somebody
    /// asked for sign-in (AccountSectionView.OpenSignIn).
    /// </summary>
    static GameObject BuildAuthDoor(Transform parent, DoorFlow flow)
    {
        RectTransform panel = UIFactory.Rect("Account", parent);
        UIFactory.Stretch(panel);

        // The sheet's own colour, edge to edge: a whole screen, not a card on
        // the blurred map. A raycast target, so nothing behind it hears a tap.
        UIFactory.Fill(panel, ThemeRole.Surface).raycastTarget = true;
        AddFade(panel.gameObject, 22f);

        AccountSectionView view = panel.gameObject.AddComponent<AccountSectionView>();
        view.startScreen = AccountSectionView.AuthScreen.SignUp;

        RectTransform guest = UIFactory.Rect("Guest", panel);
        UIFactory.Stretch(guest);
        view.anonymousGroup = guest.gameObject;

        BuildSignUpScreen(guest, flow, view);
        BuildSignInScreen(guest, flow, view);

        // Sign-up is the one on screen until the view says otherwise.
        view.signInGroup.SetActive(false);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// The account card for Profile's "Name and email" page: the signed-in half
    /// (email, name, Save, whatever the caller adds, Sign out), and for a guest
    /// two buttons that open the door on sign-up or sign-in. Replaces
    /// BuildAccountCard, which carried the merged form.
    /// </summary>
    static AccountSectionView BuildAuthCard(Transform content, bool label = true,
                                            System.Action<Transform> signedInExtras = null)
    {
        if (label)
            SectionLabel(content, "settings.account");

        RectTransform card = UIFactory.Rect("AccountCard", content);
        UIFactory.Card(card, 34f, 20f);

        AccountSectionView view = card.gameObject.AddComponent<AccountSectionView>();

        // ------------------------------------------------------- signed in
        RectTransform inGroup = UIFactory.Rect("SignedIn", card);
        UIFactory.VerticalStack(inGroup, 16f);
        view.signedInGroup = inGroup.gameObject;

        UIFactory.Text("Caption", inGroup, Caption, ThemeRole.TextMuted,
                       TextAlignmentOptions.Left, "account.signedInAs");

        view.emailLabel = UIFactory.Text("Email", inGroup, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.emailLabel.gameObject, 52f);

        // Changeable afterwards, because a name chosen in a hurry at sign-up is
        // the one that ends up on a screen in front of a room.
        UIFactory.Text("NameLabel", inGroup, Caption, ThemeRole.TextMuted,
                       TextAlignmentOptions.Left, "account.name");
        view.signedInNameField = Field(inGroup, "Name", "account.name");
        AddFieldFocus(view.signedInNameField);
        view.saveNameButton = FullWidthButton(inGroup, "SaveName", "account.saveName",
                                              ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        if (signedInExtras != null)
            signedInExtras(inGroup);

        view.signOutButton = FullWidthButton(inGroup, "SignOut", "account.signOut",
                                             ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ------------------------------------------------------- a guest
        // No form here: the door has both, with room for the stage. A guest
        // who lands on this page is pointed at it.
        RectTransform outGroup = UIFactory.Rect("Anonymous", card);
        UIFactory.VerticalStack(outGroup, 16f);
        view.anonymousGroup = outGroup.gameObject;

        TextMeshProUGUI none = UIFactory.Text("None", outGroup, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, "account.none",
                                              FontStyles.Bold);
        UIFactory.FitsItsText(none);

        TextMeshProUGUI body = UIFactory.Text("Body", outGroup, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.Left, "account.noneBody");
        UIFactory.FitsItsText(body);

        view.openSignUpButton = FullWidthButton(outGroup, "Create", "account.createProfile",
                                                ThemeRole.Accent, ThemeRole.OnAccent);
        view.openSignInButton = FullWidthButton(outGroup, "SignIn", "account.signInShort",
                                                ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ----------------------------------------------------------- status
        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 60f);
        view.statusLabel.text = string.Empty;
        view.statusLabel.gameObject.SetActive(false);

        return view;
    }

    // ============================================================== sign-up

    /// <summary>
    /// A5 on a phone: the stage across the top, and the form in a sheet with
    /// 26px top corners riding 24px up over it. Everything scrolls together,
    /// so "Make an account" is always reachable on a short phone.
    /// </summary>
    static void BuildSignUpScreen(Transform parent, DoorFlow flow, AccountSectionView view)
    {
        RectTransform screen = UIFactory.Rect("SignUp", parent);
        UIFactory.Stretch(screen);
        view.signUpGroup = screen.gameObject;

        RectTransform content = UIFactory.ScrollList("Scroll", screen, 0f);

        // Negative: the sheet is the next item and starts above the stage's end.
        content.GetComponent<VerticalLayoutGroup>().spacing = -StageSheetOverlap;

        view.stage = BuildRoleStage(content, SignUpStageHeight);

        // ---------------------------------------------------------- the sheet
        RectTransform sheet = UIFactory.Rect("Sheet", content);
        VerticalLayoutGroup sheetStack = UIFactory.VerticalStack(sheet, 28f, false);
        sheetStack.padding = new RectOffset(44, 44, 50, 198);

        // Its face: the card sprite's rounded top, run on far past the bottom
        // so its lower corners never show, whatever the scroll.
        RectTransform face = UIFactory.Rect("Face", sheet);
        UIFactory.Stretch(face);
        face.offsetMin = new Vector2(0f, -600f);
        UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;
        Ignore(face);

        // DoorFlow writes these: "Make an account", or the guest warning's
        // title and its line about where a guest's walk is kept.
        flow.accountTitle = UIFactory.Text("Title", sheet, Title, ThemeRole.TextPrimary,
                                           TextAlignmentOptions.TopLeft, "accountDoor.title",
                                           FontStyles.Bold);
        UIFactory.FitsItsText(flow.accountTitle);
        DropLocalizer(flow.accountTitle);

        TextMeshProUGUI guestLine = UIFactory.Text("GuestNote", sheet, Secondary, ThemeRole.TextSecondary,
                                                   TextAlignmentOptions.TopLeft, "guest.body");
        UIFactory.FitsItsText(guestLine);
        flow.guestNote = guestLine.gameObject;
        guestLine.gameObject.SetActive(false);

        // Name and email side by side, the password under both, as A5 has them.
        RectTransform pair = UIFactory.Rect("NameEmail", sheet);
        UIFactory.Size(pair.gameObject, AuthFieldHeight);
        UIFactory.HorizontalStack(pair, 22f, true);

        view.nameField = AuthField(pair, "Name", "account.name");
        LayoutElement nameSize = view.nameField.GetComponent<LayoutElement>();
        nameSize.minWidth = 0f;
        nameSize.preferredWidth = 1f;
        nameSize.flexibleWidth = 1f;
        // Above the field's own layout size, which is its text's width: typing
        // an email would otherwise squeeze the name to a sliver.
        nameSize.layoutPriority = 2;

        view.emailField = AuthField(pair, "Email", "account.email");
        view.emailField.contentType = TMP_InputField.ContentType.EmailAddress;
        LayoutElement emailSize = view.emailField.GetComponent<LayoutElement>();
        emailSize.minWidth = 0f;
        emailSize.preferredWidth = 1f;
        emailSize.flexibleWidth = 1f;
        emailSize.layoutPriority = 2;

        view.passwordField = AuthField(sheet, "Password", "account.password");
        view.passwordField.contentType = TMP_InputField.ContentType.Password;

        view.roleChoice = AuthRoleQuestion(sheet, out view.roleError, out view.workChoice,
                                           out view.workError);

        view.statusLabel = UIFactory.Text("Status", sheet, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.statusLabel);
        view.statusLabel.text = string.Empty;
        view.statusLabel.gameObject.SetActive(false);

        view.signUpButton = FullWidthButton(sheet, "Create", "account.create",
                                            ThemeRole.Accent, ThemeRole.OnAccent);

        view.resendButton = AuthLink(sheet, "Resend", "account.resend");
        view.resendButton.gameObject.SetActive(false);

        view.toSignInButton = AuthLink(sheet, "ToSignIn", "auth.toSignIn");

        Button later = QuietAction(sheet, "Later", "trails.notNow", flow.AccountDone,
                                   "account later");
        later.gameObject.AddComponent<WayOut>();
        flow.accountLaterLabel = later.GetComponentInChildren<TextMeshProUGUI>();
        DropLocalizer(flow.accountLaterLabel);
        view.laterLabel = flow.accountLaterLabel;
    }

    /// <summary>
    /// "How will you use AR Trail?" as A5 asks it: two tiles side by side, then
    /// for a creator the four lines of work as a 2×2 grid, and for a viewer the
    /// note on why they are not asked. Drives the same RoleChoiceView and
    /// WorkChoiceView the work sheet uses; a tile's ring is its edge.
    /// </summary>
    static RoleChoiceView AuthRoleQuestion(Transform parent, out TextMeshProUGUI roleError,
                                           out WorkChoiceView work, out TextMeshProUGUI workError)
    {
        RectTransform group = UIFactory.Rect("RoleQuestion", parent);
        UIFactory.VerticalStack(group, 22f, false);

        TextMeshProUGUI question = UIFactory.Text("RoleLabel", group, 42f, ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.BottomLeft, "role.question",
                                                  FontStyles.Bold);
        UIFactory.FitsItsText(question);

        RectTransform cards = UIFactory.Rect("RoleChoice", group);
        UIFactory.Size(cards.gameObject, 133f);
        // Tiles the row's full height, not their words' height.
        UIFactory.HorizontalStack(cards, 22f, true).childForceExpandHeight = true;
        RoleChoiceView view = cards.gameObject.AddComponent<RoleChoiceView>();

        int n = RoleChoiceView.Keys.Length;
        view.options = new Button[n];
        view.radios = new Image[n];
        view.holes = new RectTransform[n];
        view.edges = new Image[n];

        for (int i = 0; i < n; i++)
        {
            string role = RoleChoiceView.Keys[i];
            bool creator = role == AccountRole.Creator;
            view.options[i] = AuthTile(cards, creator ? "Creator" : "Viewer",
                                       creator ? "pencil" : "viewer", "role." + role,
                                       out view.edges[i]);
        }

        roleError = UIFactory.Text("RoleError", group, Secondary, ThemeRole.StatusPoor,
                                   TextAlignmentOptions.Left, "err.role");
        UIFactory.FitsItsText(roleError);
        roleError.gameObject.SetActive(false);

        // The viewer's note, on a quiet card of its own.
        RectTransform note = UIFactory.Rect("ViewerNote", group);
        VerticalLayoutGroup noteStack = UIFactory.VerticalStack(note, 0f, false);
        noteStack.padding = new RectOffset(33, 33, 28, 28);
        UIFactory.Fill(note, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        TextMeshProUGUI noteText = UIFactory.Text("Text", note, 36f, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.TopLeft, "role.viewerNote");
        UIFactory.FitsItsText(noteText);
        view.viewerNote = note.gameObject;

        // The four lines of work, two by two, in LineOfWork.Keys order.
        RectTransform workGroup = UIFactory.Rect("Work", group);
        UIFactory.VerticalStack(workGroup, 22f, false);
        view.workGroup = workGroup.gameObject;

        TextMeshProUGUI workLabel = UIFactory.Text("WorkLabel", workGroup, Secondary, ThemeRole.TextMuted,
                                                   TextAlignmentOptions.BottomLeft, "work.title");
        UIFactory.FitsItsText(workLabel);

        RectTransform grid = UIFactory.Rect("WorkChoice", workGroup);
        UIFactory.VerticalStack(grid, 22f, false);
        work = grid.gameObject.AddComponent<WorkChoiceView>();

        int jobs = LineOfWork.Keys.Length;
        work.options = new Button[jobs];
        work.radios = new Image[jobs];
        work.holes = new RectTransform[jobs];
        work.edges = new Image[jobs];

        RectTransform row = null;
        for (int i = 0; i < jobs; i++)
        {
            if (i % 2 == 0)
            {
                row = UIFactory.Rect("Row" + (i / 2), grid);
                UIFactory.Size(row.gameObject, 127f);
                UIFactory.HorizontalStack(row, 22f, true).childForceExpandHeight = true;
            }

            // The role word, as A5 labels the tiles: Teacher, Tour guide,
            // Estate broker - and "Other" rather than "Every option".
            string key = LineOfWork.Keys[i];
            string labelKey = key == "other" ? WorkChoiceView.HeaderKey(key)
                                             : WorkChoiceView.SubtextKey(key);
            work.options[i] = AuthTile(row, char.ToUpperInvariant(key[0]) + key.Substring(1),
                                       key, labelKey, out work.edges[i]);
        }

        // An odd count leaves the last tile at full width otherwise.
        if (jobs % 2 == 1 && row != null)
        {
            RectTransform spacer = UIFactory.Rect("Spacer", row);
            LayoutElement room = spacer.gameObject.AddComponent<LayoutElement>();
            room.minWidth = 0f;
            room.preferredWidth = 1f;
            room.flexibleWidth = 1f;
        }

        workError = UIFactory.Text("WorkError", workGroup, Secondary, ThemeRole.StatusPoor,
                                   TextAlignmentOptions.Left, "err.work");
        UIFactory.FitsItsText(workError);
        workError.gameObject.SetActive(false);

        // Nothing chosen yet: neither follow-up is on screen.
        view.viewerNote.SetActive(false);
        view.workGroup.SetActive(false);
        return view;
    }

    /// <summary>
    /// One A5 choice tile: a glyph and a word on the quiet card fill, with a
    /// ring the choice views paint (hairline, or the accent when chosen).
    /// Shares its row's width with its neighbour.
    /// </summary>
    static Button AuthTile(Transform parent, string name, string glyph, string labelKey,
                           out Image edge)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        LayoutElement size = rt.gameObject.AddComponent<LayoutElement>();
        size.minWidth = 0f;
        size.preferredWidth = 1f;
        size.flexibleWidth = 1f;

        Button button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded);

        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(rt, 22f);
        layout.padding = new RectOffset(28, 22, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform edgeRect = UIFactory.Rect("Edge", rt);
        UIFactory.Stretch(edgeRect);
        edge = UIFactory.Plain(edgeRect, ThemeManager.Get(ThemeRole.Line));
        edge.sprite = UISprites.RoundedOutline;
        edge.type = Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1f;
        edge.raycastTarget = false;
        Ignore(edgeRect);

        RectTransform glyphRect = UIFactory.Rect("Glyph", rt);
        UIFactory.Size(glyphRect.gameObject, 50f, 50f).flexibleWidth = 0f;
        Image icon = UIFactory.Fill(glyphRect, ThemeRole.TextPrimary);
        icon.sprite = StageSprite("icon-" + glyph);
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        if (icon.sprite == null)
            icon.enabled = false;

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, labelKey, FontStyles.Bold);
        UIFactory.FitsItsText(label);
        label.raycastTarget = false;
        LayoutElement words = label.gameObject.AddComponent<LayoutElement>();
        words.minWidth = 0f;
        words.preferredWidth = 1f;
        words.flexibleWidth = 1f;

        return button;
    }

    // ============================================================== sign-in

    /// <summary>
    /// Sign-in: a large "Sign in", a line on what account this is, email,
    /// password, the button, and the way over to sign-up. Nothing else - the
    /// role and the line of work belong to the account already.
    /// </summary>
    static void BuildSignInScreen(Transform parent, DoorFlow flow, AccountSectionView view)
    {
        RectTransform screen = UIFactory.Rect("SignIn", parent);
        UIFactory.Stretch(screen);
        view.signInGroup = screen.gameObject;

        RectTransform safe = SafeContent(screen, padBottom: true);

        RectTransform content = UIFactory.ScrollList("Scroll", safe, 33f);
        content.GetComponent<VerticalLayoutGroup>().padding =
            new RectOffset((int)SheetPad, (int)SheetPad, 154, 132);

        TextMeshProUGUI title = UIFactory.Text("Title", content, Large, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, "account.signIn",
                                               FontStyles.Bold);
        UIFactory.FitsItsText(title);

        TextMeshProUGUI body = UIFactory.Text("Body", content, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "auth.signInBody");
        UIFactory.FitsItsText(body);

        view.signInEmailField = AuthField(content, "Email", "account.email");
        view.signInEmailField.contentType = TMP_InputField.ContentType.EmailAddress;

        view.signInPasswordField = AuthField(content, "Password", "account.password");
        view.signInPasswordField.contentType = TMP_InputField.ContentType.Password;

        view.signInStatusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                                TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.signInStatusLabel);
        view.signInStatusLabel.text = string.Empty;
        view.signInStatusLabel.gameObject.SetActive(false);

        view.signInButton = FullWidthButton(content, "SignIn", "account.signIn",
                                            ThemeRole.Accent, ThemeRole.OnAccent);

        view.toSignUpButton = AuthLink(content, "ToSignUp", "auth.toSignUp");

        // Said before it happens, not after: there is no correct merge of what
        // a guest walked into an account that already has its own history.
        TextMeshProUGUI warn = UIFactory.Text("Merge", content, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft, "account.mergeWarning");
        UIFactory.FitsItsText(warn);

        Button later = QuietAction(content, "Later", "trails.notNow", flow.AccountDone,
                                   "account later (sign-in)");
        later.gameObject.AddComponent<WayOut>();
        view.signInLaterLabel = later.GetComponentInChildren<TextMeshProUGUI>();
        DropLocalizer(view.signInLaterLabel);
    }

    // ============================================================ small parts

    /// <summary>
    /// An A5 text box: 44 CSS px tall, the sheet's own white with a hairline
    /// edge, and the focused state.
    /// </summary>
    static TMP_InputField AuthField(Transform parent, string name, string placeholderKey)
    {
        TMP_InputField field = UIFactory.InputField(name, parent, placeholderKey, out _);
        UIFactory.Size(field.gameObject, AuthFieldHeight);

        if (field.textViewport != null)
        {
            field.textViewport.offsetMin = new Vector2(33f, 8f);
            field.textViewport.offsetMax = new Vector2(-33f, -8f);
        }

        AddFieldFocus(field);
        return field;
    }

    /// <summary>
    /// A link-styled button: the accent's text colour, no box, a full tap
    /// target. Wired by its view at runtime, so nothing is wired here.
    /// </summary>
    static Button AuthLink(Transform parent, string name, string key)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0f);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ThemeRole.AccentText,
                                               TextAlignmentOptions.Center, key);
        UIFactory.Stretch(label.rectTransform);
        label.raycastTarget = false;

        return button;
    }

    // ================================================================ focus

    /// <summary>
    /// Gives one text field its focused state (<see cref="FieldFocus"/>): a
    /// hairline at rest; focused, a 2px blue edge, a faint blue wash and a 3px
    /// caret blinking at 0.85s. Safe to call twice. Pill-shaped fields get a
    /// pill edge.
    /// </summary>
    public static FieldFocus AddFieldFocus(TMP_InputField field)
    {
        if (field == null)
            return null;

        FieldFocus focus = field.GetComponent<FieldFocus>();
        if (focus != null)
            return focus;

        Image fill = field.targetGraphic as Image;
        if (fill == null)
            fill = field.GetComponent<Image>();

        bool pill = fill != null && fill.sprite == UISprites.Pill;

        focus = field.gameObject.AddComponent<FieldFocus>();
        focus.fill = fill;

        ThemedElement themed = field.GetComponent<ThemedElement>();
        focus.fillRole = themed != null ? themed.role : ThemeRole.Surface;

        // Inner first, then outer, each sent to the front of the child list:
        // both draw over the field's fill and under its text.
        focus.edgeInner = FocusRing(field.transform, "FocusEdgeInner", pill, 2.5f);
        focus.edge = FocusRing(field.transform, "FocusEdge", pill, 0f);

        field.customCaretColor = true;
        field.caretColor = FieldFocus.Accent;
        field.caretWidth = FieldFocus.CaretWidth;
        field.caretBlinkRate = FieldFocus.CaretBlinkRate;

        focus.Paint(false);
        return focus;
    }

    /// <summary>
    /// Every text field under <paramref name="root"/> gets its focused state,
    /// except the code door's hidden field (its six boxes are its picture) and
    /// any field drawn nowhere. Call once, at the end of the build, on the
    /// Canvas. Returns how many fields it changed.
    /// </summary>
    public static int AddFieldFocusToAll(Transform root)
    {
        if (root == null)
            return 0;

        int changed = 0;
        TMP_InputField[] fields = root.GetComponentsInChildren<TMP_InputField>(true);

        for (int i = 0; i < fields.Length; i++)
        {
            TMP_InputField field = fields[i];

            if (field.GetComponent<FieldFocus>() != null)
                continue;

            if (field.GetComponentInParent<CodeBoxes>(true) != null)
                continue;

            Graphic fill = field.targetGraphic;
            if (fill != null && fill.color.a < 0.01f)
                continue;

            AddFieldFocus(field);
            changed++;
        }

        return changed;
    }

    /// <summary>One outline ring stretched over a field, inset by a few units.</summary>
    static Image FocusRing(Transform field, string name, bool pill, float inset)
    {
        RectTransform rt = UIFactory.Rect(name, field);
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        rt.SetAsFirstSibling();

        Image ring = UIFactory.Plain(rt, FieldFocus.Hairline);
        ring.sprite = pill ? UISprites.PillOutline : UISprites.RoundedOutline;
        ring.type = Image.Type.Sliced;

        // The inset ring's corners a little tighter, so they follow the outer
        // ring's instead of bulging out at the corners.
        ring.pixelsPerUnitMultiplier = pill ? 1f : 44f / Mathf.Max(1f, 44f - inset);
        ring.raycastTarget = false;

        if (pill)
            rt.gameObject.AddComponent<PillShape>();

        Ignore(rt);
        return ring;
    }

    // ================================================================ stage

    /// <summary>
    /// The A5 stage (RoleStage) as a hero <paramref name="height"/> tall and
    /// the full width of its parent: the sky, sun or moon and stars, a drifting
    /// cloud, the chip, the headline, three sign pills, and at the bottom the
    /// role's scene with the trail, two pins and the moment over it. Laid out
    /// from A5Phone at 2.77 reference px per CSS px; 94px of sky under the
    /// scene. Works inside a layout group (it carries its height) or outside
    /// one (it anchors to the parent's top). The sign-up uses it at 1086; the
    /// work sheet can reuse it at any height from about 900.
    /// </summary>
    public static RoleStage BuildRoleStage(Transform parent, float height)
    {
        RectTransform root = UIFactory.Rect("RoleStage", parent);
        UIFactory.TopStretch(root, height);
        UIFactory.Size(root.gameObject, height);
        root.gameObject.AddComponent<RectMask2D>();

        RoleStage stage = root.gameObject.AddComponent<RoleStage>();

        // ---- the sky: painted by the stage, so nothing in it is themed
        RectTransform skyRect = UIFactory.Rect("Sky", root);
        UIFactory.Stretch(skyRect);
        stage.sky = UIFactory.Plain(skyRect, RoleStage.Sky("none", ThemeManager.IsDark));
        stage.sky.raycastTarget = false;

        // Top right, as A5 has them: the sun's disc at (336, 70) r28, the moon
        // 4px to its left, three stars, the cloud at (240, 150) 110x36.
        stage.sun = StageImage(root, "Sun", "sun", new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                               new Vector2(-54f * Css, -70f * Css), new Vector2(84f * Css, 84f * Css));
        stage.moon = StageImage(root, "Moon", "moon", new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                                new Vector2(-58f * Css, -70f * Css), new Vector2(84f * Css, 84f * Css));
        stage.moon.gameObject.SetActive(ThemeManager.IsDark);
        stage.sun.gameObject.SetActive(!ThemeManager.IsDark);

        Vector2[] starAt = { new Vector2(260f, 40f), new Vector2(300f, 130f), new Vector2(210f, 96f) };
        float[] starSize = { 10f, 9f, 8f };
        stage.stars = new Image[starAt.Length];
        for (int i = 0; i < starAt.Length; i++)
        {
            stage.stars[i] = StageImage(root, "Star" + (i + 1), "star", new Vector2(1f, 1f),
                                        new Vector2(0.5f, 0.5f),
                                        new Vector2(-(390f - starAt[i].x) * Css, -starAt[i].y * Css),
                                        new Vector2(starSize[i], starSize[i]));
            stage.stars[i].gameObject.SetActive(ThemeManager.IsDark);
        }

        RectTransform cloud = UIFactory.Rect("Cloud", root);
        UIFactory.Corner(cloud, new Vector2(1f, 1f), new Vector2(110f * Css, 36f * Css),
                         new Vector2(-40f * Css, -150f * Css));
        stage.cloud = cloud;
        Image cloudImage = StageImage(cloud, "Image", "cloud", Vector2.zero, Vector2.zero,
                                      Vector2.zero, Vector2.zero);
        UIFactory.Stretch(cloudImage.rectTransform);
        cloudImage.color = ThemeManager.IsDark ? new Color(1f, 1f, 1f, 0.07f)
                                               : new Color(1f, 1f, 1f, 0.95f);

        // ---- the words, under the status bar
        RectTransform safe = UIFactory.Rect("Safe", root);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.top = true;

        RectTransform words = UIFactory.Rect("Words", safe);
        UIFactory.TopStretch(words, 0f, 22f * Css);
        words.offsetMin = new Vector2(18f * Css, words.offsetMin.y);
        words.offsetMax = new Vector2(-18f * Css, words.offsetMax.y);
        UIFactory.VerticalStack(words, 10f * Css).childAlignment = TextAnchor.UpperLeft;

        // The chip: seal and tag, hugging its words.
        RectTransform chipRow = UIFactory.Rect("ChipRow", words);
        UIFactory.Size(chipRow.gameObject, 32f * Css);
        UIFactory.HorizontalStack(chipRow, 0f);

        // The chip's pill, the sign pills and all the stage's ink are painted by
        // the stage (day and night palettes of their own), so none is themed.
        RectTransform chip = UIFactory.Rect("Chip", chipRow);
        stage.chip = UIFactory.Plain(chip, new Color(1f, 1f, 1f, 0.8f), UIShape.Pill);
        stage.chip.raycastTarget = false;
        HorizontalLayoutGroup chipLayout = UIFactory.HorizontalStack(chip, 7f * Css);
        chipLayout.padding = new RectOffset(Mathf.RoundToInt(4f * Css), Mathf.RoundToInt(12f * Css), 0, 0);
        chipLayout.childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Size(chip.gameObject, 32f * Css).flexibleWidth = 0f;

        RectTransform seal = UIFactory.Rect("Seal", chip);
        UIFactory.Size(seal.gameObject, 24f * Css, 24f * Css).flexibleWidth = 0f;
        stage.chipSeal = UIFactory.Plain(seal, Color.clear, UIShape.Rounded);
        // A creator's 7px corners on a 24px seal; a circle for a viewer; a ring
        // before any answer. The stage swaps the sprite and paints it.
        stage.chipSeal.pixelsPerUnitMultiplier = 44f / (7f * Css);
        stage.chipSeal.raycastTarget = false;
        stage.sealSquare = UISprites.Rounded;
        stage.sealRound = UISprites.Circle;
        stage.sealRing = UISprites.Ring;
        stage.chipSeal.sprite = stage.sealRing;

        RectTransform sealGlyph = UIFactory.Rect("Glyph", seal);
        UIFactory.Corner(sealGlyph, new Vector2(0.5f, 0.5f), new Vector2(14f * Css, 14f * Css), Vector2.zero);
        stage.chipGlyph = UIFactory.Plain(sealGlyph, Color.white);
        stage.chipGlyph.sprite = StageSprite("icon-ask");
        stage.chipGlyph.preserveAspect = true;
        stage.chipGlyph.raycastTarget = false;

        stage.chipLabel = StageText(chip, "Tag", 13f * Css, "stage.none.tag");
        stage.chipLabel.textWrappingMode = TextWrappingModes.NoWrap;

        // The headline: 28px semibold, at most 330px wide.
        stage.headLabel = StageText(words, "Head", 28f * Css, "stage.none.head");
        UIFactory.FitsItsText(stage.headLabel);
        stage.headLabel.margin = new Vector4(0f, 0f, (390f - 36f - 330f) * Css, 0f);
        stage.headLabel.lineSpacing = -8f;

        // Three sign pills: glyph and word on the chip's frosted white.
        // They wrap, as the website's flex-wrap does: a viewer's three are
        // wider than the phone.
        RectTransform signs = UIFactory.Rect("Signs", words);
        WrapRow signsWrap = signs.gameObject.AddComponent<WrapRow>();
        signsWrap.spacingX = 6f * Css;
        signsWrap.spacingY = 6f * Css;
        stage.signsRow = signs;
        stage.signPills = new GameObject[3];
        stage.signIcons = new Image[3];
        stage.signLabels = new TextMeshProUGUI[3];

        for (int i = 0; i < 3; i++)
        {
            RectTransform pill = UIFactory.Rect("Sign" + (i + 1), signs);
            UIFactory.Plain(pill, new Color(1f, 1f, 1f, 0.8f), UIShape.Pill).raycastTarget = false;
            HorizontalLayoutGroup pillLayout = UIFactory.HorizontalStack(pill, 6f * Css);
            pillLayout.padding = new RectOffset(Mathf.RoundToInt(8f * Css), Mathf.RoundToInt(10f * Css), 0, 0);
            pillLayout.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Size(pill.gameObject, 30f * Css).flexibleWidth = 0f;
            stage.signPills[i] = pill.gameObject;

            RectTransform glyph = UIFactory.Rect("Glyph", pill);
            UIFactory.Size(glyph.gameObject, 15f * Css, 15f * Css).flexibleWidth = 0f;
            stage.signIcons[i] = UIFactory.Plain(glyph, new Color32(0x00, 0x66, 0xCC, 0xFF));
            stage.signIcons[i].preserveAspect = true;
            stage.signIcons[i].raycastTarget = false;

            stage.signLabels[i] = StageText(pill, "Label", 12f * Css, null);
            stage.signLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            stage.signLabels[i].text = string.Empty;

            // Nothing chosen: no signs.
            pill.gameObject.SetActive(false);
        }

        // ---- the scene: full width at 480:200, 94px of sky under it
        RectTransform frame = UIFactory.Rect("Scene", root);
        UIFactory.BottomStretch(frame, 1080f * 200f / 480f, StageGround);
        stage.sceneGroup = frame.gameObject.AddComponent<CanvasGroup>();
        stage.sceneGroup.interactable = false;
        stage.sceneGroup.blocksRaycasts = false;

        RectTransform sceneRect = UIFactory.Rect("Drawing", frame);
        UIFactory.Stretch(sceneRect);
        stage.scene = sceneRect.gameObject.AddComponent<Image>();
        stage.scene.preserveAspect = true;
        stage.scene.raycastTarget = false;

        stage.sceneDay = new Sprite[StageSceneKeys.Length];
        stage.sceneNight = new Sprite[StageSceneKeys.Length];
        for (int i = 0; i < StageSceneKeys.Length; i++)
        {
            stage.sceneDay[i] = StageSprite("scene-" + StageSceneKeys[i] + "-day");
            stage.sceneNight[i] = StageSprite("scene-" + StageSceneKeys[i] + "-night");
        }

        // The preview's first drawing until the stage takes over.
        stage.scene.sprite = ThemeManager.IsDark ? stage.sceneNight[0] : stage.sceneDay[0];
        stage.scene.enabled = stage.scene.sprite != null;

        // The trail, in the scene's own frame, revealed left to right.
        RectTransform trailRect = UIFactory.Rect("Trail", frame);
        UIFactory.Stretch(trailRect);
        stage.trail = trailRect.gameObject.AddComponent<Image>();
        stage.trail.sprite = StageSprite("trail");
        stage.trail.type = Image.Type.Filled;
        stage.trail.fillMethod = Image.FillMethod.Horizontal;
        stage.trail.fillOrigin = (int)Image.OriginHorizontal.Left;
        stage.trail.fillAmount = 0f;
        stage.trail.preserveAspect = true;
        stage.trail.raycastTarget = false;

        // Two pins where the sprite sheet's README puts them in the 1440x600
        // frame, at this frame's 0.75.
        const float frameScale = 1080f / 1440f;
        Vector2[] pinAt = { new Vector2(317.4f, 297.3f), new Vector2(612.9f, 208.5f) };
        stage.pins = new RectTransform[pinAt.Length];
        for (int i = 0; i < pinAt.Length; i++)
        {
            RectTransform pin = UIFactory.Rect("Pin" + (i + 1), frame);
            UIFactory.Corner(pin, new Vector2(0f, 1f), new Vector2(81.2f, 106.3f) * frameScale,
                             new Vector2(pinAt[i].x, -pinAt[i].y) * frameScale);
            Image pinImage = pin.gameObject.AddComponent<Image>();
            pinImage.sprite = StageSprite("pin");
            pinImage.preserveAspect = true;
            pinImage.raycastTarget = false;
            stage.pins[i] = pin;

            // Hidden by its alpha, not switched off: the stage only fades them.
            CanvasGroup pinFade = pin.gameObject.AddComponent<CanvasGroup>();
            pinFade.alpha = 0f;
            pinFade.interactable = false;
            pinFade.blocksRaycasts = false;
        }

        // The words over the scene, not under it: a three-line Bulgarian
        // headline under a tall status bar can reach the top of the drawing,
        // which is sky there anyway.
        safe.SetAsLastSibling();

        // ---- the moment: a blue pill at the scene's top right
        RectTransform moment = UIFactory.Rect("Moment", root);
        moment.anchorMin = new Vector2(1f, 0f);
        moment.anchorMax = new Vector2(1f, 0f);
        moment.pivot = new Vector2(1f, 1f);
        moment.sizeDelta = new Vector2(0f, 30f * Css);
        moment.anchoredPosition = new Vector2(-16f * Css,
                                              StageGround + 1080f * 200f / 480f - 16f * Css);
        stage.moment = moment;
        stage.momentGroup = moment.gameObject.AddComponent<CanvasGroup>();
        stage.momentGroup.alpha = 0f;
        stage.momentGroup.interactable = false;
        stage.momentGroup.blocksRaycasts = false;

        UIFactory.Plain(moment, new Color32(0x00, 0x66, 0xCC, 0xFF), UIShape.Pill).raycastTarget = false;
        HorizontalLayoutGroup momentLayout = UIFactory.HorizontalStack(moment, 6f * Css);
        momentLayout.padding = new RectOffset(Mathf.RoundToInt(6f * Css), Mathf.RoundToInt(12f * Css), 0, 0);
        momentLayout.childAlignment = TextAnchor.MiddleLeft;
        ContentSizeFitter momentFit = moment.gameObject.AddComponent<ContentSizeFitter>();
        momentFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform disc = UIFactory.Rect("Disc", moment);
        UIFactory.Size(disc.gameObject, 18f * Css, 18f * Css).flexibleWidth = 0f;
        UIFactory.Plain(disc, new Color(1f, 1f, 1f, 0.22f), UIShape.Circle).raycastTarget = false;

        RectTransform momentGlyph = UIFactory.Rect("Glyph", disc);
        UIFactory.Corner(momentGlyph, new Vector2(0.5f, 0.5f), new Vector2(11f * Css, 11f * Css), Vector2.zero);
        stage.momentIcon = UIFactory.Plain(momentGlyph, Color.white);
        stage.momentIcon.preserveAspect = true;
        stage.momentIcon.raycastTarget = false;

        stage.momentLabel = Unthemed(UIFactory.Text("Label", moment, 12f * Css, ThemeRole.OnAccent,
                                                    TextAlignmentOptions.Left, null, FontStyles.Bold));
        stage.momentLabel.color = Color.white;
        stage.momentLabel.textWrappingMode = TextWrappingModes.NoWrap;
        stage.momentLabel.text = string.Empty;

        // ---- every glyph the stage can show, by name
        List<Sprite> icons = new List<Sprite>();
        List<string> names = new List<string>();
        foreach (string name in RoleStage.IconNames)
        {
            Sprite sprite = StageSprite("icon-" + name);
            if (sprite == null)
                continue;

            icons.Add(sprite);
            names.Add(name);
        }

        stage.icons = icons.ToArray();
        stage.iconNames = names.ToArray();

        if ((icons.Count == 0 || stage.sceneDay[0] == null) && _notes != null)
            _notes.Add("The stage sprites in " + StageArtFolder + " were not found as Sprites. " +
                       "Let Unity import them, then build again.");

        return stage;
    }

    /// <summary>A sprite from the stage's art folder by file name, or null.</summary>
    static Sprite StageSprite(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(StageArtFolder + "/" + file + ".png");
    }

    /// <summary>An untinted stage image, placed by anchor, pivot, offset and size.</summary>
    static Image StageImage(Transform parent, string name, string file, Vector2 anchor, Vector2 pivot,
                            Vector2 offset, Vector2 size)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        Image image = rt.gameObject.AddComponent<Image>();
        image.sprite = StageSprite(file);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// Stage ink: semibold, written and painted by the stage rather than
    /// localised or themed in place. Starts in the theme's ink.
    /// </summary>
    static TextMeshProUGUI StageText(Transform parent, string name, float size, string key)
    {
        TextMeshProUGUI text = Unthemed(UIFactory.Text(name, parent, size, ThemeRole.TextPrimary,
                                                       TextAlignmentOptions.Left, null, FontStyles.Bold));
        text.raycastTarget = false;
        text.text = key != null ? AppStrings.Get(key) : string.Empty;
        return text;
    }
}
