using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Builds the whole interface into the open scene and wires every control.
///
/// It owns everything under the Canvas. Re-running it deletes what it made last
/// time and makes it again, so the screen is reproducible from one menu item and
/// there is no hand-wiring left to lose. It never touches the AR side - no XR
/// Origin, no session, no anchors, no config beyond switching Streetscape Geometry
/// on - and it never touches the Canvas object itself.
///
/// Sizes are in the 1080x1920 reference resolution the CanvasScaler maps onto the
/// device. On a typical phone that is about 2.6 reference pixels per dp, which is
/// why <see cref="Tap"/> is 128 rather than 48: anything smaller lands under the
/// minimum touch target once it reaches a real screen.
/// </summary>
public static class AppUIBuilder
{
    const float ReferenceWidth = 1080f;
    const float ReferenceHeight = 1920f;

    const float Nav = 190f;
    const float Margin = 48f;

    /// <summary>Smallest size anything tappable may be, in reference pixels. 48dp.</summary>
    const float Tap = 128f;

    // Type scale. Nothing below Caption - 32 reference pixels is about 12dp, and
    // that is the floor for text a walking reader has to read.
    const float Display = 62f;
    const float Title = 48f;
    const float Body = 42f;
    const float Secondary = 36f;
    const float Caption = 32f;

    const string PrefabFolder = "Assets/Prefabs";
    const string RowPrefabPath = PrefabFolder + "/BuildingRow.prefab";
    const string PillPrefabPath = PrefabFolder + "/FilterPill.prefab";
    const string MarkerPrefabPath = PrefabFolder + "/BuildingMarker.prefab";
    const string MapPinPrefabPath = PrefabFolder + "/MapPin.prefab";
    const string TrailRowPrefabPath = PrefabFolder + "/TrailRow.prefab";
    const string PieceRowPrefabPath = PrefabFolder + "/PieceRow.prefab";
    const string OptionPrefabPath = PrefabFolder + "/AnswerOption.prefab";

    // Everything the builder owns under the Canvas. Removed and rebuilt every run.
    // Old names are kept in the list so a scene built by an earlier version is
    // cleaned up rather than left with two of everything.
    static readonly string[] GeneratedNames =
    {
        "MapPanel", "CameraPanel", "BrowsePanel", "SearchPanel", "SettingsPanel",
        "InfoPanel", "InfoSheet", "BottomNav", "ProximityToast", "Tutorial",
        "NearbyButton", "VpsIndicator", "NearbyPanel", "LiveARView", "ReadyStateView",
        "SearchContent", "SettingsContent", "Doors"
    };

    static List<string> _notes;

    /// <summary>
    /// False when the build is being run from the command line for a preview
    /// render. Dialogs have nobody to ask in batch mode.
    /// </summary>
    static bool _interactive = true;

    /// <summary>
    /// Builds without prompting, and throws instead of showing a dialog. This is
    /// what the screenshot harness calls; a build nobody can see the report of
    /// must fail loudly rather than quietly.
    /// </summary>
    public static void BuildForPreview()
    {
        _interactive = false;

        try
        {
            Build();
        }
        finally
        {
            _interactive = true;
        }
    }

    [MenuItem("Tools/UI/Build App UI")]
    public static void Build()
    {
        _notes = new List<string>();

        Canvas canvas = FindRootCanvas();
        if (canvas == null)
        {
            if (!_interactive)
                throw new System.Exception("No Canvas in the open scene.");

            EditorUtility.DisplayDialog("Build App UI", "No Canvas in the open scene.", "OK");
            return;
        }

        if (_interactive && !EditorUtility.DisplayDialog("Build App UI",
                "This rebuilds every screen under the Canvas from scratch.\n\n" +
                "Anything the builder made last time is deleted and made again. The " +
                "AR side of the scene is not touched.\n\n" +
                "Ctrl+Z undoes the whole thing.", "Build", "Cancel"))
        {
            return;
        }

        Undo.SetCurrentGroupName("Build App UI");
        int group = Undo.GetCurrentGroup();

        try
        {
            BuildAll(canvas);
        }
        catch (System.Exception exception)
        {
            // Everything is destroyed before it is rebuilt, so a failure half way
            // through would otherwise leave the scene stripped.
            Undo.RevertAllDownToGroup(group);

            if (!_interactive)
                throw;

            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Build App UI",
                "The build failed and the scene was rolled back - nothing was lost.\n\n" +
                exception.GetType().Name + ": " + exception.Message +
                "\n\nThe full stack trace is in the Console.", "OK");
            return;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        string report = _notes.Count == 0
            ? "Built the Map, Camera and Settings tabs, the browse list, the marker " +
              "sheet and the nav bar.\n\nPress Ctrl+S, then Play."
            : "Built, with " + _notes.Count + " note(s):\n\n" + string.Join("\n", _notes);

        Debug.Log("[App UI] " + report);

        if (_interactive)
            EditorUtility.DisplayDialog("Build App UI", report, "OK");
    }

