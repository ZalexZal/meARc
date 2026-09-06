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
        /// <summary>
    /// The Stadia Maps key, in one place.
    ///
    /// Two MapTileLayers are built - the map tab's and the radar's - and a key
    /// set on one of them is a map that is sharp in one place and soft in the
    /// other, which reads as a rendering bug rather than a missing setting.
    ///
    /// It ships in the APK and Stadia offers no way to restrict it - their domain
    /// authentication is referer-based and covers browsers only. Give the app its
    /// own property so this key can be rotated without touching the website's.
    /// </summary>
    const string StadiaKey = "a6a14986-fcf4-45c1-9033-e7e6040a5ed6";

const float ReferenceWidth = 1080f;
    const float ReferenceHeight = 1920f;

    const float Nav = 190f;
    const float Margin = 48f;

    /// <summary>Smallest size anything tappable may be, in reference pixels. 48dp.</summary>
    const float Tap = 128f;

    // Type scale. Nothing below Caption - 32 reference pixels is about 12dp, and
    // that is the floor for text a walking reader has to read.
    // A scale with real steps in it. The old one ran 62/48/42/36/32, which puts
    // body and secondary seventeen per cent apart - close enough that a sheet of
    // text reads as one undifferentiated block and nothing looks deliberate.
    // Ratios now sit near 1.35 at the top, where one line has to carry a whole
    // screen, and tighten towards the bottom where UI text lives.
    const float Display = 76f;
    const float Title = 54f;
    const float Body = 40f;
    const float Secondary = 33f;
    const float Caption = 28f;

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

        // Wired here rather than in BuildMapTab, which runs before the tab
        // controller exists.
        if (mapTab.settingsButton != null)
            Wire(mapTab.settingsButton.onClick, tabs.ShowSettings, "Open settings");
        else
            _notes.Add("No settings gear on the map, so settings cannot be opened.");

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
        // The map's own controls go down with the nav bar while a door is up, so
        // what the blur photographs is a map rather than a screenshot of the app.
        // Safe holds the top bar, the scale, the attribution and the dock; the map
        // itself is a sibling of it and stays.
        Transform mapChrome = mapPanel.Find("Safe");

        BuildDoors(root, navRow.parent.gameObject, trailRowPrefab,
                   mapChrome != null ? mapChrome.gameObject : null);

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

        // The stop's own contents - media, hints, the question - which live in
        // their own tables and arrive when a stop is opened rather than with
        // every marker in radius. Harmless before 0017: marker_detail 404s, the
        // warning says so once, and the sheet shows the marker's text.
        go.AddComponent<MarkerDetailService>();

        // Teams and the standings. Harmless before 0022: my_team_detail 404s,
        // the card says it could not reach the server, and nothing invents a
        // team that does not exist.
        go.AddComponent<TeamService>();

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

        // Taller than it was by a quarter. The stop's own words are the point of
        // the stop and were getting one visible line: the pieces list and a
        // two-row footer had taken the rest, and the fade over the last of it
        // made one line look like the whole entry.
        const float SheetHeight = 1440f;
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
        // Twenty-six, and a fade over the last of it. The mask cuts wherever the
        // text happens to wrap, so no gap makes the last line land whole - eighteen
        // put it against the button and forty-six sliced it through the middle of
        // the letters. What fixes it is the strip below, not the distance.
        body.offsetMin = new Vector2(Margin, Margin + FooterHeight + 26f);
        body.offsetMax = new Vector2(-Margin, -HeadRoom);

        RectTransform content = UIFactory.ScrollList("Scroll", body, 18f);

        // Room at the end of the scroll, so the last line is not flush against the
        // mask when the reader has scrolled all the way down.
        VerticalLayoutGroup overviewStack = content.GetComponent<VerticalLayoutGroup>();
        if (overviewStack != null)
            overviewStack.padding = new RectOffset(0, 0, 0, 40);

        // The fade itself: the sheet's own surface, opaque where the scroll ends
        // and gone sixty pixels up. Created after the scroll and before the footer,
        // so it draws over the text and under the buttons.
        RectTransform hem = UIFactory.Rect("Hem", overview);
        UIFactory.BottomStretch(hem, 60f, Margin + FooterHeight + 26f);
        hem.sizeDelta = new Vector2(-Margin * 2f, 60f);

        Image hemFade = UIFactory.Fill(hem, ThemeRole.Surface);
        hemFade.sprite = UISprites.FadeUp;
        hemFade.type = Image.Type.Simple;
        hemFade.raycastTarget = false;

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

        RectTransform guideInner = UIFactory.Rect("Inner", guide);
        UIFactory.Stretch(guideInner);
        HorizontalLayoutGroup guideLayout = UIFactory.HorizontalStack(guideInner, 16f);
        guideLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(guideInner, AppIcon.Navigation, 44f, ThemeRole.OnAccent);

        controller.guideLabel = UIFactory.Text("Label", guideInner, Body, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        LayoutElement guideLabelSize = UIFactory.Size(controller.guideLabel.gameObject, Tap, 420f);
        guideLabelSize.flexibleWidth = 0f;
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

        // Third in the row, which is why marker.hide lost its noun: three labels
        // sharing the sheet's width have room for one word each.
        OutlineButton(actions, "ReportButton", "marker.report", out Button report,
                      out controller.reportLabel);
        controller.reportButton = report;

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

        // Built after the sheet, so it draws over it. The sheet stays visible
        // underneath: choosing a reason for a stop whose name has just been taken
        // off the screen is a worse question than the one being asked.
        controller.reportSheet = BuildReportChooser(sheetSafe);

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

    /// <summary>
    /// The reason chooser that opens over the marker sheet.
    ///
    /// Six reasons, one tap each, and no text field. Whoever uses this is standing
    /// in a street next to something they would rather was not there; the reason
    /// plus the coordinates report_marker snapshots is enough for the person who
    /// runs the trail to walk out and look for themselves.
    ///
    /// A card rather than a sheet, because it sizes itself to the six and would
    /// have to be re-measured by hand every time the wording changed.
    /// </summary>
    static ReportSheetView BuildReportChooser(Transform parent)
    {
        RectTransform panel = Door(parent, "ReportChooser", plain: false,
                                   inner: out RectTransform inner);

        // Door pads for the system bars and nothing else, so a card resting on
        // the bottom of it grows upward from underneath our own nav - which is
        // where the last button was landing, invisible behind the tab bar.
        inner.offsetMin = new Vector2(inner.offsetMin.x, Nav + Margin);

        ReportSheetView view = panel.gameObject.AddComponent<ReportSheetView>();

        // Tapping the dimmed area is the way out that nobody has to be taught.
        // Cancel below is the one they can see.
        Button away = panel.gameObject.AddComponent<Button>();
        away.targetGraphic = panel.GetComponent<Image>();
        away.transition = Selectable.Transition.None;
        Wire(away.onClick, view.Close, "Report chooser scrim");

        RectTransform card = Card(inner, "Card", 14f);

        view.titleLabel = UIFactory.Text("Title", card, Title, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.TopLeft, "report.title",
                                         FontStyles.Bold);
        view.titleLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(view.titleLabel.gameObject, 76f);

        for (int i = 0; i < ReportSheetView.Reasons.Length; i++)
        {
            string key = "report.reason." + ReportSheetView.Reasons[i];

            view.reasonButtons[i] = FullWidthButton(card, "Reason" + i, key,
                                                    ThemeRole.SurfaceAlt,
                                                    ThemeRole.TextPrimary);
            view.reasonLabels[i] =
                view.reasonButtons[i].GetComponentInChildren<TextMeshProUGUI>();
        }

        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        UIFactory.Size(view.statusLabel.gameObject, 52f);
        view.statusLabel.gameObject.SetActive(false);

        // Not Surface: the card's own face is Surface, and a button the same
        // colour as the thing it sits on is a button nobody can see.
        view.cancelButton = FullWidthButton(card, "Cancel", "report.cancel",
                                            ThemeRole.SurfaceAlt, ThemeRole.TextSecondary);

        panel.gameObject.SetActive(false);
        return view;
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
        tileLayer.stadiaApiKey = StadiaKey;

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
        BuildTrailChip(top, out RectTransform chipRect);
        UIFactory.Corner(chipRect, new Vector2(0f, 0.5f),
                         new Vector2(800f, 84f), new Vector2(Margin - 18f, -14f));

        // Labelled, not icon-only. Recentring has no universally read symbol, and
        // this control only appears once the reader has already lost track of where
        // they are - the worst moment to make them guess.
        // The gear, at the far end of a row that is already a row of controls.
        const float GearSize = 96f;

        RectTransform gear = UIFactory.Rect("Settings", top);
        UIFactory.Corner(gear, new Vector2(1f, 0.5f), new Vector2(GearSize, GearSize),
                         new Vector2(-Margin, -14f));
        controller.settingsButton = UIFactory.Button(gear, ThemeRole.Surface, 0.96f,
                                                     UIShape.Circle);
        Outline(gear);
        Icon(gear, AppIcon.Settings, 44f, ThemeRole.TextSecondary);

        // On its own line, under the top bar.
        //
        // It only appears once the reader has panned away, so making the trail name
        // share a row with it permanently costs the name the width it needs - and
        // naming the open trail is the entire job of that bar. Below, it competes
        // with nothing and the name gets the room.
        RectTransform recentre = UIFactory.Rect("Recentre", safe);
        UIFactory.Corner(recentre, new Vector2(1f, 1f), new Vector2(330f, Tap),
                         new Vector2(-Margin, -182f));
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

        // The distance and the bar are on the map, so they take the map's ink
        // rather than the chrome's. As TextSecondary they were near-white in the
        // dark theme, which put the one thing that says how far a centimetre is
        // in white on a cream map.
        map.scaleLabel = UIFactory.Text("Label", scaleHolder, Caption, ThemeRole.TextSecondary,
                                        TextAlignmentOptions.BottomLeft);
        OnMap(map.scaleLabel, MapInk.Ink);
        UIFactory.Corner(map.scaleLabel.rectTransform, new Vector2(0f, 0f),
                         new Vector2(300f, 40f), new Vector2(0f, 14f));

        RectTransform bar = UIFactory.Rect("Bar", scaleHolder);
        UIFactory.Corner(bar, new Vector2(0f, 0f), new Vector2(160f, 8f), Vector2.zero);
        UIFactory.Plain(bar, MapInk.Ink, UIShape.Pill).raycastTarget = false;
        map.scaleBar = bar;

        // Required by the tile licence, and it belongs on screen anyway - the map
        // is somebody else's work.
        TextMeshProUGUI credit = UIFactory.Text("Attribution", safe, 23f, ThemeRole.TextMuted,
                                                TextAlignmentOptions.Left);
        UIFactory.Corner(credit.rectTransform, new Vector2(0f, 1f), new Vector2(900f, 30f),
                         new Vector2(Margin, -272f));
        credit.textWrappingMode = TextWrappingModes.NoWrap;
        credit.overflowMode = TextOverflowModes.Ellipsis;

        // The box was 560 wide for a string that needed more, so the longer
        // Stadia attribution folded onto a second line and sat on top of the
        // scale bar. A licence notice covering the distance legend is two
        // failures at once.
        //
        // All three are owed a line: Stadia drew the tiles, OpenMapTiles built
        // the schema, and the data underneath is OpenStreetMap's.
        credit.text = "\u00A9 Stadia Maps \u00B7 OpenMapTiles \u00B7 OpenStreetMap";
        OnMap(credit, MapInk.MutedInk);

        // --- the states where there is nothing to draw ---
        controller.stateCard = BuildStateCard(safe);

        // --- the panel resting on the map ---
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, Nav + 20f);
        dockArea.sizeDelta = new Vector2(-Margin * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 16f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        controller.guidance = BuildGuidanceBanner(dockArea, location, BearingArrow.Frame.NorthUp);

        controller.nextStop = BuildNextStopStrip(dockArea, location, controller);

        // Browse and Team share one row rather than stacking. The dock sits over
        // the map, so every row it gains is map the reader cannot see; two halves
        // of one row cost nothing.
        RectTransform dockRow = UIFactory.Rect("Actions", dockArea);
        UIFactory.Size(dockRow.gameObject, Tap);
        HorizontalLayoutGroup dockRowLayout = UIFactory.HorizontalStack(dockRow, 12f);
        dockRowLayout.childAlignment = TextAnchor.MiddleCenter;

        RectTransform browse = UIFactory.Rect("Browse", dockRow);
        UIFactory.Size(browse.gameObject, Tap);
        UIFactory.Flexible(browse.gameObject);
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
        UIFactory.Flexible(controller.browseLabel.gameObject);
        controller.browseLabel.textWrappingMode = TextWrappingModes.NoWrap;
        controller.browseLabel.overflowMode = TextOverflowModes.Ellipsis;

        // --- who you are walking with, and the standings
        RectTransform team = UIFactory.Rect("Team", dockRow);
        UIFactory.Size(team.gameObject, Tap);
        UIFactory.Flexible(team.gameObject);

        Button teamButton = UIFactory.Button(team, ThemeRole.Surface, 0.97f, UIShape.Rounded);
        Outline(team);

        // Wired at runtime, not here: the doors are built after the map, so
        // there is no DoorFlow to point at yet. TrailChipView solves the same
        // problem the same way.
        OpenTeamButton opener = team.gameObject.AddComponent<OpenTeamButton>();
        opener.button = teamButton;

        RectTransform teamRow = UIFactory.Rect("Row", team);
        UIFactory.Stretch(teamRow);
        HorizontalLayoutGroup teamLayout = UIFactory.HorizontalStack(teamRow, 16f);
        teamLayout.childAlignment = TextAnchor.MiddleCenter;

        Icon(teamRow, AppIcon.Users, 38f, ThemeRole.TextSecondary);

        TextMeshProUGUI teamLabel = UIFactory.Text("Label", teamRow, Secondary,
                                                   ThemeRole.TextPrimary,
                                                   TextAlignmentOptions.Left, "team.title",
                                                   FontStyles.Bold);
        UIFactory.Flexible(teamLabel.gameObject);
        teamLabel.textWrappingMode = TextWrappingModes.NoWrap;
        teamLabel.overflowMode = TextOverflowModes.Ellipsis;

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

        view.actionLabel = UIFactory.Text("Label", action, Secondary, ThemeRole.OnAccent,
                                          TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.actionLabel.rectTransform);

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

        ARCoreExtensions extensionsObject =
            Object.FindFirstObjectByType<ARCoreExtensions>(FindObjectsInactive.Include);

        controller.arExtensions = extensionsObject != null ? extensionsObject.gameObject : null;

        if (controller.arExtensions == null)
        {
            _notes.Add("No ARCore Extensions object found, so it cannot be deferred.");
        }
        else
        {
            // Saved switched off.
            //
            // ARCore Extensions running when a runtime permission dialog appears
            // kills the process: a SIGSEGV at address zero on the Unity main
            // thread, inside Unity's own Java message handling. Found by disabling
            // one root object at a time in a real build - this one alone was
            // enough to survive the dialog, and the whole Canvas was not.
            //
            // Done here rather than in a script, because the script that used to
            // do it lives on the Camera tab, which is inactive at launch, so its
            // Awake never ran. A scene that is already correct needs nobody to
            // remember to correct it.
            controller.arExtensions.SetActive(false);
            EditorUtility.SetDirty(controller.arExtensions);
        }

        // The session goes with it, so nothing claims the camera before the reader
        // has asked for a camera.
        if (controller.arSession != null)
        {
            controller.arSession.gameObject.SetActive(false);
            EditorUtility.SetDirty(controller.arSession.gameObject);
        }

        // And the Geospatial components that live somewhere else.
        //
        // ARStreetscapeGeometryManager sits on XR Origin, not on the ARCore
        // Extensions object, so switching that object off left this one running -
        // throwing a NullReferenceException every frame from launch, on a screen
        // with no camera on it, and taking the process down on a background thread
        // the moment the app was resumed.
        var deferred = new System.Collections.Generic.List<Behaviour>();

        ARStreetscapeGeometryManager streetscape =
            Object.FindFirstObjectByType<ARStreetscapeGeometryManager>(FindObjectsInactive.Include);

        if (streetscape != null)
        {
            streetscape.enabled = false;
            EditorUtility.SetDirty(streetscape);
            deferred.Add(streetscape);
        }

        controller.arDeferred = deferred.ToArray();
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
    ///
    /// North stays at the top and an arrow in the middle turns with the phone. The
    /// arrow replaced a cone drawn as the camera's field of view: on a disc three
    /// hundred units across that cone covered a third of the plot, so the markers
    /// it was meant to sit behind had to be read through it.
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

        // ---------------------------------------------------------- the map
        //
        // The disc used to be dots on a blank surface, which told a reader the
        // bearing and distance of a marker and nothing about what stood between
        // them and it. Two markers 200 m away read identically whether one is
        // across a square and the other across a river.
        //
        // It is the same MapView and MapTileLayer the map tab uses, at a fixed
        // width, not interactive, and with no pins of its own - the radar draws
        // those, and two sets of marks on one disc would be two answers to one
        // question. North stays up, matching the rest of the disc and the N.
        RectTransform mapHole = UIFactory.Rect("MapMask", rt);
        UIFactory.Stretch(mapHole);

        // Unity's Mask reads the alpha of its own graphic, so a circle sprite is
        // the clip. showMaskGraphic off, or the circle paints over the tiles.
        //
        // MaskCircle rather than Circle. Mask clips through the stencil buffer,
        // which is a hard alpha test with no anti-aliasing, so the 64-pixel
        // Circle stretched over this 300-pixel disc turned every texel of its
        // soft edge into a visible step: the map inside came out as a torn blob
        // that bulged past where the rim is drawn. 512 is denser than the disc.
        Image maskShape = UIFactory.Plain(mapHole, Color.white, UIShape.Circle);
        maskShape.sprite = UISprites.MaskCircle;
        maskShape.raycastTarget = false;
        Mask mask = mapHole.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform mapSurface = UIFactory.Rect("Map", mapHole);
        UIFactory.Stretch(mapSurface);

        MapView radarMap = mapSurface.gameObject.AddComponent<MapView>();
        radarMap.locationService = location;
        radarMap.pinPrefab = null;
        radarMap.interactable = false;

        // 1000 m across a 500 m radar, so the rim of the disc is the edge of the
        // range the dots are placed against and the two agree.
        radarMap.metresAcross = 1000f;
        radarMap.minMetresAcross = 1000f;
        radarMap.maxMetresAcross = 1000f;

        RectTransform radarTiles = UIFactory.Rect("Tiles", mapSurface);
        UIFactory.Stretch(radarTiles);

        MapTileLayer radarTileLayer = mapSurface.gameObject.AddComponent<MapTileLayer>();
        radarTileLayer.map = radarMap;
        radarTileLayer.tileParent = radarTiles;
        radarTileLayer.stadiaApiKey = StadiaKey;

        // Fewer than the map tab keeps, because this disc is 300px and is on
        // screen at the same time as a camera feed.
        radarTileLayer.maxCachedTextures = 60;

        RectTransform radarContent = UIFactory.Rect("Content", mapSurface);
        UIFactory.Stretch(radarContent);
        radarMap.contentParent = radarContent;

        // Dimmed, so the dots and the arrow stay the brightest things on it. A
        // map at full strength turns the radar into a small second map tab.
        //
        // Its own object, not mapHole: that one already carries the Image the Mask
        // clips with, and a second Image on one object comes back null.
        //
        // A fixed scrim, not Surface at 45%. Surface darkens in the dark theme
        // and brightens in the light one, so the thing meant to calm the map
        // down was washing it out instead - and the tiles now arrive already
        // dimmed in the dark theme, which this would have doubled.
        RectTransform dim = UIFactory.Rect("Dim", rt);
        UIFactory.Stretch(dim);
        Image dimImage = UIFactory.Plain(dim, MapInk.RadarScrim, UIShape.Circle);
        dimImage.sprite = UISprites.MaskCircle;
        dimImage.raycastTarget = false;

        CameraRadar radar = rt.gameObject.AddComponent<CameraRadar>();
        radar.locationService = location;
        radar.radius = Radius;
        radar.dotSize = 26f;
        radar.maxRangeMetres = 500d;
        radar.circleSprite = UISprites.Circle;

        // Both rings sit on the map, so they are drawn in the map's ink at a
        // strength that survives it. As TextPrimary at 16% the rim was a faint
        // white line over a light map, which is no line at all - and the rim is
        // what gives the disc its edge now that the mask's is a hard clip.
        RingGraphic inner = Ring(rt, "InnerRing", Size * 0.52f, ThemeRole.TextPrimary, 1f, 2f);
        inner.color = new Color(MapInk.Ink.r, MapInk.Ink.g, MapInk.Ink.b, 0.22f);

        RingGraphic rim = Ring(rt, "Rim", Size - 3f, ThemeRole.TextPrimary, 1f, 3f);
        rim.color = new Color(MapInk.Ink.r, MapInk.Ink.g, MapInk.Ink.b, 0.55f);

        // North is up and stays up, so it is worth one letter to say so.
        TextMeshProUGUI north = UIFactory.Text("North", rt, 26f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center);
        OnMap(north, MapInk.MutedInk);
        UIFactory.Corner(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(40f, 34f),
                         new Vector2(0f, -8f));
        north.text = "N";

        // Dots go above the rings and below the reader's own mark.
        RectTransform dots = UIFactory.Rect("Dots", rt);
        UIFactory.Stretch(dots);
        radar.dotParent = dots;

        // A ring under it, for the same reason every marker dot has one: white
        // on a light map is not a dot. MarkerPalette.RingFor would pick the dark
        // ring for white, and this is that, drawn as the thing behind it.
        RectTransform meRing = UIFactory.Rect("MeRing", rt);
        UIFactory.Corner(meRing, new Vector2(0.5f, 0.5f), new Vector2(26f, 26f), Vector2.zero);
        UIFactory.Plain(meRing, MarkerPalette.RingFor(Color.white),
                        UIShape.Circle).raycastTarget = false;

        RectTransform me = UIFactory.Rect("Me", rt);
        UIFactory.Corner(me, new Vector2(0.5f, 0.5f), new Vector2(18f, 18f), Vector2.zero);
        UIFactory.Plain(me, Color.white, UIShape.Circle).raycastTarget = false;

        // An arrow, not a cone.
        //
        // The cone was drawn as the camera's field of view, and on a disc this size
        // it covered a third of everything - so the markers it was meant to sit
        // behind were read through it. An arrow says the one thing that is actually
        // useful, which is which way the reader is facing, and says it the way
        // every map app the reader already uses says it.
        //
        // Built last so it draws over the dot underneath, and centred with its
        // pivot in the middle so rotating it spins it in place.
        RectTransform facing = UIFactory.Rect("Facing", rt);
        facing.anchorMin = new Vector2(0.5f, 0.5f);
        facing.anchorMax = new Vector2(0.5f, 0.5f);
        facing.pivot = new Vector2(0.5f, 0.5f);
        facing.sizeDelta = new Vector2(38f, 44f);
        facing.anchoredPosition = Vector2.zero;

        UIFactory.Plain(facing, ThemeManager.Get(ThemeRole.Accent),
                        UIShape.Triangle).raycastTarget = false;
        radar.facing = facing;

        return radar;
    }

    /// <summary>
    /// The bus-stop strip: previous, here, next.
    ///
    /// Three rows on a rail, in the place the "nearest to you" card used to sit and
    /// at the same size, so nothing on the map moves. Only the last row is a
    /// control - the other two are status, and giving them chevrons would invite a
    /// tap that does nothing.
    ///
    /// The rows are built at full strength and switched off by the component, so a
    /// trail at its start or its end keeps the same height and the map does not
    /// reflow underneath as the reader walks.
    /// </summary>
    static NextStopStrip BuildNextStopStrip(RectTransform parent,
                                            UserLocationService location,
                                            MapTabController map)
    {
        const float RowHeight = 96f;
        const float RailWidth = 38f;

        RectTransform card = UIFactory.Rect("NextStop", parent);
        UIFactory.Fill(card, ThemeRole.Surface, 0.97f, UIShape.Rounded).raycastTarget = true;

        // The card hugs the rows that are actually showing.
        //
        // Fixed at three rows it kept its full height with one row in it, which is
        // the state a reader sees first - a mostly empty box under the map before
        // any marker has loaded. The height only changes when a row appears or
        // disappears, and that happens on finding a stop or loading a trail: moments
        // where something changing is the point, not while walking.
        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 0f);
        stack.padding = new RectOffset(22, 22, 14, 14);

        Outline(card);
        Ignore((RectTransform)card.Find("Outline"));

        RectTransform rows = card;

        NextStopStrip strip = card.gameObject.AddComponent<NextStopStrip>();
        strip.locationService = location;
        strip.mapTab = map;

        // --- previous ---
        RectTransform prev = Row(rows, "Previous", RowHeight, RailWidth,
                                 out RectTransform prevRail, out RectTransform prevText);
        strip.previousRow = prev.gameObject;
        strip.previousKnob = Knob(prevRail, true, out RectTransform _);
        strip.previousWire = Wire(prevRail, RowHeight);

        strip.previousName = UIFactory.Text("Name", prevText, Secondary, ThemeRole.TextSecondary,
                                            TextAlignmentOptions.Left, null, FontStyles.Bold);
        strip.previousMeta = UIFactory.Text("Meta", prevText, Caption, ThemeRole.TextMuted,
                                            TextAlignmentOptions.Left);

        // --- here ---
        RectTransform here = Row(rows, "Here", RowHeight, RailWidth,
                                 out RectTransform hereRail, out RectTransform hereText);
        Knob(hereRail, true, out RectTransform _);
        strip.hereWire = Wire(hereRail, RowHeight).gameObject;

        strip.hereName = UIFactory.Text("Name", hereText, Secondary, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, "strip.here", FontStyles.Bold);
        strip.hereMeta = UIFactory.Text("Meta", hereText, Caption, ThemeRole.TextMuted,
                                        TextAlignmentOptions.Left);

        // --- next: the only row that is a control ---
        RectTransform next = Row(rows, "Next", RowHeight, RailWidth,
                                 out RectTransform nextRail, out RectTransform nextText);
        strip.nextRow = next.gameObject;

        strip.nextButton = UIFactory.Button(next, ThemeRole.Surface, 0f);
        next.GetComponent<Image>().raycastTarget = true;

        strip.nextKnob = Knob(nextRail, false, out RectTransform nextKnobRect);
        nextKnobRect.SetAsFirstSibling();

        strip.nextName = UIFactory.Text("Name", nextText, Secondary, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        strip.nextMeta = UIFactory.Text("Meta", nextText, Caption, ThemeRole.TextMuted,
                                        TextAlignmentOptions.Left);

        strip.nextArrow = Arrow(next, location, BearingArrow.Frame.NorthUp, 34f);

        strip.nextDistance = UIFactory.Text("Distance", next, Secondary, ThemeRole.Accent,
                                            TextAlignmentOptions.Right, null, FontStyles.Bold);
        LayoutElement distSize = UIFactory.Size(strip.nextDistance.gameObject, RowHeight, 170f);
        distSize.flexibleWidth = 0f;

        EditorUtility.SetDirty(strip);
        return strip;
    }

    /// <summary>One row of the strip: a rail on the left, text filling the rest.</summary>
    static RectTransform Row(RectTransform parent, string name, float height, float rail,
                             out RectTransform railRect, out RectTransform textRect)
    {
        RectTransform row = UIFactory.Rect(name, parent);
        UIFactory.Size(row.gameObject, height);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 12f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        railRect = UIFactory.Rect("Rail", row);
        LayoutElement railSize = UIFactory.Size(railRect.gameObject, height, rail);
        railSize.flexibleWidth = 0f;

        textRect = UIFactory.Rect("Text", row);
        UIFactory.Size(textRect.gameObject, height);
        UIFactory.Flexible(textRect.gameObject);
        UIFactory.VerticalStack(textRect, 0f, false).childAlignment = TextAnchor.MiddleLeft;

        return row;
    }

    /// <summary>
    /// The dot on the rail. Filled for a stop already found, hollow for one ahead -
    /// which is the entire bus-stop metaphor, and it costs one boolean.
    /// </summary>
    static Image Knob(RectTransform rail, bool filled, out RectTransform rect)
    {
        rect = UIFactory.Rect("Knob", rail);
        UIFactory.Corner(rect, new Vector2(0.5f, 0.5f), new Vector2(18f, 18f),
                         new Vector2(0f, 0f));

        Image knob = UIFactory.Fill(rect, filled ? ThemeRole.Accent : ThemeRole.Line,
                                    1f, UIShape.Circle);
        knob.raycastTarget = false;
        Unthemed(rect.gameObject);

        return knob;
    }

    /// <summary>The line down from a knob to the row below it.</summary>
    static Image Wire(RectTransform rail, float rowHeight)
    {
        RectTransform rect = UIFactory.Rect("Wire", rail);
        UIFactory.Corner(rect, new Vector2(0.5f, 0f), new Vector2(3f, rowHeight * 0.5f - 9f),
                         Vector2.zero);

        Image wire = UIFactory.Fill(rect, ThemeRole.Line, 1f, UIShape.Pill);
        wire.raycastTarget = false;
        Unthemed(rect.gameObject);

        return wire;
    }

    /// <summary>A circle outline drawn as geometry, crisp at any size.</summary>
    /// <summary>
    /// Strips the ThemedElement UIFactory.Text attaches and paints the text a
    /// fixed colour instead.
    ///
    /// For text that sits on the map. The ThemedElement would repaint it from
    /// the app palette on the next theme change, which is exactly the bug being
    /// fixed - the attribution was near-white on a cream map in the dark theme
    /// because it followed the chrome instead of the thing behind it.
    /// </summary>
    static void OnMap(TextMeshProUGUI text, Color colour)
    {
        ThemedElement themed = text.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);

        text.color = colour;
    }

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
    /// <summary>Cards in the first-run walkthrough. Dots and pages share it.</summary>
    const int TutorialCards = 6;

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

        // Six since the trail code and the team card were added: the original
        // four explained augmented reality and none of them explained how to
        // start playing, which is the part somebody handed a phone needs.
        for (int i = 1; i <= TutorialCards; i++)
            flow.pages.Add(TutorialPage(pages, "tutorial." + i, first: i == 1));

        RectTransform dots = UIFactory.Rect("Dots", inner);
        UIFactory.Corner(dots, new Vector2(0.5f, 0f), new Vector2(300f, 40f),
                         new Vector2(0f, Tap * 2f + 46f));
        HorizontalLayoutGroup dotsLayout = UIFactory.HorizontalStack(dots, 12f);
        dotsLayout.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < TutorialCards; i++)
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
        flow.nextLabel = UIFactory.Text("Label", next, Body, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, "tutorial.next",
                                        FontStyles.Bold);
        UIFactory.Stretch(flow.nextLabel.rectTransform);

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

    static TutorialFlow.Page TutorialPage(RectTransform parent, string key, bool first)
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

        // Keyed rather than blank, so this screen can be looked at before it is
        // ever run - and so the first frame is the first card rather than a gap.
        page.title = UIFactory.Text("Title", rt, Display, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.BottomLeft, key + ".title",
                                    FontStyles.Bold);
        UIFactory.Size(page.title.gameObject, 190f);

        page.body = UIFactory.Text("Body", rt, Body, ThemeRole.TextSecondary,
                                   TextAlignmentOptions.TopLeft, key + ".body");
        UIFactory.Size(page.body.gameObject, 260f);

        rt.gameObject.SetActive(first);
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

    /// <summary>
    /// Sign up, sign in, sign out - the two states of an account in one card.
    ///
    /// Built here rather than as a door because it is a setting, not a gate: the
    /// app opens and works without one, and the reader who wants their walking to
    /// survive a reinstall comes looking for it. A modal in the way of the map
    /// would be asking for a form before earning the right to.
    /// </summary>
    static AccountSectionView BuildAccountCard(Transform content)
    {
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
        view.saveNameButton = FullWidthButton(inGroup, "SaveName", "account.saveName",
                                              ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        view.signOutButton = FullWidthButton(inGroup, "SignOut", "account.signOut",
                                             ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ------------------------------------------------------- anonymous
        RectTransform outGroup = UIFactory.Rect("Anonymous", card);
        UIFactory.VerticalStack(outGroup, 16f);
        view.anonymousGroup = outGroup.gameObject;

        TextMeshProUGUI none = UIFactory.Text("None", outGroup, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, "account.none",
                                              FontStyles.Bold);
        UIFactory.Size(none.gameObject, 52f);

        TextMeshProUGUI body = UIFactory.Text("Body", outGroup, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.Left, "account.noneBody");
        body.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(body.gameObject, 110f);

        // First, because it is the one field that is about the person rather
        // than about the login.
        view.nameField = Field(outGroup, "Name", "account.name");

        TextMeshProUGUI nameHint = UIFactory.Text("NameHint", outGroup, Caption,
                                                  ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Left,
                                                  "account.nameHint");
        UIFactory.Size(nameHint.gameObject, 40f);

        view.emailField = Field(outGroup, "Email", "account.email");
        view.emailField.contentType = TMP_InputField.ContentType.EmailAddress;

        view.passwordField = Field(outGroup, "Password", "account.password");
        view.passwordField.contentType = TMP_InputField.ContentType.Password;

        view.signUpButton = FullWidthButton(outGroup, "Create", "account.create",
                                            ThemeRole.Accent, ThemeRole.OnAccent);
        view.signInButton = FullWidthButton(outGroup, "SignIn", "account.signIn",
                                            ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // Said before it happens, not after. There is no correct merge - two start
        // times, two teams, the same stop solved twice - so the only honest thing
        // is to warn and then keep the account's own history.
        TextMeshProUGUI warn = UIFactory.Text("Merge", outGroup, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Left,
                                              "account.mergeWarning");
        warn.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(warn.gameObject, 80f);

        // ----------------------------------------------------------- status
        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(view.statusLabel.gameObject, 60f);
        view.statusLabel.text = string.Empty;
        view.statusLabel.gameObject.SetActive(false);

        return view;
    }

    /// <summary>
    /// A text field that can be seen on a card.
    ///
    /// UIFactory.InputField paints itself Surface, which is the colour of the
    /// card it is sitting on - so the box vanished and the account form was two
    /// grey words floating in the middle of a panel. SurfaceAlt is the step the
    /// palette keeps for exactly this, a thing inside a thing.
    /// </summary>
    static TMP_InputField Field(Transform parent, string name, string placeholderKey)
    {
        TMP_InputField field = UIFactory.InputField(name, parent, placeholderKey, out _);

        ThemedElement themed = field.GetComponent<ThemedElement>();
        if (themed != null)
        {
            themed.role = ThemeRole.SurfaceAlt;
            themed.Apply();
        }

        return field;
    }

    /// <summary>A button that fills the card's width, with its label centred.</summary>
    static Button FullWidthButton(Transform parent, string name, string key,
                                  ThemeRole background, ThemeRole ink)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);

        Button button = UIFactory.Button(rt, background, 1f, UIShape.Rounded);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ink,
                                               TextAlignmentOptions.Center, key,
                                               FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return button;
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

        // A number, or a dash until there is one. Text() falls back to the object's
        // name, so before the first count arrived this card announced "Count" in
        // seventy-six point type.
        count.text = "—";

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

        BuildAccountCard(content);

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
        // No settings tab.
        //
        // The tab bar is the only surface visible on every screen, and a third of it
        // went to a page holding a theme switch, a language toggle and a link to the
        // walkthrough - opened twice in the life of an install. It is a gear on the
        // map's top row now, which is where a gear has lived since about 2008.
        tabs.mapIndicator = mapPill;
        tabs.cameraIndicator = cameraPill;

        // Persistent listeners, not AddListener: a runtime listener added here is
        // thrown away when the scene is saved, and the tab bar would do nothing.
        Wire(map.onClick, tabs.ShowMap, "Map tab");
        Wire(camera.onClick, tabs.ShowCamera, "Camera tab");

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

        // Baked as map ink rather than as Surface.
        //
        // Plain attaches no ThemedElement, so whatever colour it is handed is
        // what the prefab keeps for good - and it was handed the editor's
        // current Surface, which is how every pin on the phone ended up with the
        // dark theme's #1A1F27 plate under the light theme's near-black text.
        // Baking a colour is only wrong when the colour was supposed to move;
        // this one is not, so baking it is now correct rather than a trap.
        Image plateImage = UIFactory.Plain(plate, MapInk.Plate, UIShape.Pill);
        plateImage.raycastTarget = false;

        TextMeshProUGUI label = UIFactory.Text("Text", plate, Caption, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, null, FontStyles.Bold);
        OnMap(label, MapInk.OnPlate);
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

    /// <summary>
    /// A pin standing on a ring, rather than a cube floating over the pavement.
    ///
    /// The ring is the important half. It lies flat on the ground, so a reader
    /// takes the marker's position off the paving rather than guessing at the
    /// depth of something hanging in the air - and when localisation is a metre
    /// out, a ring around roughly the right slab still reads as a place.
    ///
    /// The pin is what carries the creator's colour. The ring stays near-white
    /// on purpose: it has to separate the marker from whatever is under it, and
    /// a ring in the same colour as the pin separates nothing.
    ///
    /// One shape. Flag, plaque and post are the same machinery with different
    /// numbers and can follow once this one has been seen outdoors.
    /// </summary>
    static GameObject CreateMarkerPrefab()
    {
        GameObject temp = new GameObject("BuildingMarker");

        // --- the pin
        GameObject pin = new GameObject("Pin");
        pin.transform.SetParent(temp.transform, false);

        MeshFilter pinMesh = pin.AddComponent<MeshFilter>();
        pinMesh.sharedMesh = MarkerShapes.Save(
            MarkerShapes.Pin(0.22f, 0.48f, 1.0f), "MarkerPin");

        MeshRenderer pinRenderer = pin.AddComponent<MeshRenderer>();
        pinRenderer.sharedMaterial = MarkerShapes.Surface("MarkerBody", Color.white, 0.35f);

        // --- the ring
        GameObject ring = new GameObject("Ring");
        ring.transform.SetParent(temp.transform, false);

        MeshFilter ringMesh = ring.AddComponent<MeshFilter>();
        ringMesh.sharedMesh = MarkerShapes.Save(
            MarkerShapes.Torus(0.5f, 0.045f), "MarkerRing");

        MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
        ringRenderer.sharedMaterial = MarkerShapes.Surface(
            "MarkerRing", new Color(0.96f, 0.97f, 0.98f), 0.2f);

        // Generous hit area: the visual is about a metre and a half but the
        // collider is wider, because a marker 80 m away is only a few pixels of
        // screen to aim at.
        BoxCollider collider = temp.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.5f, 0f);
        collider.size = new Vector3(2f, 2f, 2f);

        MarkerView view = temp.AddComponent<MarkerView>();

        // The pin only. Tinting the ring as well would give the marker one
        // colour and nothing to read it against.
        view.tintedRenderers = new[] { (Renderer)pinRenderer };
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

        // A visible surface, and an outline around it.
        //
        // This was drawn at zero alpha, so an open trail rendered as bold text on
        // the map with nothing around it - which reads as the name of the screen,
        // not as the way to change trails. It sits directly under a tab bar whose
        // first tab is called Map, so a heading in that position is read as the
        // heading of the tab you are already in, and the one control that opens
        // My trails looked like a caption.
        view.button = UIFactory.Button(rect, ThemeRole.Surface, 0.94f, UIShape.Pill);
        Outline(rect);

        RectTransform row = UIFactory.Rect("Row", rect);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(20f, 0f);
        row.offsetMax = new Vector2(-16f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 12f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        // Says what the thing beside it is. Without it the name is just a name,
        // and a name on a map is a label rather than a chooser.
        TextMeshProUGUI eyebrow = UIFactory.Text("Label", row, 24f, ThemeRole.Accent,
                                                 TextAlignmentOptions.Left, "trails.label",
                                                 FontStyles.Bold);
        eyebrow.characterSpacing = 8f;
        LayoutElement eyebrowSize = UIFactory.Size(eyebrow.gameObject, 40f, 150f);
        eyebrowSize.flexibleWidth = 0f;
        eyebrow.textWrappingMode = TextWrappingModes.NoWrap;
        eyebrow.raycastTarget = false;

        // No localisation key, deliberately.
        //
        // Passing one attaches a LocalizedLabel, and that component rewrites the
        // text from its key in OnEnable and on every language change. This label
        // is the trail's NAME, which TrailChipView writes - so the key silently
        // won every time, and the control permanently read "Map", the string
        // behind tab.map. That is not a styling problem: it is why the chip
        // looked like the title of the tab you were already in.
        //
        // TrailChipView subscribes to OnLanguageChanged itself and re-renders
        // "Choose a trail" through AppStrings when no trail is open, so nothing
        // is lost by owning the text here.
        view.nameLabel = UIFactory.Text("Name", row, Title, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        LayoutElement nameSize = UIFactory.Size(view.nameLabel.gameObject, 84f, 400f);
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
        LayoutElement modeSize = UIFactory.Size(mode.gameObject, 50f, 150f);
        modeSize.flexibleWidth = 0f;
        UIFactory.Fill(mode, ThemeRole.Accent, 0.18f, UIShape.Pill).raycastTarget = false;
        view.modeChip = mode.gameObject;

        view.modeLabel = UIFactory.Text("Label", mode, 26f, ThemeRole.Accent,
                                        TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.modeLabel.rectTransform);
        view.modeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.modeLabel.overflowMode = TextOverflowModes.Ellipsis;

        Icon(row, AppIcon.List, 34f, ThemeRole.Accent, out RectTransform chevron);
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

        view.playIcon = Icon(play, AppIcon.Play, 52f, ThemeRole.OnAccent);

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

        TextMeshProUGUI dataPlayLabel = UIFactory.Text("Label", dataPlay, Secondary,
                                                       ThemeRole.OnAccent,
                                                       TextAlignmentOptions.Center,
                                                       "media.dataPlay", FontStyles.Bold);
        UIFactory.Stretch(dataPlayLabel.rectTransform);
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

        view.checkLabel = UIFactory.Text("Label", check, Body, ThemeRole.OnAccent,
                                         TextAlignmentOptions.Center, "task.check",
                                         FontStyles.Bold);
        UIFactory.Stretch(view.checkLabel.rectTransform);
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

        Icon(row, AppIcon.Check, 40f, ThemeRole.Accent, out RectTransform tickRect);
        view.currentMark = tickRect.gameObject;
        tickRect.gameObject.SetActive(false);

        // Leaving, at the far end of the row.
        //
        // A word rather than an icon. There is no symbol that reliably means "give
        // up your place on this trail", and the one control here that undoes
        // something should not be the one the reader has to guess at. It is quiet
        // until it is armed, and then it is the only red thing on the card.
        RectTransform leave = UIFactory.Rect("Leave", row);
        UIFactory.Size(leave.gameObject, 84f, 132f);
        view.leaveButton = UIFactory.Button(leave, ThemeRole.Surface, 0f, UIShape.Rounded);

        view.leaveLabel = UIFactory.Text("Label", leave, Caption, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Center, "trails.leave",
                                         FontStyles.Bold);
        UIFactory.Stretch(view.leaveLabel.rectTransform);

        // Coloured by the row as it arms, so the theme must not paint over it.
        Unthemed(view.leaveLabel.gameObject);

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
    static DoorFlow BuildDoors(Transform root, GameObject navBar,
                               TrailRowView trailRowPrefab, GameObject mapChrome)
    {
        RectTransform holder = UIFactory.Rect("Doors", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        DoorFlow flow = holder.gameObject.AddComponent<DoorFlow>();
        flow.navBar = navBar;
        flow.hideWhileOpen = mapChrome != null
            ? new GameObject[] { mapChrome }
            : new GameObject[0];
        flow.backdrop = BuildBackdrop(holder);

        flow.welcomePanel = BuildWelcome(holder, flow);
        flow.termsPanel = BuildTerms(holder, flow);
        flow.trailsPanel = BuildTrailsDoor(holder, flow, trailRowPrefab, out TrailsDoor trails);
        flow.codePanel = BuildCodeDoor(holder, flow, out CodeDoor code);
        flow.teamPanel = BuildTeamDoor(holder, flow);

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

        WelcomeMarkers(inner);

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
        Wire(startButton.onClick, flow.PassWelcome, "Welcome start");

        TextMeshProUGUI startLabel = UIFactory.Text("Label", start, Body, ThemeRole.OnAccent,
                                                    TextAlignmentOptions.Center, "welcome.start",
                                                    FontStyles.Bold);
        UIFactory.Stretch(startLabel.rectTransform);

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
    /// Three markers in the empty half of the welcome screen.
    ///
    /// Not decoration for its own sake: the headline says markers are left in real
    /// places, and this is what one looks like in this app - the same ring and disc
    /// the map draws, which is what the reader is looking at a few seconds later.
    /// Scattered rather than lined up, and at three sizes, because a row of three
    /// equal circles is a loading indicator.
    ///
    /// Faint on purpose. It is the ground the words sit on, and anything bright
    /// enough to compete with a headline in an eighty-eight point face would be
    /// the first thing read on a screen whose whole job is one sentence.
    /// </summary>
    static void WelcomeMarkers(RectTransform inner)
    {
        RectTransform field = UIFactory.Rect("Markers", inner);
        field.anchorMin = new Vector2(0f, 1f);
        field.anchorMax = new Vector2(1f, 1f);
        field.pivot = new Vector2(0.5f, 1f);
        field.sizeDelta = new Vector2(0f, 900f);
        field.anchoredPosition = new Vector2(0f, -120f);

        // x and y as fractions of the field, then the diameter.
        float[,] spots =
        {
            { 0.24f, 0.30f, 190f },
            { 0.68f, 0.54f, 128f },
            { 0.44f, 0.82f,  92f }
        };

        for (int i = 0; i < spots.GetLength(0); i++)
        {
            float size = spots[i, 2];

            RectTransform pin = UIFactory.Rect("Marker" + i, field);
            pin.anchorMin = new Vector2(spots[i, 0], 1f - spots[i, 1]);
            pin.anchorMax = pin.anchorMin;
            pin.pivot = new Vector2(0.5f, 0.5f);
            pin.sizeDelta = new Vector2(size, size);
            pin.anchoredPosition = Vector2.zero;

            // The ring is a real hole rather than a disc in the background colour,
            // so this reads the same way the map's pins do.
            Image ring = UIFactory.Fill(pin, ThemeRole.Accent, 0.22f, UIShape.Ring);
            ring.raycastTarget = false;

            RectTransform core = UIFactory.Rect("Dot", pin);
            UIFactory.Corner(core, new Vector2(0.5f, 0.5f),
                             new Vector2(size * 0.42f, size * 0.42f), Vector2.zero);
            UIFactory.Fill(core, ThemeRole.Accent, 0.16f, UIShape.Circle).raycastTarget = false;
        }
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
        Wire(acceptButton.onClick, flow.AcceptTerms, "Accept terms");

        TextMeshProUGUI acceptLabel = UIFactory.Text("Label", accept, Body, ThemeRole.OnAccent,
                                                     TextAlignmentOptions.Center, "terms.accept",
                                                     FontStyles.Bold);
        UIFactory.Stretch(acceptLabel.rectTransform);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// My trails: a card resting on the bottom of the blurred map.
    ///
    /// It used to be a full-bleed band stapled to the top of the screen with the
    /// rows floating loose underneath, and it read as three unrelated layers -
    /// most of all because the map's own dock stayed legible below them, so half
    /// the controls on screen were ones the reader could not press. A card sitting
    /// on the bottom edge has somewhere to be, covers the dock, and leaves the
    /// blurred map showing above it, which is the part worth showing.
    ///
    /// The list has four states and only one of them is a list. A first run on a
    /// bus with one bar of signal sees the other three, so each says something
    /// true rather than showing an empty box.
    /// </summary>
    /// <summary>
    /// Who you are walking with, and where everybody stands.
    ///
    /// One card rather than two screens, because a walker opening this is asking
    /// one question and the standings are the reason they care who their team
    /// is. Rows are built once and shown or hidden: a team holds four and a
    /// school board holds a dozen, so pooling is less machinery than a prefab
    /// and a spawner, and it cannot leak them.
    /// </summary>
    static GameObject BuildTeamDoor(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Team", plain: false, inner: out RectTransform inner);

        TeamDoorView view = panel.gameObject.AddComponent<TeamDoorView>();

        RectTransform card = Card(inner, "Card", 26f);

        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.VerticalStack(head, 6f, false);
        Eyebrow(head, "team.eyebrow");
        UIFactory.Text("Title", head, Display, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "team.title", FontStyles.Bold);

        // Above both groups, because the number belongs to the trail rather than
        // to whether you happen to be on a team yet.
        view.capacityLabel = UIFactory.Text("Capacity", card, Secondary, ThemeRole.TextMuted,
                                            TextAlignmentOptions.Left);
        UIFactory.Size(view.capacityLabel.gameObject, 46f);

        // Blank until Refresh has a trail to ask about. Text() falls back to the
        // object's name, and "Capacity" is not a sentence anybody should read.
        view.capacityLabel.text = string.Empty;

        // Everything below the heading scrolls. The head and the footer are
        // pinned, so Close is always reachable - a card whose only way out has
        // scrolled off the bottom is a trap.
        RectTransform body = UIFactory.Rect("Body", card);
        LayoutElement bodySize = UIFactory.Size(body.gameObject, 260f);
        bodySize.flexibleHeight = 1f;
        bodySize.preferredHeight = 900f;

        RectTransform scroll = UIFactory.ScrollList("Scroll", body, 16f);

        // ----------------------------------------------------- no team yet
        RectTransform none = UIFactory.Rect("NoTeam", scroll);
        UIFactory.VerticalStack(none, 14f);
        view.noTeamGroup = none.gameObject;

        UIFactory.Text("None", none, Body, ThemeRole.TextPrimary,
                       TextAlignmentOptions.Left, "team.none", FontStyles.Bold);

        TextMeshProUGUI noneBody = UIFactory.Text("NoneBody", none, Secondary,
                                                  ThemeRole.TextSecondary,
                                                  TextAlignmentOptions.Left, "team.noneBody");
        noneBody.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(noneBody.gameObject, 96f);

        view.nameField = Field(none, "Name", "team.name");
        view.createButton = FullWidthButton(none, "Create", "team.create",
                                            ThemeRole.Accent, ThemeRole.OnAccent);

        view.codeField = Field(none, "Code", "team.code");
        view.joinButton = FullWidthButton(none, "Join", "team.join",
                                          ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ------------------------------------------------------- on a team
        RectTransform mine = UIFactory.Rect("Team", scroll);
        UIFactory.VerticalStack(mine, 12f);
        view.teamGroup = mine.gameObject;

        view.teamName = UIFactory.Text("Name", mine, Title, ThemeRole.TextPrimary,
                                       TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.teamName.gameObject, 60f);

        view.teamMeta = UIFactory.Text("Meta", mine, Caption, ThemeRole.TextMuted,
                                       TextAlignmentOptions.Left);
        UIFactory.Size(view.teamMeta.gameObject, 40f);

        for (int i = 0; i < TeamDoorView.MaxMembers; i++)
        {
            view.memberRows[i] = UIFactory.Text("Member" + i, mine, Body,
                                                ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Left);
            UIFactory.Size(view.memberRows[i].gameObject, 52f);
            view.memberRows[i].gameObject.SetActive(false);
        }

        UIFactory.Text("InviteLabel", mine, Caption, ThemeRole.TextMuted,
                       TextAlignmentOptions.Left, "team.invite");

        view.inviteCode = UIFactory.Text("Invite", mine, Title, ThemeRole.Accent,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        view.inviteCode.characterSpacing = 10f;
        UIFactory.Size(view.inviteCode.gameObject, 64f);

        // Said plainly, because this is the code people confuse with the trail's
        // and then wonder why nobody can join them.
        TextMeshProUGUI inviteHint = UIFactory.Text("InviteHint", mine, Caption,
                                                    ThemeRole.TextMuted,
                                                    TextAlignmentOptions.Left,
                                                    "team.inviteHint");
        inviteHint.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(inviteHint.gameObject, 68f);

        view.leaveButton = FullWidthButton(mine, "Leave", "team.leave",
                                           ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ------------------------------------------------------ the board
        RectTransform board = UIFactory.Rect("Board", scroll);
        UIFactory.VerticalStack(board, 6f);

        Eyebrow(board, "team.board");

        TextMeshProUGUI empty = UIFactory.Text("Empty", board, Secondary,
                                               ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left, "team.boardEmpty");
        UIFactory.Size(empty.gameObject, 48f);
        view.boardEmpty = empty.gameObject;

        for (int i = 0; i < TeamDoorView.MaxRows; i++)
        {
            RectTransform row = UIFactory.Rect("Row" + i, board);
            UIFactory.Size(row.gameObject, 56f);

            // The wash that marks your own row, behind the text rather than in
            // it: a board is read from across a room.
            view.boardHighlight[i] = UIFactory.Plain(row, Color.clear, UIShape.Rounded);
            view.boardHighlight[i].raycastTarget = false;
            view.boardHighlight[i].gameObject.SetActive(false);

            RectTransform line = UIFactory.Rect("Line", row);
            UIFactory.Stretch(line);
            line.offsetMin = new Vector2(14f, 0f);
            line.offsetMax = new Vector2(-14f, 0f);
            HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 14f);
            layout.childAlignment = TextAnchor.MiddleLeft;

            view.boardRank[i] = UIFactory.Text("Rank", line, Body, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left);
            UIFactory.Size(view.boardRank[i].gameObject, 52f, 56f).flexibleWidth = 0f;

            view.boardName[i] = UIFactory.Text("Name", line, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left);
            UIFactory.Flexible(view.boardName[i].gameObject);
            view.boardName[i].textWrappingMode = TextWrappingModes.NoWrap;
            view.boardName[i].overflowMode = TextOverflowModes.Ellipsis;

            view.boardPoints[i] = UIFactory.Text("Points", line, Body, ThemeRole.Accent,
                                                 TextAlignmentOptions.Right, null,
                                                 FontStyles.Bold);
            UIFactory.Size(view.boardPoints[i].gameObject, 100f, 56f).flexibleWidth = 0f;

            view.boardRows[i] = row;
            row.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------- status
        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(view.statusLabel.gameObject, 56f);
        view.statusLabel.gameObject.SetActive(false);

        view.closeButton = QuietAction(card, "Close", "team.close", flow.Close, "close team");

        return panel.gameObject;
    }

    static GameObject BuildTrailsDoor(Transform parent, DoorFlow flow,
                                      TrailRowView rowPrefab, out TrailsDoor door)
    {
        RectTransform panel = Door(parent, "Trails", plain: false, inner: out RectTransform inner);

        door = panel.gameObject.AddComponent<TrailsDoor>();
        door.rowPrefab = rowPrefab;

        RectTransform card = Card(inner, "Card", 26f);

        // --- the heading ---
        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.VerticalStack(head, 6f, false);

        Eyebrow(head, "trails.eyebrow");

        UIFactory.Text("Title", head, Display, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "trails.title", FontStyles.Bold);

        door.subtitle = UIFactory.Text("Subtitle", head, Secondary, ThemeRole.TextSecondary,
                                       TextAlignmentOptions.TopLeft, "trails.subtitle");

        // --- the list ---
        RectTransform listHolder = UIFactory.Rect("Body", card);
        door.listSize = UIFactory.Size(listHolder.gameObject, 320f);

        RectTransform content = UIFactory.ScrollList("Scroll", listHolder, 12f);
        door.listParent = content;

        door.waitingState = StateNote(content, "Waiting", "trails.loading", null);
        door.emptyState = StateNote(content, "Empty", "trails.empty", "trails.emptyBody");
        door.failedState = StateNote(content, "Failed", "trails.failed", null,
                                     out door.failedLabel);

        // --- what to do about it ---
        RectTransform actions = UIFactory.Rect("Actions", card);
        UIFactory.VerticalStack(actions, 10f, false);

        door.joinButton = PrimaryAction(actions, "Join", "trails.join", flow.OpenCode,
                                        "Open code entry");

        // The way out. Not a nicety: without it a reader with no trails had the
        // trail list and the code screen and nothing else, each leading only to
        // the other, with the nav bar hidden behind both.
        QuietAction(actions, "NotNow", "trails.notNow", flow.Close, "Leave trails");

        CardClose(card, flow.Close, "Close trails");

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// Enter a code: six boxes on a card, over the blurred map.
    ///
    /// The boxes are grouped three and three. A six-character code read off a
    /// whiteboard or out of a message is held in the head in two halves, and six
    /// evenly spaced boxes ask the reader to keep count instead.
    /// </summary>
    static GameObject BuildCodeDoor(Transform parent, DoorFlow flow, out CodeDoor door)
    {
        RectTransform panel = Door(parent, "Code", plain: false, inner: out RectTransform inner);

        door = panel.gameObject.AddComponent<CodeDoor>();

        RectTransform card = Card(inner, "Card", 26f);

        // The one card in the app that is typed into, so the one that has to get
        // out of the keyboard's way. Without this the keyboard covers the six
        // boxes - the exact thing the reader is watching while they fill them in.
        KeyboardAvoider avoider = card.gameObject.AddComponent<KeyboardAvoider>();
        avoider.target = card;

        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.VerticalStack(head, 6f, false);

        Eyebrow(head, "code.eyebrow");

        UIFactory.Text("Title", head, Display, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "code.title", FontStyles.Bold);

        UIFactory.Text("Body", head, Secondary, ThemeRole.TextSecondary,
                       TextAlignmentOptions.TopLeft, "code.body");

        // --- the boxes, and the field underneath them ---
        RectTransform boxRow = UIFactory.Rect("Boxes", card);
        UIFactory.Size(boxRow.gameObject, BoxHeight);

        CodeBoxes boxes = boxRow.gameObject.AddComponent<CodeBoxes>();
        door.boxes = boxes;

        // The real input, stretched across the whole row and drawn nowhere. Its own
        // text is transparent; the boxes above are a picture of what is in it. It
        // stays the full width of the row so a tap anywhere near the boxes lands on
        // it and puts the keyboard up.
        boxes.field = UIFactory.InputField("Field", boxRow, null,
                                           out TextMeshProUGUI hiddenPlaceholder);
        RectTransform fieldRect = boxes.field.GetComponent<RectTransform>();
        UIFactory.Stretch(fieldRect);
        Transparent(boxes.field.textComponent);
        Transparent(hiddenPlaceholder);
        boxes.field.caretColor = new Color(0f, 0f, 0f, 0f);
        boxes.field.customCaretColor = true;

        // The field's own graphic is drawn nowhere and still catches the tap, which
        // is what puts the keyboard up when the reader presses a box.
        Image catcher = boxes.field.GetComponent<Image>();
        if (catcher != null)
        {
            Transparent(catcher);
            catcher.raycastTarget = true;
        }

        boxes.cells = new CodeBoxes.Cell[CodeBoxes.Length];

        float span = CodeBoxes.Length * BoxWidth + (CodeBoxes.Length - 1) * BoxGap + BoxSplit;
        float first = -span * 0.5f + BoxWidth * 0.5f;

        for (int i = 0; i < CodeBoxes.Length; i++)
        {
            float x = first + i * (BoxWidth + BoxGap) +
                      (i >= CodeBoxes.Length / 2 ? BoxSplit : 0f);

            RectTransform box = UIFactory.Rect("Box" + i, boxRow);
            UIFactory.Corner(box, new Vector2(0.5f, 0.5f), new Vector2(BoxWidth, BoxHeight),
                             new Vector2(x, 0f));

            CodeBoxes.Cell cell = new CodeBoxes.Cell();

            // Coloured by CodeBoxes rather than by the theme, because these change
            // with what the reader has typed as well as with the palette.
            cell.fill = UIFactory.Plain(box, ThemeManager.Get(ThemeRole.SurfaceAlt),
                                        UIShape.Rounded);
            cell.fill.raycastTarget = false;

            RectTransform edge = UIFactory.Rect("Edge", box);
            UIFactory.Stretch(edge);
            cell.outline = UIFactory.Plain(
                edge, ThemeManager.Get(i == 0 ? ThemeRole.Accent : ThemeRole.Line));
            cell.outline.sprite = UISprites.RoundedOutline;
            cell.outline.type = Image.Type.Sliced;
            cell.outline.pixelsPerUnitMultiplier = 1f;
            cell.outline.raycastTarget = false;

            // A bar down the middle of the empty box, blinking. The first version
            // ran a short line along the inside of the bottom edge, which at this
            // size reads as a rendering artefact rather than as a caret.
            RectTransform caret = UIFactory.Rect("Caret", box);
            UIFactory.Corner(caret, new Vector2(0.5f, 0.5f), new Vector2(4f, 62f),
                             Vector2.zero);
            UIFactory.Plain(caret, ThemeManager.Get(ThemeRole.Accent),
                            UIShape.Pill).raycastTarget = false;
            caret.gameObject.SetActive(false);
            cell.caret = caret;

            TextMeshProUGUI letter = UIFactory.Text("Letter", box, 64f, ThemeRole.TextPrimary,
                                                    TextAlignmentOptions.Center, null,
                                                    FontStyles.Bold);
            UIFactory.Stretch(letter.rectTransform);
            letter.text = string.Empty;
            letter.raycastTarget = false;
            cell.letter = letter;

            boxes.cells[i] = cell;
        }

        // --- what came back ---
        door.message = UIFactory.Text("Message", card, Secondary, ThemeRole.StatusPoor,
                                      TextAlignmentOptions.TopLeft);
        door.message.gameObject.SetActive(false);

        RectTransform actions = UIFactory.Rect("Actions", card);
        UIFactory.VerticalStack(actions, 10f, false);

        door.joinButton = PrimaryAction(actions, "Join", "code.join", door.Join,
                                        "Join with code");
        door.joinLabel = door.joinButton.GetComponentInChildren<TextMeshProUGUI>();

        door.cancelButton = QuietAction(actions, "Cancel", "code.cancel", door.Cancel,
                                        "Cancel code entry");

        CardClose(card, door.Cancel, "Close code entry");

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
                              out RectTransform inner)
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

        // No header band any more. It existed because a bare title floating on the
        // blurred map was printed across whatever the map had in that corner, and
        // it fixed that by pasting an opaque full-bleed rectangle over the top
        // quarter of the screen - which is the thing that made these screens look
        // stapled together. A card carries its own surface, so the title has one
        // without anything being bolted to the edge of the glass.

        AddFade(panel.gameObject, 22f);

        RectTransform safe = SafeContent(panel, padBottom: true);

        inner = UIFactory.Rect("Inner", safe);
        UIFactory.Stretch(inner);
        inner.offsetMin = new Vector2(Margin, Margin);
        inner.offsetMax = new Vector2(-Margin, -Margin);

        return panel;
    }

    // ------------------------------------------------------------------- cards

    /// <summary>Corner radius aside, everything a card is made of.</summary>
    const float CardPad = 40f;
    const float CardTop = 40f;
    const float CardBottom = 34f;

    /// <summary>How far the shadow reaches past the card, and how far it falls.</summary>
    const float ShadowSpread = 40f;
    const float ShadowDrop = 14f;

    const float BoxWidth = 132f;
    const float BoxHeight = 160f;
    const float BoxGap = 16f;

    /// <summary>The wider gap in the middle, which splits six boxes into two threes.</summary>
    const float BoxSplit = 32f;

    /// <summary>
    /// A card resting on the bottom of the screen, with a shadow under it and an
    /// edge around it.
    ///
    /// It sizes itself to whatever is put in it: the stack and the fitter are on
    /// the card, and the three graphics that make it look like a surface are
    /// marked ignoreLayout so the layout does not try to stack those as well.
    ///
    /// The card GameObject carries no graphic of its own. Its own Image would draw
    /// before every child, which would put the shadow on top of the card it is
    /// supposed to be under, so the surface is a child like the rest and the
    /// drawing order is the child order: shadow, face, edge, then the content.
    /// </summary>
    static RectTransform Card(Transform parent, string name, float spacing)
    {
        RectTransform card = UIFactory.Rect(name, parent);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(1f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.offsetMin = Vector2.zero;
        card.offsetMax = Vector2.zero;

        RectTransform shade = UIFactory.Rect("Shadow", card);
        UIFactory.Stretch(shade);
        shade.offsetMin = new Vector2(-ShadowSpread, -ShadowSpread - ShadowDrop);
        shade.offsetMax = new Vector2(ShadowSpread, ShadowSpread - ShadowDrop);

        // Black in both themes, and not a theme role. A shadow is the absence of
        // light rather than a colour, and a light-theme shadow tinted towards the
        // background is just a grey ring.
        Image cast = UIFactory.Plain(shade, new Color(0f, 0f, 0f, 0.42f));
        cast.sprite = UISprites.Shadow;
        cast.type = Image.Type.Sliced;
        cast.pixelsPerUnitMultiplier = 1f;
        cast.raycastTarget = false;
        Ignore(shade);

        RectTransform face = UIFactory.Rect("Face", card);
        UIFactory.Stretch(face);

        // A raycast target, so a tap that lands on the card does not fall through
        // to the map behind a screen the reader has not finished with.
        UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;
        Ignore(face);

        RectTransform edge = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(edge);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.CardOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
        Ignore(edge);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, spacing);
        stack.padding = new RectOffset((int)CardPad, (int)CardPad,
                                       (int)CardTop, (int)CardBottom);

        return card;
    }

    /// <summary>
    /// Keeps a graphic out of its parent's layout. The card's surface, edge and
    /// shadow are all stretched to the card and are not items in its stack.
    /// </summary>
    static void Ignore(RectTransform rt)
    {
        LayoutElement element = rt.gameObject.GetComponent<LayoutElement>();
        if (element == null)
            element = rt.gameObject.AddComponent<LayoutElement>();

        element.ignoreLayout = true;
    }

    /// <summary>
    /// The small capitalised line above a card's title.
    ///
    /// It says what kind of thing the card is, which a title alone does not: "My
    /// trails" is a heading, and a heading on a surface floating over a map could
    /// be a section of the map. Set in the accent so the card has one coloured
    /// thing in it that is not a button.
    /// </summary>
    static TextMeshProUGUI Eyebrow(Transform parent, string key)
    {
        TextMeshProUGUI label = UIFactory.Text("Eyebrow", parent, 28f, ThemeRole.Accent,
                                               TextAlignmentOptions.TopLeft, key,
                                               FontStyles.Bold);
        label.characterSpacing = 10f;
        return label;
    }

    /// <summary>The one filled button on a card.</summary>
    static Button PrimaryAction(Transform parent, string name, string key,
                                UnityEngine.Events.UnityAction call, string what)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);

        Button button = UIFactory.Button(rt, ThemeRole.Accent, 1f, UIShape.Rounded);
        Wire(button.onClick, call, what);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Center, key,
                                               FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return button;
    }

    /// <summary>
    /// The one under it: a label and a tap target, with no box drawn around it.
    ///
    /// A second outlined button next to the first reads as a second choice of
    /// equal weight, and these never are - this is the way out of the card, not
    /// the other thing to do with it.
    /// </summary>
    static Button QuietAction(Transform parent, string name, string key,
                              UnityEngine.Events.UnityAction call, string what)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, 96f);

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0f);
        Wire(button.onClick, call, what);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextSecondary,
                                               TextAlignmentOptions.Center, key,
                                               FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return button;
    }

    /// <summary>
    /// The close control in a card's top corner.
    ///
    /// Always there. It used to appear only when a trail was already open, which
    /// is the one state in which the reader does not need it - and its absence in
    /// the other state is what left them with no way off the screen at all.
    /// </summary>
    static Button CardClose(RectTransform card, UnityEngine.Events.UnityAction call,
                            string what)
    {
        RectTransform rt = UIFactory.Rect("Close", card);
        UIFactory.Corner(rt, new Vector2(1f, 1f), new Vector2(84f, 84f),
                         new Vector2(-24f, -24f));
        Ignore(rt);

        Button button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Circle);
        Wire(button.onClick, call, what);
        Icon(rt, AppIcon.Close, 38f, ThemeRole.TextSecondary);

        return button;
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
        // No padding of its own. Inside a card the stack around it already spaces
        // it, and forty more at the top left the note floating well below the
        // subtitle it was standing in for.
        UIFactory.VerticalStack(rt, 8f, false);

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

    /// <summary>
    /// Strips automatic theming from anything a controller recolours itself.
    ///
    /// Only ever call this where a controller genuinely does. Eighteen of these
    /// were on primary buttons and their labels - Guide me there, Get started,
    /// I accept, Join, Play, Check, Next - where nothing recoloured anything, so
    /// the strip did not prevent a fight, it just froze the editor's theme into
    /// the asset. Every one of those buttons shipped in the dark accent, #45C4B0,
    /// and in the light theme carried white text at 2.0:1 against it.
    ///
    /// A ThemedElement is the safe default. It repaints in OnEnable as well as on
    /// the event, and a controller that sets a colour afterwards still wins.
    /// </summary>
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