    static void BuildAll(Canvas canvas)
    {
        Transform root = canvas.transform;

        ConfigureCanvas(canvas);
        RemoveGenerated(root);
        EnsureFolder();

        BuildingRowView rowPrefab = CreateRowPrefab();
        FilterPillView pillPrefab = CreatePillPrefab();
        MapPinView pinPrefab = CreateMapPinPrefab();
        TrailRowView trailRowPrefab = CreateTrailRowPrefab();
        PieceRowView pieceRowPrefab = CreatePieceRowPrefab();
        AnswerOptionView optionPrefab = CreateOptionPrefab();

        UserLocationService location = BuildServices();

        // Order in the hierarchy is draw order: panels first, then the sheet that
        // has to float over any of them, then the nav bar on top of everything.
        RectTransform mapPanel = Panel(root, "MapPanel");
        RectTransform cameraPanel = Panel(root, "CameraPanel", transparent: true);
        RectTransform browsePanel = Panel(root, "BrowsePanel");
        RectTransform settingsPanel = Panel(root, "SettingsPanel");

        InfoPanelController info = BuildInfoSheet(root, location, pieceRowPrefab, optionPrefab);

        MapTabController mapTab = BuildMapTab(mapPanel, location, pinPrefab, info);
        CameraTabController cameraTab = BuildCameraTab(cameraPanel, location, rowPrefab, info);
        SearchTabController browseTab = BuildBrowseTab(browsePanel, location, rowPrefab, pillPrefab);
        SettingsTabController settingsTab = BuildSettingsTab(settingsPanel);

        // BuildNav returns the padded row the buttons go in. The bar itself is its
        // parent, and that is what the tab controller shows and hides.
        // Under the marker sheet in draw order, so an alert can never cover
        // something the reader opened on purpose.
        BuildProximityToast(root, location, info);

        RectTransform navRow = BuildNav(root);

        TabNavigationController tabs = WireTabs(
            navRow, mapPanel, cameraPanel, browsePanel, settingsPanel, settingsTab);

        // Serialized references rather than delegates: an Action assigned here is
        // silently discarded when the scene saves, and the control then does nothing.
        mapTab.tabs = tabs;
        browseTab.tabs = tabs;
        browseTab.mapTab = mapTab;
        browseTab.cameraTab = cameraTab;
        EditorUtility.SetDirty(mapTab);
        EditorUtility.SetDirty(browseTab);

        WireTapInput(cameraPanel, info);

        // Last, so the walkthrough covers the nav bar too. A first run with a live
        // tab bar behind the explanation is a first run people tap straight past.
        settingsTab.tutorial = BuildTutorial(root);
        EditorUtility.SetDirty(settingsTab);

        // Last of all, so the way in draws over every screen it stands in front of -
        // the nav bar included, which it also switches off. A modal with a live tab
        // bar behind it is a tab bar people tap straight past.
        BuildDoors(root, navRow.parent.gameObject, trailRowPrefab);

        AddFade(mapPanel.gameObject);
        AddFade(browsePanel.gameObject);
        AddFade(settingsPanel.gameObject);

        // The app opens on the map, so that is what the scene shows at edit time
        // too - otherwise the Game view shows whichever panel happens to be last.
        mapPanel.gameObject.SetActive(true);
        cameraPanel.gameObject.SetActive(false);
        browsePanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------- canvas

    /// <summary>
    /// The Canvas the screen is built into.
    ///
    /// Not FindFirstObjectByType: the map surface carries a nested Canvas of its
    /// own for batching, and a plain search can return that one. On a rebuild that
    /// would put the entire app inside the map.
    /// </summary>
    public static Canvas FindRootCanvas()
    {
        Canvas[] all = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].isRootCanvas)
                return all[i];
        }

        return all.Length > 0 ? all[0] : null;
    }

    static void ConfigureCanvas(Canvas canvas)
    {
        Undo.RecordObject(canvas, "Build App UI");
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);

        Undo.RecordObject(scaler, "Build App UI");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        // Match on width rather than splitting the difference. Phones vary far more
        // in aspect ratio than in width, and matching width keeps the nav bar and
        // the sheets the same physical size on every one of them.
        scaler.matchWidthOrHeight = 0f;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include) == null)
        {
            _notes.Add("No EventSystem in the scene - nothing will respond to taps. " +
                       "Add one with GameObject > UI > Event System.");
        }
    }

    /// <summary>
    /// A full-screen tab panel. Opaque ones also stop taps reaching the AR world
    /// behind them; the camera's panel must stay clear, or it would cover the feed
    /// it exists to annotate.
    /// </summary>
    static RectTransform Panel(Transform root, string name, bool transparent = false)
    {
        RectTransform rt = UIFactory.Rect(name, root);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);

        if (!transparent)
            UIFactory.Fill(rt, ThemeRole.Background).raycastTarget = true;

        return rt;
    }

    // ------------------------------------------------------------------ services

    static UserLocationService BuildServices()
    {
        GameObject go = new GameObject("AppServices");
        Undo.RegisterCreatedObjectUndo(go, "Build App UI");

        // OnApplicationPause never fires on a disabled object, and the tab panels
        // are disabled whenever the reader is somewhere else - so this lives here.
        go.AddComponent<AppLifecycleWatcher>();

        // Phase 1. These were written and then never reached a scene, which is
        // why the way-in screens had nothing to ask: no identity, no membership,
        // no record of the terms. Config first, because everything else reads it.
        go.AddComponent<SupabaseConfig>();
        go.AddComponent<SupabaseAuth>();
        go.AddComponent<TrailService>();
        go.AddComponent<TermsService>();

        // Phase 2.5. Harmless before the migration is applied: every check comes
        // back Unavailable, which the sheet shows as "couldn't check that".
        go.AddComponent<MarkerTaskService>();

        // Markers come from the Supabase RPC, not from a spreadsheet and not from
        // assets in the build. RemoteBuildingSource is the old path and is
        // deliberately not added.
        go.AddComponent<RemoteMarkerSource>();

        UserLocationService location = go.AddComponent<UserLocationService>();

        // Lets the app draw faster than ARCore delivers camera frames, which is
        // what was capping every animation in the app at thirty.
        FrameRate frameRate = go.AddComponent<FrameRate>();
        frameRate.session = Object.FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        frameRate.target = 60;

        // Watches for markers coming into range while the app is open. Lives here
        // rather than on a panel, so walking past one while reading Settings still
        // counts.
        ProximityAlerts alerts = go.AddComponent<ProximityAlerts>();
        alerts.locationService = location;
        alerts.alertRadiusMetres = 60d;
        alerts.rearmRadiusMetres = 110d;
        location.earthManager =
            Object.FindFirstObjectByType<AREarthManager>(FindObjectsInactive.Include);

        if (location.earthManager == null)
        {
            _notes.Add("No AREarthManager found - distances and the calibration dot " +
                       "will stay inactive until one exists on the XR Origin.");
        }

        BuildMarkerSpawner(go, location);
        BuildOcclusion(go);

        return location;
    }

    static void BuildMarkerSpawner(GameObject services, UserLocationService location)
    {
        MarkerSpawner spawner = services.AddComponent<MarkerSpawner>();

        spawner.locationService = location;
        spawner.anchorManager =
            Object.FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        spawner.defaultPrefab = CreateMarkerPrefab();
        spawner.altitudeAboveTerrain = 1.5;
        spawner.spawnRadiusMetres = 500d;
        spawner.releaseRadiusMetres = 650d;
        spawner.maxActiveMarkers = 20;
        spawner.verboseLogging = true;

        if (spawner.anchorManager == null)
        {
            _notes.Add("No ARAnchorManager found on the XR Origin - markers cannot " +
                       "be placed until one exists.");
        }

        TappableObject leftover =
            Object.FindFirstObjectByType<TappableObject>(FindObjectsInactive.Include);

        if (leftover != null)
        {
            _notes.Add("\"" + leftover.name + "\" is a hand-placed marker still in the " +
                       "scene. Delete it, or that marker will appear twice.");
        }
    }

    static void BuildOcclusion(GameObject services)
    {
        OcclusionService occlusion = services.AddComponent<OcclusionService>();
        occlusion.streetscapeManager = EnsureStreetscapeManager();

        Camera arCamera = Camera.main;
        if (arCamera != null)
        {
            AROcclusionManager depth = arCamera.GetComponent<AROcclusionManager>();
            if (depth == null)
                depth = Undo.AddComponent<AROcclusionManager>(arCamera.gameObject);

            occlusion.occlusionManager = depth;
        }
        else
        {
            _notes.Add("No MainCamera found - depth occlusion was not set up.");
        }

        if (occlusion.streetscapeManager == null)
        {
            _notes.Add("No XR Origin found, so streetscape occlusion is inactive - " +
                       "markers will not hide behind distant buildings.");
        }
    }

    static ARStreetscapeGeometryManager EnsureStreetscapeManager()
    {
        ARStreetscapeGeometryManager manager =
            Object.FindFirstObjectByType<ARStreetscapeGeometryManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            Unity.XR.CoreUtils.XROrigin origin =
                Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);

            if (origin == null)
                return null;

            manager = Undo.AddComponent<ARStreetscapeGeometryManager>(origin.gameObject);
        }

        ARCoreExtensions extensions =
            Object.FindFirstObjectByType<ARCoreExtensions>(FindObjectsInactive.Include);

        ARCoreExtensionsConfig config =
            extensions != null ? extensions.ARCoreExtensionsConfig : null;

        if (config == null)
        {
            _notes.Add("No ARCoreExtensionsConfig linked on the ARCore Extensions object, " +
                       "so Streetscape Geometry could not be switched on.");
            return manager;
        }

        if (config.StreetscapeGeometryMode != StreetscapeGeometryMode.Enabled)
        {
            Undo.RecordObject(config, "Build App UI");
            config.StreetscapeGeometryMode = StreetscapeGeometryMode.Enabled;
            EditorUtility.SetDirty(config);

            _notes.Add("Switched Streetscape Geometry Mode on in " + config.name + ".");
        }

        return manager;
    }

    // -------------------------------------------------------------- marker sheet

    /// <summary>
    /// The sheet a marker opens into, built at the Canvas root rather than inside a
    /// tab. It is reachable from the map, from the camera and from the browse list,
    /// and a sheet parented to one of those would vanish the moment the reader was
    /// on another.
    /// </summary>
    static InfoPanelController BuildInfoSheet(Transform root, UserLocationService location,
                                              PieceRowView pieceRowPrefab,
                                              AnswerOptionView optionPrefab)
    {
        RectTransform holder = UIFactory.Rect("InfoSheet", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        InfoPanelController controller = holder.gameObject.AddComponent<InfoPanelController>();
        controller.locationService = location;
        controller.scrimButton = CreateScrim("InfoScrim", holder, out GameObject scrim, 0.55f);
        controller.scrim = scrim;

        RectTransform sheetSafe = UIFactory.Rect("Safe", holder);
        UIFactory.Stretch(sheetSafe);
        SafeAreaPad sheetPad = sheetSafe.gameObject.AddComponent<SafeAreaPad>();
        sheetPad.left = true;
        sheetPad.right = true;

        const float SheetHeight = 1180f;
        const float HeadRoom = 110f;                    // the grab bar and the close button
        const float FooterHeight = Tap * 2f + 16f;      // Guide, then the quieter row

        RectTransform sheet = UIFactory.Rect("Sheet", sheetSafe);
        UIFactory.BottomStretch(sheet, SheetHeight, Nav);
        UIFactory.Fill(sheet, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        controller.panelRoot = sheet.gameObject;

        SheetAnimator animator = sheet.gameObject.AddComponent<SheetAnimator>();
        animator.openY = Nav;
        animator.duration = 0.25f;
        controller.sheet = animator;

        SheetDragHandle drag = AddDragArea(sheet, animator);
        Wire(drag.onDismissed, controller.ClosePanel, "Marker sheet drag");

        // Everything a piece hides while it is open: the reading, and the two
        // actions. Grouped so the controller can put one thing away rather than
        // needing to know how the sheet is laid out.
        RectTransform overview = UIFactory.Rect("Overview", sheet);
        UIFactory.Stretch(overview);

        // --- the part that scrolls ---
        //
        // Only the reading scrolls. The two buttons stay where a thumb last found
        // them, which is the difference between a sheet and a page.
        RectTransform body = UIFactory.Rect("Body", overview);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(Margin, Margin + FooterHeight + 18f);
        body.offsetMax = new Vector2(-Margin, -HeadRoom);

        RectTransform content = UIFactory.ScrollList("Scroll", body, 18f);

        // Identity: the marker's own colour beside its name, so this sheet and the
        // thing standing in the street are recognisably the same object.
        RectTransform head = UIFactory.Rect("Head", content);
        UIFactory.Size(head.gameObject, 80f);
        UIFactory.HorizontalStack(head, 20f);

        controller.colourRing = Disc(head, 58f, ThemeRole.Line, UIShape.Ring,
                                     out RectTransform ringRect);
        controller.colourDot = Disc(ringRect, 34f, ThemeRole.Accent, UIShape.Circle,
                                    out RectTransform _);

        controller.titleText = UIFactory.Text("TitleText", head, Display, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.titleText.gameObject, 80f);
        UIFactory.Flexible(controller.titleText.gameObject);

        // Distance and direction together. A distance on its own is half an answer,
        // and direction is the half a reader on a street corner actually needs.
        RectTransform distanceRow = UIFactory.Rect("Distance", content);
        UIFactory.Size(distanceRow.gameObject, 54f);
        UIFactory.HorizontalStack(distanceRow, 14f);

        controller.bearingArrow = Arrow(distanceRow, location, BearingArrow.Frame.NorthUp, 40f);

        controller.distanceText = UIFactory.Text("Value", distanceRow, Body, ThemeRole.Accent,
                                                 TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.distanceText.gameObject, 54f);
        UIFactory.Flexible(controller.distanceText.gameObject);

        controller.metaText = UIFactory.Text("MetaText", content, Secondary,
                                             ThemeRole.TextSecondary,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(controller.metaText.gameObject, 46f);

        // --- what this marker holds ---
        MarkerPieceController pieces = BuildPieces(sheet, content, pieceRowPrefab, optionPrefab,
                                                   HeadRoom);
        pieces.overview = overview.gameObject;
        controller.pieces = pieces;

        controller.descriptionText = UIFactory.Text("DescriptionText", content, Body,
                                                    ThemeRole.TextSecondary,
                                                    TextAlignmentOptions.TopLeft);
        // No fixed height. Inside a scroll view a fixed height is either a hole
        // under a short entry or a cut through a long one, and the sheet had been
        // doing the second for every marker with more than about forty words.

        // --- the part that does not scroll ---
        RectTransform footer = UIFactory.Rect("Footer", overview);
        UIFactory.BottomStretch(footer, FooterHeight, Margin);
        footer.sizeDelta = new Vector2(-Margin * 2f, FooterHeight);
        UIFactory.VerticalStack(footer, 16f, false);

        // Guide is the primary action and gets the full width to itself. The rest
        // are a quieter row underneath.
        RectTransform guide = UIFactory.Rect("GuideButton", footer);
        UIFactory.Size(guide.gameObject, Tap);
        controller.guideButton = UIFactory.Button(guide, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(guide.gameObject);

        RectTransform guideInner = UIFactory.Rect("Inner", guide);
        UIFactory.Stretch(guideInner);
        HorizontalLayoutGroup guideLayout = UIFactory.HorizontalStack(guideInner, 16f);
        guideLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(guideInner, AppIcon.Navigation, 44f, ThemeRole.OnAccent);

        controller.guideLabel = UIFactory.Text("Label", guideInner, Body, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        LayoutElement guideLabelSize = UIFactory.Size(controller.guideLabel.gameObject, Tap, 420f);
        guideLabelSize.flexibleWidth = 0f;
        Unthemed(controller.guideLabel.gameObject);
        controller.guideLabel.text = AppStrings.Get("guide.start");

        RectTransform actions = UIFactory.Rect("Actions", footer);
        UIFactory.Size(actions.gameObject, Tap);
        HorizontalLayoutGroup actionLayout = UIFactory.HorizontalStack(actions, 16f, true);
        actionLayout.childAlignment = TextAnchor.MiddleCenter;

        RectTransform learnMoreRect = OutlineButton(actions, "LearnMoreButton", "info.learnMore",
                                                    out Button learnMore,
                                                    out controller.learnMoreLabel);
        controller.learnMoreButton = learnMoreRect.gameObject;
        Wire(learnMore.onClick, controller.OpenExternalLink, "Learn more");

        OutlineButton(actions, "HideButton", "marker.hide", out Button hide,
                      out controller.hideLabel);
        controller.hideButton = hide;

        RectTransform close = UIFactory.Rect("CloseButton", sheet);
        UIFactory.Corner(close, new Vector2(1f, 1f), new Vector2(Tap, Tap),
                         new Vector2(-24f, -6f));
        Button closeButton = UIFactory.Button(close, ThemeRole.SurfaceAlt, 1f, UIShape.Circle);
        Wire(closeButton.onClick, controller.ClosePanel, "Marker sheet close");
        Icon(close, AppIcon.Close, 44f, ThemeRole.TextSecondary);

        // The animator hides the sheet by moving it, not by disabling it, so this
        // object stays enabled or ShowPanel would have nothing to reveal.
        //
        // Parked from the constant, not from rect.height: layout has not run when
        // the builder gets here, so the height reads 0 and the sheet parks at 150 -
        // which is fully on screen. SnapClosed hides it the moment the app plays,
        // so this was invisible on device and covered the whole editor scene.
        sheet.anchoredPosition = new Vector2(0f, Nav - SheetHeight - 40f);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// The list of pieces on the overview, and the page one opens into.
    ///
    /// The page is a sibling of the overview rather than a screen of its own, which
    /// is the whole point of having chosen a sheet: opening a video does not take
    /// the reader off the map, it swaps what the sheet in front of the map is
    /// showing.
    /// </summary>
    static MarkerPieceController BuildPieces(RectTransform sheet, RectTransform content,
                                             PieceRowView rowPrefab,
                                             AnswerOptionView optionPrefab,
                                             float headRoom)
    {
        MarkerPieceController pieces = sheet.gameObject.AddComponent<MarkerPieceController>();
        pieces.rowPrefab = rowPrefab;
        pieces.videoIcon = UIIcons.Get(AppIcon.Video);
        pieces.audioIcon = UIIcons.Get(AppIcon.Audio);
        pieces.questionIcon = UIIcons.Get(AppIcon.Question);

        // ------------------------------------------------------ on the overview
        RectTransform section = UIFactory.Rect("Pieces", content);
        UIFactory.VerticalStack(section, 12f, false);
        pieces.section = section.gameObject;

        RectTransform header = UIFactory.Rect("Header", section);
        UIFactory.Size(header.gameObject, 46f);
        UIFactory.HorizontalStack(header, 12f);

        RectTransform chip = UIFactory.Rect("Chip", header);
        LayoutElement chipSize = UIFactory.Size(chip.gameObject, 46f, 210f);
        chipSize.flexibleWidth = 0f;
        UIFactory.Fill(chip, ThemeRole.SurfaceAlt, 1f, UIShape.Pill).raycastTarget = false;

        pieces.countLabel = UIFactory.Text("Count", chip, 28f, ThemeRole.TextSecondary,
                                           TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(pieces.countLabel.rectTransform);

        RectTransform list = UIFactory.Rect("List", section);
        UIFactory.VerticalStack(list, 10f, false);
        pieces.listParent = list;

        // ---------------------------------------------------------- the page
        RectTransform page = UIFactory.Rect("PiecePage", sheet);
        UIFactory.Stretch(page);
        page.offsetMin = new Vector2(Margin, Margin);
        page.offsetMax = new Vector2(-Margin, -headRoom);
        pieces.page = page.gameObject;

        RectTransform pageHead = UIFactory.Rect("Head", page);
        UIFactory.TopStretch(pageHead, 84f);
        HorizontalLayoutGroup headLayout = UIFactory.HorizontalStack(pageHead, 14f);
        headLayout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform back = UIFactory.Rect("Back", pageHead);
        LayoutElement backSize = UIFactory.Size(back.gameObject, 76f, 190f);
        backSize.flexibleWidth = 0f;
        pieces.backButton = UIFactory.Button(back, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);

        RectTransform backRow = UIFactory.Rect("Row", back);
        UIFactory.Stretch(backRow);
        backRow.offsetMin = new Vector2(18f, 0f);
        backRow.offsetMax = new Vector2(-18f, 0f);
        HorizontalLayoutGroup backLayout = UIFactory.HorizontalStack(backRow, 8f);
        backLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(backRow, AppIcon.Back, 32f, ThemeRole.TextSecondary);

        TextMeshProUGUI backLabel = UIFactory.Text("Label", backRow, 30f,
                                                   ThemeRole.TextSecondary,
                                                   TextAlignmentOptions.Left, "piece.back",
                                                   FontStyles.Bold);
        UIFactory.Size(backLabel.gameObject, 76f);
        UIFactory.Flexible(backLabel.gameObject);

        pieces.pageTitle = UIFactory.Text("Title", pageHead, Title, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(pieces.pageTitle.gameObject, 84f);
        UIFactory.Flexible(pieces.pageTitle.gameObject);
        pieces.pageTitle.enableAutoSizing = true;
        pieces.pageTitle.fontSizeMin = 32f;
        pieces.pageTitle.fontSizeMax = Title;
        pieces.pageTitle.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform pageBody = UIFactory.Rect("PageBody", page);
        UIFactory.Stretch(pageBody);
        pageBody.offsetMax = new Vector2(0f, -100f);

        pieces.media = BuildMediaPage(pageBody);
        pieces.question = BuildQuestionPage(pageBody, optionPrefab);

        page.gameObject.SetActive(false);
        return pieces;
    }

    static RectTransform OutlineButton(Transform parent, string name, string key,
                                       out Button button, out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);
        UIFactory.Flexible(rt.gameObject);

        button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded);

        label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Center, key, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return rt;
    }

    // ------------------------------------------------------------------- map tab

    static MapTabController BuildMapTab(RectTransform panel, UserLocationService location,
                                        MapPinView pinPrefab, InfoPanelController info)
    {
        MapTabController controller = panel.gameObject.AddComponent<MapTabController>();
        controller.locationService = location;
        controller.infoPanel = info;
        controller.radiusMetres = 500d;

        // --- the map itself ---
        RectTransform surface = UIFactory.Rect("MapSurface", panel);
        UIFactory.Stretch(surface);
        surface.offsetMin = new Vector2(0f, Nav);

        // The pan gesture lands on this, so it needs a graphic even though it looks
        // like the panel behind it.
        UIFactory.Fill(surface, ThemeRole.Background).raycastTarget = true;
        surface.gameObject.AddComponent<RectMask2D>();

        // No nested canvas here, deliberately.
        //
        // One was added to isolate the map's rebuilds from the rest of the screen,
        // and it drew over every later sibling: the map covered its own title, its
        // cards, the scale bar and the nav bar. That reproduced in both Screen
        // Space - Camera and World Space, so it was not an artefact of the capture
        // rig, and on device it would have left the reader with a map and no way
        // off it.
        //
        // The rebuild cost it was meant to solve is already gone: MapView writes
        // nothing unless a value changed and lays out only when something asks it
        // to. That was the fix worth having. This one traded a rendering bug for a
        // saving that had already been made.

        MapView map = surface.gameObject.AddComponent<MapView>();
        map.locationService = location;
        map.pinPrefab = pinPrefab;
        map.metresAcross = 400f;
        map.minMetresAcross = 60f;
        map.maxMetresAcross = 4000f;
        controller.map = map;

        // Tiles first, so every marker draws over them.
        RectTransform tiles = UIFactory.Rect("Tiles", surface);
        UIFactory.Stretch(tiles);

        MapTileLayer tileLayer = surface.gameObject.AddComponent<MapTileLayer>();
        tileLayer.map = map;
        tileLayer.tileParent = tiles;

        RectTransform content = UIFactory.Rect("Content", surface);
        UIFactory.Stretch(content);
        map.contentParent = content;

        // The accuracy circle: a faint fill with a crisp edge drawn as geometry.
        // The two distance rings that used to sit here are gone - they were drawn
        // for a map with no streets, and over real tiles they were decoration
        // covering information.
        RectTransform accuracy = UIFactory.Rect("Accuracy", content);
        UIFactory.Corner(accuracy, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), Vector2.zero);
        Color accuracyTint = ThemeManager.Get(ThemeRole.Accent);
        accuracyTint.a = 0.07f;

        map.accuracyCircle = UIFactory.Plain(accuracy, accuracyTint, UIShape.Circle);
        map.accuracyCircle.raycastTarget = false;

        // Off until MapView has a real accuracy to size it from. Built visible, it
        // was a solid teal disc across the middle of the map in the editor, and a
        // frame of one on device before the first layout pass.
        accuracy.gameObject.SetActive(false);

        RectTransform accuracyEdge = UIFactory.Rect("AccuracyEdge", content);
        UIFactory.Corner(accuracyEdge, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f),
                         Vector2.zero);
        // Added by hand, before the graphic that needs it. Belt and braces after
        // this threw thirty-one times a frame in the editor.
        accuracyEdge.gameObject.AddComponent<CanvasRenderer>();

        map.accuracyRing = accuracyEdge.gameObject.AddComponent<RingGraphic>();
        map.accuracyRing.thickness = 3f;
        map.accuracyRing.raycastTarget = false;

        Color edgeTint = ThemeManager.Get(ThemeRole.Accent);
        edgeTint.a = 0.5f;
        map.accuracyRing.color = edgeTint;

        accuracyEdge.gameObject.SetActive(false);

        RectTransform ray = UIFactory.Rect("GuidanceRay", content);
        UIFactory.Corner(ray, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), Vector2.zero);
        UIFactory.Plain(ray, ThemeManager.Get(ThemeRole.Accent), UIShape.Pill).raycastTarget = false;
        map.guidanceRay = ray;

        // The reader's own dot is drawn last, so it is never buried under a pin.
        // Its outer ring is white in both themes rather than the background colour:
        // over a dark map a dark halo is no halo, and this is the one mark on the
        // screen that must never be hard to find.
        RectTransform dot = UIFactory.Rect("UserDot", content);
        UIFactory.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(54f, 54f), Vector2.zero);
        UIFactory.Plain(dot, Color.white, UIShape.Circle).raycastTarget = false;

        RectTransform dotInner = UIFactory.Rect("Inner", dot);
        UIFactory.Corner(dotInner, new Vector2(0.5f, 0.5f), new Vector2(34f, 34f), Vector2.zero);
        UIFactory.Fill(dotInner, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;
        map.userDot = dot;

        RectTransform safe = SafeContent(panel);

        // --- top bar ---
        RectTransform top = UIFactory.Rect("TopBar", safe);
        UIFactory.TopStretch(top, 170f);

        // The heading used to read "Map", a word the tab bar was already saying two
        // inches below it. Once a reader can hold more than one trail, the name of
        // the one they are standing in is the thing the screen cannot otherwise
        // tell them - and it doubles as the way to swap.
        controller.titleText = BuildTrailChip(top, out RectTransform chipRect);
        UIFactory.Corner(chipRect, new Vector2(0f, 0.5f),
                         new Vector2(600f, 84f), new Vector2(Margin - 18f, -14f));

        // Labelled, not icon-only. Recentring has no universally read symbol, and
        // this control only appears once the reader has already lost track of where
        // they are - the worst moment to make them guess.
        RectTransform recentre = UIFactory.Rect("Recentre", top);
        UIFactory.Corner(recentre, new Vector2(1f, 0.5f), new Vector2(330f, Tap),
                         new Vector2(-Margin, -14f));
        map.recentreButton = UIFactory.Button(recentre, ThemeRole.Surface, 0.96f, UIShape.Pill);
        controller.recentreHolder = recentre.gameObject;

        RectTransform recentreRow = UIFactory.Rect("Row", recentre);
        UIFactory.Stretch(recentreRow);
        recentreRow.offsetMin = new Vector2(26f, 0f);
        recentreRow.offsetMax = new Vector2(-26f, 0f);
        HorizontalLayoutGroup recentreLayout = UIFactory.HorizontalStack(recentreRow, 14f);
        recentreLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(recentreRow, AppIcon.MyLocation, 44f, ThemeRole.Accent);

        TextMeshProUGUI recentreLabel = UIFactory.Text("Label", recentreRow, Secondary,
                                                       ThemeRole.TextPrimary,
                                                       TextAlignmentOptions.Left,
                                                       "map.recentre", FontStyles.Bold);
        UIFactory.Size(recentreLabel.gameObject, Tap);
        UIFactory.Flexible(recentreLabel.gameObject);

        // --- scale bar ---
        // Anchored under the top bar rather than above the dock. The dock changes
        // height as guidance comes and goes, and a scale bar shuffling up and down
        // with it reads as a bug.
        RectTransform scaleHolder = UIFactory.Rect("Scale", safe);
        UIFactory.Corner(scaleHolder, new Vector2(0f, 1f), new Vector2(420f, 70f),
                         new Vector2(Margin, -186f));

        map.scaleLabel = UIFactory.Text("Label", scaleHolder, Caption, ThemeRole.TextSecondary,
                                        TextAlignmentOptions.BottomLeft);
        UIFactory.Corner(map.scaleLabel.rectTransform, new Vector2(0f, 0f),
                         new Vector2(300f, 40f), new Vector2(0f, 14f));

        RectTransform bar = UIFactory.Rect("Bar", scaleHolder);
        UIFactory.Corner(bar, new Vector2(0f, 0f), new Vector2(160f, 8f), Vector2.zero);
        UIFactory.Fill(bar, ThemeRole.TextSecondary, 1f, UIShape.Pill).raycastTarget = false;
        map.scaleBar = bar;

        // Required by the tile licence, and it belongs on screen anyway - the map
        // is somebody else's work.
        TextMeshProUGUI credit = UIFactory.Text("Attribution", safe, 26f, ThemeRole.TextMuted,
                                                TextAlignmentOptions.Left);
        UIFactory.Corner(credit.rectTransform, new Vector2(0f, 1f), new Vector2(560f, 34f),
                         new Vector2(Margin, -256f));
        credit.text = "\u00A9 OpenStreetMap contributors";

        // --- the states where there is nothing to draw ---
        controller.stateCard = BuildStateCard(safe);

        // --- the panel resting on the map ---
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, Nav + 20f);
        dockArea.sizeDelta = new Vector2(-Margin * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 16f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        controller.guidance = BuildGuidanceBanner(dockArea, location, BearingArrow.Frame.NorthUp);

        RectTransform nearest = UIFactory.Rect("Nearest", dockArea);
        UIFactory.Size(nearest.gameObject, 150f);
        controller.nearestButton = UIFactory.Button(nearest, ThemeRole.Surface, 0.97f,
                                                    UIShape.Rounded);
        controller.nearestCard = nearest.gameObject;
        Outline(nearest);

        RectTransform nearestRow = UIFactory.Rect("Row", nearest);
        UIFactory.Stretch(nearestRow);
        nearestRow.offsetMin = new Vector2(28f, 0f);
        nearestRow.offsetMax = new Vector2(-28f, 0f);
        UIFactory.HorizontalStack(nearestRow, 22f);

        controller.nearestRing = Disc(nearestRow, 52f, ThemeRole.Line, UIShape.Ring,
                                      out RectTransform nearestRingRect);
        controller.nearestDot = Disc(nearestRingRect, 32f, ThemeRole.Accent, UIShape.Circle,
                                     out RectTransform _);

        RectTransform nearestText = UIFactory.Rect("Text", nearestRow);
        UIFactory.Size(nearestText.gameObject, 100f);
        UIFactory.Flexible(nearestText.gameObject);
        UIFactory.VerticalStack(nearestText, 2f, false);

        controller.nearestCaption = UIFactory.Text("Caption", nearestText, Caption,
                                                   ThemeRole.TextMuted,
                                                   TextAlignmentOptions.Left, "map.nearest");
        UIFactory.Size(controller.nearestCaption.gameObject, 40f);

        controller.nearestName = UIFactory.Text("Name", nearestText, Body, ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.nearestName.gameObject, 56f);

        controller.nearestArrow = Arrow(nearestRow, location, BearingArrow.Frame.NorthUp, 40f);

        controller.nearestDistance = UIFactory.Text("Distance", nearestRow, Body, ThemeRole.Accent,
                                                    TextAlignmentOptions.Right, null,
                                                    FontStyles.Bold);
        LayoutElement distanceSize = UIFactory.Size(controller.nearestDistance.gameObject, 60f, 180f);
        distanceSize.flexibleWidth = 0f;

        RectTransform browse = UIFactory.Rect("Browse", dockArea);
        UIFactory.Size(browse.gameObject, Tap);
        controller.browseButton = UIFactory.Button(browse, ThemeRole.Surface, 0.97f,
                                                   UIShape.Rounded);
        Outline(browse);

        RectTransform browseRow = UIFactory.Rect("Row", browse);
        UIFactory.Stretch(browseRow);
        HorizontalLayoutGroup browseLayout = UIFactory.HorizontalStack(browseRow, 16f);
        browseLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(browseRow, AppIcon.List, 38f, ThemeRole.TextSecondary);

        controller.browseLabel = UIFactory.Text("Label", browseRow, Secondary,
                                                ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Left, "map.browse",
                                                FontStyles.Bold);
        LayoutElement browseLabelSize = UIFactory.Size(controller.browseLabel.gameObject,
                                                       Tap, 320f);
        browseLabelSize.flexibleWidth = 0f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// An icon, from the rasterized set rather than assembled out of rectangles.
    /// The first version of this screen drew its own, and the camera came out
    /// looking like an apple.
    /// </summary>
    static Image Icon(Transform parent, AppIcon icon, float size, ThemeRole role,
                      out RectTransform rect)
    {
        rect = UIFactory.Rect("Icon", parent);
        UIFactory.Corner(rect, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);

        LayoutElement element = UIFactory.Size(rect.gameObject, size, size);
        element.flexibleWidth = 0f;

        Image image = UIFactory.Fill(rect, role);
        image.sprite = UIIcons.Get(icon);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;

        return image;
    }

    static Image Icon(Transform parent, AppIcon icon, float size, ThemeRole role)
    {
        return Icon(parent, icon, size, role, out RectTransform _);
    }

    /// <summary>
    /// Draws a hairline border around a card.
    ///
    /// Anything sitting over the map needs one. A surface with no edge over a busy
    /// background reads as a hole punched in the screen, and no amount of corner
    /// radius fixes it.
    /// </summary>
    static void Outline(RectTransform card, ThemeRole role = ThemeRole.Line)
    {
        RectTransform rt = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(rt);

        Image image = UIFactory.Fill(rt, role);
        image.sprite = UISprites.RoundedOutline;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        image.raycastTarget = false;

        // First child, so anything the card puts inside it draws over the border
        // rather than under it.
        rt.SetAsFirstSibling();
    }

    /// <summary>A coloured disc, or the ring that separates one from its background.</summary>
    static Image Disc(Transform parent, float size, ThemeRole role, UIShape shape,
                      out RectTransform rect)
    {
        rect = UIFactory.Rect(shape == UIShape.Ring ? "Ring" : "Dot", parent);
        UIFactory.Corner(rect, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);

        LayoutElement element = UIFactory.Size(rect.gameObject, size, size);
        element.flexibleWidth = 0f;

        Image image = UIFactory.Plain(rect, ThemeManager.Get(role), shape);
        image.raycastTarget = false;
        return image;
    }

    static BearingArrow Arrow(Transform parent, UserLocationService location,
                              BearingArrow.Frame frame, float size)
    {
        RectTransform holder = UIFactory.Rect("Arrow", parent);
        UIFactory.Corner(holder, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);

        LayoutElement element = UIFactory.Size(holder.gameObject, size, size);
        element.flexibleWidth = 0f;

        Image glyph = Icon(holder, AppIcon.Navigation, size, ThemeRole.Accent,
                           out RectTransform tip);
        Unthemed(tip.gameObject); // BearingArrow drives this colour itself

        // Replaces the arrow once the reader is close enough that a direction would
        // be noise rather than help.
        RectTransform arrived = UIFactory.Rect("Arrived", holder);
        UIFactory.Corner(arrived, new Vector2(0.5f, 0.5f),
                         new Vector2(size * 0.6f, size * 0.6f), Vector2.zero);
        UIFactory.Plain(arrived, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle)
            .raycastTarget = false;
        arrived.gameObject.SetActive(false);

        BearingArrow arrow = holder.gameObject.AddComponent<BearingArrow>();
        arrow.locationService = location;
        arrow.arrow = tip;
        arrow.tinted = glyph;
        arrow.arrivedIcon = arrived.gameObject;
        arrow.frame = frame;
        return arrow;
    }

    static GuidanceBanner BuildGuidanceBanner(Transform parent, UserLocationService location,
                                              BearingArrow.Frame frame)
    {
        RectTransform rt = UIFactory.Rect("Guidance", parent);
        UIFactory.Size(rt.gameObject, 150f);
        UIFactory.Fill(rt, ThemeRole.Surface, 0.97f, UIShape.Rounded).raycastTarget = true;
        Outline(rt, ThemeRole.Accent);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(28f, 0f);
        row.offsetMax = new Vector2(-16f, 0f);
        UIFactory.HorizontalStack(row, 20f);

        GuidanceBanner banner = rt.gameObject.AddComponent<GuidanceBanner>();
        banner.locationService = location;
        banner.root = rt.gameObject;

        banner.arrow = Arrow(row, location, frame, 56f);
        banner.colourDot = Disc(row, 26f, ThemeRole.Accent, UIShape.Circle, out RectTransform _);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.Size(text.gameObject, 100f);
        UIFactory.Flexible(text.gameObject);
        UIFactory.VerticalStack(text, 2f, false);

        banner.nameText = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(banner.nameText.gameObject, 56f);

        banner.distanceText = UIFactory.Text("Distance", text, Secondary, ThemeRole.Accent,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(banner.distanceText.gameObject, 46f);

        RectTransform stop = UIFactory.Rect("Stop", row);
        LayoutElement stopSize = UIFactory.Size(stop.gameObject, Tap, 190f);
        stopSize.flexibleWidth = 0f;
        banner.stopButton = UIFactory.Button(stop, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded);
        banner.stopLabel = UIFactory.Text("Label", stop, Secondary, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.Center, "guide.stop",
                                          FontStyles.Bold);
        UIFactory.Stretch(banner.stopLabel.rectTransform);

        rt.gameObject.SetActive(false);
        return banner;
    }

    static StateCardView BuildStateCard(RectTransform parent)
    {
        RectTransform rt = UIFactory.Rect("StateCard", parent);
        UIFactory.Corner(rt, new Vector2(0.5f, 0.5f), new Vector2(780f, 0f), new Vector2(0f, 90f));
        UIFactory.Card(rt, 38f, 18f, true);
        Outline(rt);

        StateCardView view = rt.gameObject.AddComponent<StateCardView>();
        view.root = rt.gameObject;

        view.icon = Icon(rt, AppIcon.Search, 60f, ThemeRole.TextMuted, out RectTransform iconRect);
        Unthemed(iconRect.gameObject); // the card drives this colour from its state

        view.locatingIcon = UIIcons.Get(AppIcon.MyLocation);
        view.loadingIcon = UIIcons.Get(AppIcon.MyLocation);
        view.emptyIcon = UIIcons.Get(AppIcon.Search);
        view.failedIcon = UIIcons.Get(AppIcon.Warning);
        view.offlineIcon = UIIcons.Get(AppIcon.Warning);

        view.titleText = UIFactory.Text("Title", rt, Title, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.titleText.gameObject, 64f);

        view.bodyText = UIFactory.Text("Body", rt, Secondary, ThemeRole.TextSecondary,
                                       TextAlignmentOptions.TopLeft);
        UIFactory.Size(view.bodyText.gameObject, 130f);

        RectTransform action = UIFactory.Rect("Action", rt);
        UIFactory.Size(action.gameObject, Tap);
        view.actionButton = UIFactory.Button(action, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(action.gameObject);

        view.actionLabel = UIFactory.Text("Label", action, Secondary, ThemeRole.OnAccent,
                                          TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.actionLabel.rectTransform);
        Unthemed(view.actionLabel.gameObject);

        rt.gameObject.SetActive(false);
        return view;
    }

    // ---------------------------------------------------------------- camera tab

    static CameraTabController BuildCameraTab(RectTransform panel, UserLocationService location,
                                              BuildingRowView rowPrefab, InfoPanelController info)
    {
        RectTransform live = UIFactory.Rect("LiveARView", panel);
        UIFactory.Stretch(live);

        CameraTabController controller = panel.gameObject.AddComponent<CameraTabController>();
        controller.locationService = location;
        controller.infoPanel = info;
        controller.liveARView = live.gameObject;
        controller.arSession = Object.FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        controller.cameraBackground =
            Object.FindFirstObjectByType<ARCameraBackground>(FindObjectsInactive.Include);

        if (controller.cameraBackground == null)
        {
            _notes.Add("No ARCameraBackground found, so the camera feed will keep " +
                       "drawing behind the map. Not fatal, just wasteful.");
        }

        RectTransform safe = SafeContent(live);

        controller.nearbyPanel = BuildNearbyPanel(live, location, rowPrefab);
        controller.vpsIndicator = BuildVpsIndicator(safe, location);

        BuildWarningBanner(safe, controller);

        // --- bottom stack: guidance above the dock, both above the nav bar ---
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, Nav + 20f);
        dockArea.sizeDelta = new Vector2(-Margin * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 16f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        // The radar sits in the same stack as the cards so it rises with them
        // when guidance appears, rather than being buried under it.
        controller.radar = BuildRadar(dockArea, location, controller);

        controller.guidance = BuildGuidanceBanner(dockArea, location,
                                                  BearingArrow.Frame.DeviceRelative);

        RectTransform dock = UIFactory.Rect("Dock", dockArea);
        UIFactory.Size(dock.gameObject, 150f);
        controller.dockButton = UIFactory.Button(dock, ThemeRole.Surface, 1f, UIShape.Rounded);
        controller.dock = dock.gameObject;
        Outline(dock);

        RectTransform dockRow = UIFactory.Rect("Row", dock);
        UIFactory.Stretch(dockRow);
        dockRow.offsetMin = new Vector2(28f, 0f);
        dockRow.offsetMax = new Vector2(-28f, 0f);
        UIFactory.HorizontalStack(dockRow, 22f);

        controller.dockRing = Disc(dockRow, 52f, ThemeRole.Line, UIShape.Ring,
                                   out RectTransform dockRingRect);
        controller.dockDot = Disc(dockRingRect, 32f, ThemeRole.Accent, UIShape.Circle,
                                  out RectTransform _);

        RectTransform dockText = UIFactory.Rect("Text", dockRow);
        UIFactory.Size(dockText.gameObject, 100f);
        UIFactory.Flexible(dockText.gameObject);
        UIFactory.VerticalStack(dockText, 2f, false);

        controller.dockCaption = UIFactory.Text("Caption", dockText, Caption,
                                                ThemeRole.TextMuted,
                                                TextAlignmentOptions.Left, "camera.nearest");
        UIFactory.Size(controller.dockCaption.gameObject, 40f);

        controller.dockName = UIFactory.Text("Name", dockText, Body, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.dockName.gameObject, 56f);

        controller.dockEmptyLabel = UIFactory.Text("Empty", dockRow, Secondary,
                                                   ThemeRole.TextMuted,
                                                   TextAlignmentOptions.Left, "camera.noMarkers");
        UIFactory.Size(controller.dockEmptyLabel.gameObject, 62f);
        UIFactory.Flexible(controller.dockEmptyLabel.gameObject);

        controller.dockArrow = Arrow(dockRow, location, BearingArrow.Frame.DeviceRelative, 40f);

        controller.dockDistance = UIFactory.Text("Distance", dockRow, Body, ThemeRole.Accent,
                                                 TextAlignmentOptions.Right, null, FontStyles.Bold);
        LayoutElement dockDistanceSize = UIFactory.Size(controller.dockDistance.gameObject,
                                                        60f, 180f);
        dockDistanceSize.flexibleWidth = 0f;

        // Siblings render in hierarchy order, so the sheet is raised last - otherwise
        // the dock sits on top of an open nearby list and its scrim covers nothing.
        if (controller.nearbyPanel != null)
        {
            if (controller.nearbyPanel.scrim != null)
                controller.nearbyPanel.scrim.transform.SetAsLastSibling();

            controller.nearbyPanel.transform.SetAsLastSibling();
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void BuildWarningBanner(RectTransform parent, CameraTabController controller)
    {
        RectTransform banner = UIFactory.Rect("WarningBanner", parent);
        banner.anchorMin = new Vector2(0f, 1f);
        banner.anchorMax = new Vector2(1f, 1f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(-Margin * 2f, 130f);
        banner.anchoredPosition = new Vector2(0f, -(Margin + 112f + 20f));
        UIFactory.Fill(banner, ThemeRole.Surface, 1f, UIShape.Rounded);

        // A glyph rather than a coloured background: the message has to stay
        // readable, and the state is then carried by shape as well as by hue.
        Image warning = Icon(banner, AppIcon.Warning, 44f, ThemeRole.StatusFair,
                             out RectTransform warningRect);
        UIFactory.Corner(warningRect, new Vector2(0f, 0.5f), new Vector2(44f, 44f),
                         new Vector2(34f, 0f));

        controller.warningLabel = UIFactory.Text("Label", banner, Secondary, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Left);
        UIFactory.Stretch(controller.warningLabel.rectTransform);
        controller.warningLabel.rectTransform.offsetMin = new Vector2(96f, 0f);
        controller.warningLabel.rectTransform.offsetMax = new Vector2(-(Tap + 10f), 0f);
        controller.warningLabel.text = AppStrings.Get("live.warning");

        RectTransform dismiss = UIFactory.Rect("Dismiss", banner);
        UIFactory.Corner(dismiss, new Vector2(1f, 0.5f), new Vector2(Tap, Tap),
                         new Vector2(-6f, 0f));
        controller.warningDismissButton =
            UIFactory.Button(dismiss, ThemeRole.Surface, 0f, UIShape.Circle);
        Icon(dismiss, AppIcon.Close, 40f, ThemeRole.TextSecondary);

        controller.warningBanner = banner.gameObject;
        banner.gameObject.SetActive(false);
    }

    /// <summary>
    /// The disc in the corner: every marker around the reader, and the slice the
    /// camera can actually see.
    ///
    /// Heading up rather than north up. On a screen whose whole subject is where
    /// the phone is pointing, a north-up radar would have to be read twice.
    ///
    /// Tapping it opens the nearby list, which is where the names are. The disc
    /// answers "what is around me" at a glance and hands off the rest, rather than
    /// trying to label three dots the size of a full stop.
    /// </summary>
    static CameraRadar BuildRadar(RectTransform parent, UserLocationService location,
                                  CameraTabController controller)
    {
        const float Size = 300f;
        const float Radius = Size * 0.5f;

        // A full-width row so the vertical stack keeps its rhythm; the disc itself
        // sits at the right-hand end of it.
        RectTransform row = UIFactory.Rect("RadarRow", parent);
        UIFactory.Size(row.gameObject, Size);

        RectTransform rt = UIFactory.Rect("Radar", row);
        UIFactory.Corner(rt, new Vector2(1f, 0.5f), new Vector2(Size, Size), Vector2.zero);

        // Tapping the disc opens the list. That is also what the nearby button
        // used to do, so the controller keeps its existing wiring.
        controller.nearbyButton = UIFactory.Button(rt, ThemeRole.Surface, 0.9f, UIShape.Circle);

        CameraRadar radar = rt.gameObject.AddComponent<CameraRadar>();
        radar.locationService = location;
        radar.radius = Radius;
        radar.dotSize = 26f;
        radar.maxRangeMetres = 500d;
        radar.circleSprite = UISprites.Circle;

        // The wedge, pointing up from the middle. Half a phone camera's field of
        // view either side of straight ahead.
        // The cone, pivoting on the reader. Its point sits at the centre and it
        // opens outward, fading as it goes - the shape Google Maps and Waze both
        // use, and the only thing on the disc that turns with the phone.
        //
        // Sized so its far corners stay inside the rim: at a 62 degree spread the
        // corners sit 1.17 radii from the point, so the height is held below that.
        RectTransform facing = UIFactory.Rect("Facing", rt);
        facing.anchorMin = new Vector2(0.5f, 0.5f);
        facing.anchorMax = new Vector2(0.5f, 0.5f);
        facing.pivot = new Vector2(0.5f, 0f);
        facing.sizeDelta = new Vector2(Radius * 1.02f, Radius * 0.85f);
        facing.anchoredPosition = Vector2.zero;

        Color beamTint = ThemeManager.Get(ThemeRole.Accent);
        beamTint.a = 0.55f;
        UIFactory.Plain(facing, beamTint, UIShape.Beam).raycastTarget = false;
        radar.facing = facing;

        Ring(rt, "InnerRing", Size * 0.52f, ThemeRole.TextPrimary, 0.10f, 2f);
        Ring(rt, "Rim", Size - 3f, ThemeRole.TextPrimary, 0.16f, 2f);

        // North is up and stays up, so it is worth one letter to say so.
        TextMeshProUGUI north = UIFactory.Text("North", rt, 26f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center);
        UIFactory.Corner(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(40f, 34f),
                         new Vector2(0f, -8f));
        north.text = "N";

        // Dots go above the rings and below the reader's own mark.
        RectTransform dots = UIFactory.Rect("Dots", rt);
        UIFactory.Stretch(dots);
        radar.dotParent = dots;

        RectTransform me = UIFactory.Rect("Me", rt);
        UIFactory.Corner(me, new Vector2(0.5f, 0.5f), new Vector2(16f, 16f), Vector2.zero);
        UIFactory.Plain(me, Color.white, UIShape.Circle).raycastTarget = false;

        return radar;
    }

    /// <summary>A circle outline drawn as geometry, crisp at any size.</summary>
    static RingGraphic Ring(Transform parent, string name, float size, ThemeRole role,
                            float alpha, float thickness)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Corner(rt, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);

        rt.gameObject.AddComponent<CanvasRenderer>();

        RingGraphic ring = rt.gameObject.AddComponent<RingGraphic>();
        ring.thickness = thickness;
        ring.raycastTarget = false;

        Color tint = ThemeManager.Get(role);
        tint.a = alpha;
        ring.color = tint;

        return ring;
    }

    static VpsIndicatorController BuildVpsIndicator(RectTransform parent,
                                                    UserLocationService location)
    {
        RectTransform rt = UIFactory.Rect("VpsIndicator", parent);
        UIFactory.Stretch(rt);

        // Same height as the nearby pill beside it, and on the same line.
        // A chip, not a bare dot. A coloured circle on its own is a colour; the
        // word beside it is what makes it a status, and it is the only calibration
        // state most readers will ever see.
        RectTransform dotButton = UIFactory.Rect("DotButton", rt);
        dotButton.anchorMin = new Vector2(0f, 1f);
        dotButton.anchorMax = new Vector2(0f, 1f);
        dotButton.pivot = new Vector2(0f, 1f);
        dotButton.sizeDelta = new Vector2(0f, 112f);
        dotButton.anchoredPosition = new Vector2(Margin, -Margin);

        Button button = UIFactory.Button(dotButton, ThemeRole.Surface, 1f, UIShape.Pill);

        HorizontalLayoutGroup chipLayout = UIFactory.HorizontalStack(dotButton, 14f);
        chipLayout.padding = new RectOffset(30, 34, 0, 0);
        chipLayout.childAlignment = TextAnchor.MiddleLeft;

        // Sized to its own words, so "Not calibrated" and its longer Bulgarian
        // counterpart both fit without a fixed width guessed for one of them.
        ContentSizeFitter chipFit = dotButton.gameObject.AddComponent<ContentSizeFitter>();
        chipFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        chipFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        RectTransform dot = UIFactory.Rect("Dot", dotButton);
        LayoutElement dotSize = UIFactory.Size(dot.gameObject, 26f, 26f);
        dotSize.flexibleWidth = 0f;
        Image dotImage = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.StatusNone),
                                         UIShape.Circle);
        dotImage.raycastTarget = false;

        TextMeshProUGUI dotLabel = UIFactory.Text("Label", dotButton, Secondary,
                                                  ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.Left);
        UIFactory.Size(dotLabel.gameObject, 112f);
        dotLabel.text = AppStrings.Get("vps.none.title");

        // The bubble sits under the button rather than taking the screen - the
        // reader is holding the phone up at a street while reading it.
        RectTransform bubble = UIFactory.Rect("Bubble", rt);
        bubble.anchorMin = new Vector2(0f, 1f);
        bubble.anchorMax = new Vector2(1f, 1f);
        bubble.pivot = new Vector2(0.5f, 1f);
        bubble.sizeDelta = new Vector2(-Margin * 2f, 0f);
        bubble.anchoredPosition = new Vector2(0f, -(Margin + 112f + 16f));
        UIFactory.Card(bubble, 32f, 12f, true);

        RectTransform head = UIFactory.Rect("Head", bubble);
        UIFactory.Size(head.gameObject, 56f);
        UIFactory.HorizontalStack(head, 18f);

        RectTransform bubbleDotRect = UIFactory.Rect("Dot", head);
        LayoutElement bubbleDotSize = UIFactory.Size(bubbleDotRect.gameObject, 34f, 34f);
        bubbleDotSize.flexibleWidth = 0f;
        Image bubbleDot = UIFactory.Plain(bubbleDotRect, ThemeManager.Get(ThemeRole.StatusNone),
                                          UIShape.Circle);
        bubbleDot.raycastTarget = false;

        TextMeshProUGUI title = UIFactory.Text("Title", head, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(title.gameObject, 56f);
        UIFactory.Flexible(title.gameObject);

        TextMeshProUGUI body = UIFactory.Text("Body", bubble, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft);
        UIFactory.Size(body.gameObject, 170f);

        TextMeshProUGUI accuracy = UIFactory.Text("Accuracy", bubble, Body, ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(accuracy.gameObject, 54f);

        // Index 0 keeps the catcher below the dot, so tapping the dot still toggles
        // while a tap anywhere else dismisses.
        Button scrimButton = CreateScrim("BubbleScrim", rt, out GameObject bubbleScrim, 0f);
        bubbleScrim.transform.SetSiblingIndex(0);

        AddFade(bubble.gameObject, 10f);

        VpsIndicatorController controller = rt.gameObject.AddComponent<VpsIndicatorController>();
        controller.scrim = bubbleScrim;
        controller.scrimButton = scrimButton;
        controller.locationService = location;
        controller.dotButton = button;
        controller.dotImage = dotImage;
        controller.dotLabel = dotLabel;
        controller.bubbleRoot = bubble.gameObject;
        controller.bubbleDot = bubbleDot;
        controller.bubbleTitle = title;
        controller.bubbleBody = body;
        controller.bubbleAccuracy = accuracy;

        bubble.gameObject.SetActive(false);
        return controller;
    }

    static NearbyPanelController BuildNearbyPanel(RectTransform live,
                                                  UserLocationService location,
                                                  BuildingRowView rowPrefab)
    {
        const float SheetHeight = 900f;

        RectTransform sheet = UIFactory.Rect("NearbyPanel", live);
        UIFactory.BottomStretch(sheet, SheetHeight, Nav);
        UIFactory.Fill(sheet, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;

        RectTransform head = UIFactory.Rect("Header", sheet);
        UIFactory.TopStretch(head, 90f, 100f);
        head.sizeDelta = new Vector2(-Margin * 2f, 90f);

        TextMeshProUGUI title = UIFactory.Text("Title", head, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "nearby.title",
                                               FontStyles.Bold);
        UIFactory.Stretch(title.rectTransform);
        title.rectTransform.offsetMax = new Vector2(-(Tap + 16f), 0f);

        RectTransform closeRect = UIFactory.Rect("CloseButton", head);
        UIFactory.Corner(closeRect, new Vector2(1f, 0.5f), new Vector2(Tap, Tap), Vector2.zero);
        Button close = UIFactory.Button(closeRect, ThemeRole.SurfaceAlt, 1f, UIShape.Circle);
        Icon(closeRect, AppIcon.Close, 42f, ThemeRole.TextSecondary);

        RectTransform listArea = UIFactory.Rect("ListArea", sheet);
        UIFactory.Stretch(listArea);
        listArea.offsetMin = new Vector2(Margin, Margin);
        listArea.offsetMax = new Vector2(-Margin, -200f);

        RectTransform content = UIFactory.ScrollList("Scroll", listArea);

        TextMeshProUGUI empty = UIFactory.Text("Empty", listArea, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center);
        UIFactory.Stretch(empty.rectTransform);

        NearbyPanelController controller = sheet.gameObject.AddComponent<NearbyPanelController>();
        controller.locationService = location;
        controller.sheet = sheet.gameObject.AddComponent<SheetAnimator>();
        controller.sheet.openY = Nav;
        controller.scrimButton = CreateScrim("NearbyScrim", live, out GameObject scrim, 0.55f);
        controller.scrim = scrim;

        SheetDragHandle drag = AddDragArea(sheet, controller.sheet);
        Wire(drag.onDismissed, controller.Close, "Nearby sheet drag");

        controller.listParent = content;
        controller.rowPrefab = rowPrefab;
        controller.titleText = title;
        controller.emptyText = empty;
        controller.closeButton = close;
        controller.radiusMetres = 500d;

        // Parked offscreen rather than disabled, so the controller keeps running and
        // the Game view is not blocked at edit time.
        sheet.anchoredPosition = new Vector2(0f, Nav - SheetHeight - 40f);
        return controller;
    }

    static void WireTapInput(RectTransform cameraPanel, InfoPanelController info)
    {
        CubeTapManager tap =
            Object.FindFirstObjectByType<CubeTapManager>(FindObjectsInactive.Include);

        if (tap == null)
        {
            _notes.Add("No CubeTapManager found - tapping a marker in the world will " +
                       "not open its sheet.");
            return;
        }

        // Gated to the camera panel, so a tap meant for the map never reaches a
        // marker standing behind it.
        tap.cameraPanel = cameraPanel.gameObject;
        tap.infoPanel = info;
        EditorUtility.SetDirty(tap);
    }

    // --------------------------------------------------------------------- toast

    /// <summary>
    /// The card that arrives when a marker comes into range. Built at the Canvas
    /// root so it shows on any tab.
    /// </summary>
    static ProximityToast BuildProximityToast(Transform root, UserLocationService location,
                                              InfoPanelController info)
    {
        RectTransform holder = UIFactory.Rect("ProximityToast", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        ProximityToast toast = holder.gameObject.AddComponent<ProximityToast>();
        toast.locationService = location;
        toast.infoPanel = info;
        toast.restY = Nav + 40f;
        toast.slideDistance = 240f;

        RectTransform safe = UIFactory.Rect("Safe", holder);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.left = true;
        pad.right = true;

        // Switched on and off by the toast. The component stays on the holder
        // above it, which has to keep running to hear the next alert.
        RectTransform cardHolder = UIFactory.Rect("CardHolder", safe);
        UIFactory.Stretch(cardHolder);
        toast.root = cardHolder.gameObject;

        RectTransform card = UIFactory.Rect("Card", cardHolder);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(1f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.sizeDelta = new Vector2(-Margin * 2f, 160f);
        card.anchoredPosition = new Vector2(0f, toast.restY);
        toast.card = card;

        toast.openButton = UIFactory.Button(card, ThemeRole.Surface, 1f, UIShape.Rounded);
        Outline(card, ThemeRole.Accent);

        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(28f, 0f);
        row.offsetMax = new Vector2(-16f, 0f);
        UIFactory.HorizontalStack(row, 20f);

        toast.colourRing = Disc(row, 52f, ThemeRole.Line, UIShape.Ring,
                                out RectTransform ringRect);
        toast.colourDot = Disc(ringRect, 32f, ThemeRole.Accent, UIShape.Circle,
                               out RectTransform _);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.Size(text.gameObject, 100f);
        UIFactory.Flexible(text.gameObject);
        UIFactory.VerticalStack(text, 2f, false);

        toast.captionText = UIFactory.Text("Caption", text, Caption, ThemeRole.TextMuted,
                                           TextAlignmentOptions.Left, "alert.caption");
        UIFactory.Size(toast.captionText.gameObject, 40f);

        toast.nameText = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(toast.nameText.gameObject, 56f);

        toast.distanceText = UIFactory.Text("Distance", row, Body, ThemeRole.Accent,
                                            TextAlignmentOptions.Right, null, FontStyles.Bold);
        LayoutElement distanceSize = UIFactory.Size(toast.distanceText.gameObject, 60f, 170f);
        distanceSize.flexibleWidth = 0f;

        RectTransform dismiss = UIFactory.Rect("Dismiss", row);
        LayoutElement dismissSize = UIFactory.Size(dismiss.gameObject, Tap, Tap);
        dismissSize.flexibleWidth = 0f;
        toast.dismissButton = UIFactory.Button(dismiss, ThemeRole.SurfaceAlt, 1f, UIShape.Circle);
        Icon(dismiss, AppIcon.Close, 40f, ThemeRole.TextSecondary);

        cardHolder.gameObject.SetActive(false);
        return toast;
    }

    // ------------------------------------------------------------------ tutorial

    /// <summary>
    /// The first-run walkthrough. Four cards over an opaque background, because it
    /// is explaining the screen behind it and a half-visible screen is a
    /// distraction rather than a demonstration.
    /// </summary>
    static TutorialFlow BuildTutorial(Transform root)
    {
        RectTransform holder = UIFactory.Rect("Tutorial", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        TutorialFlow flow = holder.gameObject.AddComponent<TutorialFlow>();

        // The overlay is what is shown and hidden. The holder above it has to stay
        // active or the flow never reaches Start, where the first run is decided.
        RectTransform overlay = UIFactory.Rect("Overlay", holder);
        UIFactory.Stretch(overlay);
        UIFactory.Fill(overlay, ThemeRole.Background).raycastTarget = true;
        flow.root = overlay.gameObject;

        RectTransform safe = SafeContent(overlay, padBottom: true);

        RectTransform inner = UIFactory.Rect("Inner", safe);
        UIFactory.Stretch(inner);
        inner.offsetMin = new Vector2(Margin, Margin);
        inner.offsetMax = new Vector2(-Margin, -Margin);

        RectTransform back = UIFactory.Rect("Back", inner);
        UIFactory.Corner(back, new Vector2(0f, 1f), new Vector2(240f, Tap), Vector2.zero);
        flow.backButton = UIFactory.Button(back, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
        flow.backLabel = UIFactory.Text("Label", back, Secondary, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Center, "settings.back",
                                        FontStyles.Bold);
        UIFactory.Stretch(flow.backLabel.rectTransform);
        back.gameObject.SetActive(false);

        RectTransform pages = UIFactory.Rect("Pages", inner);
        UIFactory.Stretch(pages);
        pages.offsetMin = new Vector2(0f, Tap * 2f + 120f);
        pages.offsetMax = new Vector2(0f, -(Tap + 40f));

        for (int i = 1; i <= 4; i++)
            flow.pages.Add(TutorialPage(pages, "tutorial." + i));

        RectTransform dots = UIFactory.Rect("Dots", inner);
        UIFactory.Corner(dots, new Vector2(0.5f, 0f), new Vector2(300f, 40f),
                         new Vector2(0f, Tap * 2f + 46f));
        HorizontalLayoutGroup dotsLayout = UIFactory.HorizontalStack(dots, 12f);
        dotsLayout.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < 4; i++)
        {
            RectTransform dot = UIFactory.Rect("Dot" + i, dots);
            LayoutElement dotSize = UIFactory.Size(dot.gameObject, 14f, 14f);
            dotSize.flexibleWidth = 0f;

            Image image = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.Line), UIShape.Pill);
            image.raycastTarget = false;
            flow.dots.Add(image);
        }

        RectTransform next = UIFactory.Rect("Next", inner);
        next.anchorMin = new Vector2(0f, 0f);
        next.anchorMax = new Vector2(1f, 0f);
        next.pivot = new Vector2(0.5f, 0f);
        next.sizeDelta = new Vector2(0f, Tap);
        next.anchoredPosition = new Vector2(0f, Tap + 16f);
        flow.nextButton = UIFactory.Button(next, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(next.gameObject);
        flow.nextLabel = UIFactory.Text("Label", next, Body, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(flow.nextLabel.rectTransform);
        Unthemed(flow.nextLabel.gameObject);

        RectTransform skip = UIFactory.Rect("Skip", inner);
        skip.anchorMin = new Vector2(0f, 0f);
        skip.anchorMax = new Vector2(1f, 0f);
        skip.pivot = new Vector2(0.5f, 0f);
        skip.sizeDelta = new Vector2(0f, Tap);
        skip.anchoredPosition = Vector2.zero;
        flow.skipButton = UIFactory.Button(skip, ThemeRole.Background, 0f);
        flow.skipLabel = UIFactory.Text("Label", skip, Secondary, ThemeRole.TextSecondary,
                                        TextAlignmentOptions.Center, "tutorial.skip");
        UIFactory.Stretch(flow.skipLabel.rectTransform);

        overlay.gameObject.SetActive(false);
        return flow;
    }

    static TutorialFlow.Page TutorialPage(RectTransform parent, string key)
    {
        RectTransform rt = UIFactory.Rect(key, parent);
        UIFactory.Stretch(rt);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(rt, 24f, false);
        stack.childAlignment = TextAnchor.LowerLeft;

        TutorialFlow.Page page = new TutorialFlow.Page();
        page.root = rt.gameObject;
        page.key = key;

        RectTransform spacer = UIFactory.Rect("Spacer", rt);
        LayoutElement spacerSize = spacer.gameObject.AddComponent<LayoutElement>();
        spacerSize.flexibleHeight = 1f;

        page.title = UIFactory.Text("Title", rt, Display, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.BottomLeft, null, FontStyles.Bold);
        UIFactory.Size(page.title.gameObject, 190f);

        page.body = UIFactory.Text("Body", rt, Body, ThemeRole.TextSecondary,
                                   TextAlignmentOptions.TopLeft);
        UIFactory.Size(page.body.gameObject, 260f);

        rt.gameObject.SetActive(false);
        return page;
    }

    // ---------------------------------------------------------------- browse tab

    static SearchTabController BuildBrowseTab(RectTransform panel, UserLocationService location,
                                              BuildingRowView rowPrefab,
                                              FilterPillView pillPrefab)
    {
        RectTransform rt = UIFactory.Rect("BrowseContent", SafeContent(panel));
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(Margin, Nav + Margin);
        rt.offsetMax = new Vector2(-Margin, -Margin);

        SearchTabController controller = rt.gameObject.AddComponent<SearchTabController>();
        controller.locationService = location;
        controller.rowPrefab = rowPrefab;
        controller.pillPrefab = pillPrefab;

        // Back first. Browse has no tab of its own, so without this the only way out
        // is the nav bar - and a screen whose exit is somewhere else is a trap.
        RectTransform back = UIFactory.Rect("BackButton", rt);
        UIFactory.Corner(back, new Vector2(0f, 1f), new Vector2(300f, Tap), Vector2.zero);
        controller.backButton = UIFactory.Button(back, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
        controller.backLabel = UIFactory.Text("Label", back, Secondary, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Center, "map.backToMap",
                                              FontStyles.Bold);
        UIFactory.Stretch(controller.backLabel.rectTransform);

        TextMeshProUGUI heading = UIFactory.Text("Heading", rt, Title, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Right, "tab.search",
                                                 FontStyles.Bold);
        UIFactory.Corner(heading.rectTransform, new Vector2(1f, 1f), new Vector2(460f, Tap),
                         Vector2.zero);

        RectTransform fieldRect = UIFactory.Rect("SearchField", rt);
        UIFactory.TopStretch(fieldRect, Tap, Tap + 20f);
        controller.searchField = UIFactory.InputField("Field", fieldRect, "search.placeholder",
                                                      out TextMeshProUGUI placeholder);
        UIFactory.Stretch((RectTransform)controller.searchField.transform);
        controller.searchPlaceholder = placeholder;

        // The glyph, and room made for it. A search box with no icon is a text
        // field, and the difference matters on a screen full of other text.
        Image searchGlyph = Icon(fieldRect, AppIcon.Search, 42f, ThemeRole.TextMuted,
                                 out RectTransform searchGlyphRect);
        UIFactory.Corner(searchGlyphRect, new Vector2(0f, 0.5f), new Vector2(42f, 42f),
                         new Vector2(34f, 0f));
        searchGlyph.transform.SetAsLastSibling();

        if (controller.searchField.textViewport != null)
        {
            controller.searchField.textViewport.offsetMin = new Vector2(96f, 8f);
            controller.searchField.textViewport.offsetMax = new Vector2(-30f, -8f);
        }

        // One scroll for the whole screen. The filter rows live inside it alongside
        // the results, so choosing a filter never takes the pills off screen.
        RectTransform body = UIFactory.Rect("Body", rt);
        UIFactory.Stretch(body);
        body.offsetMax = new Vector2(0f, -(Tap * 2f + 40f));
        RectTransform content = UIFactory.ScrollList("Scroll", body, 28f);

        RectTransform eras = UIFactory.Rect("EraSection", content);
        UIFactory.VerticalStack(eras, 14f, false);
        controller.eraLabel = SectionLabel(eras, "search.byEra");
        controller.eraPillParent = PillRow(eras);

        RectTransform types = UIFactory.Rect("TypeSection", content);
        UIFactory.VerticalStack(types, 14f, false);
        controller.typeLabel = SectionLabel(types, "search.byType");
        controller.typePillParent = PillRow(types);

        RectTransform closest = UIFactory.Rect("ClosestSection", content);
        UIFactory.VerticalStack(closest, 14f, false);
        controller.closestSection = closest.gameObject;
        controller.closestLabel = SectionLabel(closest, "search.closest");

        RectTransform closestList = UIFactory.Rect("List", closest);
        UIFactory.VerticalStack(closestList, 0f, false);
        controller.closestParent = closestList;

        controller.progressCard = BuildProgressCard(content);
        controller.discoverySection = controller.progressCard.gameObject;

        RectTransform results = UIFactory.Rect("ResultsSection", content);
        UIFactory.VerticalStack(results, 0f, false);
        controller.resultsSection = results.gameObject;

        TextMeshProUGUI empty = UIFactory.Text("Empty", results, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center, "search.empty");
        UIFactory.Size(empty.gameObject, 170f);
        controller.emptyText = empty;

        RectTransform resultsList = UIFactory.Rect("List", results);
        UIFactory.VerticalStack(resultsList, 0f, false);
        controller.resultsParent = resultsList;

        results.gameObject.SetActive(false);
        return controller;
    }

    static TextMeshProUGUI SectionLabel(Transform parent, string key)
    {
        TextMeshProUGUI label = UIFactory.Text("Label", parent, Caption, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left, key,
                                               FontStyles.UpperCase);
        UIFactory.Size(label.gameObject, 44f);
        label.characterSpacing = 6f;
        return label;
    }

    static RectTransform PillRow(Transform parent)
    {
        RectTransform row = UIFactory.Rect("Pills", parent);

        FlowLayoutGroup flow = row.gameObject.AddComponent<FlowLayoutGroup>();
        flow.spacingX = 16f;
        flow.spacingY = 16f;

        return row;
    }

    static DiscoveryProgressCard BuildProgressCard(Transform parent)
    {
        RectTransform card = UIFactory.Rect("DiscoveryCard", parent);
        UIFactory.Card(card, 32f, 16f);

        TextMeshProUGUI count = UIFactory.Text("Count", card, 76f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(count.gameObject, 92f);

        RectTransform bar = UIFactory.Rect("Bar", card);
        UIFactory.Size(bar.gameObject, 20f);
        UIFactory.Fill(bar, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);

        RectTransform fill = UIFactory.Rect("Fill", bar);
        UIFactory.Stretch(fill);
        UIFactory.Fill(fill, ThemeRole.Accent, 1f, UIShape.Pill);

        TextMeshProUGUI label = UIFactory.Text("Label", card, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left, "search.discovered");
        UIFactory.Size(label.gameObject, 46f);

        TextMeshProUGUI note = UIFactory.Text("Note", card, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft,
                                              "search.discoveredNote");
        UIFactory.Size(note.gameObject, 72f);

        DiscoveryProgressCard component = card.gameObject.AddComponent<DiscoveryProgressCard>();
        component.noteText = note;
        component.countText = count;
        component.labelText = label;
        component.barFill = fill;
        return component;
    }

    // -------------------------------------------------------------- settings tab

    static SettingsTabController BuildSettingsTab(RectTransform panel)
    {
        RectTransform rt = UIFactory.Rect("SettingsContent", SafeContent(panel));
        UIFactory.Stretch(rt);

        SettingsTabController controller = rt.gameObject.AddComponent<SettingsTabController>();
        SubPanelNavigator navigator = rt.gameObject.AddComponent<SubPanelNavigator>();
        controller.navigator = navigator;

        RectTransform rootView = UIFactory.Rect("Root", rt);
        UIFactory.Stretch(rootView);
        rootView.offsetMin = new Vector2(Margin, Nav + Margin);
        rootView.offsetMax = new Vector2(-Margin, -Margin);
        RectTransform content = UIFactory.ScrollList("Scroll", rootView, 26f);
        navigator.rootView = rootView.gameObject;

        controller.appearanceLabel = SectionLabel(content, "settings.appearance");
        RectTransform appearance = UIFactory.Rect("AppearanceCard", content);
        UIFactory.Card(appearance, 34f, 0f);
        controller.themeRowLabel = RowLabel(appearance, "settings.theme",
                                            out RectTransform themeRow);
        controller.themeToggle = UIFactory.Switch("Toggle", themeRow);
        Divider(appearance);
        controller.occlusionRowLabel = RowLabel(appearance, "settings.occlusion",
                                                out RectTransform occlusionRow);
        controller.occlusionToggle = UIFactory.Switch("Toggle", occlusionRow);

        controller.languageSectionLabel = SectionLabel(content, "settings.language");
        RectTransform language = UIFactory.Rect("LanguageCard", content);
        UIFactory.Card(language, 34f, 0f);
        controller.languageRowLabel = RowLabel(language, "settings.appLanguage",
                                               out RectTransform langRow);

        RectTransform segment = UIFactory.Rect("Segment", langRow);
        LayoutElement segmentSize = UIFactory.Size(segment.gameObject, 76f, 264f);
        segmentSize.flexibleWidth = 0f;
        UIFactory.Plain(segment, ThemeManager.Get(ThemeRole.SurfaceAlt), UIShape.Pill);
        HorizontalLayoutGroup segmentLayout = UIFactory.HorizontalStack(segment, 0f, true);
        segmentLayout.padding = new RectOffset(5, 5, 5, 5);

        controller.englishButton = SegmentHalf(segment, "EN", out controller.englishBackground,
                                               out controller.englishLabel);
        controller.bulgarianButton = SegmentHalf(segment, "BG",
                                                 out controller.bulgarianBackground,
                                                 out controller.bulgarianLabel);

        // Privacy: the reader's own control over what is allowed on their map.
        controller.privacyLabel = SectionLabel(content, "settings.privacy");
        RectTransform privacy = UIFactory.Rect("PrivacyCard", content);
        UIFactory.Card(privacy, 34f, 0f);
        controller.hiddenRowLabel = RowLabel(privacy, "settings.hidden",
                                             out RectTransform hiddenRow);
        controller.hiddenCountLabel = UIFactory.Text("Count", hiddenRow, Body,
                                                     ThemeRole.TextMuted,
                                                     TextAlignmentOptions.Right);
        LayoutElement hiddenCountSize = UIFactory.Size(controller.hiddenCountLabel.gameObject,
                                                        Tap, 260f);
        hiddenCountSize.flexibleWidth = 0f;

        Divider(privacy);

        // A row, not a filled button parked inside a card. It sits with the other
        // rows because it is the same kind of thing: a line you press.
        controller.unhideAllButton = ActionRow(privacy, "settings.unhideAll", ThemeRole.Accent,
                                               out controller.unhideAllLabel);

        controller.notificationsLabel = SectionLabel(content, "settings.notifications");
        RectTransform notifications = UIFactory.Rect("NotificationsCard", content);
        UIFactory.Card(notifications, 34f, 0f);
        controller.nearbyRowLabel = RowLabel(notifications, "settings.nearbyBuildings",
                                             out RectTransform nearbyRow);
        controller.nearbyToggle = UIFactory.Switch("Toggle", nearbyRow);

        // The heading says Notifications, and this fires while the app is open. The
        // note is what stops that from being a lie.
        controller.nearbyNoteLabel = UIFactory.Text("Note", notifications, Caption,
                                                    ThemeRole.TextMuted,
                                                    TextAlignmentOptions.TopLeft,
                                                    "settings.nearbyNote");
        UIFactory.Size(controller.nearbyNoteLabel.gameObject, 84f);

        controller.helpLabel = SectionLabel(content, "settings.help");
        RectTransform help = UIFactory.Rect("HelpCard", content);
        UIFactory.Card(help, 34f, 0f);
        controller.tutorialButton = HelpRow(help, "settings.replayTutorial",
                                            out controller.tutorialRowLabel);
        Divider(help);
        controller.sourcesButton = HelpRow(help, "settings.dataSources",
                                           out controller.sourcesRowLabel);
        Divider(help);
        controller.aboutButton = HelpRow(help, "settings.about", out controller.aboutRowLabel);

        RectTransform versionRow = UIFactory.Rect("Version", content);
        UIFactory.Size(versionRow.gameObject, Tap);
        controller.versionButton = UIFactory.Button(versionRow, ThemeRole.Background, 0f);
        controller.versionLabel = UIFactory.Text("Label", versionRow, Caption,
                                                 ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Center);
        UIFactory.Stretch(controller.versionLabel.rectTransform);

        controller.sourcesPage = SubPage(rt, "SourcesPage", "settings.dataSources",
                                         "sub.sources.body");
        controller.aboutPage = SubPage(rt, "AboutPage", "settings.about", "sub.about.body");

        navigator.pages = new List<GameObject>
        {
            controller.sourcesPage, controller.aboutPage
        };

        RectTransform backRect = UIFactory.Rect("BackButton", rt);
        UIFactory.Corner(backRect, new Vector2(0f, 1f), new Vector2(280f, Tap),
                         new Vector2(Margin, -Margin));
        navigator.backButton = UIFactory.Button(backRect, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
        navigator.backLabel = UIFactory.Text("Label", backRect, Secondary, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.Center, "settings.back",
                                             FontStyles.Bold);
        UIFactory.Stretch(navigator.backLabel.rectTransform);
        backRect.gameObject.SetActive(false);

        navigator.ShowRoot();
        return controller;
    }

    /// <summary>
    /// A settings row. Every row is the same height and separated by a rule, which
    /// is the difference between a settings screen and a stack of controls that
    /// happen to be near each other.
    /// </summary>
    static TextMeshProUGUI RowLabel(Transform card, string key, out RectTransform row)
    {
        row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Tap);
        UIFactory.HorizontalStack(row, 18f, false);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Size(label.gameObject, Tap);
        UIFactory.Flexible(label.gameObject);
        return label;
    }

    /// <summary>The rule between two rows of a card.</summary>
    static void Divider(Transform card)
    {
        RectTransform rt = UIFactory.Rect("Divider", card);
        UIFactory.Size(rt.gameObject, 2f);
        UIFactory.Fill(rt, ThemeRole.Line).raycastTarget = false;
    }

    /// <summary>A row whose whole width is the control, for an action rather than a value.</summary>
    static Button ActionRow(Transform card, string key, ThemeRole role,
                            out TextMeshProUGUI label)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Tap);

        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);

        label = UIFactory.Text("Label", row, Body, role,
                               TextAlignmentOptions.Left, key, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return button;
    }

    static Button HelpRow(Transform card, string key, out TextMeshProUGUI label)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Tap);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);

        label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMax = new Vector2(-56f, 0f);

        Image chevron = Icon(row, AppIcon.ChevronRight, 36f, ThemeRole.TextMuted,
                             out RectTransform chevronRect);
        UIFactory.Corner(chevronRect, new Vector2(1f, 0.5f), new Vector2(36f, 36f),
                         new Vector2(-14f, 0f));

        return button;
    }

    static Button SegmentHalf(Transform parent, string text, out Image background,
                              out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(text, parent);
        UIFactory.Size(rt.gameObject, 66f, 122f);

        // Plain, not Fill: SettingsTabController drives this colour from which
        // language is active, and a ThemedElement would overwrite it.
        background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        UIFactory.Feedback(button);

        label = UIFactory.Text("Label", rt, Caption, ThemeRole.TextMuted,
                               TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        label.text = text;
        Unthemed(label.gameObject);

        return button;
    }

    static GameObject SubPage(Transform parent, string name, string titleKey, string bodyKey)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(Margin, Nav + Margin);
        rt.offsetMax = new Vector2(-Margin, -(Margin + Tap + 20f));

        RectTransform content = UIFactory.ScrollList("Scroll", rt, 26f);

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, titleKey,
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        TextMeshProUGUI body = UIFactory.Text("Body", content, Body, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, bodyKey);
        UIFactory.Size(body.gameObject, 340f);

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    // --------------------------------------------------------------------- nav

    static RectTransform BuildNav(Transform root)
    {
        RectTransform nav = UIFactory.Rect("BottomNav", root);
        Undo.RegisterCreatedObjectUndo(nav.gameObject, "Build App UI");
        UIFactory.BottomStretch(nav, Nav);
        UIFactory.Fill(nav, ThemeRole.Surface).raycastTarget = true;

        // Half-strength, and one pixel. A hard rule across the whole width is the
        // single most Microsoft Paint thing a nav bar can do.
        RectTransform line = UIFactory.Rect("TopLine", nav);
        UIFactory.TopStretch(line, 1f);
        UIFactory.Fill(line, ThemeRole.Line, 0.5f).raycastTarget = false;

        // The fill runs to the bottom edge of the glass; only the buttons stop
        // short of the gesture bar. Padding the whole bar would leave a strip of
        // whatever is behind it along the bottom of the phone.
        RectTransform row = UIFactory.Rect("Row", nav);
        UIFactory.Stretch(row);

        SafeAreaPad pad = row.gameObject.AddComponent<SafeAreaPad>();
        pad.bottom = true;
        pad.left = true;
        pad.right = true;

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.padding = new RectOffset(0, 0, 10, 14);

        return row;
    }

    static TabNavigationController WireTabs(RectTransform navRow, RectTransform mapPanel,
                                            RectTransform cameraPanel,
                                            RectTransform browsePanel,
                                            RectTransform settingsPanel,
                                            SettingsTabController settingsTab)
    {
        TabNavigationController tabs =
            Object.FindFirstObjectByType<TabNavigationController>(FindObjectsInactive.Include);

        if (tabs == null)
        {
            GameObject go = new GameObject("UIManager");
            Undo.RegisterCreatedObjectUndo(go, "Build App UI");
            tabs = go.AddComponent<TabNavigationController>();
        }

        tabs.mapPanel = mapPanel.gameObject;
        tabs.cameraPanel = cameraPanel.gameObject;
        tabs.browsePanel = browsePanel.gameObject;
        tabs.settingsPanel = settingsPanel.gameObject;
        tabs.bottomNav = navRow.parent.gameObject;
        tabs.settingsTab = settingsTab;

        Button map = NavButton(navRow, "MapTabButton", "tab.map", NavGlyph.Map,
                               out tabs.mapLabel, out tabs.mapIcon,
                               out Image mapPill);
        Button camera = NavButton(navRow, "CameraTabButton", "tab.camera", NavGlyph.Camera,
                                  out tabs.cameraLabel, out tabs.cameraIcon,
                                  out Image cameraPill);
        Button settings = NavButton(navRow, "SettingsTabButton", "tab.settings",
                                    NavGlyph.Settings,
                                    out tabs.settingsLabel, out tabs.settingsIcon,
                                    out Image settingsPill);

        tabs.mapIndicator = mapPill;
        tabs.cameraIndicator = cameraPill;
        tabs.settingsIndicator = settingsPill;

        // Persistent listeners, not AddListener: a runtime listener added here is
        // thrown away when the scene is saved, and the tab bar would do nothing.
        Wire(map.onClick, tabs.ShowMap, "Map tab");
        Wire(camera.onClick, tabs.ShowCamera, "Camera tab");
        Wire(settings.onClick, tabs.ShowSettings, "Settings tab");

        EditorUtility.SetDirty(tabs);
        return tabs;
    }

    enum NavGlyph
    {
        Map,
        Camera,
        Settings
    }

    /// <summary>
    /// One tab: a pill that lights behind the icon, the icon, and the label under
    /// it, as one group in the middle of the bar.
    ///
    /// The first version pinned the icon to the top and the label to the bottom of
    /// a 190-unit bar, which left a hole between them and made two small marks
    /// floating in a slab.
    /// </summary>
    static Button NavButton(RectTransform navRow, string name, string key, NavGlyph glyph,
                            out TMP_Text label, out Graphic[] icon, out Image indicator)
    {
        RectTransform rt = UIFactory.Rect(name, navRow);

        // Invisible fill: the nav bar behind provides the surface, but the button
        // still needs a graphic to receive taps across its whole width.
        Image background = UIFactory.Plain(rt, new Color(1f, 1f, 1f, 0f));

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        UIFactory.Feedback(button);

        RectTransform pill = UIFactory.Rect("Indicator", rt);
        UIFactory.Corner(pill, new Vector2(0.5f, 0.5f), new Vector2(136f, 64f),
                         new Vector2(0f, 22f));
        indicator = UIFactory.Plain(pill, new Color(1f, 1f, 1f, 0f), UIShape.Pill);
        indicator.raycastTarget = false;

        RectTransform iconRect = UIFactory.Rect("Icon", rt);
        UIFactory.Corner(iconRect, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f),
                         new Vector2(0f, 22f));

        icon = DrawGlyph(iconRect, glyph);

        TextMeshProUGUI text = UIFactory.Text("Label", rt, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Center, key);
        UIFactory.Corner(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(240f, 36f),
                         new Vector2(0f, -36f));

        // TabNavigationController colours this from which tab is open, so it must
        // not also be themed automatically - the two would overwrite each other.
        Unthemed(text.gameObject);

        label = text;
        return button;
    }

    /// <summary>
    /// One rasterized sprite per tab. This used to stack rectangles and circles by
    /// eye, which is how the camera ended up reading as an apple - a round body
    /// with a stem off to one side.
    ///
    /// Still an array, because TabNavigationController colours whatever it is given
    /// and a future icon may want more than one piece.
    /// </summary>
    static Graphic[] DrawGlyph(RectTransform holder, NavGlyph glyph)
    {
        AppIcon icon = glyph == NavGlyph.Map ? AppIcon.MapPin
                     : glyph == NavGlyph.Camera ? AppIcon.Camera
                     : AppIcon.Settings;

        Image image = Icon(holder, icon, 54f, ThemeRole.TextMuted);
        Unthemed(image.gameObject); // the tab controller drives this colour

        return new Graphic[] { image };
    }

    // ----------------------------------------------------------------- prefabs

    static MapPinView CreateMapPinPrefab()
    {
        GameObject temp = new GameObject("MapPin", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;

        // A full-size hit area around a much smaller bulb, so a pin can be tapped
        // without the map being covered in circles.
        rt.sizeDelta = new Vector2(Tap, Tap);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image hit = UIFactory.Plain(rt, new Color(1f, 1f, 1f, 0f), UIShape.Circle);

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = hit;
        UIFactory.Feedback(button);

        RectTransform selection = UIFactory.Rect("Selection", rt);
        UIFactory.Corner(selection, new Vector2(0.5f, 0.5f), new Vector2(66f, 66f), Vector2.zero);
        Image selectionImage = UIFactory.Plain(selection, ThemeManager.Get(ThemeRole.Accent),
                                               UIShape.Ring);
        selectionImage.raycastTarget = false;
        selection.gameObject.SetActive(false);

        RectTransform ring = UIFactory.Rect("Ring", rt);
        UIFactory.Corner(ring, new Vector2(0.5f, 0.5f), new Vector2(34f, 34f), Vector2.zero);
        Image ringImage = UIFactory.Plain(ring, Color.white, UIShape.Circle);
        ringImage.raycastTarget = false;

        RectTransform bulb = UIFactory.Rect("Bulb", rt);
        UIFactory.Corner(bulb, new Vector2(0.5f, 0.5f), new Vector2(26f, 26f), Vector2.zero);
        Image bulbImage = UIFactory.Plain(bulb, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        bulbImage.raycastTarget = false;

        // The label rides above the bulb and never sits on it: a pin whose label is
        // centred on the coordinate lies about where the marker is by half a label.
        RectTransform plate = UIFactory.Rect("Label", rt);
        plate.anchorMin = new Vector2(0.5f, 0.5f);
        plate.anchorMax = new Vector2(0.5f, 0.5f);
        plate.pivot = new Vector2(0.5f, 0f);
        plate.sizeDelta = new Vector2(320f, 50f);
        plate.anchoredPosition = new Vector2(0f, 24f);

        Image plateImage = UIFactory.Plain(plate, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);
        plateImage.raycastTarget = false;

        TextMeshProUGUI label = UIFactory.Text("Text", plate, Caption, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(18f, 0f);
        label.rectTransform.offsetMax = new Vector2(-18f, 0f);
        label.overflowMode = TextOverflowModes.Ellipsis;

        MapPinView view = temp.AddComponent<MapPinView>();
        view.button = button;
        view.ring = ringImage;
        view.bulb = bulbImage;
        view.selection = selectionImage;
        view.label = label;
        view.labelPlate = plateImage;
        view.bulbSize = 26f;
        view.selectedBulbSize = 34f;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, MapPinPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<MapPinView>();
    }

    static GameObject CreateMarkerPrefab()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temp.name = "BuildingMarker";

        // Generous hit area: the visual is 1.5 m but the collider is wider, because
        // a marker 80 m away is only a few pixels of screen to aim at.
        BoxCollider collider = temp.GetComponent<BoxCollider>();
        collider.size = Vector3.one * 2f;

        MarkerView view = temp.AddComponent<MarkerView>();
        view.tintedRenderers = new[] { temp.GetComponent<Renderer>() };
        view.baseScale = 1.5f;
        view.clampDistance = 60f;
        view.billboard = false;

        temp.AddComponent<TappableObject>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, MarkerPrefabPath);
        Object.DestroyImmediate(temp);

        return saved;
    }

    static BuildingRowView CreateRowPrefab()
    {
        GameObject temp = new GameObject("BuildingRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, Tap);
        UIFactory.Size(temp, Tap);

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0f);

        RectTransform main = UIFactory.Rect("Main", rt);
        UIFactory.Stretch(main);
        main.offsetMax = new Vector2(-190f, 0f);
        UIFactory.VerticalStack(main, 4f, false);

        TextMeshProUGUI name = UIFactory.Text("Name", main, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(name.gameObject, 56f);

        TextMeshProUGUI subtitle = UIFactory.Text("Subtitle", main, Caption, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Left);
        UIFactory.Size(subtitle.gameObject, 44f);

        TextMeshProUGUI distance = UIFactory.Text("Distance", rt, Secondary,
                                                  ThemeRole.TextSecondary,
                                                  TextAlignmentOptions.Right);
        UIFactory.Corner(distance.rectTransform, new Vector2(1f, 0.5f), new Vector2(180f, 56f),
                         Vector2.zero);

        RectTransform line = UIFactory.Rect("Separator", rt);
        UIFactory.BottomStretch(line, 2f);
        UIFactory.Fill(line, ThemeRole.Line);

        BuildingRowView view = temp.AddComponent<BuildingRowView>();
        view.nameText = name;
        view.subtitleText = subtitle;
        view.distanceText = distance;
        view.button = button;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, RowPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<BuildingRowView>();
    }

    static FilterPillView CreatePillPrefab()
    {
        GameObject temp = new GameObject("FilterPill", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(220f, Tap);

        // No ThemedElement on either of these: FilterPillView drives both colours
        // itself, because they depend on whether the pill is the active filter.
        Image background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = background;
        UIFactory.Feedback(button);

        RectTransform labelRect = UIFactory.Rect("Label", rt);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = Secondary;
        label.alignment = TextAlignmentOptions.Center;
        label.color = ThemeManager.Get(ThemeRole.TextSecondary);
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        UIFactory.Stretch(labelRect);

        // No ContentSizeFitter and no layout group here. FilterPillView measures its
        // own label and writes the result to this LayoutElement, which the parent
        // FlowLayoutGroup then reads - one measurement instead of nested auto-sizing,
        // which is what crashed the editor on play-mode exit.
        LayoutElement element = temp.AddComponent<LayoutElement>();
        element.preferredHeight = Tap;
        element.minHeight = Tap;

        FilterPillView view = temp.AddComponent<FilterPillView>();
        view.label = label;
        view.background = background;
        view.button = button;
        view.layoutElement = element;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PillPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<FilterPillView>();
    }


    /// <summary>
    /// The trail name at the top of the map, and the way to change trail.
    ///
    /// Returns the name label, because MapTabController still holds it as its
    /// title - it is the same piece of text doing a better job, not a new one.
    /// </summary>
    static TextMeshProUGUI BuildTrailChip(Transform parent, out RectTransform rect)
    {
        rect = UIFactory.Rect("TrailChip", parent);

        TrailChipView view = rect.gameObject.AddComponent<TrailChipView>();
        view.button = UIFactory.Button(rect, ThemeRole.Surface, 0f, UIShape.Pill);

        RectTransform row = UIFactory.Rect("Row", rect);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(18f, 0f);
        row.offsetMax = new Vector2(-18f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 12f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        view.nameLabel = UIFactory.Text("Name", row, Title, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, "tab.map", FontStyles.Bold);
        LayoutElement nameSize = UIFactory.Size(view.nameLabel.gameObject, 84f, 340f);
        nameSize.flexibleWidth = 0f;

        // One line, cut with an ellipsis. Autosizing alone was not enough: a name
        // that will not fit at the smallest size wraps instead, and the chip is one
        // line tall - which put "Ямбол - център" through its own top edge.
        view.nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        view.nameLabel.enableAutoSizing = true;
        view.nameLabel.fontSizeMin = 36f;
        view.nameLabel.fontSizeMax = Title;

        // The mode chip only appears on a trail whose mode changes the rules, so
        // seeing one means something rather than being decoration on every trail.
        // Before the chevron: it belongs to the name, and the chevron belongs to
        // the whole control.
        RectTransform mode = UIFactory.Rect("Mode", row);
        LayoutElement modeSize = UIFactory.Size(mode.gameObject, 50f, 200f);
        modeSize.flexibleWidth = 0f;
        UIFactory.Fill(mode, ThemeRole.Accent, 0.18f, UIShape.Pill).raycastTarget = false;
        view.modeChip = mode.gameObject;

        view.modeLabel = UIFactory.Text("Label", mode, 26f, ThemeRole.Accent,
                                        TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.modeLabel.rectTransform);
        view.modeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.modeLabel.overflowMode = TextOverflowModes.Ellipsis;

        Icon(row, AppIcon.ChevronRight, 34f, ThemeRole.TextMuted, out RectTransform chevron);
        view.chevron = chevron.gameObject;

        return view.nameLabel;
    }


    /// <summary>
    /// The page a video or a recording plays on.
    ///
    /// Both are the same page. A recording has no picture, so the frame comes off
    /// and the transport moves up; everything else - the title, the clock, the
    /// bar - is the same control doing the same job, which is one fewer thing for
    /// a reader to learn and one fewer screen to keep in step.
    /// </summary>
    static MediaPlayerView BuildMediaPage(RectTransform parent)
    {
        RectTransform page = UIFactory.Rect("Media", parent);
        UIFactory.Stretch(page);

        MediaPlayerView view = page.gameObject.AddComponent<MediaPlayerView>();
        view.playSprite = UIIcons.Get(AppIcon.Play);
        view.pauseSprite = UIIcons.Get(AppIcon.Pause);

        // Stacked, not pinned.
        //
        // The first version anchored the title and the transport below a frame of
        // a fixed height, which is right for a video and wrong for a recording -
        // the audio page came out with the controls halfway down the sheet and a
        // 553-pixel hole above them. A layout closes the gap by itself the moment
        // the frame is switched off.
        UIFactory.VerticalStack(page, 24f, false);

        // 16:9 at the sheet's inner width. A fixed frame, not one sized from the
        // clip - a portrait video and a landscape one both belong in the same hole,
        // or opening one shuffles the whole page.
        const float ScreenHeight = 553f;

        RectTransform screen = UIFactory.Rect("Screen", page);
        UIFactory.Size(screen.gameObject, ScreenHeight);
        UIFactory.Fill(screen, ThemeRole.Background, 1f, UIShape.Rounded).raycastTarget = false;
        view.screenHolder = screen.gameObject;

        RectTransform picture = UIFactory.Rect("Picture", screen);
        UIFactory.Stretch(picture);
        picture.offsetMin = new Vector2(4f, 4f);
        picture.offsetMax = new Vector2(-4f, -4f);
        view.screen = picture.gameObject.AddComponent<RawImage>();
        view.screen.raycastTarget = false;

        // Off until a video has actually been decoded into it. A RawImage with no
        // texture draws a solid white rectangle, and this one is 984 by 553.
        view.screen.enabled = false;

        // The page's heading says what kind of thing this is - Watch, Listen - and
        // this says which one.
        TextMeshProUGUI title = UIFactory.Text("Title", page, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, null,
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 70f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        view.titleLabel = title;

        // --- loading, or not going to play ---
        RectTransform notice = UIFactory.Rect("Notice", page);
        UIFactory.Size(notice.gameObject, 92f);
        UIFactory.Fill(notice, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        view.noticeHolder = notice.gameObject;

        view.noticeLabel = UIFactory.Text("Label", notice, Secondary, ThemeRole.TextSecondary,
                                          TextAlignmentOptions.Left);
        UIFactory.Stretch(view.noticeLabel.rectTransform);
        view.noticeLabel.rectTransform.offsetMin = new Vector2(28f, 0f);
        view.noticeLabel.rectTransform.offsetMax = new Vector2(-28f, 0f);
        notice.gameObject.SetActive(false);

        // --- transport ---
        RectTransform transport = UIFactory.Rect("Transport", page);
        UIFactory.Size(transport.gameObject, Tap);

        RectTransform play = UIFactory.Rect("Play", transport);
        UIFactory.Corner(play, new Vector2(0f, 0.5f), new Vector2(Tap, Tap), Vector2.zero);
        view.playButton = UIFactory.Button(play, ThemeRole.Accent, 1f, UIShape.Circle);
        Unthemed(play.gameObject);

        view.playIcon = Icon(play, AppIcon.Play, 52f, ThemeRole.OnAccent);
        Unthemed(view.playIcon.gameObject);

        RectTransform track = UIFactory.Rect("Track", transport);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.pivot = new Vector2(0.5f, 0.5f);
        track.offsetMin = new Vector2(Tap + 26f, -7f);
        track.offsetMax = new Vector2(-210f, 7f);
        UIFactory.Fill(track, ThemeRole.Line, 1f, UIShape.Pill).raycastTarget = false;

        RectTransform fill = UIFactory.Rect("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        UIFactory.Fill(fill, ThemeRole.Accent, 1f, UIShape.Pill).raycastTarget = false;
        view.progressFill = fill;

        TextMeshProUGUI clock = UIFactory.Text("Time", transport, Caption,
                                               ThemeRole.TextSecondary,
                                               TextAlignmentOptions.Right);
        UIFactory.Corner(clock.rectTransform, new Vector2(1f, 0.5f), new Vector2(196f, 50f),
                         Vector2.zero);
        view.timeLabel = clock;

        // --- the question asked before spending somebody's data ---
        RectTransform data = UIFactory.Rect("DataWarning", page);
        UIFactory.Size(data.gameObject, 190f);
        UIFactory.Fill(data, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = true;
        Outline(data);
        view.dataHolder = data.gameObject;

        view.dataLabel = UIFactory.Text("Label", data, Secondary, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.TopLeft);
        UIFactory.TopStretch(view.dataLabel.rectTransform, 56f, 22f);
        view.dataLabel.rectTransform.offsetMin = new Vector2(26f, 0f);
        view.dataLabel.rectTransform.offsetMax = new Vector2(-26f, 0f);

        RectTransform dataPlay = UIFactory.Rect("Play", data);
        UIFactory.BottomStretch(dataPlay, Tap, 20f);
        dataPlay.sizeDelta = new Vector2(-52f, Tap);
        view.dataPlayButton = UIFactory.Button(dataPlay, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(dataPlay.gameObject);

        TextMeshProUGUI dataPlayLabel = UIFactory.Text("Label", dataPlay, Secondary,
                                                       ThemeRole.OnAccent,
                                                       TextAlignmentOptions.Center,
                                                       "media.dataPlay", FontStyles.Bold);
        UIFactory.Stretch(dataPlayLabel.rectTransform);
        Unthemed(dataPlayLabel.gameObject);
        data.gameObject.SetActive(false);

        page.gameObject.SetActive(false);
        return view;
    }

    /// <summary>
    /// The question, the answer, and the four ways a wrong one can end.
    ///
    /// Every part of it scrolls together, in the order the reader meets it: the
    /// creator's rule first because it is a warning, then the question, then the
    /// options, then whatever the server said back. The bubble sits directly under
    /// the answer it is about rather than in a corner of the screen.
    /// </summary>
    static MarkerQuestionView BuildQuestionPage(RectTransform parent,
                                                AnswerOptionView optionPrefab)
    {
        RectTransform page = UIFactory.Rect("Question", parent);
        UIFactory.Stretch(page);

        MarkerQuestionView view = page.gameObject.AddComponent<MarkerQuestionView>();
        view.optionPrefab = optionPrefab;

        RectTransform content = UIFactory.ScrollList("Scroll", page, 16f);
        view.scroll = page.GetComponentInChildren<ScrollRect>();

        // --- the creator's rule, said before it is applied ---
        RectTransform rule = UIFactory.Rect("Rule", content);
        UIFactory.Size(rule.gameObject, 52f);
        UIFactory.HorizontalStack(rule, 10f);
        view.ruleHolder = rule.gameObject;

        RectTransform ruleChip = UIFactory.Rect("Chip", rule);
        LayoutElement ruleSize = UIFactory.Size(ruleChip.gameObject, 52f, 430f);
        ruleSize.flexibleWidth = 0f;
        UIFactory.Fill(ruleChip, ThemeRole.StatusFair, 0.16f, UIShape.Pill).raycastTarget = false;

        RectTransform ruleRow = UIFactory.Rect("Row", ruleChip);
        UIFactory.Stretch(ruleRow);
        ruleRow.offsetMin = new Vector2(18f, 0f);
        ruleRow.offsetMax = new Vector2(-18f, 0f);
        HorizontalLayoutGroup ruleLayout = UIFactory.HorizontalStack(ruleRow, 10f);
        ruleLayout.childAlignment = TextAnchor.MiddleLeft;

        Icon(ruleRow, AppIcon.Warning, 28f, ThemeRole.StatusFair);

        view.ruleLabel = UIFactory.Text("Label", ruleRow, 28f, ThemeRole.StatusFair,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.ruleLabel.gameObject, 52f);
        UIFactory.Flexible(view.ruleLabel.gameObject);
        rule.gameObject.SetActive(false);

        // --- the question ---
        view.promptLabel = UIFactory.Text("Prompt", content, Title, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.TopLeft, null, FontStyles.Bold);


        RectTransform options = UIFactory.Rect("Options", content);
        UIFactory.VerticalStack(options, 12f, false);
        view.optionsParent = options;

        // --- or a typed answer ---
        RectTransform open = UIFactory.Rect("Open", content);
        UIFactory.VerticalStack(open, 14f, false);
        view.openHolder = open.gameObject;

        view.openField = UIFactory.InputField("Field", open, "task.openHint",
                                              out TextMeshProUGUI placeholder);
        view.openPlaceholder = placeholder;

        RectTransform check = UIFactory.Rect("Check", open);
        UIFactory.Size(check.gameObject, Tap);
        view.checkHolder = check.gameObject;
        view.checkButton = UIFactory.Button(check, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(check.gameObject);

        view.checkLabel = UIFactory.Text("Label", check, Body, ThemeRole.OnAccent,
                                         TextAlignmentOptions.Center, "task.check",
                                         FontStyles.Bold);
        UIFactory.Stretch(view.checkLabel.rectTransform);
        Unthemed(view.checkLabel.gameObject);
        open.gameObject.SetActive(false);

        // --- what came back ---
        RectTransform bubble = UIFactory.Rect("Bubble", content);
        view.bubbleFill = UIFactory.Plain(bubble, ThemeManager.Get(ThemeRole.SurfaceAlt),
                                          UIShape.Rounded);
        view.bubbleHolder = bubble.gameObject;

        ContentSizeFitter bubbleFit = bubble.gameObject.AddComponent<ContentSizeFitter>();
        bubbleFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup bubbleLayout = UIFactory.VerticalStack(bubble, 0f, false);
        bubbleLayout.padding = new RectOffset(28, 28, 22, 22);

        view.bubbleLabel = UIFactory.Text("Label", bubble, Secondary, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.TopLeft);


        // A stripe rather than a solid fill. A block of red for a guess is a
        // telling-off; a line beside the words is an answer.
        RectTransform edge = UIFactory.Rect("Edge", bubble);
        edge.anchorMin = new Vector2(0f, 0f);
        edge.anchorMax = new Vector2(0f, 1f);
        edge.pivot = new Vector2(0f, 0.5f);
        edge.sizeDelta = new Vector2(8f, 0f);
        edge.anchoredPosition = Vector2.zero;
        view.bubbleEdge = UIFactory.Plain(edge, ThemeManager.Get(ThemeRole.Line), UIShape.Pill);
        view.bubbleEdge.raycastTarget = false;
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        bubble.gameObject.SetActive(false);

        // --- locked out ---
        RectTransform locked = UIFactory.Rect("Lock", content);
        UIFactory.Size(locked.gameObject, Tap);
        UIFactory.Fill(locked, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        view.lockHolder = locked.gameObject;

        RectTransform lockRow = UIFactory.Rect("Row", locked);
        UIFactory.Stretch(lockRow);
        lockRow.offsetMin = new Vector2(26f, 0f);
        lockRow.offsetMax = new Vector2(-26f, 0f);
        HorizontalLayoutGroup lockLayout = UIFactory.HorizontalStack(lockRow, 16f);
        lockLayout.childAlignment = TextAnchor.MiddleLeft;

        Icon(lockRow, AppIcon.Lock, 40f, ThemeRole.StatusFair);

        view.lockLabel = UIFactory.Text("Label", lockRow, Secondary, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.lockLabel.gameObject, Tap);
        UIFactory.Flexible(view.lockLabel.gameObject);
        locked.gameObject.SetActive(false);

        // --- the running score, for the rule that moves it ---
        RectTransform score = UIFactory.Rect("Score", content);
        UIFactory.Size(score.gameObject, 56f);
        UIFactory.HorizontalStack(score, 10f);
        view.scoreHolder = score.gameObject;

        view.scoreLabel = UIFactory.Text("Label", score, Secondary, ThemeRole.Accent,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.scoreLabel.gameObject, 56f);
        UIFactory.Flexible(view.scoreLabel.gameObject);
        score.gameObject.SetActive(false);

        // --- one attempt, spent ---
        RectTransform finished = UIFactory.Rect("Finished", page);
        UIFactory.Stretch(finished);
        UIFactory.Fill(finished, ThemeRole.Surface).raycastTarget = true;
        view.finishedHolder = finished.gameObject;

        RectTransform finishedStack = UIFactory.Rect("Stack", finished);
        UIFactory.Stretch(finishedStack);
        finishedStack.offsetMin = new Vector2(0f, Tap + 40f);
        VerticalLayoutGroup finishedLayout = UIFactory.VerticalStack(finishedStack, 18f, false);
        finishedLayout.childAlignment = TextAnchor.MiddleLeft;

        Icon(finishedStack, AppIcon.Block, 72f, ThemeRole.TextMuted);

        view.finishedTitle = UIFactory.Text("Title", finishedStack, Title,
                                            ThemeRole.TextPrimary,
                                            TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.finishedTitle.gameObject, 76f);

        view.finishedBody = UIFactory.Text("Body", finishedStack, Body,
                                           ThemeRole.TextSecondary,
                                           TextAlignmentOptions.TopLeft);
        UIFactory.Size(view.finishedBody.gameObject, 100f);

        RectTransform finishedButton = UIFactory.Rect("Button", finished);
        UIFactory.BottomStretch(finishedButton, Tap);
        view.finishedButton = UIFactory.Button(finishedButton, ThemeRole.SurfaceAlt, 1f,
                                               UIShape.Rounded);

        view.finishedButtonLabel = UIFactory.Text("Label", finishedButton, Secondary,
                                                  ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.Center,
                                                  "task.backToMap", FontStyles.Bold);
        UIFactory.Stretch(view.finishedButtonLabel.rectTransform);
        finished.gameObject.SetActive(false);

        page.gameObject.SetActive(false);
        return view;
    }

    // ------------------------------------------------------------------ prefabs

    static TrailRowView CreateTrailRowPrefab()
    {
        GameObject temp = new GameObject("TrailRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 132f);
        UIFactory.Size(temp, 132f);

        TrailRowView view = temp.AddComponent<TrailRowView>();
        view.button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(26f, 0f);
        row.offsetMax = new Vector2(-26f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 20f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        view.dot = Disc(row, 30f, ThemeRole.Accent, UIShape.Circle, out RectTransform _);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 4f, false);
        UIFactory.Flexible(text.gameObject);

        view.nameLabel = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.nameLabel.gameObject, 54f);
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;

        view.metaLabel = UIFactory.Text("Meta", text, Caption, ThemeRole.TextMuted,
                                        TextAlignmentOptions.Left);
        UIFactory.Size(view.metaLabel.gameObject, 42f);

        Image tick = Icon(row, AppIcon.Check, 40f, ThemeRole.Accent, out RectTransform tickRect);
        view.currentMark = tickRect.gameObject;
        tickRect.gameObject.SetActive(false);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, TrailRowPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<TrailRowView>();
    }

    static PieceRowView CreatePieceRowPrefab()
    {
        GameObject temp = new GameObject("PieceRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 116f);
        UIFactory.Size(temp, 116f);

        PieceRowView view = temp.AddComponent<PieceRowView>();
        view.button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(24f, 0f);
        row.offsetMax = new Vector2(-24f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 18f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        view.icon = Icon(row, AppIcon.Video, 42f, ThemeRole.Accent);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 2f, false);
        UIFactory.Flexible(text.gameObject);

        view.caption = UIFactory.Text("Caption", text, 27f, ThemeRole.TextMuted,
                                      TextAlignmentOptions.Left);
        UIFactory.Size(view.caption.gameObject, 34f);

        view.titleLabel = UIFactory.Text("Title", text, Secondary, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.titleLabel.gameObject, 48f);
        view.titleLabel.overflowMode = TextOverflowModes.Ellipsis;

        Icon(row, AppIcon.ChevronRight, 34f, ThemeRole.TextMuted, out RectTransform chevron);
        view.chevron = chevron.gameObject;

        view.tickImage = Icon(row, AppIcon.Check, 36f, ThemeRole.StatusGood,
                              out RectTransform tick);
        view.tick = tick.gameObject;
        tick.gameObject.SetActive(false);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PieceRowPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<PieceRowView>();
    }

    static AnswerOptionView CreateOptionPrefab()
    {
        GameObject temp = new GameObject("AnswerOption", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 116f);
        UIFactory.Size(temp, 116f);

        AnswerOptionView view = temp.AddComponent<AnswerOptionView>();
        view.tickSprite = UIIcons.Get(AppIcon.Check);
        view.crossSprite = UIIcons.Get(AppIcon.Close);

        // Plain, not Fill: AnswerOptionView drives these colours itself from the
        // state, and a ThemedElement would repaint over it on every theme event.
        view.background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.SurfaceAlt),
                                          UIShape.Rounded);

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = view.background;
        UIFactory.Feedback(button);
        view.button = button;

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(26f, 0f);
        row.offsetMax = new Vector2(-26f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 20f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform mark = UIFactory.Rect("Mark", row);
        LayoutElement markSize = UIFactory.Size(mark.gameObject, 36f, 36f);
        markSize.flexibleWidth = 0f;
        view.marker = UIFactory.Plain(mark, ThemeManager.Get(ThemeRole.Line), UIShape.Ring);
        view.marker.raycastTarget = false;

        RectTransform labelRect = UIFactory.Rect("Label", row);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = Secondary;
        label.alignment = TextAlignmentOptions.Left;
        label.color = ThemeManager.Get(ThemeRole.TextPrimary);
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        UIFactory.Size(labelRect.gameObject, 116f);
        UIFactory.Flexible(labelRect.gameObject);
        view.label = label;

        RectTransform verdict = UIFactory.Rect("Verdict", row);
        LayoutElement verdictSize = UIFactory.Size(verdict.gameObject, 40f, 40f);
        verdictSize.flexibleWidth = 0f;
        view.verdictImage = UIFactory.Plain(verdict, ThemeManager.Get(ThemeRole.StatusPoor));
        view.verdictImage.sprite = UIIcons.Get(AppIcon.Close);
        view.verdictImage.type = Image.Type.Simple;
        view.verdictImage.preserveAspect = true;
        view.verdictImage.raycastTarget = false;
        view.verdictIcon = verdict.gameObject;
        verdict.gameObject.SetActive(false);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, OptionPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<AnswerOptionView>();
    }


    // --------------------------------------------------------------- the way in

    /// <summary>
    /// The four screens a reader passes through before the map is theirs, and the
    /// blurred still they stand on.
    ///
    /// Built last and parented at the Canvas root, so they draw over every tab and
    /// over the nav bar - which DoorFlow also switches off. A modal with a live tab
    /// bar behind it is a tab bar people tap straight past.
    /// </summary>
    static DoorFlow BuildDoors(Transform root, GameObject navBar, TrailRowView trailRowPrefab)
    {
        RectTransform holder = UIFactory.Rect("Doors", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        DoorFlow flow = holder.gameObject.AddComponent<DoorFlow>();
        flow.navBar = navBar;
        flow.backdrop = BuildBackdrop(holder);

        flow.welcomePanel = BuildWelcome(holder, flow);
        flow.termsPanel = BuildTerms(holder, flow);
        flow.trailsPanel = BuildTrailsDoor(holder, flow, trailRowPrefab, out TrailsDoor trails);
        flow.codePanel = BuildCodeDoor(holder, flow, out CodeDoor code);

        flow.trails = trails;
        flow.code = code;

        EditorUtility.SetDirty(flow);
        return flow;
    }

    /// <summary>
    /// The blurred map. The scrim under it is not decoration - it is what keeps the
    /// card readable on a device that will not give up its framebuffer, where the
    /// blur silently does not happen.
    /// </summary>
    static FrostedBackdrop BuildBackdrop(Transform parent)
    {
        RectTransform rt = UIFactory.Rect("Backdrop", parent);
        UIFactory.Stretch(rt);

        FrostedBackdrop backdrop = rt.gameObject.AddComponent<FrostedBackdrop>();

        RectTransform blur = UIFactory.Rect("Blur", rt);
        UIFactory.Stretch(blur);
        backdrop.image = blur.gameObject.AddComponent<RawImage>();
        backdrop.image.raycastTarget = false;

        // Off until there is a picture in it. A RawImage with no texture draws a
        // solid white rectangle, and this one is the size of the screen.
        backdrop.image.enabled = false;

        RectTransform scrim = UIFactory.Rect("Scrim", rt);
        UIFactory.Stretch(scrim);

        // Raycast target on purpose: it also stops taps reaching the map behind a
        // screen the reader has not finished with.
        // 0.80, not 0.62. The blur is what would otherwise smear the map's own
        // chrome into texture, and the blur is exactly the part that is allowed to
        // fail - at 0.62 with no blur, "Recentre" is still legible in the corner
        // where this screen puts its own title.
        Image dim = UIFactory.Fill(scrim, ThemeRole.Background, 0.80f);
        dim.raycastTarget = true;
        backdrop.scrim = dim;

        rt.gameObject.SetActive(false);
        return backdrop;
    }

    /// <summary>
    /// Welcome. Two sentences and two ways on: the ordinary one, and the one for
    /// somebody who was handed a code by whoever made the trail.
    /// </summary>
    static GameObject BuildWelcome(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Welcome", plain: true, inner: out RectTransform inner);

        TextMeshProUGUI brand = UIFactory.Text("Brand", inner, 30f, ThemeRole.Accent,
                                               TextAlignmentOptions.TopLeft, "app.name",
                                               FontStyles.Bold);
        UIFactory.Corner(brand.rectTransform, new Vector2(0f, 1f), new Vector2(620f, 48f),
                         Vector2.zero);
        brand.characterSpacing = 12f;

        // Bottom-aligned. The reader's thumb is at the bottom of the phone and the
        // words are the last thing they read before pressing something.
        // Half the screen, not a third. A plain screen with two sentences at the
        // very bottom and nine hundred pixels of nothing above them reads as a
        // screen that failed to load rather than one that is deliberately quiet.
        RectTransform words = UIFactory.Rect("Words", inner);
        UIFactory.BottomStretch(words, 620f, Tap * 2f + 76f);
        VerticalLayoutGroup wordStack = UIFactory.VerticalStack(words, 22f, false);
        wordStack.childAlignment = TextAnchor.LowerLeft;

        RectTransform spacer = UIFactory.Rect("Spacer", words);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        TextMeshProUGUI title = UIFactory.Text("Title", words, 88f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.BottomLeft, "welcome.title",
                                               FontStyles.Bold);
        title.lineSpacing = -12f;

        UIFactory.Text("Body", words, Body, ThemeRole.TextSecondary,
                       TextAlignmentOptions.TopLeft, "welcome.body");

        RectTransform start = UIFactory.Rect("Start", inner);
        UIFactory.BottomStretch(start, Tap, Tap + 16f);
        Button startButton = UIFactory.Button(start, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(start.gameObject);
        Wire(startButton.onClick, flow.PassWelcome, "Welcome start");

        TextMeshProUGUI startLabel = UIFactory.Text("Label", start, Body, ThemeRole.OnAccent,
                                                    TextAlignmentOptions.Center, "welcome.start",
                                                    FontStyles.Bold);
        UIFactory.Stretch(startLabel.rectTransform);
        Unthemed(startLabel.gameObject);

        RectTransform code = UIFactory.Rect("HaveCode", inner);
        UIFactory.BottomStretch(code, Tap);
        Button codeButton = UIFactory.Button(code, ThemeRole.Background, 0f);
        Wire(codeButton.onClick, flow.PassWelcomeWithCode, "Welcome code");

        TextMeshProUGUI codeLabel = UIFactory.Text("Label", code, Secondary,
                                                   ThemeRole.TextSecondary,
                                                   TextAlignmentOptions.Center,
                                                   "welcome.haveCode", FontStyles.Bold);
        UIFactory.Stretch(codeLabel.rectTransform);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// The terms. Six short sections, scrolling, with the button outside the scroll
    /// so it is always where the reader left it.
    ///
    /// Not dressed up. A non-skippable screen nobody reads is a dark pattern with
    /// extra steps, and the answer to that is short sentences, not illustration.
    /// </summary>
    static GameObject BuildTerms(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Terms", plain: true, inner: out RectTransform inner);

        TextMeshProUGUI title = UIFactory.Text("Title", inner, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, "terms.title",
                                               FontStyles.Bold);
        UIFactory.TopStretch(title.rectTransform, 90f);

        RectTransform scrollHolder = UIFactory.Rect("Body", inner);
        UIFactory.Stretch(scrollHolder);
        scrollHolder.offsetMin = new Vector2(0f, Tap + 34f);
        scrollHolder.offsetMax = new Vector2(0f, -124f);

        RectTransform content = UIFactory.ScrollList("Scroll", scrollHolder, 30f);

        for (int i = 1; i <= 6; i++)
        {
            RectTransform section = UIFactory.Rect("Section" + i, content);
            UIFactory.VerticalStack(section, 8f, false);

            // No LayoutElement and no ContentSizeFitter on either of these.
            //
            // TMP reports its own preferred height for the width it is given, and a
            // VerticalLayoutGroup with childControlHeight asks for exactly that. A
            // fixed preferred height on top of it is a second answer to the same
            // question, and the group believes the fixed one - which is why the
            // first render had "Where you are" printed through "What you leave".
            UIFactory.Text("Title", section, Body, ThemeRole.TextPrimary,
                           TextAlignmentOptions.TopLeft,
                           "terms." + i + ".title", FontStyles.Bold);

            UIFactory.Text("Body", section, Secondary, ThemeRole.TextSecondary,
                           TextAlignmentOptions.TopLeft, "terms." + i + ".body");
        }

        UIFactory.Text("Footer", content, Caption, ThemeRole.TextMuted,
                       TextAlignmentOptions.TopLeft, "terms.footer");

        RectTransform accept = UIFactory.Rect("Accept", inner);
        UIFactory.BottomStretch(accept, Tap);
        Button acceptButton = UIFactory.Button(accept, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(accept.gameObject);
        Wire(acceptButton.onClick, flow.AcceptTerms, "Accept terms");

        TextMeshProUGUI acceptLabel = UIFactory.Text("Label", accept, Body, ThemeRole.OnAccent,
                                                     TextAlignmentOptions.Center, "terms.accept",
                                                     FontStyles.Bold);
        UIFactory.Stretch(acceptLabel.rectTransform);
        Unthemed(acceptLabel.gameObject);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// My trails: one card per trail, over the blurred map.
    ///
    /// The list has four states and only one of them is a list. A first run on a
    /// bus with one bar of signal sees the other three, so each says something
    /// true rather than showing an empty box.
    /// </summary>
    static GameObject BuildTrailsDoor(Transform parent, DoorFlow flow,
                                      TrailRowView rowPrefab, out TrailsDoor door)
    {
        RectTransform panel = Door(parent, "Trails", plain: false, inner: out RectTransform inner,
                                   header: true);

        door = panel.gameObject.AddComponent<TrailsDoor>();
        door.rowPrefab = rowPrefab;

        TextMeshProUGUI title = UIFactory.Text("Title", inner, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "trails.title",
                                               FontStyles.Bold);
        UIFactory.Corner(title.rectTransform, new Vector2(0f, 1f), new Vector2(700f, 90f),
                         Vector2.zero);

        // Only there when there is an app behind this screen to go back to.
        RectTransform close = UIFactory.Rect("Close", inner);
        UIFactory.Corner(close, new Vector2(1f, 1f), new Vector2(Tap, Tap), new Vector2(0f, 8f));
        Button closeButton = UIFactory.Button(close, ThemeRole.Surface, 1f, UIShape.Circle);
        Outline(close);
        Wire(closeButton.onClick, flow.Close, "Leave trails");
        Icon(close, AppIcon.Close, 42f, ThemeRole.TextSecondary);
        door.closeButton = close.gameObject;

        RectTransform listHolder = UIFactory.Rect("Body", inner);
        UIFactory.Stretch(listHolder);
        listHolder.offsetMin = new Vector2(0f, Tap + 30f);

        // Below the header band, not tucked under its edge. The empty state starts
        // at the top of this list, and at -120 its heading was printed across the
        // band's own hairline with the map's attribution showing through beneath.
        listHolder.offsetMax = new Vector2(0f, -220f);

        RectTransform content = UIFactory.ScrollList("Scroll", listHolder, 14f);
        door.listParent = content;

        door.waitingState = StateNote(content, "Waiting", "trails.loading", null);
        door.emptyState = StateNote(content, "Empty", "trails.empty", "trails.emptyBody");
        door.failedState = StateNote(content, "Failed", "trails.failed", null,
                                     out door.failedLabel);

        RectTransform join = UIFactory.Rect("Join", inner);
        UIFactory.BottomStretch(join, Tap);
        Button joinButton = UIFactory.Button(join, ThemeRole.Surface, 1f, UIShape.Rounded);
        Outline(join);
        Wire(joinButton.onClick, flow.OpenCode, "Open code entry");
        door.joinButton = joinButton;

        TextMeshProUGUI joinLabel = UIFactory.Text("Label", join, Secondary,
                                                   ThemeRole.TextPrimary,
                                                   TextAlignmentOptions.Center, "trails.join",
                                                   FontStyles.Bold);
        UIFactory.Stretch(joinLabel.rectTransform);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>Enter a code: six boxes over one invisible field, on a card.</summary>
    static GameObject BuildCodeDoor(Transform parent, DoorFlow flow, out CodeDoor door)
    {
        RectTransform panel = Door(parent, "Code", plain: false, inner: out RectTransform inner);

        door = panel.gameObject.AddComponent<CodeDoor>();

        RectTransform card = UIFactory.Rect("Card", inner);
        UIFactory.Corner(card, new Vector2(0.5f, 0.5f), new Vector2(984f, 690f),
                         new Vector2(0f, 60f));
        UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        Outline(card);

        RectTransform stack = UIFactory.Rect("Stack", card);
        UIFactory.Stretch(stack);
        stack.offsetMin = new Vector2(40f, 34f);
        stack.offsetMax = new Vector2(-40f, -40f);

        TextMeshProUGUI title = UIFactory.Text("Title", stack, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, "code.title",
                                               FontStyles.Bold);
        UIFactory.TopStretch(title.rectTransform, 66f);

        TextMeshProUGUI body = UIFactory.Text("Body", stack, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "code.body");
        UIFactory.TopStretch(body.rectTransform, 60f, 70f);

        // --- the boxes, and the field underneath them ---
        RectTransform boxRow = UIFactory.Rect("Boxes", stack);
        UIFactory.TopStretch(boxRow, 150f, 156f);

        CodeBoxes boxes = boxRow.gameObject.AddComponent<CodeBoxes>();
        door.boxes = boxes;

        // The real input, stretched across the whole row and drawn nowhere. Its own
        // text is transparent; the boxes above are a picture of what is in it.
        boxes.field = UIFactory.InputField("Field", boxRow, null,
                                           out TextMeshProUGUI hiddenPlaceholder);
        RectTransform fieldRect = boxes.field.GetComponent<RectTransform>();
        UIFactory.Stretch(fieldRect);
        Transparent(boxes.field.GetComponent<Image>());
        Transparent(boxes.field.textComponent);
        Transparent(hiddenPlaceholder);
        boxes.field.caretColor = new Color(0f, 0f, 0f, 0f);
        boxes.field.customCaretColor = true;

        boxes.labels = new TextMeshProUGUI[CodeBoxes.Length];
        boxes.boxes = new Image[CodeBoxes.Length];

        const float BoxWidth = 138f;
        const float BoxGap = 14f;
        float rowWidth = CodeBoxes.Length * BoxWidth + (CodeBoxes.Length - 1) * BoxGap;

        for (int i = 0; i < CodeBoxes.Length; i++)
        {
            RectTransform box = UIFactory.Rect("Box" + i, boxRow);
            UIFactory.Corner(box, new Vector2(0.5f, 0.5f), new Vector2(BoxWidth, 150f),
                             new Vector2(-rowWidth * 0.5f + BoxWidth * 0.5f +
                                         i * (BoxWidth + BoxGap), 0f));

            UIFactory.Fill(box, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;

            // The lit edge is what tells the reader which box they are typing into.
            RectTransform edge = UIFactory.Rect("Edge", box);
            UIFactory.BottomStretch(edge, 6f, 12f);
            edge.sizeDelta = new Vector2(-36f, 6f);
            // The first box starts lit, so a scene that has not played looks the
            // way the app will the moment it does.
            Image line = UIFactory.Plain(
                edge, ThemeManager.Get(i == 0 ? ThemeRole.Accent : ThemeRole.Line),
                UIShape.Pill);
            line.raycastTarget = false;
            boxes.boxes[i] = line;

            TextMeshProUGUI letter = UIFactory.Text("Letter", box, 58f, ThemeRole.TextPrimary,
                                                    TextAlignmentOptions.Center, null,
                                                    FontStyles.Bold);
            UIFactory.Stretch(letter.rectTransform);
            letter.text = string.Empty;
            boxes.labels[i] = letter;
        }

        // --- what came back ---
        door.message = UIFactory.Text("Message", stack, Caption, ThemeRole.StatusPoor,
                                      TextAlignmentOptions.TopLeft);
        UIFactory.TopStretch(door.message.rectTransform, 50f, 318f);
        door.message.gameObject.SetActive(false);

        RectTransform join = UIFactory.Rect("Join", stack);
        UIFactory.TopStretch(join, Tap, 378f);
        door.joinButton = UIFactory.Button(join, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(join.gameObject);
        Wire(door.joinButton.onClick, door.Join, "Join with code");

        door.joinLabel = UIFactory.Text("Label", join, Body, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, "code.join",
                                        FontStyles.Bold);
        UIFactory.Stretch(door.joinLabel.rectTransform);
        Unthemed(door.joinLabel.gameObject);

        RectTransform cancel = UIFactory.Rect("Cancel", stack);
        UIFactory.BottomStretch(cancel, Tap);
        door.cancelButton = UIFactory.Button(cancel, ThemeRole.Surface, 0f);
        Wire(door.cancelButton.onClick, door.Cancel, "Cancel code entry");

        TextMeshProUGUI cancelLabel = UIFactory.Text("Label", cancel, Secondary,
                                                     ThemeRole.TextSecondary,
                                                     TextAlignmentOptions.Center, "code.cancel");
        UIFactory.Stretch(cancelLabel.rectTransform);

        // Six characters in means there is nothing left to decide.
        UnityEventTools.AddVoidPersistentListener(boxes.onComplete, door.Join);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // ------------------------------------------------------------ door plumbing

    /// <summary>
    /// One way-in screen. A plain one paints its own background over everything;
    /// a card one is transparent and lets the blurred map through.
    /// </summary>
    static RectTransform Door(Transform parent, string name, bool plain,
                              out RectTransform inner, bool header = false)
    {
        RectTransform panel = UIFactory.Rect(name, parent);
        UIFactory.Stretch(panel);

        if (plain)
        {
            UIFactory.Fill(panel, ThemeRole.Background).raycastTarget = true;
        }
        else
        {
            // Still a raycast target, invisibly. Without it a tap between two cards
            // lands on the map behind a screen the reader has not finished with.
            Image catcher = panel.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
        }

        // A solid band behind the title, full bleed to the top edge of the glass.
        //
        // Without it the door's own heading is printed across whatever the map had
        // in that corner - which was "My trails" over the trail chip and "No trails
        // yet" over the scale bar. Tall enough that a notch pushing the title down
        // still leaves it on the band.
        if (header)
        {
            RectTransform band = UIFactory.Rect("Header", panel);
            UIFactory.TopStretch(band, 250f);
            UIFactory.Fill(band, ThemeRole.Background).raycastTarget = false;

            RectTransform edge = UIFactory.Rect("Line", band);
            UIFactory.BottomStretch(edge, 1f);
            UIFactory.Fill(edge, ThemeRole.Line, 0.5f).raycastTarget = false;

            band.SetAsFirstSibling();
        }

        AddFade(panel.gameObject, 22f);

        RectTransform safe = SafeContent(panel, padBottom: true);

        inner = UIFactory.Rect("Inner", safe);
        UIFactory.Stretch(inner);
        inner.offsetMin = new Vector2(Margin, Margin);
        inner.offsetMax = new Vector2(-Margin, -Margin);

        return panel;
    }

    /// <summary>
    /// One of the things a list says when it is not a list: waiting, empty, or the
    /// server not answering.
    /// </summary>
    static GameObject StateNote(Transform parent, string name, string titleKey, string bodyKey)
    {
        return StateNote(parent, name, titleKey, bodyKey, out TextMeshProUGUI _);
    }

    static GameObject StateNote(Transform parent, string name, string titleKey, string bodyKey,
                                out TextMeshProUGUI titleLabel)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(rt, 12f, false);
        stack.padding = new RectOffset(0, 0, 40, 40);

        titleLabel = UIFactory.Text("Title", rt, Body, ThemeRole.TextSecondary,
                                    TextAlignmentOptions.TopLeft, titleKey, FontStyles.Bold);

        if (!string.IsNullOrEmpty(bodyKey))
        {
            UIFactory.Text("Body", rt, Secondary, ThemeRole.TextMuted,
                           TextAlignmentOptions.TopLeft, bodyKey);
        }

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    /// <summary>Keeps a graphic in the hierarchy and out of the picture.</summary>
    static void Transparent(Graphic graphic)
    {
        if (graphic == null)
            return;

        Color colour = graphic.color;
        colour.a = 0f;
        graphic.color = colour;

        // Stripped, or the theme would paint the alpha straight back on.
        Unthemed(graphic.gameObject);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Full-screen catcher behind a sheet. A dimmed one also says that what is
    /// underneath is not currently the reader's to press.
    /// </summary>
    static Button CreateScrim(string name, RectTransform parent, out GameObject scrim, float dim)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Stretch(rt);

        Image image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, dim);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        scrim = rt.gameObject;
        rt.gameObject.SetActive(false);
        return button;
    }

    /// <summary>The grab bar at the top of a sheet, and the drag that dismisses it.</summary>
    static SheetDragHandle AddDragArea(RectTransform sheet, SheetAnimator animator)
    {
        RectTransform area = UIFactory.Rect("DragArea", sheet);
        UIFactory.TopStretch(area, 90f);

        // Transparent, but a raycast target - otherwise there is nothing to grab.
        Image catcher = area.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        RectTransform grab = UIFactory.Rect("Grabber", area);
        UIFactory.Corner(grab, new Vector2(0.5f, 1f), new Vector2(130f, 10f),
                         new Vector2(0f, -24f));
        UIFactory.Fill(grab, ThemeRole.Line, 1f, UIShape.Pill).raycastTarget = false;

        SheetDragHandle handle = area.gameObject.AddComponent<SheetDragHandle>();
        handle.sheet = animator;
        handle.sheetRect = sheet;
        handle.dismissDistance = 170f;

        return handle;
    }

    /// <summary>
    /// A stretched child that keeps its contents clear of the notch and the side
    /// cutouts. Backgrounds stay on the panel outside it and still run to the edge
    /// of the glass, which is the difference between a full-bleed screen and one
    /// with black strips down the sides of it.
    /// </summary>
    static RectTransform SafeContent(RectTransform panel, bool padBottom = false)
    {
        RectTransform rt = UIFactory.Rect("Safe", panel);
        UIFactory.Stretch(rt);

        SafeAreaPad pad = rt.gameObject.AddComponent<SafeAreaPad>();
        pad.top = true;
        pad.left = true;
        pad.right = true;
        pad.bottom = padBottom;

        return rt;
    }

    /// <summary>Strips automatic theming from anything a controller recolours itself.</summary>
    static void Unthemed(GameObject go)
    {
        ThemedElement themed = go.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);
    }

    static void AddFade(GameObject go, float rise = 14f)
    {
        if (go.GetComponent<CanvasGroup>() == null)
            go.AddComponent<CanvasGroup>();

        PanelFade fade = go.GetComponent<PanelFade>();
        if (fade == null)
            fade = go.AddComponent<PanelFade>();

        fade.duration = 0.18f;
        fade.rise = rise;
    }

    /// <summary>
    /// Adds a persistent listener - one that survives the scene being saved.
    /// AddListener would not: it registers at runtime only, and a control wired that
    /// way from an editor script does nothing once the scene is reloaded.
    /// </summary>
    static void Wire(UnityEngine.Events.UnityEventBase target,
                     UnityEngine.Events.UnityAction call, string description)
    {
        if (target == null)
        {
            _notes.Add(description + " could not be wired - its event was missing.");
            return;
        }

        UnityEventTools.AddVoidPersistentListener(target, call);
    }

    static void RemoveGenerated(Transform root)
    {
        foreach (string name in GeneratedNames)
        {
            RectTransform existing = Find(root, name);
            while (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
                existing = Find(root, name);
            }
        }

        GameObject services = GameObject.Find("AppServices");
        if (services != null)
            Undo.DestroyObjectImmediate(services);

        // Anything still under the Canvas was not made here, so it is reported
        // rather than deleted.
        for (int i = 0; i < root.childCount; i++)
        {
            _notes.Add("\"" + root.GetChild(i).name + "\" was already under the Canvas and " +
                       "was left alone. Delete it if it is left over from an older build.");
        }
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    static RectTransform Find(Transform current, string name)
    {
        if (current.name == name)
            return current as RectTransform;

        for (int i = 0; i < current.childCount; i++)
        {
            RectTransform found = Find(current.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
