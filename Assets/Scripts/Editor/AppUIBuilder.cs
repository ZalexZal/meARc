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

    /// <summary>
    /// What the floating tab bar takes off the bottom of the screen: 64px of
    /// capsule standing 14px off the edge, at 2.77 reference pixels to one.
    /// </summary>
    const float Nav = 216f;
    const float Margin = 44f;

    /// <summary>The gutter things floating on the map keep from the edge of the glass: 12px.</summary>
    const float Gutter = 33f;

    /// <summary>Inside a sheet, 20px.</summary>
    const float SheetPad = 55f;

    /// <summary>A button: 50px, so a thumb finds it without looking.</summary>
    const float Btn = 138f;

    /// <summary>A row in a grouped list: 52px.</summary>
    const float Row52 = 144f;

    /// <summary>Smallest size anything tappable may be, in reference pixels. 48dp.</summary>
    const float Tap = 128f;

    // Type scale. Nothing below Caption - 32 reference pixels is about 12dp, and
    // that is the floor for text a walking reader has to read.
    // A scale with real steps in it. The old one ran 62/48/42/36/32, which puts
    // body and secondary seventeen per cent apart - close enough that a sheet of
    // text reads as one undifferentiated block and nothing looks deliberate.
    // Ratios now sit near 1.35 at the top, where one line has to carry a whole
    // screen, and tighten towards the bottom where UI text lives.
    //
    // Daylight v2 (3 October) is 12 / 14 / 17 / 21 / 28 / 34, which is this at
    // 2.77 reference pixels to a prototype pixel. Display is a screen's title,
    // Title a section's, Secondary the 14px line under a name, Caption the 12px
    // floor. Large is used once, for the Profile heading.
    const float Large = 94f;
    const float Display = 78f;
    const float Title = 58f;
    const float Body = 47f;
    const float Secondary = 39f;
    const float Caption = 33f;

    const string PrefabFolder = "Assets/Prefabs";
    const string RowPrefabPath = PrefabFolder + "/BuildingRow.prefab";
    const string PillPrefabPath = PrefabFolder + "/FilterPill.prefab";
    const string MarkerPrefabPath = PrefabFolder + "/BuildingMarker.prefab";
    const string MapPinPrefabPath = PrefabFolder + "/MapPin.prefab";
    const string TrailRowPrefabPath = PrefabFolder + "/TrailRow.prefab";
    const string PieceRowPrefabPath = PrefabFolder + "/PieceRow.prefab";
    const string GraphNodePrefabPath = PrefabFolder + "/TrailGraphNode.prefab";
    const string OptionPrefabPath = PrefabFolder + "/AnswerOption.prefab";

    // Everything the builder owns under the Canvas. Removed and rebuilt every run.
    // Old names are kept in the list so a scene built by an earlier version is
    // cleaned up rather than left with two of everything.
    static readonly string[] GeneratedNames =
    {
        "MapPanel", "CameraPanel", "BrowsePanel", "SearchPanel", "SettingsPanel",
        "InfoPanel", "InfoSheet", "BottomNav", "ProximityToast", "Tutorial",
        "NearbyButton", "VpsIndicator", "NearbyPanel", "LiveARView", "ReadyStateView",
        "SearchContent", "SettingsContent", "Doors", "WorkSheet"
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

        AppFonts.Ensure();
        ConfigureCanvas(canvas);
        RemoveGenerated(root);
        EnsureFolder();

        BuildingRowView rowPrefab = CreateRowPrefab();
        MapPinView pinPrefab = CreateMapPinPrefab();
        TrailRowView trailRowPrefab = CreateTrailRowPrefab();
        PieceRowView pieceRowPrefab = CreatePieceRowPrefab();
        TrailGraphNodeView graphNodePrefab = CreateGraphNodePrefab();
        AnswerOptionView optionPrefab = CreateOptionPrefab();

        UserLocationService location = BuildServices();

        // Order in the hierarchy is draw order: panels first, then the sheet that
        // has to float over any of them, then the nav bar on top of everything.
        RectTransform mapPanel = Panel(root, "MapPanel");
        RectTransform cameraPanel = Panel(root, "CameraPanel", transparent: true);
        RectTransform browsePanel = Panel(root, "BrowsePanel");
        RectTransform settingsPanel = Panel(root, "SettingsPanel");

        // The glass bar floats over the panels and under every sheet, toast and
        // door built after it.
        RectTransform navRow = BuildNav(root);

        InfoPanelController info = BuildInfoSheet(root, location, pieceRowPrefab, optionPrefab);

        MapTabController mapTab = BuildMapTab(mapPanel, location, pinPrefab, info);
        CameraTabController cameraTab = BuildCameraTab(cameraPanel, location, rowPrefab, info);
        SearchTabController browseTab = BuildBrowseTab(browsePanel, location, rowPrefab,
                                                       graphNodePrefab);
        SettingsTabController settingsTab = BuildSettingsTab(settingsPanel);

        // BuildNav returns the padded row the buttons go in. The bar itself is its
        // parent, and that is what the tab controller shows and hides.
        // Under the marker sheet in draw order, so an alert can never cover
        // something the reader opened on purpose.
        ProximityToast toast = BuildProximityToast(root, location, info);

        // The phone's back button. It presses whatever Close or Back is on top,
        // so back always agrees with the control the reader can see.
        root.gameObject.AddComponent<BackKey>();

        TabNavigationController tabs = WireTabs(
            navRow, mapPanel, cameraPanel, browsePanel, settingsPanel, settingsTab);

        // Serialized references rather than delegates: an Action assigned here is
        // silently discarded when the scene saves, and the control then does nothing.
        mapTab.tabs = tabs;

        // Both next-stop cards open the list and the camera through the tabs.
        foreach (NextStopCard card in root.GetComponentsInChildren<NextStopCard>(true))
        {
            card.tabs = tabs;
            card.stopList = info.stopList;
            EditorUtility.SetDirty(card);
        }

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

        if (info.capture != null)
        {
            info.capture.tabs = tabs;
            EditorUtility.SetDirty(info.capture);
        }

        WireTapInput(cameraPanel, info);

        // Last, so the walkthrough covers the nav bar too. A first run with a live
        // tab bar behind the explanation is a first run people tap straight past.
        settingsTab.tutorial = BuildTutorial(root);
        EditorUtility.SetDirty(settingsTab);

        // Over the tab bar, as every sheet is.
        // No badge sheet: AR Trails is a tool, not a game (6 Nov 2026).

        settingsTab.languageSheet = BuildLanguageSheet(root, settingsTab.languageValueLabel);
        settingsTab.workSheet = BuildWorkSheet(root);

        // Last of all, so the way in draws over every screen it stands in front of -
        // the nav bar included, which it also switches off. A modal with a live tab
        // bar behind it is a tab bar people tap straight past.
        // The map's own controls go down with the nav bar while a door is up, so
        // what the blur photographs is a map rather than a screenshot of the app.
        // Safe holds the top bar, the scale, the attribution and the dock; the map
        // itself is a sibling of it and stays.
        Transform mapChrome = mapPanel.Find("Safe");

        DoorFlow doors = BuildDoors(root, tabs.bottomNav, trailRowPrefab,
                                    mapChrome != null ? mapChrome.gameObject : null,
                                    mapPanel.GetComponentInChildren<MapView>(true));
        doors.workSheet = settingsTab.workSheet;
        EditorUtility.SetDirty(doors);

        AddFade(mapPanel.gameObject);
        AddFade(browsePanel.gameObject);
        AddFade(settingsPanel.gameObject);

        // The toast says "done" for whatever is on top - a door, a page, the
        // stop sheet - so it is drawn over all of them.
        toast.transform.SetAsLastSibling();

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

        // Under 14: whether a parent has agreed. The server enforces it; this
        // is what lets the app show the right door instead of a refusal.
        go.AddComponent<ConsentService>();

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

        // Your numbers and badges. Harmless before 0038: my_profile 404s once, and
        // the You tab shows who you are without the numbers.
        go.AddComponent<ProfileService>();

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
        spawner.styles = CreateShapeStyles(spawner.defaultPrefab);
        LightMarkers();
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
    // ------------------------------------------------------------ Daylight sheet

    /// <summary>The grabber and the 48px bar under it.</summary>
    const float SheetHead = 164f;

    /// <summary>The footer: 12 above a 50px button and 16 under it.</summary>
    const float SheetFoot = 215f;

    /// <summary>Kept clear above a sheet at full height, under the status bar.</summary>
    const float SheetGap = 122f;

    /// <summary>
    /// The stop sheet: one sheet the stop opens in, at half height or full.
    ///
    ///   ━━━━
    ///   ‹            title              ✕      the bar: back on a page, close always
    ///   ③  ● You're here · 6 m                 the stop's number and where it is
    ///   Театър „Невена Коканова“
    ///   from 1949 · Drama theatre
    ///   Its words...
    ///   ┌ ▶ The stage from inside        › ┐   what is here, as one inset group
    ///   └ ? Question for 10 points       › ┘
    ///   ─────────────────────────────────
    ///   (⋯)  [      Answer the question   ]    the one thing to do, and the rest
    ///
    /// Pieces open as pages pushed into the same sheet; hints and the report
    /// chooser are pages over it. The ⋯ menu holds learn more, hide, move and
    /// report.
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
        controller.scrimButton = CreateScrim("InfoScrim", holder, out GameObject scrim, 0.3f);
        controller.scrim = scrim;

        RectTransform sheetSafe = UIFactory.Rect("Safe", holder);
        UIFactory.Stretch(sheetSafe);
        SafeAreaPad sheetPad = sheetSafe.gameObject.AddComponent<SafeAreaPad>();
        sheetPad.left = true;
        sheetPad.right = true;

        // Anchored to the bottom of the screen with a height SheetDetents sets, so
        // the footer stays under the thumb at either size.
        RectTransform sheet = UIFactory.Rect("Sheet", sheetSafe);
        UIFactory.BottomStretch(sheet, 1150f, 0f);
        controller.panelRoot = sheet.gameObject;
        SheetFace(sheet);

        SheetAnimator animator = sheet.gameObject.AddComponent<SheetAnimator>();
        animator.openY = 0f;
        controller.sheet = animator;

        RectTransform head = SheetHeader(sheet, out Button back, out TextMeshProUGUI title,
                                         out Button close, out CanvasGroup backGroup);
        SheetDetents detents = head.gameObject.AddComponent<SheetDetents>();
        detents.sheetRect = sheet;
        detents.largeGap = SheetGap;
        controller.detents = detents;

        Wire(close.onClick, controller.ClosePanel, "Stop sheet close");

        RectTransform area = SheetArea(sheet);

        // --- the overview: the stop, its words, what is here, and the footer
        RectTransform overview = UIFactory.Rect("Overview", area);
        UIFactory.Stretch(overview);

        RectTransform body = UIFactory.Rect("Body", overview);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(0f, SheetFoot);

        RectTransform content = SheetScroll(body, detents);

        // The stop's number in its colour, and where it is from here.
        RectTransform status = UIFactory.Rect("Status", content);
        UIFactory.Size(status.gameObject, 89f);
        HorizontalLayoutGroup statusRow = UIFactory.HorizontalStack(status, 28f);
        statusRow.childAlignment = TextAnchor.MiddleLeft;

        RectTransform badge = UIFactory.Rect("Badge", status);
        UIFactory.Size(badge.gameObject, 89f, 89f).flexibleWidth = 0f;
        controller.colourDot = UIFactory.Plain(badge, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        controller.colourDot.raycastTarget = false;
        BadgeEdge(badge);

        controller.numberLabel = UIFactory.Text("Number", badge, Secondary, ThemeRole.OnAccent,
                                                TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(controller.numberLabel.rectTransform);
        Unthemed(controller.numberLabel.gameObject);

        RectTransform line = UIFactory.Rect("Line", status);
        UIFactory.Flexible(line.gameObject);
        HorizontalLayoutGroup lineRow = UIFactory.HorizontalStack(line, 17f);
        lineRow.childAlignment = TextAnchor.MiddleLeft;

        RectTransform dot = UIFactory.Rect("Dot", line);
        UIFactory.Size(dot.gameObject, 22f, 22f).flexibleWidth = 0f;
        controller.statusDot = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.GoodDot), UIShape.Circle);
        controller.statusDot.raycastTarget = false;

        controller.bearingArrow = Arrow(line, location, BearingArrow.Frame.NorthUp, 39f);

        controller.distanceText = UIFactory.Text("Value", line, Secondary, ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Left);
        UIFactory.Flexible(controller.distanceText.gameObject);
        controller.distanceText.textWrappingMode = TextWrappingModes.NoWrap;
        Unthemed(controller.distanceText.gameObject);

        // Its name, and the line about it.
        RectTransform heading = UIFactory.Rect("Heading", content);
        UIFactory.VerticalStack(heading, 22f, false);

        controller.titleText = UIFactory.Text("TitleText", heading, Display, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(controller.titleText);

        controller.metaText = UIFactory.Text("MetaText", heading, Secondary, ThemeRole.TextMuted,
                                             TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(controller.metaText);

        controller.descriptionText = UIFactory.Text("DescriptionText", content, Body,
                                                    ThemeRole.TextSecondary,
                                                    TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(controller.descriptionText);

        // What is here: one inset group of rows, and the pages they open.
        MarkerPieceController pieces = BuildPieces(sheet, content, area, pieceRowPrefab, optionPrefab,
                                                   back, title, backGroup);
        pieces.overview = overview.gameObject;
        controller.pieces = pieces;

        // --- the footer: ⋯ and the one thing to do next
        RectTransform footer = SheetFooter(overview);

        RectTransform more = UIFactory.Rect("More", footer);
        UIFactory.Size(more.gameObject, Btn, Btn).flexibleWidth = 0f;
        Button moreButton = UIFactory.Button(more, ThemeRole.Fill, 1f, UIShape.Circle);
        Icon(more, AppIcon.More, 61f, ThemeRole.TextPrimary);

        RectTransform primary = UIFactory.Rect("Primary", footer);
        UIFactory.Size(primary.gameObject, Btn);
        UIFactory.Flexible(primary.gameObject);
        controller.footerPrimary = UIFactory.Button(primary, ThemeRole.Accent, 1f, UIShape.Pill);

        RectTransform primaryRow = UIFactory.Rect("Row", primary);
        UIFactory.Stretch(primaryRow);
        HorizontalLayoutGroup primaryLayout = UIFactory.HorizontalStack(primaryRow, 22f);
        primaryLayout.childAlignment = TextAnchor.MiddleCenter;

        controller.footerPrimaryIcon = Icon(primaryRow, AppIcon.Navigation, 46f, ThemeRole.OnAccent).gameObject;

        controller.footerPrimaryLabel = UIFactory.Text("Label", primaryRow, Body, ThemeRole.OnAccent,
                                                       TextAlignmentOptions.Center);
        controller.footerPrimaryLabel.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform secondary = UIFactory.Rect("Secondary", footer);
        UIFactory.Size(secondary.gameObject, Btn);
        UIFactory.Flexible(secondary.gameObject);
        controller.footerSecondary = UIFactory.Button(secondary, ThemeRole.Fill, 1f, UIShape.Pill);

        controller.footerSecondaryLabel = UIFactory.Text("Label", secondary, Body, ThemeRole.TextPrimary,
                                                         TextAlignmentOptions.Center);
        UIFactory.Stretch(controller.footerSecondaryLabel.rectTransform);
        secondary.gameObject.SetActive(false);

        // --- the ⋯ menu, over everything in the sheet
        controller.menu = BuildSheetMenu(holder, moreButton, controller);

        // The animator hides the sheet by moving it, so it stays enabled.
        sheet.anchoredPosition = new Vector2(0f, -1150f - 40f);

        // Pages over the sheet, built after it so they draw on top.
        controller.reportSheet = BuildReportChooser(sheetSafe, controller);
        controller.hintSheet = BuildHintCard(sheetSafe, controller);
        controller.stopList = BuildStopList(sheetSafe, controller, location);

        if (controller.pieces != null && controller.pieces.question != null)
            controller.pieces.question.hintSheet = controller.hintSheet;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// A sheet's surface: 24px corners at the top, run on past the bottom of the
    /// screen so the bottom corners never show, and a hairline round it.
    /// </summary>
    static void SheetFace(RectTransform sheet)
    {
        RectTransform face = UIFactory.Rect("Face", sheet);
        UIFactory.Stretch(face);
        face.offsetMin = new Vector2(0f, -120f);
        UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;

        RectTransform edge = UIFactory.Rect("Outline", sheet);
        UIFactory.Stretch(edge);
        edge.offsetMin = new Vector2(0f, -120f);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.CardOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
    }

    /// <summary>
    /// The top of a sheet: the grabber, and a 48px bar with back on the left, the
    /// title in the middle and close on the right. Back is in a slot of its own
    /// so it can fade out without the title moving off centre.
    /// </summary>
    static RectTransform SheetHeader(RectTransform sheet, out Button back, out TextMeshProUGUI title,
                                     out Button close, out CanvasGroup backGroup)
    {
        RectTransform head = UIFactory.Rect("Head", sheet);
        UIFactory.TopStretch(head, SheetHead);

        // Transparent but a raycast target: the whole header is what is dragged.
        Image catcher = head.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        RectTransform grab = UIFactory.Rect("Grabber", head);
        UIFactory.Corner(grab, new Vector2(0.5f, 1f), new Vector2(100f, 14f), new Vector2(0f, -17f));
        Image grip = UIFactory.Fill(grab, ThemeRole.FillStrong, 1f, UIShape.Pill);
        grip.raycastTarget = false;
        PillFit(grip, 14f);

        RectTransform bar = UIFactory.Rect("Bar", head);
        UIFactory.TopStretch(bar, 133f, 31f);
        HorizontalLayoutGroup row = UIFactory.HorizontalStack(bar, 22f);
        row.padding = new RectOffset(33, 33, 0, 0);
        row.childAlignment = TextAnchor.MiddleCenter;

        RectTransform backSlot = UIFactory.Rect("BackSlot", bar);
        UIFactory.Size(backSlot.gameObject, 122f, 122f).flexibleWidth = 0f;
        backGroup = backSlot.gameObject.AddComponent<CanvasGroup>();
        backGroup.alpha = 0f;
        backGroup.interactable = false;
        backGroup.blocksRaycasts = false;

        RectTransform backRect = UIFactory.Rect("Back", backSlot);
        UIFactory.Stretch(backRect);
        back = UIFactory.Button(backRect, ThemeRole.Fill, 1f, UIShape.Circle);
        back.gameObject.AddComponent<WayOut>();
        Icon(backRect, AppIcon.ChevronLeft, 55f, ThemeRole.TextPrimary);

        title = UIFactory.Text("Title", bar, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Center,
                               null, FontStyles.Bold);
        UIFactory.Flexible(title.gameObject);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.raycastTarget = false;

        RectTransform closeRect = UIFactory.Rect("Close", bar);
        UIFactory.Size(closeRect.gameObject, 122f, 122f).flexibleWidth = 0f;
        close = UIFactory.Button(closeRect, ThemeRole.Fill, 1f, UIShape.Circle);
        Icon(closeRect, AppIcon.Close, 44f, ThemeRole.TextPrimary);

        return head;
    }

    /// <summary>
    /// Everything under a sheet's header, kept clear of the gesture bar. The
    /// SafeAreaPad owns all four offsets of the rect it is on, so the header's
    /// room is taken in a child of it.
    /// </summary>
    static RectTransform SheetArea(RectTransform sheet)
    {
        RectTransform lower = UIFactory.Rect("Lower", sheet);
        UIFactory.Stretch(lower);
        lower.gameObject.AddComponent<SafeAreaPad>().bottom = true;

        RectTransform area = UIFactory.Rect("Area", lower);
        UIFactory.Stretch(area);
        area.offsetMax = new Vector2(0f, -SheetHead);
        return area;
    }

    /// <summary>
    /// A sheet's scrolling body: 20px in from the sides, 16 between things, and
    /// a drag that hands over to the sheet when the sheet is what should move.
    /// </summary>
    static RectTransform SheetScroll(RectTransform body, SheetDetents detents)
    {
        RectTransform content = UIFactory.ScrollList("Scroll", body, 44f);
        content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(55, 55, 11, 55);

        ScrollRect scroll = body.GetComponentInChildren<ScrollRect>();
        SheetScrollHandoff handoff = scroll != null ? scroll.GetComponent<SheetScrollHandoff>() : null;

        if (detents != null && handoff != null)
            handoff.detents = detents;

        return content;
    }

    /// <summary>The footer row: a hairline over it, 20px in, the buttons 10 apart.</summary>
    static RectTransform SheetFooter(RectTransform parent)
    {
        RectTransform footer = UIFactory.Rect("Footer", parent);
        UIFactory.BottomStretch(footer, SheetFoot);
        UIFactory.Fill(footer, ThemeRole.Surface).raycastTarget = true;

        RectTransform rule = UIFactory.Rect("Rule", footer);
        UIFactory.TopStretch(rule, 3f);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);

        HorizontalLayoutGroup row = UIFactory.HorizontalStack(footer, 28f);
        row.padding = new RectOffset(55, 55, 33, 44);
        row.childAlignment = TextAnchor.MiddleLeft;
        return footer;
    }

    /// <summary>
    /// The ⋯ menu: a card that opens upward from the button, over a catcher
    /// that closes it. Learn more, hide, move and report - the controller shows
    /// the ones that apply to the stop on screen.
    /// </summary>
    static SheetMenu BuildSheetMenu(RectTransform holder, Button opener, InfoPanelController controller)
    {
        RectTransform catcherRect = UIFactory.Rect("MenuCatch", holder);
        UIFactory.Stretch(catcherRect);
        Image catcherImage = catcherRect.gameObject.AddComponent<Image>();
        catcherImage.color = new Color(0f, 0f, 0f, 0f);
        Button catcher = catcherRect.gameObject.AddComponent<Button>();
        catcher.targetGraphic = catcherImage;
        catcher.transition = Selectable.Transition.None;
        catcher.gameObject.AddComponent<WayOut>();

        RectTransform safe = UIFactory.Rect("MenuSafe", holder);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.bottom = true;
        pad.left = true;

        // Its bottom edge 8px over the ⋯ button's top.
        RectTransform panel = UIFactory.Rect("Menu", safe);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
        panel.pivot = new Vector2(0f, 0f);
        panel.sizeDelta = new Vector2(637f, 0f);
        panel.anchoredPosition = new Vector2(55f, 44f + Btn + 22f);

        RectTransform shadow = UIFactory.Rect("Shadow", panel);
        UIFactory.Stretch(shadow);
        shadow.offsetMin = new Vector2(-40f, -73f);
        shadow.offsetMax = new Vector2(40f, 7f);
        Image soft = UIFactory.Plain(shadow, new Color(0f, 0f, 0f, 0.18f));
        soft.sprite = UISprites.Shadow;
        soft.type = Image.Type.Sliced;
        soft.raycastTarget = false;
        Ignore(shadow);

        RectTransform face = UIFactory.Rect("Face", panel);
        UIFactory.Stretch(face);
        UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        Ignore(face);

        RectTransform edge = UIFactory.Rect("Outline", panel);
        UIFactory.Stretch(edge);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.RoundedOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
        Ignore(edge);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(panel, 0f, false);
        stack.padding = new RectOffset(17, 17, 17, 17);
        ContentSizeFitter fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button learn = MenuItem(panel, "LearnMore", AppIcon.OpenInNew, "info.learnMore", false,
                                out controller.learnMoreLabel);
        controller.learnMoreButton = learn.gameObject;
        Wire(learn.onClick, controller.OpenExternalLink, "Learn more");

        controller.hideButton = MenuItem(panel, "Hide", AppIcon.Hide, "marker.hide", false,
                                         out controller.hideLabel);
        controller.moveButton = MenuItem(panel, "Move", AppIcon.Move, "menu.move", false,
                                         out controller.moveLabel);
        controller.moveButton.gameObject.SetActive(false);
        controller.reportButton = MenuItem(panel, "Report", AppIcon.Flag, "marker.report", true,
                                           out controller.reportLabel);

        SheetMenu menu = holder.gameObject.AddComponent<SheetMenu>();
        menu.catcher = catcher;
        menu.panel = panel;
        menu.opener = opener;
        menu.items = new[] { learn, controller.hideButton, controller.moveButton, controller.reportButton };

        catcherRect.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        return menu;
    }

    static Button MenuItem(RectTransform panel, string name, AppIcon icon, string key, bool danger,
                           out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, panel);
        UIFactory.Size(rt.gameObject, 133f);
        Button button = UIFactory.Button(rt, ThemeRole.Fill, 0f, UIShape.Rounded);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 33f);
        layout.padding = new RectOffset(33, 33, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;

        ThemeRole ink = danger ? ThemeRole.StatusPoor : ThemeRole.TextPrimary;
        Icon(row, icon, 55f, ink);

        label = UIFactory.Text("Label", row, Body, ink, TextAlignmentOptions.Left, key);
        UIFactory.Flexible(label.gameObject);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return button;
    }

    /// <summary>
    /// What is here, as one inset group of rows, and the page a row opens into.
    ///
    /// The page is a sibling of the overview inside the same sheet: opening a
    /// video does not take the reader off the map, it swaps what the sheet in
    /// front of the map is showing, and Back in the header swaps it back.
    /// </summary>
    static MarkerPieceController BuildPieces(RectTransform sheet, RectTransform content,
                                             RectTransform area, PieceRowView rowPrefab,
                                             AnswerOptionView optionPrefab, Button back,
                                             TextMeshProUGUI title, CanvasGroup backGroup)
    {
        MarkerPieceController pieces = sheet.gameObject.AddComponent<MarkerPieceController>();
        pieces.rowPrefab = rowPrefab;
        pieces.videoIcon = UIIcons.Get(AppIcon.Play);
        pieces.audioIcon = UIIcons.Get(AppIcon.Audio);
        pieces.photoIcon = UIIcons.Get(AppIcon.Mountain);
        pieces.questionIcon = UIIcons.Get(AppIcon.Question);
        pieces.lockIcon = UIIcons.Get(AppIcon.Lock);

        RectTransform section = UIFactory.Rect("Pieces", content);
        UIFactory.Fill(section, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        VerticalLayoutGroup inset = UIFactory.VerticalStack(section, 0f, false);
        inset.padding = new RectOffset(39, 39, 0, 0);
        pieces.section = section.gameObject;
        pieces.listParent = section;

        PieceRowView locked = (PieceRowView)PrefabUtility.InstantiatePrefab(rowPrefab, section);
        locked.name = "Locked";
        locked.gameObject.SetActive(false);
        pieces.lockedRow = locked;

        // ---------------------------------------------------------- the page
        RectTransform page = UIFactory.Rect("PiecePage", area);
        UIFactory.Stretch(page);
        UIFactory.Fill(page, ThemeRole.Surface).raycastTarget = true;
        page.gameObject.AddComponent<PagePush>();
        pieces.page = page.gameObject;

        // A photo has nothing to scroll; pulled, it moves the sheet.
        page.gameObject.AddComponent<DetentsDrag>().detents = sheet.GetComponentInChildren<SheetDetents>(true);
        pieces.pageTitle = title;
        pieces.backButton = back;
        pieces.backGroup = backGroup;

        RectTransform mediaBody = UIFactory.Rect("MediaBody", page);
        UIFactory.Stretch(mediaBody);
        mediaBody.offsetMin = new Vector2(55f, 55f);
        mediaBody.offsetMax = new Vector2(-55f, -11f);
        pieces.media = BuildMediaPage(mediaBody);

        pieces.question = BuildQuestionPage(page, optionPrefab);

        page.gameObject.SetActive(false);
        return pieces;
    }

    static RectTransform OutlineButton(Transform parent, string name, string key,
                                       out Button button, out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Btn);
        UIFactory.Flexible(rt.gameObject);

        button = UIFactory.Button(rt, ThemeRole.Fill, 1f, UIShape.Pill);

        label = UIFactory.Text("Label", rt, Body, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Center, key);
        UIFactory.Stretch(label.rectTransform);

        return rt;
    }

    /// <summary>
    /// The hint ladder, over the marker sheet.
    ///
    /// A card rather than a page, because opening a hint is a decision made
    /// while looking at the question - taking the question off the screen to
    /// show the price of help would be hiding the thing being weighed.
    /// </summary>
    /// <summary>
    /// A page over the stop sheet, at its full height: the same header - back,
    /// title, close - and a body under it. Back closes this page; close closes
    /// the sheet as well.
    /// </summary>
    static RectTransform PushedPage(Transform parent, string name, string titleKey,
                                    out RectTransform content, out RectTransform footer,
                                    out Button back, out Button close, bool hasFooter,
                                    bool dim = false)
    {
        RectTransform panel = UIFactory.Rect(name, parent);
        UIFactory.Stretch(panel);

        // Catches taps above the page, so nothing behind it is pressed by mistake.
        // Dimmed when nothing else is: a page over a stop sheet has the sheet's
        // own scrim behind it already.
        Image catcher = panel.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, dim ? 0.3f : 0f);

        RectTransform safe = UIFactory.Rect("Safe", panel);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.top = true;

        RectTransform sheet = UIFactory.Rect("Sheet", safe);
        UIFactory.Stretch(sheet);
        sheet.offsetMax = new Vector2(0f, -SheetGap);
        sheet.gameObject.AddComponent<PagePush>();
        SheetFace(sheet);

        RectTransform head = SheetHeader(sheet, out back, out TextMeshProUGUI title, out close,
                                         out CanvasGroup backGroup);

        // On the page, not its header: a page is swiped down from wherever the
        // thumb happens to be, and its list hands over a pull from its top.
        Swipeable(sheet, close);
        backGroup.alpha = 1f;
        backGroup.interactable = true;
        backGroup.blocksRaycasts = true;

        title.text = AppStrings.Get(titleKey);
        LocalizedLabel localized = title.gameObject.AddComponent<LocalizedLabel>();
        localized.key = titleKey;

        RectTransform area = SheetArea(sheet);

        RectTransform body = UIFactory.Rect("Body", area);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(0f, hasFooter ? SheetFoot : 0f);
        content = SheetScroll(body, null);

        footer = hasFooter ? SheetFooter(area) : null;
        return panel;
    }

    /// <summary>
    /// Hints, as a page over the question: what they cost, then each one with
    /// its price, the button that opens it, and - for one that costs - a second
    /// press to be sure.
    /// </summary>
    static HintSheetView BuildHintCard(Transform parent, InfoPanelController info)
    {
        RectTransform panel = PushedPage(parent, "HintCard", "hint.title", out RectTransform content,
                                         out RectTransform _, out Button back, out Button close, false);

        HintSheetView view = panel.gameObject.AddComponent<HintSheetView>();
        view.closeButton = back;
        Wire(close.onClick, view.Close, "Hint page close");
        Wire(close.onClick, info.CloseIfOpen, "Hint page close sheet");

        view.introLabel = UIFactory.Text("Intro", content, Secondary, ThemeRole.TextMuted,
                                         TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.introLabel);

        view.emptyLabel = UIFactory.Text("Empty", content, Secondary, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Left, "hint.none");
        UIFactory.Size(view.emptyLabel.gameObject, 60f);

        for (int i = 0; i < HintSheetView.MaxRows; i++)
        {
            RectTransform row = UIFactory.Rect("Hint" + i, content);
            UIFactory.Fill(row, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;

            VerticalLayoutGroup rowStack = UIFactory.VerticalStack(row, 22f, false);
            rowStack.padding = new RectOffset(39, 39, 22, 39);
            view.rows[i] = row;

            RectTransform top = UIFactory.Rect("Top", row);
            UIFactory.Size(top.gameObject, 122f);
            HorizontalLayoutGroup topRow = UIFactory.HorizontalStack(top, 22f);
            topRow.childAlignment = TextAnchor.MiddleLeft;

            view.rowTitles[i] = UIFactory.Text("Name", top, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
            UIFactory.Flexible(view.rowTitles[i].gameObject);
            view.rowTitles[i].textWrappingMode = TextWrappingModes.NoWrap;
            view.rowTitles[i].raycastTarget = false;

            view.rowChips[i] = UIFactory.Text("Note", top, Secondary, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Right);
            view.rowChips[i].textWrappingMode = TextWrappingModes.NoWrap;
            view.rowChips[i].raycastTarget = false;
            view.rowChipHolders[i] = view.rowChips[i].gameObject;

            view.rowButtons[i] = SmallPillButton(top, "Unlock", ThemeRole.Fill, ThemeRole.TextPrimary,
                                                 out view.rowActions[i]);

            view.rowCancels[i] = SmallPillButton(top, "Cancel", ThemeRole.Surface, ThemeRole.AccentText,
                                                 out TextMeshProUGUI cancelLabel, 0f);
            cancelLabel.text = AppStrings.Get("hint.cancel");
            cancelLabel.gameObject.AddComponent<LocalizedLabel>().key = "hint.cancel";
            view.rowCancels[i].gameObject.SetActive(false);

            view.rowConfirms[i] = SmallPillButton(top, "Confirm", ThemeRole.Accent, ThemeRole.OnAccent,
                                                  out view.rowConfirmLabels[i]);
            view.rowConfirms[i].gameObject.SetActive(false);

            view.rowBodies[i] = UIFactory.Text("Body", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft);
            UIFactory.FitsItsText(view.rowBodies[i]);
            view.rowBodies[i].raycastTarget = false;

            ContentSizeFitter fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            row.gameObject.SetActive(false);
        }

        view.spentLabel = UIFactory.Text("Spent", content, Secondary, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Left);
        UIFactory.Size(view.spentLabel.gameObject, 54f);
        view.spentLabel.gameObject.SetActive(false);

        view.statusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.statusLabel);
        view.statusLabel.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);
        return view;
    }

    /// <summary>
    /// Every stop, as a page over the map: how many are found, then each one in
    /// order with its number in its colour and where it stands.
    /// </summary>
    static StopListView BuildStopList(Transform parent, InfoPanelController info, UserLocationService location)
    {
        RectTransform panel = PushedPage(parent, "StopList", "list.title", out RectTransform content,
                                         out RectTransform _, out Button back, out Button close, false, true);

        StopListView view = panel.gameObject.AddComponent<StopListView>();
        view.locationService = location;
        // Pulled up out of the next-stop card: the sheet moves, the dim follows.
        view.sheet = (RectTransform)panel.Find("Safe/Sheet");
        view.scrim = panel.GetComponent<Image>();
        view.scrimAlpha = 0.3f;
        view.infoPanel = info;
        view.backButton = back;
        view.closeButton = close;

        view.summaryLabel = UIFactory.Text("Summary", content, Secondary, ThemeRole.TextMuted,
                                           TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.summaryLabel);

        RectTransform list = Inset(content, "List");
        view.listParent = list;

        // The row every stop is drawn with.
        RectTransform row = UIFactory.Rect("Row", list);
        UIFactory.Size(row.gameObject, 166f);
        UIFactory.Button(row, ThemeRole.SurfaceAlt, 0f);

        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;

        RectTransform line = UIFactory.Rect("Line", row);
        UIFactory.Stretch(line);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 33f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform badge = UIFactory.Rect("Badge", line);
        UIFactory.Size(badge.gameObject, 72f, 72f).flexibleWidth = 0f;
        UIFactory.Plain(badge, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle).raycastTarget = false;

        RectTransform hole = UIFactory.Rect("Hole", badge);
        UIFactory.Corner(hole, new Vector2(0.5f, 0.5f), new Vector2(58f, 58f), Vector2.zero);
        UIFactory.Plain(hole, ThemeManager.Get(ThemeRole.SurfaceAlt), UIShape.Circle).raycastTarget = false;
        BadgeEdge(badge);

        TextMeshProUGUI number = UIFactory.Text("Number", badge, Caption, ThemeRole.OnAccent,
                                                TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(number.rectTransform);
        Unthemed(number.gameObject);

        RectTransform text = UIFactory.Rect("Text", line);
        UIFactory.VerticalStack(text, 6f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        TextMeshProUGUI name = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left);
        UIFactory.Size(name.gameObject, 58f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform status = UIFactory.Rect("Status", text);
        UIFactory.Size(status.gameObject, 50f);
        UIFactory.HorizontalStack(status, 17f).childAlignment = TextAnchor.MiddleLeft;

        RectTransform dot = UIFactory.Rect("Dot", status);
        UIFactory.Size(dot.gameObject, 22f, 22f).flexibleWidth = 0f;
        UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.GoodDot), UIShape.Circle).raycastTarget = false;

        TextMeshProUGUI label = UIFactory.Text("Label", status, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left);
        UIFactory.Flexible(label.gameObject);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Unthemed(label.gameObject);

        TextMeshProUGUI hidden = UIFactory.Text("Hidden", line, Secondary, ThemeRole.TextMuted,
                                                TextAlignmentOptions.Right);
        UIFactory.Size(hidden.gameObject, 50f, 160f).flexibleWidth = 0f;
        hidden.gameObject.SetActive(false);

        Chevron(line);

        view.rowTemplate = row;
        row.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);
        return view;
    }

    /// <summary>A 44px pill as wide as its words, for buttons inside a row.</summary>
    static Button SmallPillButton(Transform parent, string name, ThemeRole fill, ThemeRole ink,
                                  out TextMeshProUGUI label, float alpha = 1f)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, 122f).flexibleWidth = 0f;
        Button button = UIFactory.Button(rt, fill, alpha, UIShape.Pill);

        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(rt, 0f);
        layout.padding = new RectOffset(44, 44, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        label = UIFactory.Text("Label", rt, Body, ink, TextAlignmentOptions.Center);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        LayoutElement size = UIFactory.Size(label.gameObject, 122f);
        size.preferredWidth = -1f;
        return button;
    }

    /// <summary>
    /// Reporting, as a page: a sentence naming what is being reported, the
    /// reasons as a radio list, room for a few words, and Send. Blocking whoever
    /// made it sits underneath, because it answers the same moment.
    /// </summary>
    static ReportSheetView BuildReportChooser(Transform parent, InfoPanelController info)
    {
        RectTransform panel = PushedPage(parent, "ReportChooser", "report.header", out RectTransform content,
                                         out RectTransform footer, out Button back, out Button close, true);

        ReportSheetView view = panel.gameObject.AddComponent<ReportSheetView>();
        view.cancelButton = back;
        Wire(close.onClick, view.Close, "Report page close");
        if (info != null)
            Wire(close.onClick, info.CloseIfOpen, "Report page close sheet");

        view.introLabel = UIFactory.Text("Intro", content, Body, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.introLabel);

        RectTransform options = UIFactory.Rect("Reasons", content);
        UIFactory.VerticalStack(options, 28f, false);

        for (int i = 0; i < ReportSheetView.Reasons.Length; i++)
        {
            string key = "report.reason." + ReportSheetView.Reasons[i];
            RadioRow(options, "Reason" + i, key, out view.reasonButtons[i], out view.reasonLabels[i],
                     out view.reasonRadios[i], out view.reasonHoles[i], out view.reasonEdges[i]);
        }

        view.detailField = UIFactory.InputField("Detail", content, "report.detail",
                                                out TextMeshProUGUI _);
        view.detailField.lineType = TMP_InputField.LineType.MultiLineNewline;
        view.detailField.characterLimit = 500;
        UIFactory.Size(view.detailField.gameObject, 266f);
        if (view.detailField.textComponent != null)
            view.detailField.textComponent.alignment = TextAlignmentOptions.TopLeft;
        if (view.detailField.placeholder is TextMeshProUGUI hint)
            hint.alignment = TextAlignmentOptions.TopLeft;
        ThemedElement detailFill = view.detailField.GetComponent<ThemedElement>();
        if (detailFill != null)
        {
            detailFill.role = ThemeRole.SurfaceAlt;
            detailFill.Apply();
        }

        view.statusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.statusLabel);
        view.statusLabel.gameObject.SetActive(false);

        // Red words, no box: it takes you out of their trails, and the first tap
        // only says so.
        RectTransform block = UIFactory.Rect("Block", content);
        UIFactory.Size(block.gameObject, Btn);
        view.blockButton = UIFactory.Button(block, ThemeRole.Surface, 0f, UIShape.Pill);
        view.blockLabel = UIFactory.Text("Label", block, Body, ThemeRole.StatusPoor,
                                         TextAlignmentOptions.Center);
        UIFactory.Stretch(view.blockLabel.rectTransform);
        view.blockLabel.textWrappingMode = TextWrappingModes.Normal;
        view.blockLabel.enableAutoSizing = true;
        view.blockLabel.fontSizeMax = Body;
        view.blockLabel.fontSizeMin = Secondary;

        RectTransform send = UIFactory.Rect("Send", footer);
        UIFactory.Size(send.gameObject, Btn);
        UIFactory.Flexible(send.gameObject);
        view.sendButton = UIFactory.Button(send, ThemeRole.Accent, 1f, UIShape.Pill);
        view.sendLabel = UIFactory.Text("Label", send, Body, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, "report.send");
        UIFactory.Stretch(view.sendLabel.rectTransform);

        panel.gameObject.SetActive(false);
        return view;
    }

    /// <summary>
    /// One choice in a radio list: a 56px row with a hairline round it, a 22px
    /// radio and its words. The owner paints the chosen state.
    /// </summary>
    static void RadioRow(Transform parent, string name, string key, out Button button,
                         out TextMeshProUGUI label, out Image radio, out RectTransform hole,
                         out Image edge)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, 155f);
        button = UIFactory.Button(rt, ThemeRole.Surface, 1f, UIShape.Rounded);

        RectTransform edgeRect = UIFactory.Rect("Edge", rt);
        UIFactory.Stretch(edgeRect);
        edge = UIFactory.Plain(edgeRect, ThemeManager.Get(ThemeRole.Line));
        edge.sprite = UISprites.RoundedOutline;
        edge.type = Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1f;
        edge.raycastTarget = false;

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 39f);
        layout.padding = new RectOffset(44, 44, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform disc = UIFactory.Rect("Radio", row);
        UIFactory.Size(disc.gameObject, 61f, 61f).flexibleWidth = 0f;
        radio = UIFactory.Plain(disc, ThemeManager.Get(ThemeRole.FillStrong), UIShape.Circle);
        radio.raycastTarget = false;

        hole = UIFactory.Rect("Hole", disc);
        UIFactory.Corner(hole, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f), Vector2.zero);
        UIFactory.Fill(hole, ThemeRole.Surface, 1f, UIShape.Circle).raycastTarget = false;

        label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Left, key);
        UIFactory.Flexible(label.gameObject);
        label.textWrappingMode = TextWrappingModes.Normal;
    }

    // ------------------------------------------------------------------- map tab    // ------------------------------------------------------------------- map tab

    static MapTabController BuildMapTab(RectTransform panel, UserLocationService location,
                                        MapPinView pinPrefab, InfoPanelController info)
    {
        MapTabController controller = panel.gameObject.AddComponent<MapTabController>();
        controller.locationService = location;
        controller.infoPanel = info;
        controller.radiusMetres = 500d;

        // --- the map itself, full bleed: Daylight floats every control over it
        RectTransform surface = UIFactory.Rect("MapSurface", panel);
        UIFactory.Stretch(surface);

        // The pan gesture lands on this, so it needs a graphic. Its colour is the
        // map's own ground, set by the tile layer: where a tile has not arrived,
        // the reader sees map, not the app's background - black in dark mode.
        Image mapGround = UIFactory.Plain(surface, new Color(0.957f, 0.953f, 0.945f, 1f));
        mapGround.raycastTarget = true;
        surface.gameObject.AddComponent<RectMask2D>();

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
        tileLayer.ground = mapGround;

        RectTransform content = UIFactory.Rect("Content", surface);
        UIFactory.Stretch(content);
        map.contentParent = content;

        // The trail's order, under everything else on the map.
        RectTransform route = UIFactory.Rect("Route", content);
        UIFactory.Stretch(route);
        map.routeParent = route;
        map.legSprite = UISprites.Pill;

        // The accuracy circle: the blue at 12%, and no edge - Daylight draws the
        // dot's uncertainty as a wash, not as a second ring around it.
        RectTransform accuracy = UIFactory.Rect("Accuracy", content);
        UIFactory.Corner(accuracy, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), Vector2.zero);
        Color accuracyTint = ThemeManager.Get(ThemeRole.Accent);
        accuracyTint.a = 0.12f;
        map.accuracyCircle = UIFactory.Plain(accuracy, accuracyTint, UIShape.Circle);
        map.accuracyCircle.raycastTarget = false;
        accuracy.gameObject.SetActive(false);

        // The way to the stop being walked to: dashes, so it reads as "go this
        // way" rather than as a street the map does not have.
        RectTransform ray = UIFactory.Rect("GuidanceRay", content);
        UIFactory.Corner(ray, new Vector2(0.5f, 0.5f), new Vector2(0f, 17f), Vector2.zero);
        Image rayImage = UIFactory.Plain(ray, ThemeManager.Get(ThemeRole.Accent));
        rayImage.sprite = UISprites.Dash;
        rayImage.type = Image.Type.Tiled;
        rayImage.pixelsPerUnitMultiplier = 2f;
        rayImage.raycastTarget = false;
        map.guidanceRay = ray;

        // The reader's dot: 20px with a 3px white ring, a pulse that says the fix
        // is live, and a cone the way the phone faces. Drawn last of the fixed
        // marks, so it is never buried under one.
        RectTransform dot = UIFactory.Rect("UserDot", content);
        UIFactory.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(55f, 55f), Vector2.zero);

        RectTransform beam = UIFactory.Rect("Beam", dot);
        UIFactory.Corner(beam, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), Vector2.zero);
        Color beamTint = ThemeManager.Get(ThemeRole.Accent);
        beamTint.a = 0.16f;
        Image beamImage = UIFactory.Plain(beam, beamTint);
        beamImage.sprite = UISprites.Beam;
        beamImage.raycastTarget = false;
        beam.gameObject.SetActive(false);

        RectTransform pulse = UIFactory.Rect("Pulse", dot);
        UIFactory.Corner(pulse, new Vector2(0.5f, 0.5f), new Vector2(55f, 55f), Vector2.zero);
        Image pulseImage = UIFactory.Plain(pulse, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        pulseImage.raycastTarget = false;

        RectTransform ringRect = UIFactory.Rect("Ring", dot);
        UIFactory.Stretch(ringRect);
        UIFactory.Plain(ringRect, Color.white, UIShape.Circle).raycastTarget = false;

        RectTransform dotInner = UIFactory.Rect("Inner", dot);
        UIFactory.Corner(dotInner, new Vector2(0.5f, 0.5f), new Vector2(39f, 39f), Vector2.zero);
        UIFactory.Fill(dotInner, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;
        map.userDot = dot;

        UserDotView dotView = dot.gameObject.AddComponent<UserDotView>();
        dotView.locationService = location;
        dotView.pulse = pulse;
        dotView.pulseImage = pulseImage;
        dotView.beam = beam;

        // Chrome: padded clear of the notch and the gesture bar, over the map.
        RectTransform safe = SafeContent(panel, padBottom: true);

        // --- the top: the trail, and the team on a scored one
        RectTransform top = UIFactory.Rect("TopBar", safe);
        UIFactory.TopStretch(top, 0f, Gutter);
        top.sizeDelta = new Vector2(-Gutter * 2f, 0f);
        VerticalLayoutGroup topStack = UIFactory.VerticalStack(top, 22f);
        topStack.childAlignment = TextAnchor.UpperLeft;
        topStack.childForceExpandWidth = false;

        TrailChipView chip = BuildTrailChip(top, out RectTransform chipRect);
        if (info != null)
            chip.reportSheet = info.reportSheet;

        BuildTeamChip(top);

        // --- the states where there is nothing to draw
        controller.stateCard = BuildStateCard(safe);

        // --- the card resting above the tab bar
        //
        // "Panel" still: the demo coach finds this by name and rests its one line
        // of instruction on top of it.
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, 255f);
        dockArea.sizeDelta = new Vector2(-Gutter * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 28f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        map.coverTop = top;
        map.coverBottom = dockArea;

        NextStopCard card = BuildNextStopCard(dockArea, location, false);
        card.mapTab = controller;
        card.infoPanel = info;
        controller.nextStop = card;

        // The dock is as tall as what is in it, so its top edge is the top of the
        // card - or of the demo's coach when that rests on the card.
        ContentSizeFitter dockFit = dockArea.gameObject.AddComponent<ContentSizeFitter>();
        dockFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The recentre button and the tiles' credit ride on the dock's top edge,
        // so they move with it as it grows and shrinks.
        RectTransform recentre = UIFactory.Rect("Recentre", dockArea);
        recentre.anchorMin = new Vector2(1f, 1f);
        recentre.anchorMax = new Vector2(1f, 1f);
        recentre.pivot = new Vector2(1f, 0f);
        recentre.sizeDelta = new Vector2(133f, 133f);
        recentre.anchoredPosition = new Vector2(0f, 28f);
        Ignore(recentre);
        map.recentreButton = GlassButton(recentre, UIShape.Circle);
        controller.recentreHolder = recentre.gameObject;
        Icon(recentre, AppIcon.Navigation, 50f, ThemeRole.AccentText);

        // Required by the tile licence: Stadia drew the tiles, OpenMapTiles built
        // the schema, and the data underneath is OpenStreetMap's.
        TextMeshProUGUI credit = UIFactory.Text("Attribution", dockArea, Caption,
                                                ThemeRole.TextSecondary, TextAlignmentOptions.BottomLeft);
        RectTransform creditRect = credit.rectTransform;
        creditRect.anchorMin = new Vector2(0f, 1f);
        creditRect.anchorMax = new Vector2(1f, 1f);
        creditRect.pivot = new Vector2(0f, 0f);
        creditRect.sizeDelta = new Vector2(-170f, 44f);
        creditRect.anchoredPosition = new Vector2(17f, 14f);
        Ignore(creditRect);
        credit.textWrappingMode = TextWrappingModes.NoWrap;
        credit.overflowMode = TextOverflowModes.Ellipsis;
        // Small print, like Apple Maps' "Legal": required, readable, never in
        // the way of a pin (it was body-sized - design scan, 6 Nov 2026).
        credit.fontSize = 24f;
        creditRect.sizeDelta = new Vector2(-170f, 32f);
        credit.text = "\u00A9 Stadia Maps \u00B7 OpenMapTiles \u00B7 OpenStreetMap";
        OnMap(credit, MapInk.MutedInk);

        // The chip's pull-down, over everything else on the map.
        chip.menu = BuildTrailMenu(panel, chipRect, chip.chevron != null
                                                       ? (RectTransform)chip.chevron.transform : null);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// The trail chip's menu: Yours (a tick on the open one), Nearby (the three
    /// closest public trails, distance on the right), then "Search all trails"
    /// and "Join with a code" in blue. Hung under the chip by the view.
    /// </summary>
    static TrailMenuView BuildTrailMenu(RectTransform panel, RectTransform chip, RectTransform chevron)
    {
        RectTransform root = UIFactory.Rect("TrailMenu", panel);
        UIFactory.Stretch(root);
        TrailMenuView view = root.gameObject.AddComponent<TrailMenuView>();
        view.anchor = chip;
        view.chevron = chevron;
        view.categoryIcons = CategoryIcons();

        // A tap anywhere off the menu folds it; so does Back.
        RectTransform scrimRect = UIFactory.Rect("Scrim", root);
        UIFactory.Stretch(scrimRect);
        view.scrim = UIFactory.Plain(scrimRect, new Color(0f, 0f, 0f, 0f));
        view.scrim.raycastTarget = true;
        view.scrimButton = scrimRect.gameObject.AddComponent<Button>();
        view.scrimButton.targetGraphic = view.scrim;
        view.scrimButton.transition = Selectable.Transition.None;
        scrimRect.gameObject.AddComponent<WayOut>();

        RectTransform card = UIFactory.Rect("Card", root);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(1f, 1f);
        card.sizeDelta = new Vector2(900f, 400f);
        view.card = card;
        view.cardGroup = card.gameObject.AddComponent<CanvasGroup>();

        Image face = UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Card);
        face.raycastTarget = true;

        RectTransform edge = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(edge);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.CardOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
        Ignore(edge);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 0f);
        stack.padding = new RectOffset(0, 0, 14, 14);

        // --- Yours
        view.mineLabel = MenuSection(card, "MineLabel", "trails.subtitle");

        RectTransform rows = UIFactory.Rect("Trails", card);
        UIFactory.VerticalStack(rows, 0f);
        view.rowsParent = rows;

        // --- one trail:  (◎)  Name                         ✓ / 350 m
        //                      Category · City · 2 of 5
        RectTransform row = UIFactory.Rect("Row", rows);
        UIFactory.Size(row.gameObject, MenuRowHeight);
        UIFactory.Button(row, ThemeRole.Fill, 0f);
        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        rule.offsetMin = new Vector2(MenuText, rule.offsetMin.y);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);

        RectTransform chipRect = UIFactory.Rect("Chip", row);
        UIFactory.Corner(chipRect, new Vector2(0f, 0.5f), new Vector2(99f, 99f),
                         new Vector2(MenuSide, 0f));
        UIFactory.Fill(chipRect, ThemeRole.Fill, 1f, UIShape.Circle).raycastTarget = false;
        Icon(chipRect, AppIcon.Dot, 52f, ThemeRole.TextSecondary).gameObject.name = "Icon";

        RectTransform checkBox = UIFactory.Rect("Check", row);
        UIFactory.Corner(checkBox, new Vector2(1f, 0.5f), new Vector2(80f, MenuRowHeight),
                         new Vector2(-MenuSide + 10f, 0f));
        Image tick = Icon(checkBox, AppIcon.Check, 46f, ThemeRole.AccentText);
        tick.gameObject.name = "Icon";

        TextMeshProUGUI distance = UIFactory.Text("Distance", row, Secondary, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Right);
        UIFactory.Corner(distance.rectTransform, new Vector2(1f, 0.5f), new Vector2(200f, 60f),
                         new Vector2(-MenuSide, 0f));
        distance.textWrappingMode = TextWrappingModes.NoWrap;
        distance.gameObject.SetActive(false);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.Stretch(text);
        text.offsetMin = new Vector2(MenuText, 0f);
        text.offsetMax = new Vector2(-MenuSide - 200f, 0f);
        UIFactory.VerticalStack(text, 2f, false).childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI name = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left);
        UIFactory.Size(name.gameObject, 58f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.richText = false;   // a trail's name is whatever its author typed

        TextMeshProUGUI sub = UIFactory.Text("Sub", text, Caption + 3f, ThemeRole.TextSecondary,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(sub.gameObject, 44f);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;

        view.rowTemplate = row;
        row.gameObject.SetActive(false);

        // --- Nearby
        view.nearDivider = MenuDivider(card, "NearDivider");
        view.nearLabel = MenuSection(card, "NearLabel", "menu.nearby");

        RectTransform near = UIFactory.Rect("Near", card);
        UIFactory.VerticalStack(near, 0f);
        view.nearParent = near;

        // --- the way to the rest, in the accent: they are actions, not trails
        view.actionsDivider = MenuDivider(card, "ActionsDivider");
        view.allButton = MenuRow(card, "All", AppIcon.Search, "menu.searchAll", false,
                                 ThemeRole.AccentText);
        view.joinButton = MenuRow(card, "Join", AppIcon.Plus, "trails.join", false,
                                  ThemeRole.AccentText);

        root.gameObject.SetActive(false);
        EditorUtility.SetDirty(view);
        return view;
    }

    const float MenuSide = 44f;
    const float MenuText = MenuSide + 99f + 33f;
    const float MenuRowHeight = 150f;

    /// <summary>education, tourism, estate, other: TrailFilter.Categories order.</summary>
    static Sprite[] CategoryIcons()
    {
        return new[]
        {
            UIIcons.Get(AppIcon.Book),
            UIIcons.Get(AppIcon.Landmark),
            UIIcons.Get(AppIcon.Home),
            UIIcons.Get(AppIcon.Dot)
        };
    }

    /// <summary>"Твоите", "Наблизо": 13px semibold grey over a group of rows.</summary>
    static GameObject MenuSection(RectTransform card, string name, string key)
    {
        TextMeshProUGUI label = UIFactory.Text(name, card, Caption + 3f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.BottomLeft, key, FontStyles.Bold);
        UIFactory.Size(label.gameObject, 72f);
        label.margin = new Vector4(MenuSide, 0f, MenuSide, 8f);
        return label.gameObject;
    }

    /// <summary>A hairline between two groups, with a little air either side.</summary>
    static GameObject MenuDivider(RectTransform card, string name)
    {
        RectTransform holder = UIFactory.Rect(name, card);
        UIFactory.Size(holder.gameObject, 31f);

        RectTransform hairline = UIFactory.Rect("Line", holder);
        hairline.anchorMin = new Vector2(0f, 0.5f);
        hairline.anchorMax = new Vector2(1f, 0.5f);
        hairline.sizeDelta = new Vector2(0f, 3f);
        UIFactory.Fill(hairline, ThemeRole.Line).raycastTarget = false;
        return holder.gameObject;
    }

    static Button MenuRow(RectTransform card, string name, AppIcon icon, string key, bool rule,
                          ThemeRole role = ThemeRole.TextPrimary)
    {
        RectTransform row = UIFactory.Rect(name, card);
        UIFactory.Size(row.gameObject, 140f);
        Button button = UIFactory.Button(row, ThemeRole.Fill, 0f);

        if (rule)
        {
            RectTransform line = UIFactory.Rect("Rule", row);
            UIFactory.TopStretch(line, 3f);
            line.offsetMin = new Vector2(MenuText, line.offsetMin.y);
            UIFactory.Fill(line, ThemeRole.Line).raycastTarget = false;
            Ignore(line);
        }

        // In the same column as the trails' icon chips.
        RectTransform glyph = UIFactory.Rect("Glyph", row);
        UIFactory.Corner(glyph, new Vector2(0f, 0.5f), new Vector2(99f, 140f),
                         new Vector2(MenuSide, 0f));
        Icon(glyph, icon, 50f, role);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, role,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(MenuText, 0f);
        label.rectTransform.offsetMax = new Vector2(-MenuSide, 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        return button;
    }

    /// <summary>
    /// A frosted control floating over the map or the camera: the glass, its rim,
    /// and a Button on it. Daylight's glass is for controls only - anything that
    /// is read sits on a solid card.
    /// </summary>
    static Button GlassButton(RectTransform rt, UIShape shape)
    {
        Image glass = UIFactory.Fill(rt, ThemeRole.Glass, 1f, shape);
        glass.raycastTarget = true;

        RectTransform rim = UIFactory.Rect("Rim", rt);
        UIFactory.Stretch(rim);
        Image edge = UIFactory.Fill(rim, ThemeRole.Line);
        edge.sprite = shape == UIShape.Circle ? UISprites.RingThin : UISprites.PillOutline;
        edge.type = shape == UIShape.Circle ? Image.Type.Simple : Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1f;
        edge.raycastTarget = false;
        if (shape != UIShape.Circle)
            rim.gameObject.AddComponent<PillShape>();
        rim.SetAsFirstSibling();
        Ignore(rim);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = glass;
        UIFactory.Feedback(button);
        return button;
    }

    /// <summary>
    /// The team chip under the trail's name: a small glass capsule as wide as its
    /// words. The component sits on the row around it, because it hides the chip
    /// on a walk and a component that hides its own object never hears the trail
    /// change back.
    /// </summary>
    static void BuildTeamChip(RectTransform top)
    {
        RectTransform row = UIFactory.Rect("TeamRow", top);
        UIFactory.Size(row.gameObject, 100f);
        HorizontalLayoutGroup rowLayout = UIFactory.HorizontalStack(row, 0f);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform chip = UIFactory.Rect("TeamChip", row);
        UIFactory.Size(chip.gameObject, 100f).flexibleWidth = 0f;
        Button button = GlassButton(chip, UIShape.Pill);

        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(chip, 22f);
        layout.padding = new RectOffset(28, 33, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;

        ContentSizeFitter fit = chip.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        Icon(chip, AppIcon.Users, 46f, ThemeRole.TextPrimary);

        TextMeshProUGUI label = UIFactory.Text("Label", chip, Secondary, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Size(label.gameObject, 100f);
        label.text = AppStrings.Get("chip.noTeam");

        Icon(chip, AppIcon.ChevronRight, 40f, ThemeRole.TextMuted);

        TeamChipView view = top.gameObject.AddComponent<TeamChipView>();
        view.button = button;
        view.label = label;
        view.holder = row.gameObject;
    }

    /// <summary>
    /// The next-stop card: a solid surface with the stop on it and the one thing
    /// to do about it. Built twice - on the map, and dark over the camera, where
    /// it is painted in fixed colours because the feed behind it is the same in
    /// either theme.
    /// </summary>
    static NextStopCard BuildNextStopCard(RectTransform parent, UserLocationService location, bool onCamera)
    {
        RectTransform card = UIFactory.Rect(onCamera ? "CameraCard" : "NextStop", parent);


        RectTransform face = UIFactory.Rect("Face", card);
        UIFactory.Stretch(face);
        // Themed on the camera too: a light card over the feed in light mode,
        // as Apple Maps does in its AR walking view.
        Image faceImage = UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Card);
        faceImage.raycastTarget = true;
        Ignore(face);

        RectTransform edge = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(edge);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.CardOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
        Ignore(edge);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 28f);
        stack.padding = new RectOffset(44, 44, 17, 44);
        stack.childAlignment = TextAnchor.UpperLeft;

        NextStopCard view = card.gameObject.AddComponent<NextStopCard>();
        view.locationService = location;
        view.onCamera = onCamera;
        view.cameraSprite = UIIcons.Get(AppIcon.Camera);
        view.infoSprite = UIIcons.Get(AppIcon.Info);

        // --- the handle: says the card goes up
        RectTransform handleRow = UIFactory.Rect("Handle", card);
        UIFactory.Size(handleRow.gameObject, 14f);
        RectTransform handle = UIFactory.Rect("Bar", handleRow);
        UIFactory.Corner(handle, new Vector2(0.5f, 0.5f), new Vector2(100f, 14f), Vector2.zero);
        Image grip = UIFactory.Fill(handle, ThemeRole.FillStrong, 1f, UIShape.Pill);
        grip.raycastTarget = false;
        PillFit(grip, 14f);

        // A thumb-sized target round the grabber, reaching into the padding
        // above it and the gap below.
        RectTransform handleHit = UIFactory.Rect("Hit", handleRow);
        UIFactory.Stretch(handleHit);
        handleHit.offsetMin = new Vector2(0f, -28f);
        handleHit.offsetMax = new Vector2(0f, 17f);
        Image handleCatch = handleHit.gameObject.AddComponent<Image>();
        handleCatch.color = new Color(0f, 0f, 0f, 0f);
        view.handleButton = handleHit.gameObject.AddComponent<Button>();
        view.handleButton.targetGraphic = handleCatch;
        view.handleButton.transition = Selectable.Transition.None;
        Ignore(handleHit);

        // --- compact: the whole card in one line, under the grabber
        //
        //   (1)  The Bezisten                        ➤ 299 m
        //
        // Swiped down to, and back up from. Its own row rather than the full
        // card's rows squeezed, so neither has to compromise for the other.
        RectTransform compact = UIFactory.Rect("Compact", card);
        UIFactory.Size(compact.gameObject, 72f);
        HorizontalLayoutGroup compactRow = UIFactory.HorizontalStack(compact, 28f);
        compactRow.childAlignment = TextAnchor.MiddleLeft;
        Image compactCatch = compact.gameObject.AddComponent<Image>();
        compactCatch.color = new Color(0f, 0f, 0f, 0f);
        view.compactButton = compact.gameObject.AddComponent<Button>();
        view.compactButton.targetGraphic = compactCatch;
        view.compactButton.transition = Selectable.Transition.None;
        view.compactRow = compact.gameObject;

        RectTransform smallBadge = UIFactory.Rect("Badge", compact);
        UIFactory.Size(smallBadge.gameObject, 72f, 72f).flexibleWidth = 0f;
        view.compactBadge = UIFactory.Plain(smallBadge, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        view.compactBadge.raycastTarget = false;
        BadgeEdge(smallBadge);

        view.compactNumber = UIFactory.Text("Number", smallBadge, Caption, ThemeRole.OnAccent,
                                            TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.compactNumber.rectTransform);
        Unthemed(view.compactNumber.gameObject);

        view.compactTick = Icon(smallBadge, AppIcon.Check, 36f, ThemeRole.OnAccent);
        Unthemed(view.compactTick.gameObject);
        view.compactTick.gameObject.SetActive(false);

        view.compactTitle = CardText("Title", compact, Body, null,
                                     ThemeRole.TextPrimary, TextAlignmentOptions.Left, true);
        UIFactory.Flexible(view.compactTitle.gameObject);
        view.compactTitle.textWrappingMode = TextWrappingModes.NoWrap;
        view.compactTitle.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform compactMeta = UIFactory.Rect("Meta", compact);
        HorizontalLayoutGroup metaRow = UIFactory.HorizontalStack(compactMeta, 14f);
        metaRow.childAlignment = TextAnchor.MiddleRight;
        metaRow.childForceExpandWidth = false;
        LayoutElement metaSize = compactMeta.gameObject.AddComponent<LayoutElement>();
        metaSize.flexibleWidth = 0f;

        RectTransform compactDot = UIFactory.Rect("Dot", compactMeta);
        UIFactory.Size(compactDot.gameObject, 22f, 22f).flexibleWidth = 0f;
        view.compactDot = UIFactory.Plain(compactDot, ThemeManager.Get(ThemeRole.GoodDot), UIShape.Circle);
        view.compactDot.raycastTarget = false;

        view.compactArrow = Arrow(compactMeta, location, onCamera ? BearingArrow.Frame.DeviceRelative
                                                              : BearingArrow.Frame.NorthUp, 36f);

        view.compactMeta = CardText("Label", compactMeta, Secondary, null,
                                    ThemeRole.TextMuted, TextAlignmentOptions.Right, false);
        view.compactMeta.textWrappingMode = TextWrappingModes.NoWrap;
        Unthemed(view.compactMeta.gameObject);

        compact.gameObject.SetActive(false);

        // --- head: where this stop sits in the trail, and the way to all of them
        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.Size(head.gameObject, 100f);
        HorizontalLayoutGroup headRow = UIFactory.HorizontalStack(head, 16f);
        headRow.childAlignment = TextAnchor.MiddleLeft;

        view.headLabel = CardText("Label", head, Secondary, null,
                                  ThemeRole.TextMuted, TextAlignmentOptions.Left, false);
        UIFactory.Flexible(view.headLabel.gameObject);
        view.headLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.headLabel.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform list = UIFactory.Rect("All", head);
        UIFactory.Size(list.gameObject, Tap, 360f).flexibleWidth = 0f;
        view.listButton = UIFactory.Button(list, ThemeRole.Surface, 0f);
        view.listLabel = CardText("Label", list, Secondary, null,
                                  ThemeRole.AccentText, TextAlignmentOptions.Right, false);
        UIFactory.Stretch(view.listLabel.rectTransform);
        view.listLabel.text = AppStrings.Get("card.all");

        // --- the stop: its number in its colour, its name, and how far
        RectTransform main = UIFactory.Rect("Stop", card);
        HorizontalLayoutGroup mainRow = UIFactory.HorizontalStack(main, 33f);
        mainRow.childAlignment = TextAnchor.UpperLeft;

        RectTransform badge = UIFactory.Rect("Badge", main);
        UIFactory.Size(badge.gameObject, 89f, 89f).flexibleWidth = 0f;
        view.badge = UIFactory.Plain(badge, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        view.badge.raycastTarget = false;
        BadgeEdge(badge);

        view.numberLabel = UIFactory.Text("Number", badge, Secondary, ThemeRole.OnAccent,
                                          TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.numberLabel.rectTransform);
        Unthemed(view.numberLabel.gameObject);

        view.badgeTick = Icon(badge, AppIcon.Check, 46f, ThemeRole.OnAccent);
        Unthemed(view.badgeTick.gameObject);
        view.badgeTick.gameObject.SetActive(false);

        RectTransform text = UIFactory.Rect("Text", main);
        UIFactory.VerticalStack(text, 11f, false);
        UIFactory.Flexible(text.gameObject);

        view.titleLabel = CardText("Title", text, Title, null,
                                   ThemeRole.TextPrimary, TextAlignmentOptions.TopLeft, true);
        view.titleLabel.textWrappingMode = TextWrappingModes.Normal;
        view.titleLabel.overflowMode = TextOverflowModes.Ellipsis;
        view.titleLabel.maxVisibleLines = 2;

        RectTransform status = UIFactory.Rect("Status", text);
        UIFactory.Size(status.gameObject, 54f);
        HorizontalLayoutGroup statusRow = UIFactory.HorizontalStack(status, 17f);
        statusRow.childAlignment = TextAnchor.MiddleLeft;

        RectTransform statusDot = UIFactory.Rect("Dot", status);
        UIFactory.Size(statusDot.gameObject, 22f, 22f).flexibleWidth = 0f;
        view.statusDot = UIFactory.Plain(statusDot, ThemeManager.Get(ThemeRole.GoodDot), UIShape.Circle);
        view.statusDot.raycastTarget = false;

        view.statusArrow = Arrow(status, location, onCamera ? BearingArrow.Frame.DeviceRelative
                                                        : BearingArrow.Frame.NorthUp, 39f);

        view.statusLabel = CardText("Label", status, Secondary, null,
                                    ThemeRole.TextMuted, TextAlignmentOptions.Left, false);
        UIFactory.Flexible(view.statusLabel.gameObject);
        view.statusLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.statusLabel.overflowMode = TextOverflowModes.Ellipsis;
        // The card colours this by state: green for "you're here".
        Unthemed(view.statusLabel.gameObject);

        // --- the actions
        RectTransform actions = UIFactory.Rect("Actions", card);
        UIFactory.Size(actions.gameObject, Btn);
        HorizontalLayoutGroup actionRow = UIFactory.HorizontalStack(actions, 28f);
        actionRow.childAlignment = TextAnchor.MiddleLeft;

        RectTransform primary = UIFactory.Rect("Primary", actions);
        UIFactory.Size(primary.gameObject, Btn);
        UIFactory.Flexible(primary.gameObject);
        view.primaryButton = UIFactory.Button(primary, ThemeRole.Accent, 1f, UIShape.Pill);

        RectTransform primaryRow = UIFactory.Rect("Row", primary);
        UIFactory.Stretch(primaryRow);
        HorizontalLayoutGroup primaryLayout = UIFactory.HorizontalStack(primaryRow, 22f);
        primaryLayout.childAlignment = TextAnchor.MiddleCenter;

        Image nav = Icon(primaryRow, AppIcon.Navigation, 46f, ThemeRole.OnAccent);
        view.primaryIcon = nav.gameObject;

        view.primaryLabel = UIFactory.Text("Label", primaryRow, Body, ThemeRole.OnAccent,
                                           TextAlignmentOptions.Center);
        view.primaryLabel.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform secondary = UIFactory.Rect("Secondary", actions);
        UIFactory.Size(secondary.gameObject, Btn);
        UIFactory.Flexible(secondary.gameObject);
        view.secondaryButton = UIFactory.Button(secondary, ThemeRole.Fill, 1f, UIShape.Pill);

        view.secondaryLabel = CardText("Label", secondary, Body, null,
                                       ThemeRole.TextPrimary, TextAlignmentOptions.Center, false);
        UIFactory.Stretch(view.secondaryLabel.rectTransform);

        RectTransform iconHolder = UIFactory.Rect("Icon", actions);
        UIFactory.Size(iconHolder.gameObject, Btn, Btn).flexibleWidth = 0f;
        view.iconButton = UIFactory.Button(iconHolder, ThemeRole.Fill, 1f, UIShape.Circle);

        view.iconImage = Icon(iconHolder, AppIcon.Info, 61f, ThemeRole.TextPrimary);

        // What the compact row stands in for.
        view.fullRows = new[] { head.gameObject, main.gameObject, actions.gameObject };

        EditorUtility.SetDirty(view);
        return view;
    }

    /// <summary>
    /// Keeps a thin pill a pill. The pill sprite's caps are 80px; squeezed into a
    /// 14px bar they smear into an ellipse, so the image is told to draw the
    /// sprite at the size where the caps are exactly half its height.
    /// </summary>
    static void PillFit(Image image, float height)
    {
        image.pixelsPerUnitMultiplier = 160f / Mathf.Max(1f, height);
    }

    /// <summary>
    /// The hairline round a stop's number disc, drawn over its colour. Barely
    /// there on a coloured disc; the whole edge of a white one.
    /// </summary>
    static void BadgeEdge(RectTransform badge)
    {
        RectTransform edge = UIFactory.Rect("Edge", badge);
        UIFactory.Stretch(edge);
        Image ring = UIFactory.Fill(edge, ThemeRole.Line);
        ring.sprite = UISprites.RingThin;
        ring.type = Image.Type.Simple;
        ring.raycastTarget = false;

        LayoutElement skip = edge.gameObject.AddComponent<LayoutElement>();
        skip.ignoreLayout = true;
    }

    /// <summary>A label on the card: themed on the map, a fixed colour over the camera.</summary>
    static TextMeshProUGUI CardText(string name, Transform parent, float size, Color? fixedColour,
                                    ThemeRole role, TextAlignmentOptions align, bool bold)
    {
        TextMeshProUGUI text = UIFactory.Text(name, parent, size, role, align, null,
                                              bold ? FontStyles.Bold : FontStyles.Normal);
        if (fixedColour.HasValue)
        {
            Unthemed(text.gameObject);
            text.color = fixedColour.Value;
        }

        return text;
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

        // And never an item in its own card's stack. A card with a vertical
        // layout on it was laying this out as a row, which drew an empty
        // rounded box above the content of every card built this way.
        Ignore(rt);
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

        Image glyph = Icon(holder, AppIcon.Navigation, size, ThemeRole.AccentText,
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

        banner.distanceText = UIFactory.Text("Distance", text, Secondary, ThemeRole.AccentText,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(banner.distanceText.gameObject, 46f);

        RectTransform stop = UIFactory.Rect("Stop", row);
        LayoutElement stopSize = UIFactory.Size(stop.gameObject, Tap, 190f);
        stopSize.flexibleWidth = 0f;
        banner.stopButton = UIFactory.Button(stop, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
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

        RectTransform safe = SafeContent(live, padBottom: true);

        controller.nearbyPanel = BuildNearbyPanel(live, location, rowPrefab);

        // While it calibrates: the phone, swinging, and what to do with it.
        GameObject coach = BuildCoach(safe);

        controller.vpsIndicator = BuildVpsIndicator(safe, location);
        controller.vpsIndicator.coach = coach;
        controller.vpsIndicator.help = BuildArHelp(info.transform, location);

        BuildWarningBanner(safe, controller);
        BuildEdgeIndicator(safe, location, info);

        // --- bottom: the radar over the card, both above the tab bar
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, 255f);
        dockArea.sizeDelta = new Vector2(-Gutter * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 28f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        controller.radar = BuildRadar(dockArea, location, controller);

        NextStopCard card = BuildNextStopCard(dockArea, location, true);
        card.infoPanel = info;

                // Google's notice before the first Geospatial session. Over everything
        // else on the tab, because nothing behind it is running yet.
        BuildArNotice(safe, controller);
        controller.hideWithArNotice = new[] { dockArea.gameObject };

        // In the dock's place while a stop is being moved: the same spot at the
        // bottom of the camera, where a thumb already is.
        info.capture = BuildCaptureBar(dockArea);
        info.capture.coach = (RectTransform)coach.transform;
        info.capture.hideWhileOpen = new[] { card.gameObject, controller.radar != null
                                                 ? controller.radar.transform.parent.gameObject : null };

        controller.capture = info.capture;
        EditorUtility.SetDirty(info);

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

    static void BuildArNotice(RectTransform parent, CameraTabController controller)
    {
        // At the foot of the tab, where the next-stop card sits (hidden while
        // this is up), like a sheet - not floating over the middle.
        RectTransform card = UIFactory.Rect("ArNotice", parent);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(1f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(-Margin * 2f, 0f);
        UIFactory.Card(card, 30f, 16f);
        Outline(card);
        ContentSizeFitter fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIFactory.Text("Title", card, Title, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "arnotice.title", FontStyles.Bold);

        TextMeshProUGUI body = UIFactory.Text("Body", card, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "arnotice.body");
        body.textWrappingMode = TextWrappingModes.Normal;

        controller.arNoticeContinue = FullWidthButton(card, "Continue", "arnotice.continue",
                                                      ThemeRole.Accent, ThemeRole.OnAccent);
        controller.arNoticeLearnMore = FullWidthButton(card, "LearnMore", "arnotice.learnMore",
                                                       ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        controller.arNotice = card.gameObject;
        card.gameObject.SetActive(false);
    }

    /// <summary>
    /// Capture mode's bar: which stop, how far it has moved, a pad of four
    /// one-metre steps, "where I'm standing", and Save / Cancel.
    /// </summary>
    static CaptureMode BuildCaptureBar(Transform parent)
    {
        RectTransform card = UIFactory.Rect("CaptureBar", parent);
        UIFactory.Card(card, 28f, 14f);
        Outline(card);

        CaptureMode view = card.gameObject.AddComponent<CaptureMode>();
        view.spawner = Object.FindFirstObjectByType<MarkerSpawner>(FindObjectsInactive.Include);

        if (view.spawner == null)
            _notes.Add("No MarkerSpawner found, so capture mode cannot preview a move.");

        view.titleLabel = UIFactory.Text("Title", card, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        view.titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.titleLabel.overflowMode = TextOverflowModes.Ellipsis;

        // A whole line high. At 52 the bold line did not fit, and Ellipsis hides
        // a line that does not fit at all: the card had no title.
        UIFactory.Size(view.titleLabel.gameObject, 64f);

        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextSecondary,
                                          TextAlignmentOptions.TopLeft, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 96f);

        TextMeshProUGUI hint = UIFactory.Text("Hint", card, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Left, "capture.hint");
        hint.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(hint);

        // Pin, sphere, cone, cube, sign - one press each, saved and swapped in
        // the camera at once. CaptureMode writes the label ("Shape: Cone").
        view.shapeButton = FullWidthButton(card, "Shape", "shape.pin",
                                           ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);
        view.shapeLabel = view.shapeButton.GetComponentInChildren<TextMeshProUGUI>();
        DropLocalizer(view.shapeLabel);

        RectTransform pad = UIFactory.Rect("Pad", card);
        UIFactory.Size(pad.gameObject, Tap);
        UIFactory.HorizontalStack(pad, 12f, true);

        view.leftButton = PadButton(pad, "Left", "←");
        view.backButton = PadButton(pad, "Back", "↓");
        view.forwardButton = PadButton(pad, "Forward", "↑");
        view.rightButton = PadButton(pad, "Right", "→");

        view.hereButton = FullWidthButton(card, "Here", "capture.here",
                                          ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        RectTransform finish = UIFactory.Rect("Finish", card);
        UIFactory.Size(finish.gameObject, Tap);
        UIFactory.HorizontalStack(finish, 12f, true);

        view.cancelButton = FullWidthButton(finish, "Cancel", "capture.cancel",
                                            ThemeRole.SurfaceAlt, ThemeRole.TextSecondary);
        UIFactory.Flexible(view.cancelButton.gameObject);
        view.cancelButton.gameObject.AddComponent<WayOut>();

        view.saveButton = FullWidthButton(finish, "Save", "capture.save",
                                          ThemeRole.Accent, ThemeRole.OnAccent);
        UIFactory.Flexible(view.saveButton.gameObject);

        card.gameObject.SetActive(false);
        return view;
    }

    static Button PadButton(Transform parent, string name, string glyph)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);
        UIFactory.Flexible(rt.gameObject);

        Button button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, null, FontStyles.Bold);
        label.text = glyph;
        UIFactory.Stretch(label.rectTransform);

        return button;
    }

    static void BuildWarningBanner(RectTransform parent, CameraTabController controller)
    {
        RectTransform banner = UIFactory.Rect("WarningBanner", parent);
        banner.anchorMin = new Vector2(0f, 1f);
        banner.anchorMax = new Vector2(1f, 1f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(-Gutter * 2f, 144f);
        banner.anchoredPosition = new Vector2(0f, -(Gutter + 111f + 22f));
        UIFactory.Plain(banner, new Color(28f / 255f, 28f / 255f, 30f / 255f, 0.86f), UIShape.Rounded);

        // As tall as its words: a fixed 144 cut a four-line warning off at the
        // top (design scan, 6 Nov 2026). The icon and close button sit outside
        // the layout, pinned to the sides.
        VerticalLayoutGroup bannerStack = banner.gameObject.AddComponent<VerticalLayoutGroup>();
        bannerStack.padding = new RectOffset(117, 132, 30, 30);
        bannerStack.childControlWidth = true; bannerStack.childControlHeight = true;
        bannerStack.childForceExpandWidth = true; bannerStack.childForceExpandHeight = false;
        ContentSizeFitter bannerFit = banner.gameObject.AddComponent<ContentSizeFitter>();
        bannerFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image warning = Icon(banner, AppIcon.Warning, 50f, ThemeRole.WarnDot, out RectTransform warningRect);
        warningRect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIFactory.Corner(warningRect, new Vector2(0f, 0.5f), new Vector2(50f, 50f), new Vector2(39f, 0f));
        Unthemed(warning.gameObject);
        warning.color = new Color(1f, 0.624f, 0.039f);

        controller.warningLabel = UIFactory.Text("Label", banner, Secondary, ThemeRole.OnAccent,
                                                 TextAlignmentOptions.Left);
        controller.warningLabel.textWrappingMode = TextWrappingModes.Normal;
        Unthemed(controller.warningLabel.gameObject);
        controller.warningLabel.color = Color.white;
        controller.warningLabel.text = AppStrings.Get("camera.refused");

        RectTransform dismiss = UIFactory.Rect("Dismiss", banner);
        dismiss.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIFactory.Corner(dismiss, new Vector2(1f, 0.5f), new Vector2(122f, 122f), new Vector2(-11f, 0f));
        controller.warningDismissButton = UIFactory.Button(dismiss, ThemeRole.Surface, 0f, UIShape.Circle);
        White(Icon(dismiss, AppIcon.Close, 44f, ThemeRole.OnAccent));

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
        // 64px of dark glass, as the prototype has it: the stops around the
        // reader as dots, north up, and a wedge turning the way the phone faces.
        const float Size = 177f;
        const float Radius = Size * 0.5f;

        RectTransform row = UIFactory.Rect("RadarRow", parent);
        UIFactory.Size(row.gameObject, Size);

        RectTransform rt = UIFactory.Rect("Radar", row);
        UIFactory.Corner(rt, new Vector2(1f, 0.5f), new Vector2(Size, Size), Vector2.zero);

        // The card's own surface, a little see-through, so the radar and the
        // card under it are one family in light mode as well as dark.
        Image disc = UIFactory.Fill(rt, ThemeRole.Surface, 0.88f, UIShape.Circle);
        disc.sprite = UISprites.MaskCircle;
        Button open = rt.gameObject.AddComponent<Button>();
        open.targetGraphic = disc;
        UIFactory.Feedback(open);
        controller.nearbyButton = open;

        CameraRadar radar = rt.gameObject.AddComponent<CameraRadar>();
        radar.locationService = location;
        radar.radius = Radius - 14f;
        radar.dotSize = 22f;
        radar.maxRangeMetres = 500d;
        radar.circleSprite = UISprites.Circle;

        // The way the phone faces: a wedge, turned by the radar, clipped to the
        // disc so its corners never stick out past the glass.
        RectTransform clip = UIFactory.Rect("Clip", rt);
        UIFactory.Stretch(clip);
        Image clipShape = UIFactory.Plain(clip, Color.white, UIShape.Circle);
        clipShape.sprite = UISprites.MaskCircle;
        clipShape.raycastTarget = false;
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        RectTransform facing = UIFactory.Rect("Facing", clip);
        facing.anchorMin = facing.anchorMax = new Vector2(0.5f, 0.5f);
        facing.pivot = new Vector2(0.5f, 0.5f);
        facing.sizeDelta = new Vector2(Size - 11f, Size - 11f);
        facing.anchoredPosition = Vector2.zero;
        Image wedge = UIFactory.Plain(facing, new Color(41f / 255f, 151f / 255f, 1f, 0.35f));
        wedge.sprite = UISprites.Beam;
        wedge.raycastTarget = false;
        radar.facing = facing;

        TextMeshProUGUI north = UIFactory.Text("North", rt, 25f, ThemeRole.TextSecondary,
                                               TextAlignmentOptions.Center, "radar.north", FontStyles.Bold);
        UIFactory.Corner(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(40f, 30f), new Vector2(0f, -8f));

        RectTransform dots = UIFactory.Rect("Dots", rt);
        UIFactory.Stretch(dots);
        radar.dotParent = dots;

        RectTransform meRing = UIFactory.Rect("MeRing", rt);
        UIFactory.Corner(meRing, new Vector2(0.5f, 0.5f), new Vector2(28f, 28f), Vector2.zero);
        UIFactory.Plain(meRing, Color.white, UIShape.Circle).raycastTarget = false;

        RectTransform me = UIFactory.Rect("Me", rt);
        UIFactory.Corner(me, new Vector2(0.5f, 0.5f), new Vector2(19f, 19f), Vector2.zero);
        UIFactory.Plain(me, new Color(41f / 255f, 151f / 255f, 1f), UIShape.Circle).raycastTarget = false;

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
        strip.previousButton = UIFactory.Button(prev, ThemeRole.Surface, 0f);
        prev.GetComponent<Image>().raycastTarget = true;
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

        strip.nextDistance = UIFactory.Text("Distance", next, Secondary, ThemeRole.AccentText,
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

    /// <summary>The glass the camera's controls float on: dark in either theme.</summary>
    static readonly Color DarkGlass = new Color(28f / 255f, 28f / 255f, 30f / 255f, 0.62f);

    /// <summary>
    /// The calibration chip: a 40px dark pill in the top-left corner with a dot
    /// and a sentence, which shrinks to the dot once the stops are in place.
    /// </summary>
    static VpsIndicatorController BuildVpsIndicator(RectTransform parent,
                                                    UserLocationService location)
    {
        RectTransform rt = UIFactory.Rect("VpsIndicator", parent);
        UIFactory.Stretch(rt);

        RectTransform chip = UIFactory.Rect("DotButton", rt);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(0f, 1f);
        chip.sizeDelta = new Vector2(700f, 111f);
        chip.anchoredPosition = new Vector2(Gutter, -Gutter);

        Image glass = UIFactory.Plain(chip, DarkGlass, UIShape.Pill);
        Button button = chip.gameObject.AddComponent<Button>();
        button.targetGraphic = glass;
        UIFactory.Feedback(button);
        chip.gameObject.AddComponent<RectMask2D>();

        RectTransform dot = UIFactory.Rect("Dot", chip);
        UIFactory.Corner(dot, new Vector2(0f, 0.5f), new Vector2(28f, 28f), new Vector2(41f, 0f));
        Image dotImage = UIFactory.Plain(dot, new Color(0.56f, 0.56f, 0.58f), UIShape.Circle);
        dotImage.raycastTarget = false;

        // Turning while it looks: a light ring with one bright quarter.
        RectTransform spinner = UIFactory.Rect("Spinner", chip);
        UIFactory.Corner(spinner, new Vector2(0f, 0.5f), new Vector2(30f, 30f), new Vector2(55f, 0f));
        spinner.pivot = new Vector2(0.5f, 0.5f);
        spinner.anchoredPosition = new Vector2(55f, 0f);
        Image track = UIFactory.Plain(spinner, new Color(1f, 1f, 1f, 0.35f));
        track.sprite = UISprites.Ring;
        track.raycastTarget = false;

        RectTransform quarter = UIFactory.Rect("Quarter", spinner);
        UIFactory.Stretch(quarter);
        Image bright = UIFactory.Plain(quarter, Color.white);
        bright.sprite = UISprites.Ring;
        bright.type = Image.Type.Filled;
        bright.fillMethod = Image.FillMethod.Radial360;
        bright.fillAmount = 0.25f;
        bright.raycastTarget = false;

        RectTransform words = UIFactory.Rect("Words", chip);
        UIFactory.Stretch(words);
        words.offsetMin = new Vector2(92f, 0f);
        CanvasGroup fade = words.gameObject.AddComponent<CanvasGroup>();
        fade.blocksRaycasts = false;

        TextMeshProUGUI label = UIFactory.Text("Label", words, Secondary, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        Unthemed(label.gameObject);
        label.color = Color.white;
        label.text = AppStrings.Get("vps.search");

        VpsIndicatorController controller = rt.gameObject.AddComponent<VpsIndicatorController>();
        controller.locationService = location;
        controller.dotButton = button;
        controller.dotImage = dotImage;
        controller.dotLabel = label;
        controller.chip = chip;
        controller.labelGroup = fade;
        controller.spinner = spinner;
        return controller;
    }

    /// <summary>
    /// The coach: a phone outline swinging left and right over "Slowly point the
    /// camera at the buildings around you", a third of the way down the feed.
    /// </summary>
    static GameObject BuildCoach(RectTransform parent)
    {
        RectTransform coach = UIFactory.Rect("Coach", parent);
        coach.anchorMin = new Vector2(0f, 0.66f);
        coach.anchorMax = new Vector2(1f, 0.66f);
        coach.pivot = new Vector2(0.5f, 1f);
        coach.sizeDelta = new Vector2(-133f, 420f);
        coach.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(coach, 44f, false);
        stack.childAlignment = TextAnchor.UpperCenter;
        stack.childForceExpandWidth = true;

        RectTransform slot = UIFactory.Rect("PhoneSlot", coach);
        UIFactory.Size(slot.gameObject, 205f);

        RectTransform phone = UIFactory.Rect("Phone", slot);
        UIFactory.Corner(phone, new Vector2(0.5f, 0.5f), new Vector2(122f, 205f), Vector2.zero);
        Image outline = UIFactory.Plain(phone, Color.white);
        outline.sprite = UISprites.RoundedOutline;
        outline.type = Image.Type.Sliced;
        outline.pixelsPerUnitMultiplier = 1.6f;
        outline.raycastTarget = false;
        phone.gameObject.AddComponent<CoachSweep>();

        TextMeshProUGUI line = UIFactory.Text("Line", coach, Body, ThemeRole.OnAccent,
                                              TextAlignmentOptions.Top, "camera.coach", FontStyles.Bold);
        UIFactory.FitsItsText(line);
        Unthemed(line.gameObject);
        line.color = Color.white;
        line.raycastTarget = false;
        Shadowed(line);

        coach.gameObject.SetActive(false);
        return coach.gameObject;
    }

    /// <summary>
    /// White words straight on the camera feed need the soft shadow the
    /// prototype gives them; a TMP underlay on a material of their own.
    /// </summary>
    static void Shadowed(TextMeshProUGUI text)
    {
        Material source = text.fontSharedMaterial;
        if (source == null)
            return;

        string path = "Assets/UI/" + source.name.Replace(" ", "") + "-Shadow.mat";
        Material shadow = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (shadow == null)
        {
            shadow = new Material(source);
            AssetDatabase.CreateAsset(shadow, path);
        }

        shadow.CopyPropertiesFromMaterial(source);
        shadow.shaderKeywords = source.shaderKeywords;
        shadow.EnableKeyword("UNDERLAY_ON");
        shadow.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.6f));
        shadow.SetFloat("_UnderlayOffsetX", 0f);
        shadow.SetFloat("_UnderlayOffsetY", -0.25f);
        shadow.SetFloat("_UnderlayDilate", 0.1f);
        shadow.SetFloat("_UnderlaySoftness", 0.7f);
        EditorUtility.SetDirty(shadow);

        text.fontSharedMaterial = shadow;
    }

    /// <summary>
    /// The pills at the left and right edges, a third of the way down, that say
    /// which way to turn for the next stop.
    /// </summary>
    static void BuildEdgeIndicator(RectTransform parent, UserLocationService location,
                                   InfoPanelController info)
    {
        RectTransform rt = UIFactory.Rect("Edge", parent);
        UIFactory.Stretch(rt);

        EdgeIndicator edge = rt.gameObject.AddComponent<EdgeIndicator>();
        edge.locationService = location;
        edge.infoPanel = info;

        edge.left = EdgePill(rt, "Left", true, out edge.leftLabel);
        edge.right = EdgePill(rt, "Right", false, out edge.rightLabel);
    }

    static Button EdgePill(RectTransform parent, string name, bool left, out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        Vector2 corner = new Vector2(left ? 0f : 1f, 0.7f);
        rt.anchorMin = rt.anchorMax = corner;
        rt.pivot = new Vector2(left ? 0f : 1f, 1f);
        rt.sizeDelta = new Vector2(400f, 111f);
        rt.anchoredPosition = new Vector2(left ? Gutter : -Gutter, 0f);

        Image glass = UIFactory.Plain(rt, new Color(28f / 255f, 28f / 255f, 30f / 255f, 0.7f), UIShape.Pill);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = glass;
        UIFactory.Feedback(button);

        HorizontalLayoutGroup row = UIFactory.HorizontalStack(rt, 17f);
        row.padding = new RectOffset(left ? 28 : 39, left ? 39 : 28, 0, 0);
        row.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (left)
            White(Icon(rt, AppIcon.ChevronLeft, 50f, ThemeRole.OnAccent));

        label = UIFactory.Text("Label", rt, Secondary, ThemeRole.OnAccent, TextAlignmentOptions.Center,
                               null, FontStyles.Bold);
        Unthemed(label.gameObject);
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement size = UIFactory.Size(label.gameObject, 111f);
        size.preferredWidth = -1f;

        if (!left)
            White(Icon(rt, AppIcon.ChevronRight, 50f, ThemeRole.OnAccent));

        rt.gameObject.SetActive(false);
        return button;
    }

    static void White(Image image)
    {
        Unthemed(image.gameObject);
        image.color = Color.white;
    }

    /// <summary>
    /// "Why are there no stops?" - a page over everything, opened from the chip
    /// when the camera cannot get its bearings. Three things to try, and the map
    /// as the way on regardless.
    /// </summary>
    static ArHelpView BuildArHelp(Transform parent, UserLocationService location)
    {
        RectTransform panel = PushedPage(parent, "ArHelp", "arhelp.title", out RectTransform content,
                                         out RectTransform _, out Button back, out Button close, false, true);

        ArHelpView view = panel.gameObject.AddComponent<ArHelpView>();
        view.locationService = location;
        view.backButton = back;
        view.closeButton = close;

        TextMeshProUGUI body = UIFactory.Text("Body", content, Body, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "arhelp.body");
        UIFactory.FitsItsText(body);

        RectTransform steps = UIFactory.Rect("Steps", content);
        UIFactory.VerticalStack(steps, 50f, false);

        Step(steps, AppIcon.Walk, "arhelp.s1", "arhelp.s1b");
        Step(steps, AppIcon.Camera, "arhelp.s2", "arhelp.s2b");
        Step(steps, AppIcon.Info, "arhelp.s3", "arhelp.s3b");

        view.noteLabel = Note(content, AppIcon.MapFold);

        panel.gameObject.SetActive(false);
        return view;
    }

    /// <summary>One step of a how-to: an icon in a tinted box, a heading, a line.</summary>
    static void Step(Transform parent, AppIcon icon, string titleKey, string bodyKey)
    {
        RectTransform row = UIFactory.Rect("Step", parent);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 39f);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;

        RectTransform box = UIFactory.Rect("Box", row);
        UIFactory.Size(box.gameObject, 122f, 122f).flexibleWidth = 0f;
        UIFactory.Fill(box, ThemeRole.AccentTint, 1f, UIShape.Small).raycastTarget = false;
        RectTransform glyph = UIFactory.Rect("Icon", box);
        UIFactory.Corner(glyph, new Vector2(0.5f, 0.5f), new Vector2(61f, 61f), Vector2.zero);
        Image image = UIFactory.Fill(glyph, ThemeRole.AccentText);
        image.sprite = UIIcons.Get(icon);
        image.preserveAspect = true;
        image.raycastTarget = false;

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 6f, false);
        UIFactory.Flexible(text.gameObject);

        TextMeshProUGUI title = UIFactory.Text("Title", text, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, titleKey, FontStyles.Bold);
        UIFactory.FitsItsText(title);

        TextMeshProUGUI line = UIFactory.Text("Body", text, Secondary, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft, bodyKey);
        UIFactory.FitsItsText(line);
    }

    /// <summary>A quiet note on a tinted box: an icon and a sentence.</summary>
    static TextMeshProUGUI Note(Transform parent, AppIcon icon, string key = null)
    {
        RectTransform note = UIFactory.Rect("Note", parent);
        UIFactory.Fill(note, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        HorizontalLayoutGroup row = UIFactory.HorizontalStack(note, 28f);
        row.padding = new RectOffset(39, 39, 33, 33);
        row.childAlignment = TextAnchor.UpperLeft;
        row.childControlHeight = true;
        ContentSizeFitter fit = note.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Icon(note, icon, 50f, ThemeRole.TextMuted);

        TextMeshProUGUI label = UIFactory.Text("Label", note, Secondary, ThemeRole.TextSecondary,
                                               TextAlignmentOptions.TopLeft, key);
        UIFactory.FitsItsText(label);
        UIFactory.Flexible(label.gameObject);
        return label;
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

        // Parked well below the screen: the glass tab bar is see-through, so a
        // sheet parked just under it showed its top edge through the bar.
        controller.sheet.closedPadding = Nav + 400f;
        controller.scrimButton = CreateScrim("NearbyScrim", live, out GameObject scrim, 0.55f);
        controller.scrim = scrim;

        SheetDragHandle drag = AddDragArea(sheet, controller.sheet);
        Wire(drag.onDismissed, controller.Close, "Nearby sheet drag");

        controller.listParent = content;
        controller.rowPrefab = rowPrefab;
        controller.titleText = title;
        controller.emptyText = empty;
        controller.closeButton = close;
        close.gameObject.AddComponent<WayOut>();
        controller.radiusMetres = 500d;

        // Parked offscreen rather than disabled, so the controller keeps running and
        // the Game view is not blocked at edit time.
        sheet.anchoredPosition = new Vector2(0f, -SheetHeight - 400f);
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
    /// <summary>
    /// The toast: a dark pill 12px in from each side and 10 under the status
    /// bar - near-black in the light theme, near-white in the dark - with one
    /// line and at most one action.
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
        toast.restY = -28f;
        toast.slideDistance = -300f;

        RectTransform safe = UIFactory.Rect("Safe", holder);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.top = true;
        pad.left = true;
        pad.right = true;

        RectTransform cardHolder = UIFactory.Rect("CardHolder", safe);
        UIFactory.Stretch(cardHolder);
        toast.root = cardHolder.gameObject;

        RectTransform card = UIFactory.Rect("Card", cardHolder);
        card.anchorMin = new Vector2(0f, 1f);
        card.anchorMax = new Vector2(1f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(-Gutter * 2f, 144f);
        card.anchoredPosition = new Vector2(0f, toast.restY);
        toast.card = card;

        Image face = UIFactory.Fill(card, ThemeRole.Toast, 1f, UIShape.Rounded);
        face.raycastTarget = true;

        HorizontalLayoutGroup row = UIFactory.HorizontalStack(card, 33f);
        row.padding = new RectOffset(50, 22, 0, 0);
        row.childAlignment = TextAnchor.MiddleLeft;

        toast.messageText = UIFactory.Text("Message", card, Secondary, ThemeRole.OnToast,
                                           TextAlignmentOptions.Left);
        UIFactory.Flexible(toast.messageText.gameObject);
        toast.messageText.textWrappingMode = TextWrappingModes.Normal;
        toast.messageText.raycastTarget = false;

        RectTransform action = UIFactory.Rect("Action", card);
        UIFactory.Size(action.gameObject, 122f).flexibleWidth = 0f;
        toast.openButton = UIFactory.Button(action, ThemeRole.Toast, 0f, UIShape.Pill);
        HorizontalLayoutGroup actionRow = UIFactory.HorizontalStack(action, 0f);
        actionRow.padding = new RectOffset(33, 33, 0, 0);
        ContentSizeFitter fit = action.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        toast.actionLabel = UIFactory.Text("Label", action, Secondary, ThemeRole.OnToast,
                                           TextAlignmentOptions.Center, null, FontStyles.Bold);
        toast.actionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Size(toast.actionLabel.gameObject, 122f).preferredWidth = -1f;

        cardHolder.gameObject.SetActive(false);
        return toast;
    }

    /// <summary>
    /// How it works: one sheet, three steps - choose a trail, reach the stop,
    /// open it on the spot - and the line about the camera and location. Opened
    /// from Profile's help, over everything.
    /// </summary>
    static TutorialFlow BuildTutorial(Transform root)
    {
        RectTransform holder = UIFactory.Rect("Tutorial", root);
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build App UI");
        UIFactory.Stretch(holder);

        TutorialFlow flow = holder.gameObject.AddComponent<TutorialFlow>();

        // The overlay is what is shown and hidden; the holder stays active.
        RectTransform overlay = UIFactory.Rect("Overlay", holder);
        UIFactory.Stretch(overlay);
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.3f);
        dim.raycastTarget = true;
        flow.root = overlay.gameObject;

        RectTransform page = PushedPage(overlay, "Sheet", "how.bar", out RectTransform content,
                                        out RectTransform footer, out Button back, out Button close, true);
        Object.DestroyImmediate(page.GetComponent<Image>());
        back.transform.parent.GetComponent<CanvasGroup>().alpha = 0f;
        back.transform.parent.GetComponent<CanvasGroup>().blocksRaycasts = false;
        close.gameObject.AddComponent<WayOut>();
        Wire(close.onClick, flow.Finish, "How it works close");

        TutorialFlow.Page only = new TutorialFlow.Page();
        only.root = content.gameObject;
        only.key = "how";

        only.title = UIFactory.Text("Title", content, Display, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.TopLeft, "how.title", FontStyles.Bold);
        UIFactory.FitsItsText(only.title);

        RectTransform steps = UIFactory.Rect("Steps", content);
        UIFactory.VerticalStack(steps, 50f, false);
        Step(steps, AppIcon.MapFold, "how.s1", "how.s1b");
        Step(steps, AppIcon.Walk, "how.s2", "how.s2b");
        Step(steps, AppIcon.Spark, "how.s3", "how.s3b");

        Note(content, AppIcon.Info, "how.note");
        flow.pages.Add(only);

        RectTransform ok = UIFactory.Rect("Ok", footer);
        UIFactory.Size(ok.gameObject, Btn);
        UIFactory.Flexible(ok.gameObject);
        flow.nextButton = UIFactory.Button(ok, ThemeRole.Accent, 1f, UIShape.Pill);
        flow.nextLabel = UIFactory.Text("Label", ok, Body, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, "how.ok");
        UIFactory.Stretch(flow.nextLabel.rectTransform);

        overlay.gameObject.SetActive(false);
        return flow;
    }

    /// <summary>
    /// The drawing for a tutorial card, if one has been made for it.
    ///
    /// Named from the card's own key, so adding a picture is dropping a file into
    /// a folder rather than editing this list. Missing is a normal answer - two of
    /// the six cards are about typing a code and are better off without one.
    /// </summary>
    static Sprite TutorialArt(string key)
    {
        string number = key.Substring(key.IndexOf('.') + 1);
        string path = "Assets/Art/Tutorial/tutorial-" + number + ".png";

        if (!System.IO.File.Exists(path))
            return null;

        // A PNG imports as a plain texture, and LoadAssetAtPath<Sprite> returns
        // null for one of those. Set once, here, so the folder is the only thing
        // anybody has to remember.
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        // Single, not just Sprite. This project's default sprite mode is Multiple -
        // a sheet to be sliced - and the main asset of one of those is the texture,
        // so LoadAssetAtPath<Sprite> comes back null and the card draws nothing at
        // all. Setting the type without the mode gets you a picture that imports
        // correctly, reports no error, and is invisible.
        if (importer != null &&
            (importer.textureType != TextureImporterType.Sprite ||
             importer.spriteImportMode != SpriteImportMode.Single))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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

        // A picture where the card has one, and the empty space it used to be
        // where it does not.
        //
        // The guide explains an app you look through a camera at, in six screens
        // of nothing but words with more than half of each one blank. Four of the
        // six are about places - where a marker stands, the map against the
        // camera, being led somewhere, taking one off your map - and those are
        // drawn. The other two are about typing a code, which no drawing explains
        // better than the sentence does, so those keep the space.
        Sprite art = TutorialArt(key);

        if (art != null)
        {
            RectTransform picture = UIFactory.Rect("Art", rt);
            LayoutElement pictureSize = picture.gameObject.AddComponent<LayoutElement>();
            pictureSize.flexibleHeight = 1f;
            pictureSize.minHeight = 300f;

            Image image = picture.gameObject.AddComponent<Image>();
            image.sprite = art;

            // The drawing carries its own colours and its own transparency, so it
            // is not tinted and not themed: the paper was keyed out of it, which
            // leaves line work and one green ring, and both read on either ground.
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        else
        {
            // Centred, not pushed to the floor.
            //
            // A flexible spacer above the words put them at the bottom of an
            // otherwise empty screen, which on a phone reads as a picture that
            // failed to load rather than a card that never had one. The same
            // words in the middle of the card read as the whole card.
            stack.childAlignment = TextAnchor.MiddleLeft;
        }

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
                                              TrailGraphNodeView graphNodePrefab)
    {
        RectTransform rt = UIFactory.Rect("BrowseContent", SafeContent(panel));
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(Margin, Nav + Margin);
        rt.offsetMax = new Vector2(-Margin, -Margin);

        SearchTabController controller = rt.gameObject.AddComponent<SearchTabController>();
        controller.locationService = location;
        controller.rowPrefab = rowPrefab;

        // Back first. Browse has no tab of its own, so without this the only way out
        // is the nav bar - and a screen whose exit is somewhere else is a trap.
        // An arrow, then the title, reading left to right the way every other
        // titled screen in this app does. It was a filled slab on the left and
        // the title pushed to the right, which reads as two unrelated things.
        RectTransform back = UIFactory.Rect("BackButton", rt);
        UIFactory.Corner(back, new Vector2(0f, 1f), new Vector2(Tap, Tap), Vector2.zero);
        controller.backButton = UIFactory.Button(back, ThemeRole.Surface, 0f, UIShape.Circle);
        controller.backButton.gameObject.AddComponent<WayOut>();
        Icon(back, AppIcon.Back, 44f, ThemeRole.TextSecondary);

        // Kept and hidden: the controller writes to it, and the button still
        // needs a name for anything reading the screen out loud.
        controller.backLabel = UIFactory.Text("Label", back, Secondary, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Center, "map.backToMap",
                                              FontStyles.Bold);
        UIFactory.Stretch(controller.backLabel.rectTransform);
        controller.backLabel.gameObject.SetActive(false);

        TextMeshProUGUI heading = UIFactory.Text("Heading", rt, Display, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Left, "search.title",
                                                 FontStyles.Bold);
        UIFactory.Corner(heading.rectTransform, new Vector2(0f, 1f), new Vector2(700f, Tap),
                         new Vector2(Tap + 20f, 0f));

        // One scroll for everything under the title, so the summary can lead and
        // the search box can follow it rather than sitting above it in a fixed
        // slot. This screen answers "how am I doing" first and "where is X"
        // second, which is the order somebody opens it in.
        RectTransform body = UIFactory.Rect("Body", rt);
        UIFactory.Stretch(body);
        body.offsetMax = new Vector2(0f, -(Tap + 24f));
        RectTransform content = UIFactory.ScrollList("Scroll", body, 28f);

        controller.progressCard = BuildProgressCard(content);
        controller.discoverySection = controller.progressCard.gameObject;

        // The whole walk, on its own coordinates. Under the summary, because it
        // answers the same question at a different resolution: the card says how
        // many, this says which ones and where.
        RectTransform graphCard = UIFactory.Rect("TrailGraph", content);
        LayoutElement graphSize = UIFactory.Size(graphCard.gameObject, 900f);
        graphSize.flexibleHeight = 0f;
        UIFactory.Fill(graphCard, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        Outline(graphCard);

        controller.graphSection = graphCard.gameObject;

        TrailGraphView graph = graphCard.gameObject.AddComponent<TrailGraphView>();
        graph.nodePrefab = graphNodePrefab;
        controller.graph = graph;

        // The window clips; the thing inside it moves. RectMask2D rather than a
        // Mask, because a stencil mask is a hard alpha test and every disc in
        // here has a soft edge.
        RectTransform window = UIFactory.Rect("Window", graphCard);
        UIFactory.Stretch(window);
        window.offsetMin = new Vector2(6f, 6f);
        window.offsetMax = new Vector2(-6f, -6f);
        window.gameObject.AddComponent<RectMask2D>();
        graph.viewport = window;

        RectTransform pannable = UIFactory.Rect("Content", window);
        pannable.anchorMin = pannable.anchorMax = new Vector2(0.5f, 0.5f);
        pannable.sizeDelta = Vector2.zero;
        graph.content = pannable;

        // Nothing to draw. Its own words rather than the map's, because "no trail
        // open" on a screen with a progress bar above it needs no second button.
        RectTransform graphEmpty = UIFactory.Rect("Empty", window);
        UIFactory.Stretch(graphEmpty);
        graph.emptyGroup = graphEmpty.gameObject;

        TextMeshProUGUI graphEmptyText = UIFactory.Text("Label", graphEmpty, Secondary,
                                                        ThemeRole.TextMuted,
                                                        TextAlignmentOptions.Center,
                                                        "graph.empty");
        UIFactory.Stretch(graphEmptyText.rectTransform);
        graphEmptyText.textWrappingMode = TextWrappingModes.Normal;

        // The walker's own place on the line, between the last stop found and the
        // one ahead. Built here rather than in the node prefab because it is not
        // a stop: it moves as the walk does, and nothing about it is tappable.
        RectTransform here = UIFactory.Rect("Here", pannable);
        here.anchorMin = here.anchorMax = new Vector2(0.5f, 0.5f);
        here.sizeDelta = new Vector2(560f, 60f);
        graph.herePip = here;

        // The pip is already put on the line by the view, so its own parts sit at
        // its centre. Offsetting them again by the knob's distance pushed both
        // off the left edge of the card.
        RectTransform hereDot = UIFactory.Rect("Dot", here);
        UIFactory.Corner(hereDot, new Vector2(0.5f, 0.5f), new Vector2(38f, 38f),
                         Vector2.zero);
        UIFactory.Fill(hereDot, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;

        graph.hereLabel = UIFactory.Text("Label", here, Caption, ThemeRole.AccentText,
                                         TextAlignmentOptions.Left, "strip.here",
                                         FontStyles.Bold);
        UIFactory.Corner(graph.hereLabel.rectTransform, new Vector2(0.5f, 0.5f),
                         new Vector2(420f, 40f), new Vector2(240f, 0f));
        graph.hereLabel.raycastTarget = false;

        // Back to seeing all of it, for anybody who has dragged themselves lost.
        RectTransform fit = UIFactory.Rect("Fit", graphCard);
        UIFactory.Corner(fit, new Vector2(1f, 0f), new Vector2(Tap, Tap),
                         new Vector2(-16f, 16f));
        Button fitButton = UIFactory.Button(fit, ThemeRole.Surface, 0.92f, UIShape.Circle);
        Outline(fit);
        Icon(fit, AppIcon.MyLocation, 40f, ThemeRole.TextSecondary);
        Wire(fitButton.onClick, graph.Fit, "Trail graph fit");

        RectTransform fieldRect = UIFactory.Rect("SearchField", content);
        UIFactory.Size(fieldRect.gameObject, Tap);
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

        RectTransform closest = UIFactory.Rect("ClosestSection", content);
        UIFactory.VerticalStack(closest, 14f, false);
        controller.closestSection = closest.gameObject;
        controller.closestLabel = SectionLabel(closest, "search.closest");

        RectTransform closestList = UIFactory.Rect("List", closest);
        UIFactory.VerticalStack(closestList, 0f, false);
        controller.closestParent = closestList;

        // Same reason as the other two: the controller hides it once it knows
        // there is nothing near, and until then it is a heading over nothing.
        closest.gameObject.SetActive(false);

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
    static AccountSectionView BuildAccountCard(Transform content, bool label = true,
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
        view.saveNameButton = FullWidthButton(inGroup, "SaveName", "account.saveName",
                                              ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // Whatever the caller wants among the things only an account has - the
        // You tab puts "Email and password" here.
        if (signedInExtras != null)
            signedInExtras(inGroup);

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
        UIFactory.FitsItsText(body);

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

        // Required for a new account, nothing chosen: the website asks the same
        // question at its sign-up, in the same words.
        TextMeshProUGUI workLabel = UIFactory.Text("WorkLabel", outGroup, Body, ThemeRole.TextPrimary,
                                                   TextAlignmentOptions.BottomLeft, "work.title",
                                                   FontStyles.Bold);
        UIFactory.Size(workLabel.gameObject, 76f);
        view.workChoice = WorkChoices(outGroup);

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
        UIFactory.FitsItsText(warn);

        // ----------------------------------------------------------- status
        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 60f);
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
        UIFactory.Size(rt.gameObject, Btn);

        Button button = UIFactory.Button(rt, background, 1f, UIShape.Pill);

        // 17px regular, as Daylight sets every button: the capsule is what says
        // "action", and a bold label on it says it twice.
        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ink,
                                               TextAlignmentOptions.Center, key);
        UIFactory.Stretch(label.rectTransform);
        label.margin = new Vector4(30f, 0f, 30f, 0f);

        return button;
    }

    static TextMeshProUGUI SectionLabel(Transform parent, string key)
    {
        // Sentence case and the face's own spacing, as a grouped list labels its
        // groups. Spaced-out capitals in Cyrillic read as a shout and take twice
        // the width.
        TextMeshProUGUI label = UIFactory.Text("Label", parent, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.BottomLeft, key);
        UIFactory.Size(label.gameObject, 76f);
        label.margin = new Vector4(Margin, 0f, Margin, 8f);
        return label;
    }


    static DiscoveryProgressCard BuildProgressCard(Transform parent)
    {
        RectTransform card = UIFactory.Rect("DiscoveryCard", parent);
        UIFactory.Card(card, 32f, 16f);

        // The number and what it counts, on one line. They were stacked with a
        // twenty-pixel bar between them, so the figure read as a headline and the
        // words under the bar read as a caption for the bar.
        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.Size(head.gameObject, 88f);
        HorizontalLayoutGroup headRow = UIFactory.HorizontalStack(head, 16f);
        headRow.childAlignment = TextAnchor.LowerLeft;

        TextMeshProUGUI count = UIFactory.Text("Count", head, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.BottomLeft, null,
                                               FontStyles.Bold);
        // No fixed width: the label should sit against the number, not across a
        // gap sized for the largest number this card will ever hold.
        LayoutElement countSize = UIFactory.Size(count.gameObject, 88f);
        countSize.flexibleWidth = 0f;
        count.enableAutoSizing = false;

        // A dash until there is a number. Text() falls back to the object's name,
        // so before the first count arrived this card announced "Count" in
        // seventy-six point type.
        count.text = "—";

        TextMeshProUGUI label = UIFactory.Text("Label", head, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.BottomLeft,
                                               "search.discovered");
        UIFactory.Size(label.gameObject, 62f);
        UIFactory.Flexible(label.gameObject);

        // Ten, not twenty. At twenty it was a rounded slab the width of the card
        // and read as a divider rather than as a measure of anything.
        RectTransform bar = UIFactory.Rect("Bar", card);
        UIFactory.Size(bar.gameObject, 10f);
        UIFactory.Fill(bar, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);

        RectTransform fill = UIFactory.Rect("Fill", bar);
        UIFactory.Stretch(fill);
        UIFactory.Fill(fill, ThemeRole.Accent, 1f, UIShape.Pill);

        // Points and standing, side by side under the bar. Scored on another
        // screen entirely, which is exactly why they belong on the one somebody
        // opens to ask how it is going.
        RectTransform stats = UIFactory.Rect("Stats", card);
        UIFactory.Size(stats.gameObject, 96f);
        HorizontalLayoutGroup statsRow = UIFactory.HorizontalStack(stats, 24f, true);
        statsRow.childAlignment = TextAnchor.LowerLeft;

        RectTransform pointsCell = UIFactory.Rect("Points", stats);
        UIFactory.VerticalStack(pointsCell, 0f, false);

        TextMeshProUGUI pointsValue = UIFactory.Text("Value", pointsCell, Title,
                                                     ThemeRole.TextPrimary,
                                                     TextAlignmentOptions.Left, null,
                                                     FontStyles.Bold);
        UIFactory.Size(pointsValue.gameObject, 58f);

        TextMeshProUGUI pointsCaption = UIFactory.Text("Caption", pointsCell, Caption,
                                                       ThemeRole.TextMuted,
                                                       TextAlignmentOptions.Left);
        UIFactory.Size(pointsCaption.gameObject, 34f);

        RectTransform standingCell = UIFactory.Rect("Standing", stats);
        UIFactory.VerticalStack(standingCell, 0f, false);

        TextMeshProUGUI standingValue = UIFactory.Text("Value", standingCell, Title,
                                                       ThemeRole.TextPrimary,
                                                       TextAlignmentOptions.Left, null,
                                                       FontStyles.Bold);
        UIFactory.Size(standingValue.gameObject, 58f);

        TextMeshProUGUI standingCaption = UIFactory.Text("Caption", standingCell, Caption,
                                                         ThemeRole.TextMuted,
                                                         TextAlignmentOptions.Left);
        UIFactory.Size(standingCaption.gameObject, 34f);

        stats.gameObject.SetActive(false);

        TextMeshProUGUI note = UIFactory.Text("Note", card, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft,
                                              "search.discoveredNote");
        UIFactory.FitsItsText(note);

        DiscoveryProgressCard component = card.gameObject.AddComponent<DiscoveryProgressCard>();
        component.statsRow = stats.gameObject;
        component.pointsText = pointsValue;
        component.pointsLabel = pointsCaption;
        component.standingText = standingValue;
        component.standingLabel = standingCaption;
        component.noteText = note;
        component.countText = count;
        component.labelText = label;
        component.barFill = fill;

        // Built empty, not full.
        //
        // A stretched fill is a meter that reads "finished" until something
        // refreshes it. Refresh runs in OnEnable so nobody would see it for
        // long - but "not for long" is the wrong answer for the one control on
        // the screen whose whole job is to say how far you have got, and it is
        // how every picture of this screen came out showing a completed walk.
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        return component;
    }

    // -------------------------------------------------------------- settings tab

    /// <summary>
    /// Profile, as Daylight v2 lays it out: a large title that hands over to a
    /// glass bar as it scrolls, the person (or the guest's reason to make a
    /// profile), three numbers, every badge, what is being walked now, and then
    /// grouped settings - the theme, the alert, the language, privacy, help -
    /// with leaving at the very end.
    /// </summary>
    static SettingsTabController BuildSettingsTab(RectTransform panel)
    {
        RectTransform rt = UIFactory.Rect("SettingsContent", SafeContent(panel));
        UIFactory.Stretch(rt);

        SettingsTabController controller = rt.gameObject.AddComponent<SettingsTabController>();
        SubPanelNavigator navigator = rt.gameObject.AddComponent<SubPanelNavigator>();
        controller.navigator = navigator;

        RectTransform rootView = UIFactory.Rect("Root", rt);
        UIFactory.Stretch(rootView);
        RectTransform content = UIFactory.ScrollList("Scroll", rootView, 22f);
        content.GetComponent<VerticalLayoutGroup>().padding =
            new RectOffset((int)Margin, (int)Margin, 155, (int)(Nav + 120f));
        controller.scroll = content.GetComponentInParent<ScrollRect>();
        navigator.rootView = rootView.gameObject;

        // --- the large title
        RectTransform titleHolder = UIFactory.Rect("BigTitle", content);
        UIFactory.Size(titleHolder.gameObject, 150f);
        CanvasGroup bigTitle = titleHolder.gameObject.AddComponent<CanvasGroup>();
        TextMeshProUGUI big = UIFactory.Text("Title", titleHolder, Large, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.TopLeft, "tab.you", FontStyles.Bold);
        UIFactory.Stretch(big.rectTransform);
        big.margin = new Vector4(11f, 0f, 0f, 0f);

        // --- who: the guest's reason to make a profile, or the person
        ProfileView profile = BuildProfileCard(content, controller);

        // No badges, levels or XP: a tool, not a game (6 Nov 2026).
        profile.medalButtons = new Button[0];

        // --- what is being walked now
        GroupLabel(content, "now.title", out TextMeshProUGUI nowLabel);
        RectTransform now = Group(content, "NowGroup");
        NowWalkingView walking = now.gameObject.AddComponent<NowWalkingView>();
        walking.holders = new[] { nowLabel.gameObject, now.gameObject };
        walking.trailButton = TwoLineRow(now, "Trail", out walking.trailName, out walking.trailSub, true);
        walking.teamButton = TwoLineRow(now, "Team", out walking.teamName, out walking.teamSub, true);

        // --- settings
        GroupLabel(content, "settings.title", out TextMeshProUGUI _);
        RectTransform settings = Group(content, "SettingsGroup");

        BuildThemeRow(settings);

        controller.nearbyToggle = SwitchRow(settings, "settings.nearbyBuildings", "settings.nearbyNote",
                                            out controller.nearbyRowLabel, out controller.nearbyNoteLabel);
        controller.occlusionToggle = SwitchRow(settings, "settings.occlusion", null,
                                               out controller.occlusionRowLabel, out TextMeshProUGUI _);

        controller.languageButton = ValueRow(settings, "settings.appLanguage", out controller.languageValueLabel);

        // --- privacy
        GroupLabel(content, "settings.privacy", out TextMeshProUGUI _);
        RectTransform privacy = Group(content, "PrivacyCard");
        controller.hiddenRowLabel = RowLabel(privacy, "settings.hidden", out RectTransform hiddenRow);
        controller.hiddenCountLabel = UIFactory.Text("Count", hiddenRow, Body, ThemeRole.TextMuted,
                                                     TextAlignmentOptions.Right);
        LayoutElement hiddenCountSize = UIFactory.Size(controller.hiddenCountLabel.gameObject, Tap, 300f);
        hiddenCountSize.flexibleWidth = 0f;
        controller.unhideAllButton = ActionRow(privacy, "settings.unhideAll", ThemeRole.AccentText,
                                               out controller.unhideAllLabel);
        controller.blockedButton = HelpRow(privacy, "settings.blocked", out TextMeshProUGUI _);

        // --- the account, for somebody who has one
        GroupLabel(content, "settings.account", out TextMeshProUGUI accountLabel);
        RectTransform account = Group(content, "AccountGroup");
        controller.accountButton = HelpRow(account, "settings.nameEmail", out TextMeshProUGUI _);
        controller.securityButton = HelpRow(account, "settings.security", out TextMeshProUGUI _);

        // What the account does, and a way to change it. The value runs long in
        // Bulgarian ("Брокер на недвижими имоти"), so it gets more room than a
        // language name and an ellipsis rather than a second line.
        controller.workButton = ValueRow(account, "work.row", out controller.workValueLabel);
        LayoutElement workValueSize = controller.workValueLabel.GetComponent<LayoutElement>();
        workValueSize.minWidth = 0f;
        workValueSize.preferredWidth = 500f;
        controller.workValueLabel.overflowMode = TextOverflowModes.Ellipsis;
        Transform workRowLabel = controller.workValueLabel.transform.parent.Find("Label");
        if (workRowLabel != null)
            workRowLabel.GetComponent<TextMeshProUGUI>().textWrappingMode = TextWrappingModes.NoWrap;

        // --- help
        GroupLabel(content, "settings.help", out TextMeshProUGUI _);
        RectTransform help = Group(content, "HelpCard");
        controller.howButton = HelpRow(help, "settings.how", out TextMeshProUGUI _);
        controller.feedbackButton = HelpRow(help, "settings.feedback", out TextMeshProUGUI _);
        controller.tutorialButton = HelpRow(help, "settings.replayTutorial", out controller.tutorialRowLabel);
        controller.termsButton = LinkRow(help, "settings.terms");
        controller.privacyButton = LinkRow(help, "settings.privacyPolicy");
        controller.sourcesButton = HelpRow(help, "settings.dataSources", out controller.sourcesRowLabel);
        controller.aboutButton = HelpRow(help, "settings.about", out controller.aboutRowLabel);
        Button signOut = ActionRow(help, "account.signOut", ThemeRole.AccentText, out TextMeshProUGUI _);

        // --- leaving, last and in plain sight
        GroupLabel(content, "settings.yourData", out TextMeshProUGUI _);
        BuildDeleteCard(content);

        RectTransform versionRow = UIFactory.Rect("Version", content);
        UIFactory.Size(versionRow.gameObject, Tap);
        controller.versionButton = UIFactory.Button(versionRow, ThemeRole.Background, 0f);
        controller.versionLabel = UIFactory.Text("Label", versionRow, Caption, ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Center);
        UIFactory.Stretch(controller.versionLabel.rectTransform);

        profile.signedInOnly = new[] { accountLabel.gameObject, account.gameObject, signOut.gameObject };

        // --- the pages a row opens
        controller.sourcesPage = SubPage(rt, "SourcesPage", "settings.dataSources", "sub.sources.body");
        controller.aboutPage = SubPage(rt, "AboutPage", "settings.about", "sub.about.body");
        controller.securityPage = BuildSecurityPage(rt);
        controller.blockedPage = BuildBlockedPage(rt);
        controller.feedbackPage = BuildFeedbackPage(rt);
        controller.accountPage = BuildAccountPage(rt, out AccountSectionView accountView);
        Wire(signOut.onClick, accountView.SignOut, "Sign out");

        navigator.pages = new List<GameObject>
        {
            controller.sourcesPage, controller.aboutPage,
            controller.securityPage, controller.blockedPage,
            controller.feedbackPage, controller.accountPage
        };

        // Back, in the corner of every page: a 44px circle, as on a sheet.
        RectTransform backRect = UIFactory.Rect("BackButton", rt);
        UIFactory.Corner(backRect, new Vector2(0f, 1f), new Vector2(122f, 122f), new Vector2(33f, -6f));
        navigator.backButton = UIFactory.Button(backRect, ThemeRole.Fill, 1f, UIShape.Circle);
        navigator.backButton.gameObject.AddComponent<WayOut>();
        Icon(backRect, AppIcon.ChevronLeft, 55f, ThemeRole.TextPrimary);
        backRect.gameObject.SetActive(false);

        // --- the bar the large title hands over to, under the status bar
        RectTransform barSafe = UIFactory.Rect("TitleBar", panel);
        UIFactory.Stretch(barSafe);
        barSafe.gameObject.AddComponent<SafeAreaPad>().top = true;
        CanvasGroup barGroup = barSafe.gameObject.AddComponent<CanvasGroup>();
        barGroup.alpha = 0f;
        barGroup.blocksRaycasts = false;

        RectTransform bar = UIFactory.Rect("Bar", barSafe);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, 133f + 300f);
        bar.anchoredPosition = new Vector2(0f, -133f);
        UIFactory.Fill(bar, ThemeRole.Background, 1f).raycastTarget = true;

        RectTransform barRule = UIFactory.Rect("Rule", bar);
        UIFactory.BottomStretch(barRule, 3f);
        UIFactory.Fill(barRule, ThemeRole.Line).raycastTarget = false;

        TextMeshProUGUI barTitle = UIFactory.Text("Title", bar, Body, ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.Center, "tab.you", FontStyles.Bold);
        barTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
        barTitle.rectTransform.anchorMax = new Vector2(1f, 0f);
        barTitle.rectTransform.pivot = new Vector2(0.5f, 0f);
        barTitle.rectTransform.sizeDelta = new Vector2(0f, 133f);
        barTitle.rectTransform.anchoredPosition = Vector2.zero;

        LargeTitleHeader header = rootView.gameObject.AddComponent<LargeTitleHeader>();
        header.scroll = controller.scroll;
        header.bigTitle = bigTitle;
        header.bar = barGroup;

        navigator.ShowRoot();
        return controller;
    }

    // ------------------------------------------------------------ grouped list

    /// <summary>
    /// A group's label: 14px grey, 24px above it and 8 below, 16 in - the line
    /// that names what the white card under it is about.
    /// </summary>
    static void GroupLabel(Transform parent, string key, out TextMeshProUGUI label)
    {
        label = UIFactory.Text("GroupLabel", parent, Secondary, ThemeRole.TextMuted,
                               TextAlignmentOptions.BottomLeft, key);
        UIFactory.Size(label.gameObject, 110f);
        label.margin = new Vector4(44f, 0f, 44f, 0f);
    }

    /// <summary>A white card of rows, 18px corners, 16 in at the sides.</summary>
    static RectTransform Group(Transform parent, string name)
    {
        RectTransform group = UIFactory.Rect(name, parent);
        UIFactory.Fill(group, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(group, 0f, false);
        stack.padding = new RectOffset(44, 44, 11, 11);
        return group;
    }

    /// <summary>The hairline above every row of a group but its first.</summary>
    static void RowRule(Transform row)
    {
        if (row.GetSiblingIndex() == 0)
            return;

        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);
    }

    /// <summary>A row with a title, a grey line under it, and a chevron.</summary>
    static Button TwoLineRow(Transform group, string name, out TextMeshProUGUI title,
                             out TextMeshProUGUI sub, bool chevron)
    {
        RectTransform row = UIFactory.Rect(name, group);
        UIFactory.Size(row.gameObject, 166f);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);
        RowRule(row);

        RectTransform line = UIFactory.Rect("Line", row);
        UIFactory.Stretch(line);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 33f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform text = UIFactory.Rect("Text", line);
        UIFactory.VerticalStack(text, 6f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        title = UIFactory.Text("Title", text, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Left);
        UIFactory.Size(title.gameObject, 58f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        sub = UIFactory.Text("Sub", text, Secondary, ThemeRole.TextMuted, TextAlignmentOptions.Left);
        UIFactory.Size(sub.gameObject, 50f);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;

        if (chevron)
            Chevron(line);

        return button;
    }

    static void Chevron(Transform row)
    {
        Image glyph = Icon(row, AppIcon.ChevronRight, 44f, ThemeRole.TextMuted);
        glyph.GetComponent<ThemedElement>().preserveAlpha = true;
        glyph.color = new Color(glyph.color.r, glyph.color.g, glyph.color.b, 0.6f);
    }

    /// <summary>A row with a value on its right in grey, and a chevron.</summary>
    static Button ValueRow(Transform group, string key, out TextMeshProUGUI value)
    {
        RectTransform row = UIFactory.Rect("Row", group);
        UIFactory.Size(row.gameObject, Row52);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);
        RowRule(row);

        RectTransform line = UIFactory.Rect("Line", row);
        UIFactory.Stretch(line);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 22f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI label = UIFactory.Text("Label", line, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Flexible(label.gameObject);

        value = UIFactory.Text("Value", line, Body, ThemeRole.TextMuted, TextAlignmentOptions.Right);
        value.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Size(value.gameObject, Row52, 300f).flexibleWidth = 0f;

        Chevron(line);
        return button;
    }

    /// <summary>A switch with its title and, under the title, what it means.</summary>
    static Toggle SwitchRow(Transform group, string key, string noteKey, out TextMeshProUGUI title,
                            out TextMeshProUGUI note)
    {
        RectTransform row = UIFactory.Rect("SwitchRow", group);
        LayoutElement rowSize = row.gameObject.AddComponent<LayoutElement>();
        rowSize.minHeight = noteKey != null ? 177f : Row52;
        RowRule(row);

        // The row is its own layout, so it grows when a title wraps.
        HorizontalLayoutGroup rowLayout = UIFactory.HorizontalStack(row, 0f);
        rowLayout.padding = new RectOffset(0, 0, 28, 28);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;

        RectTransform line = UIFactory.Rect("Line", row);
        UIFactory.Flexible(line.gameObject);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 33f);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;

        RectTransform text = UIFactory.Rect("Text", line);
        UIFactory.VerticalStack(text, 6f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        title = UIFactory.Text("Title", text, Body, ThemeRole.TextPrimary, TextAlignmentOptions.Left, key);
        UIFactory.FitsItsText(title);

        note = null;
        if (noteKey != null)
        {
            note = UIFactory.Text("Note", text, Secondary, ThemeRole.TextMuted, TextAlignmentOptions.Left,
                                  noteKey);
            UIFactory.FitsItsText(note);
        }

        return UIFactory.Switch("Toggle", line);
    }

    /// <summary>
    /// Theme: its name above a three-way segmented control - like the phone,
    /// light, dark.
    /// </summary>
    static void BuildThemeRow(Transform group)
    {
        RectTransform row = UIFactory.Rect("ThemeRow", group);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(row, 22f, false);
        stack.padding = new RectOffset(0, 0, 33, 33);
        RowRule(row);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "settings.theme");
        UIFactory.Size(label.gameObject, 64f);

        RectTransform seg = UIFactory.Rect("Segment", row);
        UIFactory.Size(seg.gameObject, 111f);
        UIFactory.Fill(seg, ThemeRole.Fill, 1f, UIShape.Small).raycastTarget = false;
        HorizontalLayoutGroup segRow = UIFactory.HorizontalStack(seg, 6f, true);
        segRow.padding = new RectOffset(6, 6, 6, 6);

        ThemeChoiceView view = seg.gameObject.AddComponent<ThemeChoiceView>();
        string[] keys = { "theme.system", "theme.light", "theme.dark" };

        // One thumb that slides between the choices, under their labels.
        RectTransform thumb = UIFactory.Rect("Thumb", seg);
        thumb.anchorMin = new Vector2(0.5f, 0.5f);
        thumb.anchorMax = new Vector2(0.5f, 0.5f);
        thumb.pivot = new Vector2(0f, 0.5f);
        view.thumbImage = UIFactory.Plain(thumb, ThemeManager.Get(ThemeRole.Surface), UIShape.Small);
        view.thumbImage.raycastTarget = false;
        view.thumb = thumb;
        Ignore(thumb);

        for (int i = 0; i < 3; i++)
        {
            RectTransform half = UIFactory.Rect("Choice" + i, seg);
            UIFactory.Flexible(half.gameObject);
            view.faces[i] = UIFactory.Plain(half, ThemeManager.Get(ThemeRole.Surface), UIShape.Small);
            view.buttons[i] = half.gameObject.AddComponent<Button>();
            view.buttons[i].targetGraphic = view.faces[i];
            UIFactory.Feedback(view.buttons[i]);

            view.labels[i] = UIFactory.Text("Label", half, Secondary, ThemeRole.TextPrimary,
                                            TextAlignmentOptions.Center, null);
            UIFactory.Stretch(view.labels[i].rectTransform);
            view.labels[i].text = AppStrings.Get(keys[i]);
            view.labels[i].textWrappingMode = TextWrappingModes.NoWrap;
            // "Automatic" fits its third of the bar; the old "Like the phone"
            // did not. (Auto-sizing here collapsed the whole control - 6 Nov.)
            Unthemed(view.labels[i].gameObject);
        }

        view.Paint();
    }

    /// <summary>
    /// Every badge as a grid of three: a 56px disc - tinted when earned, an
    /// outline when not - and its name under it. Each opens the page with the
    /// rules and how far along each one is.
    /// </summary>
    static Button[] BuildBadgeGrid(RectTransform group, ProfileView view)
    {
        int n = Badges.Keys.Length;
        view.badgeSprites = BadgeSprites();
        view.medalSlots = new GameObject[n];
        view.medalRings = new Image[n];
        view.medalIcons = new Image[n];
        view.medalLabels = new TextMeshProUGUI[n];
        view.medalEdges = new Image[n];

        RectTransform grid = UIFactory.Rect("Grid", group);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 3;
        layout.cellSize = new Vector2(298f, 270f);
        layout.spacing = new Vector2(5f, 11f);
        layout.padding = new RectOffset(0, 0, 33, 33);
        layout.childAlignment = TextAnchor.UpperCenter;

        Button[] buttons = new Button[n];

        for (int i = 0; i < n; i++)
        {
            RectTransform slot = UIFactory.Rect("Badge" + i, grid);
            buttons[i] = UIFactory.Button(slot, ThemeRole.Surface, 0f);
            VerticalLayoutGroup stack = UIFactory.VerticalStack(slot, 22f, false);
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childForceExpandWidth = false;
            stack.padding = new RectOffset(0, 0, 17, 0);
            view.medalSlots[i] = slot.gameObject;

            RectTransform disc = Medal(slot, 155f, out view.medalRings[i], out view.medalIcons[i]);
            view.medalIcons[i].rectTransform.sizeDelta = new Vector2(72f, 72f);

            RectTransform edge = UIFactory.Rect("Edge", disc);
            UIFactory.Stretch(edge);
            view.medalEdges[i] = UIFactory.Fill(edge, ThemeRole.Line);
            view.medalEdges[i].sprite = UISprites.RingThin;
            view.medalEdges[i].raycastTarget = false;
            edge.gameObject.SetActive(false);

            view.medalLabels[i] = UIFactory.Text("Label", slot, Caption, ThemeRole.TextSecondary,
                                                 TextAlignmentOptions.Top);
            view.medalLabels[i].textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Size(view.medalLabels[i].gameObject, 86f, 280f);
        }

        return buttons;
    }

    /// <summary>
    /// A sheet at Daylight's medium height, over everything including the tab
    /// bar: the grabber, a header with the title and close, and a body. The dim
    /// behind it and a swipe down close it too.
    /// </summary>
    static RectTransform MediumSheet(Transform root, string name, string titleKey,
                                     out RectTransform body, out Button close, out Button outside)
    {
        RectTransform panel = UIFactory.Rect(name, root);
        UIFactory.Stretch(panel);

        Image dim = panel.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.3f);
        outside = panel.gameObject.AddComponent<Button>();
        outside.targetGraphic = dim;
        outside.transition = Selectable.Transition.None;

        RectTransform sheet = UIFactory.Rect("Sheet", panel);
        sheet.anchorMin = Vector2.zero;
        sheet.anchorMax = new Vector2(1f, 0.62f);
        sheet.offsetMin = Vector2.zero;
        sheet.offsetMax = Vector2.zero;
        SheetFace(sheet);

        SheetHeader(sheet, out Button _, out TextMeshProUGUI title, out close, out CanvasGroup _);
        title.text = titleKey != null ? AppStrings.Get(titleKey) : string.Empty;

        if (titleKey != null)
            title.gameObject.AddComponent<LocalizedLabel>().key = titleKey;

        close.gameObject.AddComponent<WayOut>();
        Swipeable(sheet, close);

        RectTransform area = SheetArea(sheet);
        body = UIFactory.Rect("Body", area);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(SheetPad, 0f);
        body.offsetMax = new Vector2(-SheetPad, 0f);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(body, 33f, false);
        stack.padding = new RectOffset(0, 0, 11, 0);

        panel.gameObject.SetActive(false);
        return panel;
    }

    /// <summary>
    /// A badge on a sheet of its own: the disc at 96px, its name, its rule, and
    /// earned or how far along.
    /// </summary>
    static BadgeSheetView BuildBadgeSheet(Transform root, Sprite[] sprites)
    {
        RectTransform panel = MediumSheet(root, "BadgeSheet", null, out RectTransform body,
                                          out Button close, out Button outside);
        BadgeSheetView view = panel.gameObject.AddComponent<BadgeSheetView>();
        view.sprites = sprites;
        Wire(close.onClick, view.Close, "Badge sheet close");
        Wire(outside.onClick, view.Close, "Badge sheet outside");

        VerticalLayoutGroup stack = body.GetComponent<VerticalLayoutGroup>();
        stack.childAlignment = TextAnchor.UpperCenter;
        stack.childForceExpandWidth = false;

        RectTransform disc = Medal(body, 266f, out view.ring, out view.icon);
        view.icon.rectTransform.sizeDelta = new Vector2(122f, 122f);

        RectTransform edge = UIFactory.Rect("Edge", disc);
        UIFactory.Stretch(edge);
        view.edge = UIFactory.Fill(edge, ThemeRole.Line);
        view.edge.sprite = UISprites.RingThin;
        view.edge.raycastTarget = false;

        view.title = UIFactory.Text("Title", body, Title, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.title);

        view.rule = UIFactory.Text("Rule", body, Body, ThemeRole.TextSecondary,
                                   TextAlignmentOptions.Center);
        UIFactory.FitsItsText(view.rule);
        LayoutElement ruleWidth = view.rule.gameObject.AddComponent<LayoutElement>();
        ruleWidth.preferredWidth = 900f;

        // Up to 280px wide, a 6px track with the accent along it.
        RectTransform bar = UIFactory.Rect("Bar", body);
        UIFactory.Size(bar.gameObject, 17f, 776f).flexibleWidth = 0f;
        UIFactory.Fill(bar, ThemeRole.Fill, 1f, UIShape.Pill).raycastTarget = false;
        view.barHolder = bar.gameObject;

        RectTransform fill = UIFactory.Rect("Fill", bar);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0.5f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        view.barFillImage = UIFactory.Plain(fill, ThemeManager.Get(ThemeRole.Accent), UIShape.Pill);
        view.barFillImage.raycastTarget = false;
        view.barFill = fill;

        view.caption = UIFactory.Text("Caption", body, Secondary, ThemeRole.TextMuted,
                                      TextAlignmentOptions.Center);
        UIFactory.FitsItsText(view.caption);

        return view;
    }

    /// <summary>
    /// Language, as Daylight has it: a medium sheet with the two languages as a
    /// radio list and a line on what the choice changes.
    /// </summary>
    static LanguageChoiceView BuildLanguageSheet(Transform root, TextMeshProUGUI value)
    {
        RectTransform panel = MediumSheet(root, "LanguageSheet", "settings.appLanguage",
                                          out RectTransform body, out Button close, out Button outside);
        LanguageChoiceView view = panel.gameObject.AddComponent<LanguageChoiceView>();
        view.valueLabel = value;
        Wire(close.onClick, view.Close, "Language sheet close");
        Wire(outside.onClick, view.Close, "Language sheet outside");

        RectTransform options = UIFactory.Rect("Options", body);
        UIFactory.VerticalStack(options, 28f, false);

        RadioRow(options, "Bulgarian", null, out view.bulgarian, out TextMeshProUGUI bg,
                 out view.bulgarianRadio, out view.bulgarianHole, out view.bulgarianEdge);
        bg.text = "Български";
        RadioRow(options, "English", null, out view.english, out TextMeshProUGUI en,
                 out view.englishRadio, out view.englishHole, out view.englishEdge);
        en.text = "English";

        TextMeshProUGUI note = UIFactory.Text("Note", body, Secondary, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft, "settings.languageNote");
        UIFactory.FitsItsText(note);
        return view;
    }

    /// <summary>
    /// The line of work as a sheet: the four choices, why they are asked, and
    /// Save. Settings opens it to change the answer; DoorFlow puts it up once
    /// a launch for an account that has never given one. Taller than the
    /// medium sheet, because four rows and a Save do not fit in one.
    /// </summary>
    static WorkSheetView BuildWorkSheet(Transform root)
    {
        RectTransform panel = MediumSheet(root, "WorkSheet", null,
                                          out RectTransform body, out Button close, out Button outside);
        WorkSheetView view = panel.gameObject.AddComponent<WorkSheetView>();
        Wire(close.onClick, view.Close, "Work sheet close");
        Wire(outside.onClick, view.Close, "Work sheet outside");

        RectTransform sheet = (RectTransform)panel.Find("Sheet");
        if (sheet != null)
            sheet.anchorMax = new Vector2(1f, 0.76f);

        // The header's title, written by the view: it is a question when asked
        // and a setting when opened from Settings.
        Transform titleObject = panel.Find("Sheet/Head/Bar/Title");
        view.title = titleObject != null ? titleObject.GetComponent<TextMeshProUGUI>() : null;
        if (view.title != null)
            view.title.text = AppStrings.Get("work.title");

        view.askLine = UIFactory.Text("Ask", body, Secondary, ThemeRole.TextSecondary,
                                      TextAlignmentOptions.TopLeft, "work.ask");
        UIFactory.FitsItsText(view.askLine);

        view.choice = WorkChoices(body);

        view.statusLabel = UIFactory.Text("Status", body, Secondary, ThemeRole.StatusPoor,
                                          TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.statusLabel);
        view.statusLabel.text = string.Empty;
        view.statusLabel.gameObject.SetActive(false);

        view.saveButton = FullWidthButton(body, "Save", "work.save", ThemeRole.Accent, ThemeRole.OnAccent);
        return view;
    }

    /// <summary>
    /// The four lines of work as radio rows, in LineOfWork.Keys order, with
    /// nothing chosen. "Other" carries its hint under its name, as the website
    /// has it.
    /// </summary>
    static WorkChoiceView WorkChoices(Transform parent)
    {
        RectTransform list = UIFactory.Rect("WorkChoice", parent);
        UIFactory.VerticalStack(list, 22f, false);
        WorkChoiceView view = list.gameObject.AddComponent<WorkChoiceView>();

        int n = LineOfWork.Keys.Length;
        view.options = new Button[n];
        view.radios = new Image[n];
        view.holes = new RectTransform[n];
        view.edges = new Image[n];

        for (int i = 0; i < n; i++)
        {
            string work = LineOfWork.Keys[i];
            WorkRow(list, work, work == "other" ? "work.otherHint" : null,
                    out view.options[i], out view.radios[i], out view.holes[i], out view.edges[i]);
        }

        return view;
    }

    /// <summary>
    /// One line of work: RadioRow's 56px row with its hairline and 22px radio,
    /// but growing past 56px when there is a hint under the name, or when a
    /// Bulgarian name wraps.
    /// </summary>
    static void WorkRow(Transform parent, string work, string hintKey, out Button button,
                        out Image radio, out RectTransform hole, out Image edge)
    {
        RectTransform rt = UIFactory.Rect(char.ToUpperInvariant(work[0]) + work.Substring(1), parent);
        button = UIFactory.Button(rt, ThemeRole.Surface, 1f, UIShape.Rounded);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 155f;

        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(rt, 39f);
        layout.padding = new RectOffset(44, 44, 34, 34);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform edgeRect = UIFactory.Rect("Edge", rt);
        UIFactory.Stretch(edgeRect);
        edge = UIFactory.Plain(edgeRect, ThemeManager.Get(ThemeRole.Line));
        edge.sprite = UISprites.RoundedOutline;
        edge.type = Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1f;
        edge.raycastTarget = false;
        Ignore(edgeRect);

        RectTransform disc = UIFactory.Rect("Radio", rt);
        UIFactory.Size(disc.gameObject, 61f, 61f).flexibleWidth = 0f;
        radio = UIFactory.Plain(disc, ThemeManager.Get(ThemeRole.FillStrong), UIShape.Circle);
        radio.raycastTarget = false;

        hole = UIFactory.Rect("Hole", disc);
        UIFactory.Corner(hole, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f), Vector2.zero);
        UIFactory.Fill(hole, ThemeRole.Surface, 1f, UIShape.Circle).raycastTarget = false;

        // Takes what is left of the row, and no more: without a preferred width
        // of its own it would ask for the hint's unwrapped length.
        RectTransform words = UIFactory.Rect("Words", rt);
        LayoutElement wordsSize = words.gameObject.AddComponent<LayoutElement>();
        wordsSize.minWidth = 0f;
        wordsSize.preferredWidth = 1f;
        wordsSize.flexibleWidth = 1f;
        UIFactory.VerticalStack(words, 6f, false).childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI label = UIFactory.Text("Label", words, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "work." + work);
        UIFactory.FitsItsText(label);
        label.raycastTarget = false;

        if (hintKey != null)
        {
            TextMeshProUGUI hint = UIFactory.Text("Hint", words, Caption, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Left, hintKey);
            UIFactory.FitsItsText(hint);
            hint.raycastTarget = false;
        }
    }

    /// <summary>Name, email, and changing them: the account card, as a page.</summary>
    static GameObject BuildAccountPage(Transform parent, out AccountSectionView view)
    {
        RectTransform rt = PageFrame(parent, "AccountPage", out RectTransform content);

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "settings.nameEmail", FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        view = BuildAccountCard(content, false);
        return rt.gameObject;
    }

    // ----------------------------------------------------------------- you

    /// <summary>The glyph for each badge, in Badges.Keys order.</summary>
    static Sprite[] BadgeSprites()
    {
        AppIcon[] icons =
        {
            AppIcon.MapPin, AppIcon.Navigation, AppIcon.Flag, AppIcon.Clock, AppIcon.Book,
            AppIcon.Star, AppIcon.Trail, AppIcon.Users, AppIcon.Home, AppIcon.Mountain,
            AppIcon.Camera
        };

        Sprite[] sprites = new Sprite[icons.Length];
        for (int i = 0; i < icons.Length; i++)
            sprites[i] = UIIcons.Get(icons[i]);

        return sprites;
    }

    /// <summary>
    /// A badge's disc: a wash of its colour with the glyph on it. Both are
    /// coloured by the view from the badge's state, so neither is themed.
    /// </summary>
    static RectTransform Medal(Transform parent, float size, out Image ring, out Image icon)
    {
        RectTransform disc = UIFactory.Rect("Disc", parent);
        LayoutElement discSize = UIFactory.Size(disc.gameObject, size, size);
        discSize.flexibleWidth = 0f;

        ring = UIFactory.Plain(disc, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        ring.raycastTarget = false;
        Unthemed(ring.gameObject);

        RectTransform glyph = UIFactory.Rect("Glyph", disc);
        UIFactory.Corner(glyph, new Vector2(0.5f, 0.5f), new Vector2(size * 0.52f, size * 0.52f),
                         Vector2.zero);
        icon = glyph.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = ThemeManager.Get(ThemeRole.Accent);

        return disc;
    }

    /// <summary>
    /// The top of the You tab. Concept A: one scroll, you at the top.
    /// </summary>
    static ProfileView BuildProfileCard(Transform content, SettingsTabController controller)
    {
        RectTransform card = Group(content, "ProfileCard");
        VerticalLayoutGroup pad = card.GetComponent<VerticalLayoutGroup>();
        pad.padding = new RectOffset(44, 44, 44, 44);
        pad.spacing = 39f;

        ProfileView view = card.gameObject.AddComponent<ProfileView>();
        view.badgeSprites = BadgeSprites();

        // ---- a guest: where their progress is, and the way to keep it
        RectTransform guest = UIFactory.Rect("Guest", card);
        UIFactory.VerticalStack(guest, 28f, false);
        view.guestGroup = guest.gameObject;

        TextMeshProUGUI only = UIFactory.Text("Title", guest, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.TopLeft, "profile.onlyHere", FontStyles.Bold);
        UIFactory.FitsItsText(only);

        TextMeshProUGUI why = UIFactory.Text("Body", guest, Secondary, ThemeRole.TextSecondary,
                                             TextAlignmentOptions.TopLeft, "profile.keepIt");
        UIFactory.FitsItsText(why);

        RectTransform buttons = UIFactory.Rect("Buttons", guest);
        UIFactory.Size(buttons.gameObject, Btn);
        UIFactory.HorizontalStack(buttons, 28f).childAlignment = TextAnchor.MiddleLeft;

        view.createButton = WideButton(buttons, "Create", ThemeRole.Accent, ThemeRole.OnAccent,
                                       "account.createProfile", out TextMeshProUGUI _);
        RectTransform signIn = UIFactory.Rect("SignIn", buttons);
        UIFactory.Size(signIn.gameObject, Btn, 300f).flexibleWidth = 0f;
        view.signInButton = UIFactory.Button(signIn, ThemeRole.Fill, 1f, UIShape.Pill);
        TextMeshProUGUI signInLabel = UIFactory.Text("Label", signIn, Body, ThemeRole.TextPrimary,
                                                     TextAlignmentOptions.Center, "account.signInShort");
        UIFactory.Stretch(signInLabel.rectTransform);

        // ---- somebody: their face, their name, since when
        RectTransform me = UIFactory.Rect("Me", card);
        UIFactory.Size(me.gameObject, 220f);
        HorizontalLayoutGroup meRow = UIFactory.HorizontalStack(me, 39f, false);
        meRow.childAlignment = TextAnchor.MiddleLeft;
        view.meGroup = me.gameObject;

        RectTransform face = UIFactory.Rect("Face", me);
        UIFactory.Size(face.gameObject, 166f, 166f).flexibleWidth = 0f;
        view.face = UIFactory.Plain(face, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        view.face.raycastTarget = false;
        Unthemed(view.face.gameObject);

        view.faceLabel = UIFactory.Text("Initials", face, Title, ThemeRole.OnAccent,
                                        TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.faceLabel.rectTransform);
        view.faceLabel.raycastTarget = false;
        Unthemed(view.faceLabel.gameObject);

        RectTransform who = UIFactory.Rect("Who", me);
        UIFactory.VerticalStack(who, 6f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(who.gameObject);

        view.nameLabel = UIFactory.Text("Name", who, Title, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.nameLabel.gameObject, 72f);
        view.nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;

        view.subLabel = UIFactory.Text("Sub", who, Secondary, ThemeRole.TextMuted,
                                       TextAlignmentOptions.Left, null);
        UIFactory.Size(view.subLabel.gameObject, 56f);

        me.gameObject.SetActive(false);

        // ---- the two numbers, each a card of its own, under the head card
        RectTransform stats = UIFactory.Rect("Stats", content);
        UIFactory.Size(stats.gameObject, 177f);
        UIFactory.HorizontalStack(stats, 22f, true);
        view.statsGroup = stats.gameObject;

        view.trailsValue = StatCell(stats, "profile.trails");
        view.stopsValue = StatCell(stats, "profile.stops");

        return view;
    }

    /// <summary>A number over its label, on a card of its own.</summary>
    static TextMeshProUGUI StatCell(Transform strip, string captionKey)
    {
        RectTransform cell = UIFactory.Rect("Stat", strip);
        UIFactory.Fill(cell, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(cell, 6f, false);
        stack.childAlignment = TextAnchor.MiddleCenter;
        stack.padding = new RectOffset(17, 17, 33, 33);

        TextMeshProUGUI value = UIFactory.Text("Value", cell, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Size(value.gameObject, 66f);
        value.text = "0";

        TextMeshProUGUI caption = UIFactory.Text("Caption", cell, Caption, ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Center, captionKey);
        UIFactory.Size(caption.gameObject, 40f);
        return value;
    }

    /// <summary>A help row that leaves the app, so it wears the open-outside glyph.</summary>
    static Button LinkRow(Transform card, string key)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Row52);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);
        RowRule(row);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMax = new Vector2(-66f, 0f);

        Icon(row, AppIcon.OpenInNew, 44f, ThemeRole.TextMuted, out RectTransform glyphRect);
        UIFactory.Corner(glyphRect, new Vector2(1f, 0.5f), new Vector2(44f, 44f), Vector2.zero);

        return button;
    }

    /// <summary>
    /// A page Profile pushes: slides in from the right under the back circle in
    /// its corner, and scrolls on under the glass tab bar.
    /// </summary>
    static RectTransform PageFrame(Transform parent, string name, out RectTransform content)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Stretch(rt);
        rt.offsetMax = new Vector2(0f, -150f);
        rt.gameObject.AddComponent<PagePush>();

        content = UIFactory.ScrollList("Scroll", rt, 33f);
        content.GetComponent<VerticalLayoutGroup>().padding =
            new RectOffset((int)Margin, (int)Margin, 0, (int)(Nav + 120f));
        rt.gameObject.SetActive(false);
        return rt;
    }

    /// <summary>Every badge, with its rule and how far along it is.</summary>
    static GameObject BuildBadgesPage(Transform parent)
    {
        RectTransform rt = PageFrame(parent, "BadgesPage", out RectTransform content);
        BadgesPageView view = rt.gameObject.AddComponent<BadgesPageView>();

        view.titleLabel = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.titleLabel.gameObject, 84f);

        view.countLabel = UIFactory.Text("Count", content, Secondary, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.Left, null);
        UIFactory.FitsItsText(view.countLabel);

        Sprite[] sprites = BadgeSprites();
        int n = Badges.Keys.Length;

        view.rings = new Image[n];
        view.icons = new Image[n];
        view.titles = new TextMeshProUGUI[n];
        view.rules = new TextMeshProUGUI[n];
        view.progress = new TextMeshProUGUI[n];

        for (int i = 0; i < n; i++)
        {
            RectTransform row = UIFactory.Rect("Badge_" + Badges.Keys[i], content);
            UIFactory.Fill(row, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;

            HorizontalLayoutGroup line = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.padding = new RectOffset(28, 28, 26, 26);
            line.spacing = 26f;
            line.childAlignment = TextAnchor.MiddleLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;

            Medal(row, 108f, out view.rings[i], out view.icons[i]);
            view.icons[i].sprite = sprites[i];

            RectTransform text = UIFactory.Rect("Text", row);
            UIFactory.VerticalStack(text, 4f, false);
            UIFactory.Flexible(text.gameObject);

            view.titles[i] = UIFactory.Text("Title", text, Body, ThemeRole.TextPrimary,
                                            TextAlignmentOptions.Left, null, FontStyles.Bold);
            UIFactory.Size(view.titles[i].gameObject, 52f);

            view.rules[i] = UIFactory.Text("Rule", text, Caption, ThemeRole.TextSecondary,
                                           TextAlignmentOptions.TopLeft, null);
            view.rules[i].textWrappingMode = TextWrappingModes.Normal;
            LayoutElement ruleSize = view.rules[i].gameObject.AddComponent<LayoutElement>();
            ruleSize.minHeight = 76f;

            view.progress[i] = UIFactory.Text("Progress", text, Caption, ThemeRole.AccentText,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
            UIFactory.Size(view.progress[i].gameObject, 40f);
        }

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    /// <summary>Change the email address or the password - the website's settings, here.</summary>
    static GameObject BuildSecurityPage(Transform parent)
    {
        RectTransform rt = PageFrame(parent, "SecurityPage", out RectTransform content);
        AccountSecurityView view = rt.gameObject.AddComponent<AccountSecurityView>();

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "security.title",
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        view.currentEmailLabel = UIFactory.Text("Current", content, Secondary, ThemeRole.TextSecondary,
                                                TextAlignmentOptions.Left, null);
        UIFactory.Size(view.currentEmailLabel.gameObject, 52f);

        RectTransform email = UIFactory.Rect("EmailCard", content);
        UIFactory.Card(email, 34f, 18f);
        view.newEmailField = Field(email, "NewEmail", "security.newEmail");
        view.newEmailField.contentType = TMP_InputField.ContentType.EmailAddress;
        view.emailButton = FullWidthButton(email, "ChangeEmail", "security.changeEmail",
                                           ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        RectTransform password = UIFactory.Rect("PasswordCard", content);
        UIFactory.Card(password, 34f, 18f);
        view.currentPasswordField = Field(password, "Current", "security.currentPw");
        view.currentPasswordField.contentType = TMP_InputField.ContentType.Password;
        view.newPasswordField = Field(password, "New", "security.newPw");
        view.newPasswordField.contentType = TMP_InputField.ContentType.Password;
        view.againPasswordField = Field(password, "Again", "security.againPw");
        view.againPasswordField.contentType = TMP_InputField.ContentType.Password;
        view.passwordButton = FullWidthButton(password, "ChangePassword", "security.changePw",
                                              ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        view.statusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 96f);
        view.statusLabel.gameObject.SetActive(false);

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    /// <summary>The people this reader blocked: one row each, with its way back.</summary>
    static GameObject BuildBlockedPage(Transform parent)
    {
        RectTransform rt = PageFrame(parent, "BlockedPage", out RectTransform content);
        BlockedListView view = rt.gameObject.AddComponent<BlockedListView>();

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "blocked.title",
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        TextMeshProUGUI lede = UIFactory.Text("Lede", content, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "blocked.lede");
        lede.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(lede);

        RectTransform card = UIFactory.Rect("List", content);
        UIFactory.Card(card, 34f, 0f);

        view.emptyLabel = UIFactory.Text("Empty", card, Body, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Left, "blocked.none");
        UIFactory.Size(view.emptyLabel.gameObject, Tap);

        for (int i = 0; i < BlockedListView.MaxRows; i++)
        {
            RectTransform row = UIFactory.Rect("Row" + i, card);
            UIFactory.Size(row.gameObject, Tap);
            UIFactory.HorizontalStack(row, 18f, false);

            view.rowLabels[i] = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null);
            UIFactory.Flexible(view.rowLabels[i].gameObject);
            view.rowLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            view.rowLabels[i].overflowMode = TextOverflowModes.Ellipsis;

            RectTransform undo = UIFactory.Rect("Unblock", row);
            // 220 held "Unblock" and broke "Отблокирай" in two on the phone.
            UIFactory.Size(undo.gameObject, Tap, 300f).flexibleWidth = 0f;
            view.unblockButtons[i] = UIFactory.Button(undo, ThemeRole.Surface, 0f);
            TextMeshProUGUI undoLabel = UIFactory.Text("Label", undo, Body, ThemeRole.AccentText,
                                                       TextAlignmentOptions.Right,
                                                       "blocked.unblock", FontStyles.Bold);
            undoLabel.textWrappingMode = TextWrappingModes.NoWrap;
            UIFactory.Stretch(undoLabel.rectTransform);

            view.rows[i] = row.gameObject;
            row.gameObject.SetActive(false);
        }

        view.statusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 56f);
        view.statusLabel.gameObject.SetActive(false);

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    /// <summary>You → Send feedback: a message, what is sent with it, and Send.</summary>
    static GameObject BuildFeedbackPage(Transform parent)
    {
        RectTransform rt = PageFrame(parent, "FeedbackPage", out RectTransform content);
        FeedbackView view = rt.gameObject.AddComponent<FeedbackView>();

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "feedback.title",
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        TextMeshProUGUI lede = UIFactory.Text("Lede", content, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "feedback.lede");
        lede.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(lede);

        RectTransform card = UIFactory.Rect("Card", content);
        UIFactory.Card(card, 34f, 20f);

        // A paragraph, not a line: several lines tall, wrapping, and Enter is a
        // new line rather than a send.
        view.messageField = Field(card, "Message", "feedback.placeholder");
        UIFactory.Size(view.messageField.gameObject, 340f);
        view.messageField.lineType = TMP_InputField.LineType.MultiLineNewline;
        view.messageField.characterLimit = 2000;
        view.messageField.textComponent.textWrappingMode = TextWrappingModes.Normal;
        view.messageField.textComponent.alignment = TextAlignmentOptions.TopLeft;

        if (view.messageField.placeholder is TextMeshProUGUI hint)
        {
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.alignment = TextAlignmentOptions.TopLeft;
        }

        // No key: the view fills in the version and the phone model.
        view.attachedLabel = UIFactory.Text("Attached", card, Caption, ThemeRole.TextMuted,
                                            TextAlignmentOptions.TopLeft, null);
        view.attachedLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(view.attachedLabel);

        RowLabel(card, "feedback.reply", out RectTransform replyRow);
        view.replyToggle = UIFactory.Switch("Toggle", replyRow);
        view.replyRow = replyRow.gameObject;

        view.sendButton = FullWidthButton(card, "Send", "feedback.send",
                                          ThemeRole.Accent, ThemeRole.OnAccent);

        view.statusLabel = UIFactory.Text("Status", content, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 56f);
        view.statusLabel.gameObject.SetActive(false);

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    /// <summary>Delete the account: one button, then one confirmation.</summary>
    static void BuildDeleteCard(Transform content)
    {
        RectTransform card = Group(content, "DeleteCard");
        card.GetComponent<VerticalLayoutGroup>().spacing = 22f;
        DeleteAccountView view = card.gameObject.AddComponent<DeleteAccountView>();

        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Tap);
        view.deleteButton = UIFactory.Button(row, ThemeRole.Surface, 0f);

        // No key: the view writes "Delete my account" or "Delete what this phone
        // has sent" depending on who is signed in, and a key would overwrite it.
        view.deleteLabel = UIFactory.Text("Label", row, Body, ThemeRole.StatusPoor,
                                          TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Stretch(view.deleteLabel.rectTransform);

        RectTransform confirm = UIFactory.Rect("Confirm", card);
        UIFactory.VerticalStack(confirm, 16f, false);
        view.confirmGroup = confirm.gameObject;

        view.confirmText = UIFactory.Text("Text", confirm, Secondary, ThemeRole.TextSecondary,
                                          TextAlignmentOptions.TopLeft, null);
        view.confirmText.textWrappingMode = TextWrappingModes.Normal;
        LayoutElement confirmSize = view.confirmText.gameObject.AddComponent<LayoutElement>();
        confirmSize.minHeight = 120f;

        view.confirmButton = FullWidthButton(confirm, "Yes", "delete.yes",
                                             ThemeRole.StatusPoor, ThemeRole.OnAccent);
        view.cancelButton = FullWidthButton(confirm, "No", "delete.no",
                                            ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);
        view.cancelButton.gameObject.AddComponent<WayOut>();
        confirm.gameObject.SetActive(false);

        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        LayoutElement statusSize = view.statusLabel.gameObject.AddComponent<LayoutElement>();
        statusSize.minHeight = 60f;
        view.statusLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// A settings row. Every row is the same height and separated by a rule, which
    /// is the difference between a settings screen and a stack of controls that
    /// happen to be near each other.
    /// </summary>
    static TextMeshProUGUI RowLabel(Transform card, string key, out RectTransform row)
    {
        row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Row52);
        UIFactory.HorizontalStack(row, 22f, false).childAlignment = TextAnchor.MiddleLeft;
        RowRule(row);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Size(label.gameObject, Tap);
        UIFactory.Flexible(label.gameObject);
        return label;
    }

    /// <summary>An inset group: the sheet's second surface, 18px corners, 14 in.</summary>
    static RectTransform Inset(Transform parent, string name)
    {
        RectTransform group = UIFactory.Rect(name, parent);
        UIFactory.Fill(group, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        VerticalLayoutGroup stack = UIFactory.VerticalStack(group, 0f, false);
        stack.padding = new RectOffset(39, 39, 0, 0);
        return group;
    }

    /// <summary>The rule between two rows of a card.</summary>
    static void Divider(Transform card)
    {
        RectTransform rt = UIFactory.Rect("Divider", card);
        UIFactory.Size(rt.gameObject, 3f);
        UIFactory.Fill(rt, ThemeRole.Line).raycastTarget = false;
    }

    /// <summary>A row whose whole width is the control, for an action rather than a value.</summary>
    static Button ActionRow(Transform card, string key, ThemeRole role,
                            out TextMeshProUGUI label)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Row52);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);
        RowRule(row);

        label = UIFactory.Text("Label", row, Body, role, TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);

        return button;
    }

    static Button HelpRow(Transform card, string key, out TextMeshProUGUI label)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, Row52);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);
        RowRule(row);

        label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMax = new Vector2(-66f, 0f);

        Image chevron = Icon(row, AppIcon.ChevronRight, 44f, ThemeRole.TextMuted,
                             out RectTransform chevronRect);
        UIFactory.Corner(chevronRect, new Vector2(1f, 0.5f), new Vector2(44f, 44f), Vector2.zero);
        chevron.GetComponent<ThemedElement>().preserveAlpha = true;
        chevron.color = new Color(chevron.color.r, chevron.color.g, chevron.color.b, 0.6f);

        return button;
    }

    static Button SegmentHalf(Transform parent, string text, out Image background,
                              out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(text, parent);
        UIFactory.Size(rt.gameObject, 66f, 122f);

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
        RectTransform rt = PageFrame(parent, name, out RectTransform content);

        TextMeshProUGUI title = UIFactory.Text("Title", content, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, titleKey, FontStyles.Bold);
        UIFactory.Size(title.gameObject, 84f);

        TextMeshProUGUI body = UIFactory.Text("Body", content, Body, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, bodyKey);
        UIFactory.FitsItsText(body);

        return rt.gameObject;
    }

    // --------------------------------------------------------------------- nav

    /// <summary>
    /// The floating glass capsule from Daylight v2: 16px in from each side, 14px
    /// off the bottom, 64px tall, and a lens 52px tall that rides under the open
    /// tab. Returns the bar; the tabs go into it in WireTabs.
    ///
    /// Built before the sheets, so every sheet and door draws over it - a sheet
    /// comes up from the bottom edge of the glass and covers the bar, as the
    /// prototype's does, rather than stopping on top of it.
    /// </summary>
    static RectTransform BuildNav(Transform root)
    {
        RectTransform nav = UIFactory.Rect("BottomNav", root);
        Undo.RegisterCreatedObjectUndo(nav.gameObject, "Build App UI");
        UIFactory.Stretch(nav);

        RectTransform safe = UIFactory.Rect("Safe", nav);
        UIFactory.Stretch(safe);
        SafeAreaPad pad = safe.gameObject.AddComponent<SafeAreaPad>();
        pad.bottom = true;
        pad.left = true;
        pad.right = true;

        const float BarHeight = 177f;
        const float BarSide = 44f;
        const float BarFloat = 39f;

        RectTransform bar = UIFactory.Rect("Bar", safe);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(-BarSide * 2f, BarHeight);
        bar.anchoredPosition = new Vector2(0f, BarFloat);

        // The one shadow the system allows a control: this one has nothing to
        // stand on, and without it the glass reads as a hole in the map.
        RectTransform shade = UIFactory.Rect("Shadow", bar);
        UIFactory.Stretch(shade);
        shade.offsetMin = new Vector2(-40f, -40f - 28f);
        shade.offsetMax = new Vector2(40f, 40f - 28f);
        Image cast = UIFactory.Plain(shade, new Color(0.08f, 0.09f, 0.14f, 0.16f));
        cast.sprite = UISprites.PillShadow;
        cast.type = Image.Type.Sliced;
        cast.pixelsPerUnitMultiplier = 1f;
        cast.raycastTarget = false;

        // The glass, and the whole bar's target for taps and drags.
        RectTransform face = UIFactory.Rect("Glass", bar);
        UIFactory.Stretch(face);
        Image glass = UIFactory.Plain(face, ThemeManager.Get(ThemeRole.Glass), UIShape.Pill);
        glass.raycastTarget = true;

        RectTransform rimRect = UIFactory.Rect("Rim", bar);
        UIFactory.Stretch(rimRect);
        Image rim = UIFactory.Plain(rimRect, ThemeManager.Get(ThemeRole.GlassRim));
        rim.sprite = UISprites.PillOutline;
        rim.type = Image.Type.Sliced;
        rim.pixelsPerUnitMultiplier = 1f;
        rim.raycastTarget = false;
        rimRect.gameObject.AddComponent<PillShape>();

        // The lens. Its own shadow, fill and rim, all scaled together as it
        // stretches, so it reads as one piece of glass on top of another.
        RectTransform lens = UIFactory.Rect("Lens", bar);
        lens.anchorMin = new Vector2(0f, 0.5f);
        lens.anchorMax = new Vector2(0f, 0.5f);
        lens.pivot = new Vector2(0.5f, 0.5f);
        lens.sizeDelta = new Vector2(320f, 144f);
        lens.anchoredPosition = new Vector2(174f, 0f);

        RectTransform lensShade = UIFactory.Rect("Shadow", lens);
        UIFactory.Stretch(lensShade);
        lensShade.offsetMin = new Vector2(-34f, -34f - 8f);
        lensShade.offsetMax = new Vector2(34f, 34f - 8f);
        Image lensCast = UIFactory.Plain(lensShade, new Color(0f, 0f, 0f, 0.10f));
        lensCast.sprite = UISprites.PillShadow;
        lensCast.type = Image.Type.Sliced;
        lensCast.pixelsPerUnitMultiplier = 1f;
        lensCast.raycastTarget = false;

        RectTransform lensFace = UIFactory.Rect("Fill", lens);
        UIFactory.Stretch(lensFace);
        Image lensFill = UIFactory.Plain(lensFace, Color.white, UIShape.Pill);
        lensFill.raycastTarget = false;

        RectTransform lensEdge = UIFactory.Rect("Rim", lens);
        UIFactory.Stretch(lensEdge);
        Image lensRim = UIFactory.Plain(lensEdge, new Color(0f, 0f, 0f, 0.07f));
        lensRim.sprite = UISprites.PillOutline;
        lensRim.type = Image.Type.Sliced;
        lensRim.pixelsPerUnitMultiplier = 1f;
        lensRim.raycastTarget = false;
        lensEdge.gameObject.AddComponent<PillShape>();

        RectTransform row = UIFactory.Rect("Tabs", bar);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(14f, 14f);
        row.offsetMax = new Vector2(-14f, -14f);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        GlassTabBar glassBar = bar.gameObject.AddComponent<GlassTabBar>();
        glassBar.bar = bar;
        glassBar.lens = lens;
        glassBar.glass = glass;
        glassBar.rim = rim;
        glassBar.lensFill = lensFill;
        glassBar.lensRim = lensRim;
        glassBar.padding = 14f;

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

        GlassTabBar glass = navRow.GetComponentInParent<GlassTabBar>(true);

        tabs.mapPanel = mapPanel.gameObject;
        tabs.cameraPanel = cameraPanel.gameObject;
        tabs.browsePanel = browsePanel.gameObject;
        tabs.settingsPanel = settingsPanel.gameObject;
        tabs.bottomNav = Find(navRow.root, "BottomNav").gameObject;
        tabs.settingsTab = settingsTab;
        tabs.glass = glass;

        // The tabs are drawn, not pressed: the bar takes the press for all three,
        // so it can tell a tap from a slide. See GlassTabBar.
        tabs.mapLabel = null;
        tabs.cameraLabel = null;
        tabs.settingsLabel = null;
        tabs.mapIndicator = null;
        tabs.cameraIndicator = null;
        tabs.settingsIndicator = null;

        glass.navigation = tabs;
        glass.tabs = new[]
        {
            GlassTab(navRow, "MapTab", "tab.map", NavGlyph.Map),
            GlassTab(navRow, "CameraTab", "tab.camera", NavGlyph.Camera),
            GlassTab(navRow, "YouTab", "tab.you", NavGlyph.You)
        };

        tabs.mapIcon = glass.tabs[0].icon;
        tabs.cameraIcon = glass.tabs[1].icon;
        tabs.settingsIcon = glass.tabs[2].icon;

        EditorUtility.SetDirty(glass);
        EditorUtility.SetDirty(tabs);
        return tabs;
    }

    enum NavGlyph
    {
        Map,
        Camera,
        Settings,
        You
    }

    /// <summary>One tab in the glass bar: a 26px icon over a 12px label, centred.</summary>
    static GlassTabBar.Tab GlassTab(RectTransform row, string name, string key, NavGlyph glyph)
    {
        RectTransform cell = UIFactory.Rect(name, row);

        RectTransform content = UIFactory.Rect("Content", cell);
        UIFactory.Stretch(content);
        content.pivot = new Vector2(0.5f, 0.32f);

        RectTransform iconRect = UIFactory.Rect("Icon", content);
        UIFactory.Corner(iconRect, new Vector2(0.5f, 0.5f), new Vector2(72f, 72f),
                         new Vector2(0f, 20f));

        Graphic[] icon = DrawGlyph(iconRect, glyph);

        TextMeshProUGUI text = UIFactory.Text("Label", content, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Center, key, FontStyles.Bold);
        UIFactory.Corner(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300f, 40f),
                         new Vector2(0f, -38f));
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        // The bar colours these as the lens passes, so the theme must not.
        Unthemed(text.gameObject);

        return new GlassTabBar.Tab { root = content, icon = icon, label = text };
    }

    /// <summary>
    /// One rasterized sprite per tab: the folded map, the camera, the person.
    /// Still an array, because the You tab carries two - a person for a guest,
    /// initials for somebody with an account - and both are lit with the tab.
    /// </summary>
    static Graphic[] DrawGlyph(RectTransform holder, NavGlyph glyph)
    {
        if (glyph == NavGlyph.You)
        {
            // The person, for everybody. Initials in its place read as a stray
            // letter on the bar rather than as a face.
            Image person = Icon(holder, AppIcon.Person, 64f, ThemeRole.TextMuted);
            Unthemed(person.gameObject);
            person.raycastTarget = false;

            return new Graphic[] { person };
        }

        AppIcon icon = glyph == NavGlyph.Map ? AppIcon.MapFold
                     : glyph == NavGlyph.Camera ? AppIcon.Camera
                     : AppIcon.Settings;

        Image image = Icon(holder, icon, 70f, ThemeRole.TextMuted);
        Unthemed(image.gameObject); // the bar drives this colour
        image.raycastTarget = false;

        return new Graphic[] { image };
    }

    // ----------------------------------------------------------------- prefabs

    /// <summary>
    /// A pin: a 44px target around a 26 to 36px dot, a halo for the next stop, its
    /// number or a tick, and a glass name plate above it that only the next stop
    /// shows. MapPinView draws the state.
    /// </summary>
    static MapPinView CreateMapPinPrefab()
    {
        GameObject temp = new GameObject("MapPin", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;

        rt.sizeDelta = new Vector2(122f, 122f);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image hit = UIFactory.Plain(rt, new Color(1f, 1f, 1f, 0f), UIShape.Circle);

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = hit;
        UIFactory.Feedback(button);

        RectTransform halo = UIFactory.Rect("Halo", rt);
        UIFactory.Corner(halo, new Vector2(0.5f, 0.5f), new Vector2(128f, 128f), Vector2.zero);
        Image haloImage = UIFactory.Plain(halo, new Color(1f, 1f, 1f, 0.28f), UIShape.Circle);
        haloImage.raycastTarget = false;
        halo.gameObject.SetActive(false);

        RectTransform ring = UIFactory.Rect("Ring", rt);
        UIFactory.Corner(ring, new Vector2(0.5f, 0.5f), new Vector2(72f, 72f), Vector2.zero);
        Image ringImage = UIFactory.Plain(ring, Color.white, UIShape.Circle);
        ringImage.raycastTarget = false;

        RectTransform bulb = UIFactory.Rect("Bulb", rt);
        UIFactory.Corner(bulb, new Vector2(0.5f, 0.5f), new Vector2(56f, 56f), Vector2.zero);
        Image bulbImage = UIFactory.Plain(bulb, ThemeManager.Get(ThemeRole.Accent), UIShape.Circle);
        bulbImage.raycastTarget = false;

        TextMeshProUGUI number = UIFactory.Text("Number", rt, Caption, ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Corner(number.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(100f, 100f),
                         Vector2.zero);
        number.textWrappingMode = TextWrappingModes.NoWrap;
        number.text = string.Empty;
        Unthemed(number.gameObject);

        RectTransform tickRect = UIFactory.Rect("Tick", rt);
        UIFactory.Corner(tickRect, new Vector2(0.5f, 0.5f), new Vector2(36f, 36f), Vector2.zero);
        Image tick = UIFactory.Plain(tickRect, Color.white);
        tick.sprite = UIIcons.Get(AppIcon.Check);
        tick.preserveAspect = true;
        tick.raycastTarget = false;
        tickRect.gameObject.SetActive(false);

        // The name rides above the dot and never sits on it.
        RectTransform plate = UIFactory.Rect("Label", rt);
        plate.anchorMin = new Vector2(0.5f, 0.5f);
        plate.anchorMax = new Vector2(0.5f, 0.5f);
        plate.pivot = new Vector2(0.5f, 0f);
        plate.sizeDelta = new Vector2(0f, 75f);
        plate.anchoredPosition = new Vector2(0f, 55f);

        Image plateImage = UIFactory.Plain(plate, ThemeManager.Get(ThemeRole.Glass), UIShape.Pill);
        plateImage.raycastTarget = false;

        HorizontalLayoutGroup plateRow = UIFactory.HorizontalStack(plate, 0f);
        plateRow.padding = new RectOffset(28, 28, 0, 0);
        plateRow.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter plateFit = plate.gameObject.AddComponent<ContentSizeFitter>();
        plateFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI label = UIFactory.Text("Text", plate, Secondary, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, null, FontStyles.Bold);
        Unthemed(label.gameObject);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement labelSize = UIFactory.Size(label.gameObject, 75f);
        labelSize.preferredWidth = -1f;
        plate.gameObject.SetActive(false);

        MapPinView view = temp.AddComponent<MapPinView>();
        view.button = button;
        view.ring = ringImage;
        view.bulb = bulbImage;
        view.halo = haloImage;
        view.number = number;
        view.tick = tick;
        view.label = label;
        view.labelPlate = plateImage;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, MapPinPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<MapPinView>();
    }

    /// <summary>
    /// A pin, point down, in the creator's colour.
    ///
    /// It used to stand on a white ring on the ground; seen outdoors the ring
    /// read as clutter rather than as a place, and it went (2 Oct).
    /// </summary>
    static GameObject CreateMarkerPrefab()
    {
        GameObject temp = new GameObject("BuildingMarker");

        // --- the pin
        GameObject pin = new GameObject("Pin");
        pin.transform.SetParent(temp.transform, false);

        MeshFilter pinMesh = pin.AddComponent<MeshFilter>();
        pinMesh.sharedMesh = MarkerShapes.Save(
            MarkerShapes.Pin(0.24f, 1.0f), "MarkerPin");

        MeshRenderer pinRenderer = pin.AddComponent<MeshRenderer>();
        pinRenderer.sharedMaterial = MarkerShapes.Surface("MarkerBody", Color.white, 0.35f);

        // Generous hit area: the visual is about a metre and a half but the
        // collider is wider, because a marker 80 m away is only a few pixels of
        // screen to aim at.
        BoxCollider collider = temp.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.5f, 0f);
        collider.size = new Vector3(2f, 2f, 2f);

        MarkerView view = temp.AddComponent<MarkerView>();

        view.tintedRenderers = new[] { (Renderer)pinRenderer };
        view.baseScale = 1.5f;
        view.clampDistance = 60f;
        view.billboard = false;

        temp.AddComponent<TappableObject>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, MarkerPrefabPath);
        Object.DestroyImmediate(temp);

        return saved;
    }

    /// <summary>
    /// The shapes a creator can pick on the website (0054), keyed exactly as the
    /// database keys them. "default" and "pin" are both the pin, so a stop that
    /// predates shapes looks the way it always did.
    /// </summary>
    static List<MarkerSpawner.MarkerStyle> CreateShapeStyles(GameObject pin)
    {
        var styles = new List<MarkerSpawner.MarkerStyle>
        {
            new MarkerSpawner.MarkerStyle { key = "default", prefab = pin },
            new MarkerSpawner.MarkerStyle { key = "pin", prefab = pin },
        };

        foreach (string key in new[] { "sphere", "cone", "cube", "sign" })
            styles.Add(new MarkerSpawner.MarkerStyle { key = key, prefab = CreateShapePrefab(key) });

        return styles;
    }

    /// <summary>
    /// One shape, the same size as the pin.
    ///
    /// Sphere, cube and the sign's parts are Unity's own meshes; the cone is
    /// generated, like the pin. The part that carries the creator's colour is the
    /// one tinted: the ball, the cone, the box, the sign's frame. A sign's face
    /// stays white so its words read, and a sign turns to face the reader - the
    /// other shapes look the same from every side and do not need to.
    /// </summary>
    static GameObject CreateShapePrefab(string key)
    {
        GameObject temp = new GameObject("Marker_" + key);

        Material body = MarkerShapes.Surface("MarkerBody", Color.white, 0.35f);
        Material pale = MarkerShapes.Surface("MarkerPost", new Color(0.96f, 0.97f, 0.98f), 0.2f);

        Renderer tinted;
        BoxCollider collider = temp.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.5f, 0f);
        collider.size = new Vector3(2f, 2f, 2f);

        MarkerView view = temp.AddComponent<MarkerView>();
        view.baseScale = 1.5f;
        view.clampDistance = 60f;
        view.billboard = false;

        switch (key)
        {
            case "sphere":
                // Hovering a little, so it reads as a ball over a place rather
                // than one lying on the pavement.
                tinted = ShapePart(temp.transform, "Ball", Builtin("New-Sphere.fbx"), body,
                                   new Vector3(0f, 0.62f, 0f), Vector3.one * 0.6f);
                break;

            case "cone":
                tinted = ShapePart(temp.transform, "Cone",
                                   MarkerShapes.Save(MarkerShapes.Cone(0.32f, 0.95f), "MarkerCone"),
                                   body, Vector3.zero, Vector3.one);
                break;

            case "cube":
                tinted = ShapePart(temp.transform, "Box", Builtin("Cube.fbx"), body,
                                   new Vector3(0f, 0.3f, 0f), Vector3.one * 0.6f);
                break;

            default: // "sign"
                ShapePart(temp.transform, "Post", Builtin("New-Cylinder.fbx"), pale,
                          new Vector3(0f, 0.35f, 0f), new Vector3(0.06f, 0.35f, 0.06f));

                tinted = ShapePart(temp.transform, "Board", Builtin("Cube.fbx"), body,
                                   new Vector3(0f, 1.1f, 0f), new Vector3(1.3f, 0.78f, 0.06f));

                // The face, on the side that turns towards the reader: MarkerView
                // points a billboard's +Z away from the camera, and a quad faces -Z.
                ShapePart(temp.transform, "Face", Builtin("Quad.fbx"),
                          MarkerShapes.Surface("MarkerSignFace", Color.white, 0.1f),
                          new Vector3(0f, 1.1f, -0.032f), new Vector3(1.18f, 0.66f, 1f));

                GameObject words = new GameObject("Words");
                words.transform.SetParent(temp.transform, false);
                words.transform.localPosition = new Vector3(0f, 1.1f, -0.036f);

                TextMeshPro label = words.AddComponent<TextMeshPro>();
                TMP_FontAsset face = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/Fonts/Inter/Inter-SemiBold SDF.asset");
                if (face != null)
                    label.font = face;

                label.rectTransform.sizeDelta = new Vector2(1.06f, 0.56f);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.enableAutoSizing = true;
                label.fontSizeMin = 0.8f;
                label.fontSizeMax = 2.6f;
                label.color = new Color(0.11f, 0.11f, 0.12f);
                // Seen in the preview; on a phone the spawner writes the stop's own words.
                label.text = "AR Trail";

                view.signLabel = label;
                view.billboard = true;

                collider.center = new Vector3(0f, 0.9f, 0f);
                collider.size = new Vector3(2f, 2.4f, 1.2f);
                break;
        }

        view.tintedRenderers = new[] { tinted };
        temp.AddComponent<TappableObject>();

        string path = PrefabFolder + "/Marker" + char.ToUpperInvariant(key[0]) + key.Substring(1) + ".prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);

        return saved;
    }

    /// <summary>
    /// Light from every side, not only from the one sun.
    ///
    /// The scene took its ambient light from the skybox, and a skybox lights
    /// nothing until lighting is generated, which it never was: the ambient
    /// probe was all zeros. Outdoors every side of a marker the sun missed came
    /// out black, the creator's colour with it. MarkerPreview used to add its
    /// own ambient light, which is why the preview looked right and the street
    /// did not.
    ///
    /// The sun goes on the camera, shining from over the reader's shoulder. In
    /// a fixed direction it lit whichever side of a marker happened to face it,
    /// and somebody standing on the other side saw the creator's colour at half
    /// strength against a sunlit street. A marker is a sign, not a sculpture:
    /// the side being read is the side that gets the light.
    /// </summary>
    static void LightMarkers()
    {
        // One flat colour, because on a phone MarkerSpawner rebuilds the light
        // from it (see there) and a flat colour is exact to rebuild.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.60f);

        Light sun = null;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (light.type == LightType.Directional)
                sun = light;

        if (sun == null || Camera.main == null)
        {
            _notes.Add("No directional light or MainCamera - markers are lit by ambient light only.");
            return;
        }

        sun.transform.SetParent(Camera.main.transform, false);
        sun.transform.localPosition = Vector3.zero;
        sun.transform.localRotation = Quaternion.Euler(30f, 20f, 0f);
    }

    /// <summary>
    /// One of Unity's own meshes. "New-Sphere" and "New-Cylinder" are the ones
    /// GameObject.CreatePrimitive uses (1 m across); plain "Sphere.fbx" and
    /// "Cylinder.fbx" are older meshes twice that size, which made the ball
    /// twice its intended size and sat it on the ring.
    /// </summary>
    static Mesh Builtin(string file)
    {
        return Resources.GetBuiltinResource<Mesh>(file);
    }

    static Renderer ShapePart(Transform parent, string name, Mesh mesh, Material material,
                              Vector3 position, Vector3 scale)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;

        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        return renderer;
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



    /// <summary>
    /// The trail name at the top of the map, and the way to change trail.
    ///
    /// Returns the name label, because MapTabController still holds it as its
    /// title - it is the same piece of text doing a better job, not a new one.
    /// </summary>
    /// <summary>
    /// The trail pill: a glass capsule across the top of the map with the trail's
    /// name, a line saying what kind of trail it is and how it is going, and a
    /// chevron that says it opens the list.
    /// </summary>
    static TrailChipView BuildTrailChip(Transform parent, out RectTransform rect)
    {
        rect = UIFactory.Rect("TrailChip", parent);
        UIFactory.Size(rect.gameObject, 155f);
        LayoutElement width = rect.GetComponent<LayoutElement>();
        width.flexibleWidth = 1f;
        width.minWidth = ReferenceWidth - Gutter * 2f;

        TrailChipView view = rect.gameObject.AddComponent<TrailChipView>();
        view.button = GlassButton(rect, UIShape.Pill);
        rect.GetComponent<TapFeedback>().pressedScale = 0.98f;

        RectTransform row = UIFactory.Rect("Row", rect);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(50f, 0f);
        row.offsetMax = new Vector2(-22f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 28f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 3f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        // No localisation key: the view writes the trail's own name here.
        view.nameLabel = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.nameLabel.gameObject, 62f);
        view.nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;

        view.subLabel = UIFactory.Text("Sub", text, Secondary, ThemeRole.TextSecondary,
                                       TextAlignmentOptions.Left);
        UIFactory.Size(view.subLabel.gameObject, 52f);
        view.subLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.subLabel.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform chevronBox = UIFactory.Rect("Chevron", row);
        UIFactory.Size(chevronBox.gameObject, 111f, 111f).flexibleWidth = 0f;
        Icon(chevronBox, AppIcon.ChevronDown, 50f, ThemeRole.TextMuted);
        view.chevron = chevronBox.gameObject;

        return view;
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

        // The page's heading says what kind of thing this is - Watch, Listen - and
        // this says which one.
        //
        // Above the frame, not below it. One builder makes both pages, and with
        // the title after the screen in the stack they disagreed: on a recording
        // the frame is switched off, so the title came first and read as a
        // heading; on a video it came second, so the reader met an unlabelled
        // black rectangle and was told afterwards what they had been watching.
        // The name of the thing goes before the thing.
        TextMeshProUGUI title = UIFactory.Text("Title", page, Title, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, null,
                                               FontStyles.Bold);
        UIFactory.Size(title.gameObject, 70f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        view.titleLabel = title;

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
    /// <summary>
    /// The question, as a page in the stop sheet.
    ///
    ///   🕓 10 points · a wrong answer locks it for 5 minutes
    ///   How old was she when she started here?
    ///   ( ) 16   (•) 18   ( ) 21   ( ) 25          a radio list, 56px rows
    ///   [ ✓ Right · +10 points / the fact ]         or the lock, with its ring
    ///   ─────────────────────────────────
    ///   [💡 Hints]  [         Check          ]      or Next stop, or Explore
    /// </summary>
    static MarkerQuestionView BuildQuestionPage(RectTransform parent,
                                                AnswerOptionView optionPrefab)
    {
        RectTransform page = UIFactory.Rect("Question", parent);
        UIFactory.Stretch(page);

        MarkerQuestionView view = page.gameObject.AddComponent<MarkerQuestionView>();
        view.optionPrefab = optionPrefab;
        view.rightSprite = UIIcons.Get(AppIcon.Check);
        view.wrongSprite = UIIcons.Get(AppIcon.Close);
        view.infoSprite = UIIcons.Get(AppIcon.Info);

        RectTransform body = UIFactory.Rect("Body", page);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(0f, SheetFoot);

        RectTransform content = SheetScroll(body, null);
        view.scroll = body.GetComponentInChildren<ScrollRect>();

        // --- what it is worth and what a wrong answer costs, in one line
        RectTransform rule = UIFactory.Rect("Rule", content);
        HorizontalLayoutGroup ruleRow = UIFactory.HorizontalStack(rule, 17f);
        ruleRow.childAlignment = TextAnchor.UpperLeft;
        ruleRow.childControlHeight = true;
        view.ruleHolder = rule.gameObject;

        Icon(rule, AppIcon.Clock, 44f, ThemeRole.TextMuted);

        view.ruleLabel = UIFactory.Text("Label", rule, Secondary, ThemeRole.TextMuted,
                                        TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.ruleLabel);
        UIFactory.Flexible(view.ruleLabel.gameObject);
        rule.gameObject.SetActive(false);

        // --- the question
        view.promptLabel = UIFactory.Text("Prompt", content, Title, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.promptLabel);

        RectTransform options = UIFactory.Rect("Options", content);
        UIFactory.VerticalStack(options, 28f, false);
        view.optionsParent = options;

        // --- or a typed answer
        RectTransform open = UIFactory.Rect("Open", content);
        UIFactory.VerticalStack(open, 14f, false);
        view.openHolder = open.gameObject;

        view.openField = UIFactory.InputField("Field", open, "task.openHint",
                                              out TextMeshProUGUI placeholder);
        view.openPlaceholder = placeholder;

        ThemedElement openFill = view.openField.GetComponent<ThemedElement>();
        if (openFill != null)
        {
            openFill.role = ThemeRole.SurfaceAlt;
            openFill.Apply();
        }

        open.gameObject.SetActive(false);

        // --- what came back: a heading and a line, on the verdict's own wash
        RectTransform bubble = UIFactory.Rect("Bubble", content);
        view.bubbleFill = UIFactory.Plain(bubble, ThemeManager.Get(ThemeRole.SurfaceAlt), UIShape.Rounded);
        view.bubbleFill.raycastTarget = false;
        view.bubbleHolder = bubble.gameObject;

        HorizontalLayoutGroup bubbleRow = UIFactory.HorizontalStack(bubble, 33f);
        bubbleRow.padding = new RectOffset(39, 39, 39, 39);
        bubbleRow.childAlignment = TextAnchor.UpperLeft;
        bubbleRow.childControlHeight = true;
        ContentSizeFitter bubbleFit = bubble.gameObject.AddComponent<ContentSizeFitter>();
        bubbleFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        view.bubbleIcon = Icon(bubble, AppIcon.Check, 66f, ThemeRole.StatusGood);
        Unthemed(view.bubbleIcon.gameObject);

        RectTransform bubbleText = UIFactory.Rect("Text", bubble);
        UIFactory.VerticalStack(bubbleText, 6f, false);
        UIFactory.Flexible(bubbleText.gameObject);

        view.bubbleTitle = UIFactory.Text("Title", bubbleText, Body, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.bubbleTitle);
        Unthemed(view.bubbleTitle.gameObject);

        view.bubbleLabel = UIFactory.Text("Label", bubbleText, Secondary, ThemeRole.TextSecondary,
                                          TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.bubbleLabel);
        Unthemed(view.bubbleLabel.gameObject);
        bubble.gameObject.SetActive(false);

        // --- locked out: a ring that empties as the wait runs down
        RectTransform locked = UIFactory.Rect("Lock", content);
        UIFactory.Fill(locked, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        view.lockHolder = locked.gameObject;

        HorizontalLayoutGroup lockRow = UIFactory.HorizontalStack(locked, 33f);
        lockRow.padding = new RectOffset(39, 39, 39, 39);
        lockRow.childAlignment = TextAnchor.MiddleLeft;
        lockRow.childControlHeight = true;
        ContentSizeFitter lockFit = locked.gameObject.AddComponent<ContentSizeFitter>();
        lockFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform ring = UIFactory.Rect("Ring", locked);
        UIFactory.Size(ring.gameObject, 111f, 111f).flexibleWidth = 0f;
        Image track = UIFactory.Fill(ring, ThemeRole.FillStrong);
        track.sprite = UISprites.Ring;
        track.raycastTarget = false;

        RectTransform arc = UIFactory.Rect("Arc", ring);
        UIFactory.Stretch(arc);
        view.lockArc = UIFactory.Plain(arc, ThemeManager.Get(ThemeRole.BadDot));
        view.lockArc.sprite = UISprites.Ring;
        view.lockArc.type = Image.Type.Filled;
        view.lockArc.fillMethod = Image.FillMethod.Radial360;
        view.lockArc.fillOrigin = (int)Image.Origin360.Top;
        view.lockArc.fillClockwise = false;
        view.lockArc.raycastTarget = false;

        RectTransform lockText = UIFactory.Rect("Text", locked);
        UIFactory.VerticalStack(lockText, 6f, false);
        UIFactory.Flexible(lockText.gameObject);

        view.lockTitle = UIFactory.Text("Title", lockText, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.lockTitle);

        view.lockLabel = UIFactory.Text("Label", lockText, Secondary, ThemeRole.TextSecondary,
                                        TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.lockLabel);
        locked.gameObject.SetActive(false);

        // --- the running score, for the rule that moves it
        RectTransform score = UIFactory.Rect("Score", content);
        UIFactory.Size(score.gameObject, 56f);
        UIFactory.HorizontalStack(score, 10f);
        view.scoreHolder = score.gameObject;

        view.scoreLabel = UIFactory.Text("Label", score, Secondary, ThemeRole.AccentText,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.scoreLabel.gameObject, 56f);
        UIFactory.Flexible(view.scoreLabel.gameObject);
        score.gameObject.SetActive(false);

        // --- the footer: Hints and Check; then Next stop; or Explore while locked
        RectTransform footer = SheetFooter(page);

        RectTransform hints = UIFactory.Rect("Hints", footer);
        LayoutElement hintsSize = UIFactory.Size(hints.gameObject, Btn);
        hintsSize.flexibleWidth = 0f;
        view.hintsHolder = hints.gameObject;
        view.hintsButton = UIFactory.Button(hints, ThemeRole.Fill, 1f, UIShape.Pill);

        HorizontalLayoutGroup hintsRow = UIFactory.HorizontalStack(hints, 17f);
        hintsRow.padding = new RectOffset(55, 61, 0, 0);
        hintsRow.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter hintsFit = hints.gameObject.AddComponent<ContentSizeFitter>();
        hintsFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        Icon(hints, AppIcon.Bulb, 50f, ThemeRole.TextPrimary);

        view.hintsLabel = UIFactory.Text("Label", hints, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left, "hint.title");
        view.hintsLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.hintsLabel.raycastTarget = false;
        UIFactory.Size(view.hintsLabel.gameObject, Btn).preferredWidth = -1f;
        hints.gameObject.SetActive(false);

        RectTransform check = UIFactory.Rect("Check", footer);
        UIFactory.Size(check.gameObject, Btn);
        UIFactory.Flexible(check.gameObject);
        view.checkHolder = check.gameObject;
        view.checkButton = UIFactory.Button(check, ThemeRole.Accent, 1f, UIShape.Pill);

        view.checkLabel = UIFactory.Text("Label", check, Body, ThemeRole.OnAccent,
                                         TextAlignmentOptions.Center, "task.check");
        UIFactory.Stretch(view.checkLabel.rectTransform);
        view.checkLabel.raycastTarget = false;


        view.nextButton = WideButton(footer, "Next", ThemeRole.Accent, ThemeRole.OnAccent, "task.next",
                                     out view.nextLabel);
        view.nextButton.gameObject.SetActive(false);

        view.exploreButton = WideButton(footer, "Explore", ThemeRole.Fill, ThemeRole.TextPrimary,
                                        "task.explore", out view.exploreLabel);
        view.exploreButton.gameObject.SetActive(false);

        // --- one attempt, spent
        RectTransform finished = UIFactory.Rect("Finished", page);
        UIFactory.Stretch(finished);
        UIFactory.Fill(finished, ThemeRole.Surface).raycastTarget = true;
        view.finishedHolder = finished.gameObject;

        RectTransform finishedStack = UIFactory.Rect("Stack", finished);
        UIFactory.Stretch(finishedStack);
        finishedStack.offsetMin = new Vector2(55f, SheetFoot);
        finishedStack.offsetMax = new Vector2(-55f, -11f);
        VerticalLayoutGroup finishedLayout = UIFactory.VerticalStack(finishedStack, 33f, false);
        finishedLayout.childAlignment = TextAnchor.MiddleLeft;

        Icon(finishedStack, AppIcon.Block, 72f, ThemeRole.TextMuted);

        view.finishedTitle = UIFactory.Text("Title", finishedStack, Title, ThemeRole.TextPrimary,
                                            TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.finishedTitle);

        view.finishedBody = UIFactory.Text("Body", finishedStack, Body, ThemeRole.TextSecondary,
                                           TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.finishedBody);

        RectTransform finishedFooter = SheetFooter(finished);
        view.finishedButton = WideButton(finishedFooter, "Button", ThemeRole.Fill, ThemeRole.TextPrimary,
                                         "task.backToMap", out view.finishedButtonLabel);
        finished.gameObject.SetActive(false);

        page.gameObject.SetActive(false);
        return view;
    }

    /// <summary>A grey 50px pill with an icon before its word, sharing its row.</summary>
    static Button IconWideButton(Transform parent, string name, AppIcon icon, string key)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Btn);
        UIFactory.Flexible(rt.gameObject);
        Button button = UIFactory.Button(rt, ThemeRole.Fill, 1f, UIShape.Pill);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 22f);
        layout.childAlignment = TextAnchor.MiddleCenter;

        Icon(row, icon, 50f, ThemeRole.TextPrimary);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, key);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Size(label.gameObject, Btn).preferredWidth = -1f;
        return button;
    }

    /// <summary>A 50px pill that takes the rest of its row, with one label.</summary>
    static Button WideButton(Transform parent, string name, ThemeRole fill, ThemeRole ink, string key,
                             out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Btn);
        UIFactory.Flexible(rt.gameObject);
        Button button = UIFactory.Button(rt, fill, 1f, UIShape.Pill);

        label = UIFactory.Text("Label", rt, Body, ink, TextAlignmentOptions.Center, key);
        UIFactory.Stretch(label.rectTransform);
        label.raycastTarget = false;
        return button;
    }

    // ------------------------------------------------------------------ prefabs

    /// <summary>
    /// A trail in the list: its name, what kind of trail it is and how it is
    /// played, and how far along it is - a tick on the one being walked, and
    /// Leave at the end of the ones joined.
    /// </summary>
    static TrailRowView CreateTrailRowPrefab()
    {
        GameObject temp = new GameObject("TrailRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 210f);

        // As tall as its words: the line saying how a trail is played wraps, and
        // cutting it after three words cut off the part that explains anything.
        LayoutElement size = temp.AddComponent<LayoutElement>();
        size.minHeight = 177f;

        TrailRowView view = temp.AddComponent<TrailRowView>();
        view.button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 0f);
        TapFeedback feel = temp.GetComponent<TapFeedback>();
        if (feel != null)
            feel.pressedScale = 0.98f;

        RectTransform divider = UIFactory.Rect("Divider", rt);
        UIFactory.TopStretch(divider, 3f);
        UIFactory.Fill(divider, ThemeRole.Line).raycastTarget = false;
        Ignore(divider);
        view.divider = divider.gameObject;

        HorizontalLayoutGroup rowLayout = UIFactory.HorizontalStack(rt, 0f);
        rowLayout.padding = new RectOffset(0, 0, 33, 33);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Flexible(row.gameObject);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 28f);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 4f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        view.nameLabel = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left);
        UIFactory.Size(view.nameLabel.gameObject, 58f);
        view.nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;

        view.metaLabel = UIFactory.Text("Meta", text, Secondary, ThemeRole.TextMuted,
                                        TextAlignmentOptions.TopLeft);
        UIFactory.FitsItsText(view.metaLabel);

        view.progressLabel = UIFactory.Text("Progress", text, Secondary, ThemeRole.TextMuted,
                                            TextAlignmentOptions.Left);
        UIFactory.Size(view.progressLabel.gameObject, 50f);
        view.progressLabel.textWrappingMode = TextWrappingModes.NoWrap;

        Icon(row, AppIcon.Check, 61f, ThemeRole.AccentText, out RectTransform tickRect);
        view.currentMark = tickRect.gameObject;
        tickRect.gameObject.SetActive(false);

        // Leaving, at the far end of the row: a word, quiet until armed.
        RectTransform leave = UIFactory.Rect("Leave", row);
        UIFactory.Size(leave.gameObject, 122f, 160f).flexibleWidth = 0f;
        view.leaveButton = UIFactory.Button(leave, ThemeRole.Surface, 0f, UIShape.Pill);

        view.leaveLabel = UIFactory.Text("Label", leave, Caption, ThemeRole.TextMuted,
                                         TextAlignmentOptions.Center, "trails.leave");
        UIFactory.Stretch(view.leaveLabel.rectTransform);
        Unthemed(view.leaveLabel.gameObject);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, TrailRowPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<TrailRowView>();
    }

    static TrailGraphNodeView CreateGraphNodePrefab()
    {
        GameObject temp = new GameObject("TrailGraphNode", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(880f, 150f);

        TrailGraphNodeView view = temp.AddComponent<TrailGraphNodeView>();

        // The ring is a sibling of the disc, not a child of it.
        //
        // A child draws after its parent, so a "ring" inside the disc is a filled
        // circle painted straight over it - which is exactly what happened: every
        // node came out as a flat disc of the ring's colour, dark for some markers
        // and white-on-white for others. Behind it, and larger, is a halo.
        // The stop sits on the line, which runs through the middle of the row,
        // and its name reads off to the right the way a station's does.
        // The whole row is the control.
        //
        // The button was wired all along; the target was the numbered disc
        // alone - 88 units of an 880-unit row - with the stop's name, the part
        // anybody actually aims at, inert beside it. On a phone that reads as a
        // list that does not respond. First child, so it sits under every visual
        // and is invisible; the labels are not raycast targets, so nothing
        // fights it for the press.
        RectTransform hit = UIFactory.Rect("Hit", rt);
        UIFactory.Stretch(hit);

        RectTransform ring = UIFactory.Rect("Ring", rt);
        UIFactory.Corner(ring, new Vector2(0.5f, 0.5f), new Vector2(102f, 102f),
                         new Vector2(-330f, 0f));
        view.ring = UIFactory.Plain(ring, Color.white, UIShape.Circle);
        view.ring.raycastTarget = false;

        RectTransform disc = UIFactory.Rect("Disc", rt);
        UIFactory.Corner(disc, new Vector2(0.5f, 0.5f), new Vector2(88f, 88f),
                         new Vector2(-330f, 0f));
        view.disc = UIFactory.Fill(disc, ThemeRole.Surface, 1f, UIShape.Circle);
        view.button = UIFactory.Button(hit, ThemeRole.Surface, 0.001f);

        RectTransform fill = UIFactory.Rect("Fill", disc);
        UIFactory.Stretch(fill);
        view.fill = UIFactory.Plain(fill, Color.white, UIShape.Circle);
        view.fill.raycastTarget = false;
        fill.gameObject.SetActive(false);

        view.numberLabel = UIFactory.Text("Number", disc, Secondary, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.numberLabel.rectTransform);

        view.nameLabel = UIFactory.Text("Name", rt, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Corner(view.nameLabel.rectTransform, new Vector2(0.5f, 0.5f),
                         new Vector2(500f, 64f), new Vector2(-10f, 20f));
        view.nameLabel.textWrappingMode = TextWrappingModes.Normal;
        view.nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        view.nameLabel.raycastTarget = false;

        // Right-aligned at the end of the row, the way a fare or a platform
        // number sits at the end of a station's line.
        view.pointsLabel = UIFactory.Text("Points", rt, Secondary, ThemeRole.AccentText,
                                          TextAlignmentOptions.Right, null, FontStyles.Bold);
        UIFactory.Corner(view.pointsLabel.rectTransform, new Vector2(0.5f, 0.5f),
                         new Vector2(180f, 50f), new Vector2(350f, 0f));
        view.pointsLabel.raycastTarget = false;

        view.distanceLabel = UIFactory.Text("Distance", rt, Caption, ThemeRole.TextMuted,
                                            TextAlignmentOptions.Left);
        UIFactory.Corner(view.distanceLabel.rectTransform, new Vector2(0.5f, 0.5f),
                         new Vector2(500f, 38f), new Vector2(-10f, -24f));
        view.distanceLabel.raycastTarget = false;

        // Last, so nothing is drawn over the press.
        //
        // It was built first to keep it under the visuals, which is right for
        // drawing and wrong for input: a Canvas gives the press to the frontmost
        // graphic under the finger, so the disc kept its own tap and the rest of
        // the row went to whatever was above. Invisible at this alpha either way.
        hit.SetAsLastSibling();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, GraphNodePrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<TrailGraphNodeView>();
    }

    /// <summary>
    /// A row of the stop's inset group: a 36px box with the icon in it, the
    /// thing's name, what kind of thing it is under that, and a chevron. A
    /// hairline above every row but the first.
    /// </summary>
    static PieceRowView CreatePieceRowPrefab()
    {
        GameObject temp = new GameObject("PieceRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 155f);
        UIFactory.Size(temp, 155f);

        PieceRowView view = temp.AddComponent<PieceRowView>();

        // Pressable across the whole row, drawn as nothing: the group is the surface.
        view.button = UIFactory.Button(rt, ThemeRole.SurfaceAlt, 0f);
        TapFeedback feel = temp.GetComponent<TapFeedback>();
        if (feel != null)
        {
            feel.disabledAlpha = 1f;
            feel.pressedScale = 0.98f;
        }

        RectTransform divider = UIFactory.Rect("Divider", rt);
        UIFactory.TopStretch(divider, 3f);
        UIFactory.Fill(divider, ThemeRole.Line).raycastTarget = false;
        view.divider = divider.gameObject;

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 33f);
        layout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform box = UIFactory.Rect("Box", row);
        UIFactory.Size(box.gameObject, 100f, 100f).flexibleWidth = 0f;
        view.box = UIFactory.Fill(box, ThemeRole.Surface, 1f, UIShape.Small);
        view.box.raycastTarget = false;

        RectTransform glyph = UIFactory.Rect("Icon", box);
        UIFactory.Corner(glyph, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f), Vector2.zero);
        view.icon = UIFactory.Fill(glyph, ThemeRole.AccentText);
        view.icon.sprite = UIIcons.Get(AppIcon.Play);
        view.icon.preserveAspect = true;
        view.icon.raycastTarget = false;

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.VerticalStack(text, 6f, false).childAlignment = TextAnchor.MiddleLeft;
        UIFactory.Flexible(text.gameObject);

        view.titleLabel = UIFactory.Text("Title", text, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.Left);
        UIFactory.Size(view.titleLabel.gameObject, 58f);
        view.titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.titleLabel.overflowMode = TextOverflowModes.Ellipsis;

        view.caption = UIFactory.Text("Caption", text, Secondary, ThemeRole.TextMuted,
                                      TextAlignmentOptions.Left);
        UIFactory.Size(view.caption.gameObject, 50f);
        view.caption.textWrappingMode = TextWrappingModes.NoWrap;
        view.caption.overflowMode = TextOverflowModes.Ellipsis;

        Image chevron = Icon(row, AppIcon.ChevronRight, 44f, ThemeRole.TextMuted, out RectTransform chev);
        chevron.color = new Color(chevron.color.r, chevron.color.g, chevron.color.b, 0.6f);
        view.chevron = chev.gameObject;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PieceRowPrefabPath);
        Object.DestroyImmediate(temp);

        return saved.GetComponent<PieceRowView>();
    }

    /// <summary>
    /// An answer: a 56px row with a hairline round it, a 22px radio, and the
    /// words. AnswerOptionView paints the five states.
    /// </summary>
    static AnswerOptionView CreateOptionPrefab()
    {
        GameObject temp = new GameObject("AnswerOption", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 155f);
        LayoutElement size = UIFactory.Size(temp, 155f);
        size.minHeight = 155f;

        AnswerOptionView view = temp.AddComponent<AnswerOptionView>();
        view.tickSprite = UIIcons.Get(AppIcon.Check);
        view.crossSprite = UIIcons.Get(AppIcon.Close);

        // Plain, not Fill: the view paints these from the state, and a
        // ThemedElement would paint over it on every theme event.
        view.background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Rounded);

        RectTransform edge = UIFactory.Rect("Edge", rt);
        UIFactory.Stretch(edge);
        view.edge = UIFactory.Plain(edge, ThemeManager.Get(ThemeRole.Line));
        view.edge.sprite = UISprites.RoundedOutline;
        view.edge.type = Image.Type.Sliced;
        view.edge.pixelsPerUnitMultiplier = 1f;
        view.edge.raycastTarget = false;

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = view.background;
        TapFeedback feel = UIFactory.Feedback(button);
        feel.disabledAlpha = 1f;
        feel.pressedScale = 0.98f;
        view.button = button;

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 39f);
        layout.padding = new RectOffset(44, 44, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        view.fade = row.gameObject.AddComponent<CanvasGroup>();

        RectTransform mark = UIFactory.Rect("Radio", row);
        UIFactory.Size(mark.gameObject, 61f, 61f).flexibleWidth = 0f;
        view.radio = UIFactory.Plain(mark, ThemeManager.Get(ThemeRole.FillStrong), UIShape.Circle);
        view.radio.raycastTarget = false;

        view.radioHole = UIFactory.Rect("Hole", mark);
        UIFactory.Corner(view.radioHole, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f), Vector2.zero);
        view.radioHoleImage = UIFactory.Plain(view.radioHole, ThemeManager.Get(ThemeRole.Surface), UIShape.Circle);
        view.radioHoleImage.raycastTarget = false;

        RectTransform tick = UIFactory.Rect("Tick", mark);
        UIFactory.Corner(tick, new Vector2(0.5f, 0.5f), new Vector2(36f, 36f), Vector2.zero);
        view.radioTick = UIFactory.Plain(tick, Color.white);
        view.radioTick.sprite = UIIcons.Get(AppIcon.Check);
        view.radioTick.preserveAspect = true;
        view.radioTick.raycastTarget = false;
        tick.gameObject.SetActive(false);

        RectTransform labelRect = UIFactory.Rect("Label", row);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = Body;
        label.alignment = TextAlignmentOptions.Left;
        label.color = ThemeManager.Get(ThemeRole.TextPrimary);
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        UIFactory.Size(labelRect.gameObject, 155f);
        UIFactory.Flexible(labelRect.gameObject);
        view.label = label;

        RectTransform verdict = UIFactory.Rect("Verdict", row);
        LayoutElement verdictSize = UIFactory.Size(verdict.gameObject, 50f, 50f);
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
                               TrailRowView trailRowPrefab, GameObject mapChrome,
                               MapView mapView)
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
        flow.teamPanel = BuildTeamDoor(holder, flow, out TeamDoorView teamDoor);
        flow.accountPanel = BuildAccountDoor(holder, flow);
        flow.readyPanel = BuildReadyDoor(holder, flow);
        flow.consentPanel = BuildConsentDoor(holder, flow, out ConsentDoorView consentView);
        flow.previewPanel = BuildTrailPreview(holder, flow, out TrailPreviewView previewView);
        flow.preview = previewView;
        flow.consent = consentView;
        flow.coach = BuildCoach(holder, mapChrome, mapView);
        holder.gameObject.AddComponent<JoinLinks>();

        flow.trails = trails;
        flow.code = code;
        flow.team = teamDoor;

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

        TextMeshProUGUI brand = UIFactory.Text("Brand", inner, 34f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, "app.name",
                                               FontStyles.Bold);
        UIFactory.Corner(brand.rectTransform, new Vector2(0f, 1f), new Vector2(620f, 48f),
                         Vector2.zero);

        WelcomeMarkers(inner);

        // Bottom-aligned. The reader's thumb is at the bottom of the phone and the
        // words are the last thing they read before pressing something.
        // Half the screen, not a third. A plain screen with two sentences at the
        // very bottom and nine hundred pixels of nothing above them reads as a
        // screen that failed to load rather than one that is deliberately quiet.
        RectTransform words = UIFactory.Rect("Words", inner);
        UIFactory.BottomStretch(words, 620f, Tap * 2f + 14f + 60f);
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

        // Two doors, both open.
        //
        // A code was the quiet line under the loud button, which is the wrong way
        // round for every organised walk: on the day of a test or a school trip
        // everybody arrives already holding one. It is the wrong way round the
        // other way too - most people, most of the time, will not have a code and
        // should not be made to feel they are missing something.
        //
        // So neither is quiet. Side by side, equal width, and the two of them read
        // as a question with two answers rather than an instruction with a way to
        // disobey it.
        // One filled capsule and one quiet one, stacked. Trying it is what most
        // people have come to do; the code is the answer for the ones who were
        // handed one, and it is still a full-width target a thumb can find.
        RectTransform ways = UIFactory.Rect("Ways", inner);
        UIFactory.BottomStretch(ways, Tap * 2f + 14f);
        UIFactory.VerticalStack(ways, 14f, false);

        RectTransform start = UIFactory.Rect("Start", ways);
        UIFactory.Size(start.gameObject, Tap);
        Button startButton = UIFactory.Button(start, ThemeRole.Accent, 1f, UIShape.Pill);
        Wire(startButton.onClick, flow.PassWelcome, "Welcome start");

        TextMeshProUGUI startLabel = UIFactory.Text("Label", start, Body, ThemeRole.OnAccent,
                                                    TextAlignmentOptions.Center, "welcome.start",
                                                    FontStyles.Bold);
        UIFactory.Stretch(startLabel.rectTransform);
        startLabel.raycastTarget = false;

        RectTransform code = UIFactory.Rect("HaveCode", ways);
        UIFactory.Size(code.gameObject, Tap);
        Button codeButton = UIFactory.Button(code, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
        Wire(codeButton.onClick, flow.PassWelcomeWithCode, "Welcome code");

        TextMeshProUGUI codeLabel = UIFactory.Text("Label", code, Body, ThemeRole.AccentText,
                                                   TextAlignmentOptions.Center,
                                                   "welcome.haveCode", FontStyles.Bold);
        UIFactory.Stretch(codeLabel.rectTransform);
        codeLabel.raycastTarget = false;

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// A short walk, drawn in the empty half of the welcome screen.
    ///
    /// It was three rings scattered at three sizes, which was honest about what a
    /// marker looks like and said nothing about what the app is. The app is not
    /// markers; it is markers in an order, joined up, walked between - and the
    /// screen that says "markers left in real places" can show that in the space
    /// it already had rather than decorating it.
    ///
    /// So: five stops, numbered, joined by a line, crossing the width rather
    /// than running down one edge - a trail through a town does not stay in the
    /// left-hand column. The same shape the progress
    /// screen draws, which is what the reader meets a few minutes later. It
    /// meanders rather than running straight, because a straight line of four
    /// equal circles is a progress bar and this is a walk through a town.
    ///
    /// Faint on purpose. It is the ground the words stand on, and anything bright
    /// enough to compete with a headline in an eighty-eight point face would be
    /// the first thing read on a screen whose whole job is one sentence.
    /// </summary>
    static void WelcomeMarkers(RectTransform inner)
    {
        const float FieldW = 880f;
        const float FieldH = 940f;
        const float Stop = 76f;

        RectTransform field = UIFactory.Rect("Walk", inner);
        field.anchorMin = new Vector2(0f, 1f);
        field.anchorMax = new Vector2(0f, 1f);
        field.pivot = new Vector2(0f, 1f);
        field.sizeDelta = new Vector2(FieldW, FieldH);
        field.anchoredPosition = new Vector2(0f, -210f);

        // Where each stop sits, from the top-left of the field. Read bottom to
        // top: the walk climbs away from the words rather than towards them.
        Vector2[] stops =
        {
            new Vector2( 88f, -858f),
            new Vector2(336f, -726f),
            new Vector2(238f, -498f),
            new Vector2(566f, -382f),
            new Vector2(778f, -150f)
        };

        // The line first, so the stops sit on top of their own joins rather than
        // having a segment drawn across their faces.
        for (int i = 0; i < stops.Length - 1; i++)
        {
            Vector2 from = stops[i];
            Vector2 to = stops[i + 1];
            Vector2 span = to - from;

            RectTransform leg = UIFactory.Rect("Leg" + i, field);
            leg.anchorMin = new Vector2(0f, 1f);
            leg.anchorMax = new Vector2(0f, 1f);
            leg.pivot = new Vector2(0f, 0.5f);
            leg.sizeDelta = new Vector2(span.magnitude, 5f);
            leg.anchoredPosition = from;
            leg.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            UIFactory.Fill(leg, ThemeRole.Accent, 0.20f, UIShape.Pill).raycastTarget = false;
        }

        for (int i = 0; i < stops.Length; i++)
        {
            RectTransform pin = UIFactory.Rect("Stop" + i, field);
            pin.anchorMin = new Vector2(0f, 1f);
            pin.anchorMax = new Vector2(0f, 1f);
            pin.pivot = new Vector2(0.5f, 0.5f);
            pin.sizeDelta = new Vector2(Stop, Stop);
            pin.anchoredPosition = stops[i];

            // A disc in the background colour under the ring, so the leg does not
            // show through the middle of a stop.
            UIFactory.Fill(pin, ThemeRole.Background, 1f, UIShape.Circle).raycastTarget = false;

            RectTransform ring = UIFactory.Rect("Ring", pin);
            UIFactory.Stretch(ring);
            UIFactory.Fill(ring, ThemeRole.Accent, 0.30f, UIShape.Ring).raycastTarget = false;

            // Numbered, because the order is the point. Without them this is four
            // circles and a squiggle.
            TextMeshProUGUI number = UIFactory.Text("Number", pin, Secondary, ThemeRole.AccentText,
                                                    TextAlignmentOptions.Center, null,
                                                    FontStyles.Bold);
            UIFactory.Stretch(number.rectTransform);
            number.text = (i + 1).ToString();
            number.alpha = 0.55f;
            number.raycastTarget = false;
        }
    }

    /// <summary>
    /// The terms. Four short sections, scrolling, with the button outside the scroll
    /// so it is always where the reader left it.
    ///
    /// Not dressed up. A non-skippable screen nobody reads is a dark pattern with
    /// extra steps, and the answer to that is short sentences, not illustration.
    /// </summary>
    /// <summary>The height of the age question under the terms, pills and all.</summary>
    // Question, pills, and room for a two-line refusal under them - at 200 the
    // "13 and older" sentence sat behind the Accept button, found on a phone.
    const float AgeBlock = 290f;

    /// <summary>For a label its view writes itself: a LocalizedLabel would rewrite it on enable.</summary>
    static void DropLocalizer(TextMeshProUGUI label)
    {
        LocalizedLabel localizer = label != null ? label.GetComponent<LocalizedLabel>() : null;

        if (localizer != null)
            Object.DestroyImmediate(localizer);
    }

    static Button AgePill(Transform parent, string name, string key, out Image fill,
                          out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Tap);
        UIFactory.Flexible(rt.gameObject);

        // Plain rather than themed: AgeChoiceView paints the chosen one in the
        // accent, and a themed image would repaint it grey on the next theme swap.
        fill = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.SurfaceAlt), UIShape.Pill);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;
        UIFactory.Feedback(button);

        // Three to a row, and "12 или по-малко" is the longest of them: shrinks
        // rather than wraps.
        label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Center, key, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);
        label.enableAutoSizing = true;
        label.fontSizeMax = Secondary;
        label.fontSizeMin = Caption;
        label.textWrappingMode = TextWrappingModes.NoWrap;

        // AgeChoiceView colours the label with the pill - white on the chosen
        // one. A themed label would put it back to the plain ink on the next
        // theme change and leave dark text on the accent.
        ThemedElement themed = label.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);

        return button;
    }

    /// <summary>
    /// Under 14 and no parent has agreed: ask for a parent's email, then wait.
    /// </summary>
    static GameObject BuildConsentDoor(Transform parent, DoorFlow flow, out ConsentDoorView made)
    {
        RectTransform panel = Door(parent, "Consent", plain: false, inner: out RectTransform inner);

        ConsentDoorView view = panel.gameObject.AddComponent<ConsentDoorView>();
        made = view;

        RectTransform card = Card(inner, "Card", 22f);

        // Typed into, so it moves out of the keyboard's way like the code card.
        KeyboardAvoider avoider = card.gameObject.AddComponent<KeyboardAvoider>();
        avoider.target = card;

        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.VerticalStack(head, 8f, false);

        UIFactory.Text("Title", head, Display, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "consent.title", FontStyles.Bold);

        UIFactory.Text("Body", head, Secondary, ThemeRole.TextSecondary,
                       TextAlignmentOptions.TopLeft, "consent.body");

        // --- asking
        RectTransform ask = UIFactory.Rect("Ask", card);
        UIFactory.VerticalStack(ask, 14f, false);
        view.askGroup = ask.gameObject;

        view.emailField = Field(ask, "ParentEmail", "consent.emailHint");
        view.emailField.contentType = TMP_InputField.ContentType.EmailAddress;

        view.sendButton = FullWidthButton(ask, "Send", "consent.send",
                                          ThemeRole.Accent, ThemeRole.OnAccent);

        // Or the parent started it: a code from their parent account.
        TextMeshProUGUI or = UIFactory.Text("Or", ask, Caption, ThemeRole.TextMuted,
                                            TextAlignmentOptions.Left, "consent.orCode");
        or.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(or);

        view.codeField = Field(ask, "ParentCode", "consent.codeHint");
        view.codeField.characterLimit = 12;

        view.codeButton = FullWidthButton(ask, "UseCode", "consent.useCode",
                                          ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // --- waiting
        RectTransform wait = UIFactory.Rect("Wait", card);
        UIFactory.VerticalStack(wait, 14f, false);
        view.waitGroup = wait.gameObject;

        view.waitLabel = UIFactory.Text("Waiting", wait, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.TopLeft, null);
        view.waitLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.FitsItsText(view.waitLabel);

        view.checkButton = FullWidthButton(wait, "Check", "consent.check",
                                           ThemeRole.Accent, ThemeRole.OnAccent);
        view.changeButton = FullWidthButton(wait, "Change", "consent.change",
                                            ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);
        wait.gameObject.SetActive(false);

        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left, null);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 56f);
        view.statusLabel.gameObject.SetActive(false);

        // The way on that needs nobody's permission: the demo, which sends
        // nothing anywhere. No "I'm actually 14" - that was a one-tap way past
        // the parent, and the server no longer takes it (0051).
        Button demo = QuietAction(card, "Demo", "consent.demo", flow.StartDemo, "consent demo");
        demo.gameObject.AddComponent<WayOut>();

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    static GameObject BuildTerms(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Terms", plain: true, inner: out RectTransform inner);

        TextMeshProUGUI title = UIFactory.Text("Title", inner, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.TopLeft, "terms.title",
                                               FontStyles.Bold);
        UIFactory.TopStretch(title.rectTransform, 90f);

        RectTransform scrollHolder = UIFactory.Rect("Body", inner);
        UIFactory.Stretch(scrollHolder);
        scrollHolder.offsetMin = new Vector2(0f, Tap + 34f + AgeBlock);
        scrollHolder.offsetMax = new Vector2(0f, -124f);

        RectTransform content = UIFactory.ScrollList("Scroll", scrollHolder, 30f);

        for (int i = 1; i <= 4; i++)
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

        // How old - asked here because it decides what the terms allow. Two
        // pills, nothing preselected; Accept will not go through without one.
        RectTransform age = UIFactory.Rect("Age", inner);
        UIFactory.BottomStretch(age, AgeBlock - 16f, Tap + 16f);
        UIFactory.VerticalStack(age, 10f, false);
        AgeChoiceView ageView = age.gameObject.AddComponent<AgeChoiceView>();
        flow.termsAge = ageView;

        TextMeshProUGUI ageQuestion = UIFactory.Text("Question", age, Body, ThemeRole.TextPrimary,
                                                     TextAlignmentOptions.Left, "age.question",
                                                     FontStyles.Bold);
        UIFactory.Size(ageQuestion.gameObject, 52f);

        RectTransform pills = UIFactory.Rect("Pills", age);
        UIFactory.Size(pills.gameObject, Tap);
        UIFactory.HorizontalStack(pills, 14f, true);

        ageView.olderButton = AgePill(pills, "Older", "age.older", out ageView.olderFill,
                                      out ageView.olderLabel);
        ageView.thirteenButton = AgePill(pills, "Thirteen", "age.thirteen", out ageView.thirteenFill,
                                         out ageView.thirteenLabel);
        ageView.youngerButton = AgePill(pills, "Younger", "age.younger", out ageView.youngerFill,
                                        out ageView.youngerLabel);

        // No key: the view writes "choose one" or "13 and older" as it needs.
        ageView.nagLabel = UIFactory.Text("Nag", age, Caption, ThemeRole.StatusPoor,
                                          TextAlignmentOptions.Left, null);
        ageView.nagLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.Size(ageView.nagLabel.gameObject, 84f);
        ageView.nagLabel.gameObject.SetActive(false);

        RectTransform accept = UIFactory.Rect("Accept", inner);
        UIFactory.BottomStretch(accept, Tap);
        Button acceptButton = UIFactory.Button(accept, ThemeRole.Accent, 1f, UIShape.Rounded);
        Wire(acceptButton.onClick, flow.AcceptTerms, "Accept terms");
        ageView.acceptButton = accept.gameObject;

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
    /// <summary>
    /// The offer of an account, straight after the first tutorial.
    ///
    /// The same account form Settings has, so there is one of it to keep right,
    /// on a card pinned to the top of the screen - the form is the thing people
    /// type into, and the top half is the half the keyboard leaves alone.
    /// </summary>
    static GameObject BuildAccountDoor(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Account", plain: false, inner: out RectTransform inner);

        RectTransform card = Card(inner, "Card", 22f);

        // Top, not bottom. Every other card rests on the bottom of the screen
        // because nothing on it is typed into.
        //
        // And the whole height, with the form scrolling under the title. The
        // line-of-work choices (Task 14) made the form taller than a phone; the
        // guest warning was already cutting its own "Continue as guest" off at
        // the bottom of the screen before they were added.
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(1f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.offsetMin = Vector2.zero;
        card.offsetMax = Vector2.zero;
        ContentSizeFitter cardFit = card.GetComponent<ContentSizeFitter>();
        if (cardFit != null)
            Object.DestroyImmediate(cardFit);

        // The same door is the guest warning, so DoorFlow writes its title, its
        // guest line and Later's words itself.
        flow.accountTitle = SheetTitle(card, "accountDoor.title");
        DropLocalizer(flow.accountTitle);

        RectTransform form = UIFactory.ScrollList("Scroll", card, 22f);
        LayoutElement formSize = form.parent.parent.gameObject.AddComponent<LayoutElement>();
        formSize.minHeight = 0f;
        formSize.flexibleHeight = 1f;
        form.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 0, 66);

        TextMeshProUGUI guestLine = UIFactory.Text("GuestNote", form, Secondary, ThemeRole.TextSecondary,
                                                   TextAlignmentOptions.TopLeft, "guest.body");
        UIFactory.FitsItsText(guestLine);
        guestLine.raycastTarget = false;
        flow.guestNote = guestLine.gameObject;
        guestLine.gameObject.SetActive(false);

        BuildAccountCard(form, label: false);

        Button later = QuietAction(form, "Later", "trails.notNow", flow.AccountDone,
                                   "account later");
        later.gameObject.AddComponent<WayOut>();
        flow.accountLaterLabel = later.GetComponentInChildren<TextMeshProUGUI>();
        DropLocalizer(flow.accountLaterLabel);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// The end of the demo. A moment of "done", then the two ways into a real
    /// trail - the library first, because most people will not have a code.
    /// </summary>
    static GameObject BuildReadyDoor(Transform parent, DoorFlow flow)
    {
        RectTransform panel = Door(parent, "Ready", plain: false, inner: out RectTransform inner);

        RectTransform card = Card(inner, "Card", 22f);

        RectTransform badgeRow = UIFactory.Rect("Badge", card);
        UIFactory.Size(badgeRow.gameObject, 132f);
        HorizontalLayoutGroup badgeLayout = UIFactory.HorizontalStack(badgeRow, 0f);
        badgeLayout.childAlignment = TextAnchor.MiddleLeft;

        RectTransform badge = UIFactory.Rect("Disc", badgeRow);
        LayoutElement badgeSize = UIFactory.Size(badge.gameObject, 132f, 132f);
        badgeSize.flexibleWidth = 0f;
        UIFactory.Fill(badge, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;
        Icon(badge, AppIcon.Check, 64f, ThemeRole.OnAccent);

        RectTransform head = UIFactory.Rect("Head", card);
        UIFactory.VerticalStack(head, 10f, false);
        UIFactory.Text("Title", head, Display, ThemeRole.TextPrimary,
                       TextAlignmentOptions.TopLeft, "ready.title", FontStyles.Bold);
        TextMeshProUGUI body = UIFactory.Text("Body", head, Secondary, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, "ready.body");
        UIFactory.FitsItsText(body);

        RectTransform actions = UIFactory.Rect("Actions", card);
        UIFactory.VerticalStack(actions, 14f, false);

        PrimaryAction(actions, "Near", "ready.near", flow.OpenLibrary, "Ready library");

        Button code = FullWidthButton(actions, "Code", "ready.code",
                                      ThemeRole.SurfaceAlt, ThemeRole.Accent);
        Wire(code.onClick, flow.OpenCode, "Ready code");

        Button notNow = QuietAction(actions, "NotNow", "trails.notNow", flow.Close, "Ready not now");
        notNow.gameObject.AddComponent<WayOut>();
        Swipeable(card, notNow);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// The demo's one line of instruction, resting on the map above its card.
    ///
    /// The card lives in the map's chrome, so a door going up hides it with the
    /// rest; the component that runs it lives on the always-active door holder,
    /// so it keeps listening while the camera tab is up.
    /// </summary>
    static DemoCoach BuildCoach(Transform holder, GameObject mapChrome, MapView mapView)
    {
        DemoCoach coach = holder.gameObject.AddComponent<DemoCoach>();
        coach.map = mapView;

        Transform dock = mapChrome != null ? mapChrome.transform.Find("Panel") : null;
        if (dock == null)
        {
            _notes.Add("No map dock, so the demo has nowhere to say what to do.");
            return coach;
        }

        RectTransform card = UIFactory.Rect("DemoCoach", dock);
        card.SetAsFirstSibling();
        UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        Outline(card, ThemeRole.Accent);

        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 16f, false);
        stack.padding = new RectOffset(34, 34, 28, 24);

        // No localisation key: DemoCoach writes this line, and a LocalizedLabel
        // would put step one back every time the map panel is shown again.
        coach.line = UIFactory.Text("Line", card, Body, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        coach.line.text = AppStrings.Get("demo.step1");
        UIFactory.FitsItsText(coach.line);

        RectTransform row = UIFactory.Rect("Actions", card);
        UIFactory.Size(row.gameObject, 96f);
        HorizontalLayoutGroup rowLayout = UIFactory.HorizontalStack(row, 14f);
        rowLayout.childAlignment = TextAnchor.MiddleRight;
        rowLayout.childForceExpandWidth = false;

        RectTransform skip = UIFactory.Rect("Skip", row);
        LayoutElement skipSize = UIFactory.Size(skip.gameObject, 96f, 220f);
        skipSize.flexibleWidth = 0f;
        coach.skipButton = UIFactory.Button(skip, ThemeRole.Surface, 0f);
        UIFactory.Stretch(UIFactory.Text("Label", skip, Secondary, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.Center, "demo.skip",
                                         FontStyles.Bold).rectTransform);

        RectTransform finish = UIFactory.Rect("Finish", row);
        LayoutElement finishSize = UIFactory.Size(finish.gameObject, 96f, 280f);
        finishSize.flexibleWidth = 0f;
        coach.finishButton = UIFactory.Button(finish, ThemeRole.Accent, 1f, UIShape.Pill);
        UIFactory.Stretch(UIFactory.Text("Label", finish, Secondary, ThemeRole.OnAccent,
                                         TextAlignmentOptions.Center, "demo.finish",
                                         FontStyles.Bold).rectTransform);

        coach.card = card.gameObject;
        card.gameObject.SetActive(false);

        EditorUtility.SetDirty(coach);
        return coach;
    }

    static GameObject BuildTeamDoor(Transform parent, DoorFlow flow,
                                    out TeamDoorView made)
    {
        RectTransform panel = Door(parent, "Team", plain: false, inner: out RectTransform inner);

        TeamDoorView view = panel.gameObject.AddComponent<TeamDoorView>();
        made = view;
        TapOutside(panel, flow.Close, "Team outside");

        RectTransform card = Card(inner, "Card", 26f);

        // The whole height of the screen, not the height of what is in it.
        //
        // A card that sizes to its content had to be capped, or it grew off the
        // bottom of the phone and took Close with it - and a capped card is a
        // short strip that makes you scroll a form that would fit perfectly well
        // on the screen it is sitting on. Pinned top and bottom instead, the
        // body takes whatever is left, the form starts near the top of the
        // phone where the keyboard cannot reach it, and Close is always at the
        // bottom because the card's bottom is the screen's.
        Object.DestroyImmediate(card.GetComponent<ContentSizeFitter>());
        UIFactory.Stretch(card);

        SheetTitle(card, "team.title");
        CardClose(card, flow.Close, "Close team");

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
        // Nearly the whole screen. This is the card ten people crowd around, and
        // a board that has to be scrolled to be read is a board nobody reads.
        // Whatever height the card has left. The board scrolls inside it when
        // there are more teams than fit; the form above it never needs to.
        RectTransform body = UIFactory.Rect("Body", card);
        LayoutElement bodySize = UIFactory.Size(body.gameObject, 260f);
        bodySize.flexibleHeight = 1f;

        // Daylight's 16px between the sheet's blocks - code, standings, who.
        RectTransform scroll = UIFactory.ScrollList("Scroll", body, 44f);

        // ----------------------------------------------------- no team yet
        RectTransform none = UIFactory.Rect("NoTeam", scroll);
        UIFactory.VerticalStack(none, 14f);
        view.noTeamGroup = none.gameObject;

        view.noneTitle = UIFactory.Text("None", none, Body, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.Left, "team.none",
                                        FontStyles.Bold);

        TextMeshProUGUI noneBody = UIFactory.Text("NoneBody", none, Secondary,
                                                  ThemeRole.TextSecondary,
                                                  TextAlignmentOptions.Left, "team.noneBody");
        view.noneBody = noneBody;
        noneBody.textWrappingMode = TextWrappingModes.Normal;

        // As tall as what it says, which is not a fixed number.
        //
        // This asked for 96 pixels through Size, and Size sets the preferred
        // height as well as the minimum - so the layout gave it 96 whatever was
        // in it, and TMP drew the rest of the words outside the box. On the
        // longest of them, the account explanation, that painted the last line
        // through the standings below and the first line through the heading
        // above. A minimum and no preference lets the stack ask the text.
        LayoutElement noneSize = noneBody.gameObject.AddComponent<LayoutElement>();
        noneSize.minHeight = 96f;
        noneSize.preferredHeight = -1f;

        view.nameField = Field(none, "Name", "team.name");
        view.createButton = FullWidthButton(none, "Create", "team.create",
                                            ThemeRole.Accent, ThemeRole.OnAccent);

        // The way to the thing the sentence above says is missing. It used to
        // say "make one under Settings", which is directions, not a door.
        view.accountButton = FullWidthButton(none, "MakeAccount", "team.makeAccount",
                                             ThemeRole.Accent, ThemeRole.OnAccent);
        Wire(view.accountButton.onClick, flow.OpenAccount, "Team card account");
        view.accountButton.gameObject.SetActive(false);

        view.codeField = Field(none, "Code", "team.code");
        view.joinButton = FullWidthButton(none, "Join", "team.join",
                                          ThemeRole.SurfaceAlt, ThemeRole.TextPrimary);

        // ------------------------------------------------------- on a team
        RectTransform mine = UIFactory.Rect("Team", scroll);
        UIFactory.VerticalStack(mine, 22f);
        view.teamGroup = mine.gameObject;

        // The name, large, and who is in it - the heading of the sheet.
        view.teamName = UIFactory.Text("Name", mine, Display, ThemeRole.TextPrimary,
                                       TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.teamName);

        view.teamMeta = UIFactory.Text("Meta", mine, Secondary, ThemeRole.TextMuted,
                                       TextAlignmentOptions.Left);
        UIFactory.FitsItsText(view.teamMeta);

        // The code first, and large.
        //
        // It is the thing somebody reads out across a room while three people
        // type it, so it goes above everything except the heading and it is set
        // at display size. It used to sit under the member list, which is the
        // one part of this card nobody reads aloud.
        UIFactory.Text("InviteLabel", mine, Caption, ThemeRole.TextMuted,
                       TextAlignmentOptions.Left, "team.invite");

        // Six boxes, one character each, in the accent - see TeamDoorView.ShowCode.
        RectTransform codeRow = UIFactory.Rect("Invite", mine);
        UIFactory.Size(codeRow.gameObject, 155f);
        UIFactory.HorizontalStack(codeRow, 17f, true);

        for (int i = 0; i < TeamDoorView.CodeLength; i++)
        {
            RectTransform box = UIFactory.Rect("Box" + i, codeRow);
            UIFactory.Size(box.gameObject, 155f);
            UIFactory.Flexible(box.gameObject);
            UIFactory.Fill(box, ThemeRole.SurfaceAlt, 1f, UIShape.Small).raycastTarget = false;

            TextMeshProUGUI cell = UIFactory.Text("Char", box, Large * 0.83f, ThemeRole.AccentText,
                                                  TextAlignmentOptions.Center, null, FontStyles.Bold);
            UIFactory.Stretch(cell.rectTransform);
            view.inviteCells[i] = cell;
        }

        // Said plainly, because this is the code people confuse with the trail's
        // and then wonder why nobody can join them.
        TextMeshProUGUI inviteHint = UIFactory.Text("InviteHint", mine, Caption,
                                                    ThemeRole.TextMuted,
                                                    TextAlignmentOptions.Left,
                                                    "team.inviteHint");
        UIFactory.FitsItsText(inviteHint);

        RectTransform pass = UIFactory.Rect("Pass", mine);
        UIFactory.Size(pass.gameObject, Btn);
        UIFactory.HorizontalStack(pass, 28f, true);
        view.copyButton = IconWideButton(pass, "Copy", AppIcon.Copy, "team.copy");
        view.shareButton = IconWideButton(pass, "Share", AppIcon.Share, "team.share");

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
            // Daylight's 48px row. At 56 the name's line did not fit, and a line
            // set to Ellipsis that does not fit is not drawn at all: the board
            // showed ranks and points and no teams.
            RectTransform row = UIFactory.Rect("Row" + i, board);
            UIFactory.Size(row.gameObject, 133f);

            // The wash that marks your own row, behind the text rather than in
            // it: a board is read from across a room.
            //
            // Its own child, not an Image on the row.
            //
            // Plain() adds the Image to whatever it is given, so passing the row
            // made boardHighlight[i].gameObject the row itself - and DrawBoard
            // switches the highlight off for every team that is not yours, which
            // switched the whole row off with it. The board could only ever draw
            // one line: your own. Six teams went in and one came out, and it did
            // that in every build since the board was written.
            RectTransform wash = UIFactory.Rect("Highlight", row);
            UIFactory.Stretch(wash);
            view.boardHighlight[i] = UIFactory.Plain(wash, Color.clear, UIShape.Rounded);
            view.boardHighlight[i].raycastTarget = false;
            wash.gameObject.SetActive(false);

            RectTransform line = UIFactory.Rect("Line", row);
            UIFactory.Stretch(line);
            line.offsetMin = new Vector2(33f, 0f);
            line.offsetMax = new Vector2(-33f, 0f);
            HorizontalLayoutGroup layout = UIFactory.HorizontalStack(line, 33f);
            layout.childAlignment = TextAnchor.MiddleLeft;

            view.boardRank[i] = UIFactory.Text("Rank", line, Body, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left);
            UIFactory.Size(view.boardRank[i].gameObject, 66f, 55f).flexibleWidth = 0f;

            view.boardName[i] = UIFactory.Text("Name", line, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left);
            UIFactory.Flexible(view.boardName[i].gameObject);
            view.boardName[i].textWrappingMode = TextWrappingModes.NoWrap;
            view.boardName[i].overflowMode = TextOverflowModes.Ellipsis;

            view.boardPoints[i] = UIFactory.Text("Points", line, Body, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Right, null,
                                                 FontStyles.Bold);

            // Size takes height then width, and this had them the other way
            // round: a hundred tall and fifty-six wide, so any score of three
            // digits wrapped down its own column. 320 read as 32 above a 0.
            UIFactory.Size(view.boardPoints[i].gameObject, 66f, 150f).flexibleWidth = 0f;
            view.boardPoints[i].textWrappingMode = TextWrappingModes.NoWrap;

            // The whole row is the target: a team name is what somebody would
            // report, and it is the widest thing on the line. A clear image
            // rather than TapFeedback, which would grey your own row out when
            // DrawBoard turns its button off.
            Image hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Button tap = row.gameObject.AddComponent<Button>();
            tap.targetGraphic = hit;
            tap.transition = Selectable.Transition.None;
            view.boardButtons[i] = tap;

            view.boardRows[i] = row;
            row.gameObject.SetActive(false);
        }

        // --------------------------------------- who is on it, and the way out
        //
        // Under the board rather than over it. A team looks at its own name once
        // and at the standings all afternoon.
        RectTransform who = UIFactory.Rect("Who", scroll);
        UIFactory.VerticalStack(who, 10f);
        view.teamGroupTail = who.gameObject;

        // What a moderator did to them, directly under the score it explains.
        //
        // Off unless something has been done, which is nearly always. When it is
        // on it is the only thing on this card in the warning colour, because a
        // team looking for why their points moved should find it without
        // reading the card.
        RectTransform moved = UIFactory.Rect("Moved", who);
        UIFactory.Fill(moved, ThemeRole.SurfaceAlt, 0f, UIShape.Rounded);
        Outline(moved);

        ContentSizeFitter movedFit = moved.gameObject.AddComponent<ContentSizeFitter>();
        movedFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup movedStack = UIFactory.VerticalStack(moved, 6f, false);
        movedStack.padding = new RectOffset(24, 24, 20, 20);

        view.movedGroup = moved.gameObject;

        view.movedTitle = UIFactory.Text("Title", moved, Secondary, ThemeRole.StatusPoor,
                                         TextAlignmentOptions.Left, null, FontStyles.Bold);
        view.movedTitle.textWrappingMode = TextWrappingModes.Normal;
        view.movedTitle.raycastTarget = false;

        view.movedBody = UIFactory.Text("Body", moved, Caption, ThemeRole.TextSecondary,
                                        TextAlignmentOptions.TopLeft);
        view.movedBody.textWrappingMode = TextWrappingModes.Normal;
        view.movedBody.raycastTarget = false;

        moved.gameObject.SetActive(false);

        // A heading, or the names read as the last line of the standings.
        Eyebrow(who, "team.members");

        for (int i = 0; i < TeamDoorView.MaxMembers; i++)
        {
            view.memberRows[i] = UIFactory.Text("Member" + i, who, Body,
                                                ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Left);
            UIFactory.Size(view.memberRows[i].gameObject, 52f);
            view.memberRows[i].gameObject.SetActive(false);

            // A button over the row: the captain taps a teammate (0058).
            view.memberButtons[i] = view.memberRows[i].gameObject.AddComponent<Button>();
            view.memberButtons[i].targetGraphic = view.memberRows[i];
            view.memberButtons[i].transition = Selectable.Transition.None;
        }

        // What the captain can do to the teammate they tapped.
        RectTransform captain = UIFactory.Rect("Captain", who);
        UIFactory.VerticalStack(captain, 12f, false);
        UIFactory.Fill(captain, ThemeRole.SurfaceAlt, 1f, UIShape.Rounded).raycastTarget = false;
        captain.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 20, 20);
        ContentSizeFitter captainFit = captain.gameObject.AddComponent<ContentSizeFitter>();
        captainFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        view.captainGroup = captain.gameObject;

        view.captainLabel = UIFactory.Text("Name", captain, Body, ThemeRole.TextPrimary,
                                           TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.captainLabel.gameObject, 52f);
        view.captainLabel.raycastTarget = false;

        RectTransform captainRow = UIFactory.Rect("Actions", captain);
        UIFactory.Size(captainRow.gameObject, Btn);
        UIFactory.HorizontalStack(captainRow, 22f).childAlignment = TextAnchor.MiddleLeft;
        view.makeCaptainButton = WideButton(captainRow, "MakeCaptain", ThemeRole.Accent, ThemeRole.OnAccent,
                                            "team.makeCaptain", out TextMeshProUGUI _);
        view.removeButton = WideButton(captainRow, "Remove", ThemeRole.Fill, ThemeRole.StatusPoor,
                                       "team.remove", out TextMeshProUGUI removeLabel);
        DropLocalizer(removeLabel);
        captain.gameObject.SetActive(false);

        // Red words, and a red button once armed: TeamDoorView paints both.
        RectTransform leave = UIFactory.Rect("Leave", who);
        UIFactory.Size(leave.gameObject, Btn);
        view.leaveFace = UIFactory.Plain(leave, new Color(1f, 0.23f, 0.19f, 0f), UIShape.Pill);
        view.leaveButton = leave.gameObject.AddComponent<Button>();
        view.leaveButton.targetGraphic = view.leaveFace;
        UIFactory.Feedback(view.leaveButton);
        TextMeshProUGUI leaveLabel = UIFactory.Text("Label", leave, Body, ThemeRole.StatusPoor,
                                                    TextAlignmentOptions.Center, "team.leave");
        UIFactory.Stretch(leaveLabel.rectTransform);
        DropLocalizer(leaveLabel);
        Unthemed(leaveLabel.gameObject);

        // The captain's: the same red-until-armed button, for the whole team.
        RectTransform giveUp = UIFactory.Rect("GiveUp", who);
        UIFactory.Size(giveUp.gameObject, Btn);
        view.giveUpFace = UIFactory.Plain(giveUp, new Color(1f, 0.23f, 0.19f, 0f), UIShape.Pill);
        view.giveUpButton = giveUp.gameObject.AddComponent<Button>();
        view.giveUpButton.targetGraphic = view.giveUpFace;
        UIFactory.Feedback(view.giveUpButton);
        TextMeshProUGUI giveUpLabel = UIFactory.Text("Label", giveUp, Body, ThemeRole.StatusPoor,
                                                     TextAlignmentOptions.Center, "team.giveUp");
        UIFactory.Stretch(giveUpLabel.rectTransform);
        DropLocalizer(giveUpLabel);
        Unthemed(giveUpLabel.gameObject);
        giveUp.gameObject.SetActive(false);

        // ---------------------------------------------------------- status
        view.statusLabel = UIFactory.Text("Status", card, Secondary, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Left);
        view.statusLabel.textWrappingMode = TextWrappingModes.Normal;
        UIFactory.ReservesButGrows(view.statusLabel.gameObject, 56f);
        view.statusLabel.gameObject.SetActive(false);

        view.closeButton = card.Find("Close").GetComponent<Button>();

        // Its own chooser, built last so it draws over the card. The stop sheet's
        // one lives under the doors and would open behind this.
        view.reportSheet = BuildReportChooser(panel, null);

        return panel.gameObject;
    }

    /// <summary>
    /// "Маршрути": a full-height sheet on the parchment, with the search, the
    /// category chips, the city and Nearest pills, a count, the white list card
    /// and "Join with a code" at the bottom. TrailsDoor fills and filters it.
    /// </summary>
    static GameObject BuildTrailsDoor(Transform parent, DoorFlow flow,
                                      TrailRowView rowPrefab, out TrailsDoor door)
    {
        RectTransform panel = Door(parent, "Trails", plain: false, inner: out RectTransform inner);

        door = panel.gameObject.AddComponent<TrailsDoor>();
        door.categoryIcons = CategoryIcons();
        TapOutside(panel, flow.Close, "Trails outside");

        RectTransform card = Card(inner, "Card", 28f);

        // Full height, not hugging its list: the sheet must not jump while the
        // reader types and the list shrinks under their finger.
        ContentSizeFitter hug = card.GetComponent<ContentSizeFitter>();
        if (hug != null)
            Object.DestroyImmediate(hug);
        card.anchorMax = new Vector2(1f, 1f);
        card.offsetMin = Vector2.zero;
        card.offsetMax = Vector2.zero;

        // Parchment under white cards, as the grouped lists elsewhere.
        Transform faceT = card.Find("Face");
        ThemedElement faceTheme = faceT != null ? faceT.GetComponent<ThemedElement>() : null;
        if (faceTheme != null)
        {
            faceTheme.role = ThemeRole.Background;
            faceTheme.Apply();
        }

        // --- the title, large and to the left, as Daylight's large titles
        RectTransform bar = UIFactory.Rect("TitleBar", card);
        UIFactory.Size(bar.gameObject, 133f);
        TextMeshProUGUI title = UIFactory.Text("Title", bar, Display, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "trails.title",
                                               FontStyles.Bold);
        UIFactory.Stretch(title.rectTransform);
        title.margin = new Vector4(0f, 0f, 150f, 0f);
        title.textWrappingMode = TextWrappingModes.NoWrap;

        // --- the search: a white pill with a hairline and a magnifier
        RectTransform fieldRect = UIFactory.Rect("SearchField", card);
        UIFactory.Size(fieldRect.gameObject, 121f);
        door.searchField = UIFactory.InputField("Field", fieldRect, "find.search",
                                                out TextMeshProUGUI _);
        door.searchField.richText = false;
        RectTransform field = (RectTransform)door.searchField.transform;
        UIFactory.Stretch(field);
        UIFactory.ApplyShape(field.GetComponent<Image>(), UIShape.Pill);
        PillEdge(field, ThemeRole.Line);

        Image searchGlyph = Icon(fieldRect, AppIcon.Search, 42f, ThemeRole.TextMuted,
                                 out RectTransform searchGlyphRect);
        UIFactory.Corner(searchGlyphRect, new Vector2(0f, 0.5f), new Vector2(42f, 42f),
                         new Vector2(55f, 0f));
        searchGlyph.transform.SetAsLastSibling();

        if (door.searchField.textViewport != null)
        {
            door.searchField.textViewport.offsetMin = new Vector2(124f, 8f);
            door.searchField.textViewport.offsetMax = new Vector2(-44f, -8f);
        }

        // --- the categories, a row that scrolls sideways
        RectTransform chipStrip = UIFactory.Rect("Categories", card);
        UIFactory.Size(chipStrip.gameObject, 105f);
        ScrollRect chipScroll = chipStrip.gameObject.AddComponent<ScrollRect>();
        chipScroll.horizontal = true;
        chipScroll.vertical = false;
        chipScroll.movementType = ScrollRect.MovementType.Clamped;

        RectTransform chipView = UIFactory.Rect("Viewport", chipStrip);
        UIFactory.Stretch(chipView);
        chipView.gameObject.AddComponent<RectMask2D>();
        Image chipCatcher = chipView.gameObject.AddComponent<Image>();
        chipCatcher.color = new Color(0f, 0f, 0f, 0f);

        RectTransform chipRow = UIFactory.Rect("Content", chipView);
        chipRow.anchorMin = new Vector2(0f, 0f);
        chipRow.anchorMax = new Vector2(0f, 1f);
        chipRow.pivot = new Vector2(0f, 0.5f);
        chipRow.sizeDelta = Vector2.zero;
        HorizontalLayoutGroup chipLayout = UIFactory.HorizontalStack(chipRow, 22f);
        chipLayout.childForceExpandHeight = true;
        ContentSizeFitter chipFit = chipRow.gameObject.AddComponent<ContentSizeFitter>();
        chipFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        chipScroll.viewport = chipView;
        chipScroll.content = chipRow;

        string[] chipKeys = { "find.all", "cat.education", "cat.tourism", "cat.estate", "cat.other" };
        door.chips = new TrailsDoor.Chip[chipKeys.Length];
        for (int i = 0; i < chipKeys.Length; i++)
            door.chips[i] = FilterChip(chipRow, "Chip" + i, chipKeys[i], i > 0);

        // --- the city and Nearest
        RectTransform filters = UIFactory.Rect("Filters", card);
        // flexibleHeight 0: a group that force-expands its children's height
        // reports itself flexible, and the card's stack would hand it the spare.
        UIFactory.Size(filters.gameObject, 105f).flexibleHeight = 0f;
        HorizontalLayoutGroup filterLayout = UIFactory.HorizontalStack(filters, 22f);
        filterLayout.childForceExpandHeight = true;

        TrailsDoor.Chip city = FilterChip(filters, "City", "find.allCities", false);
        door.cityButton = city.button;
        door.cityLabel = city.label;
        Icon(city.button.transform, AppIcon.ChevronDown, 34f, ThemeRole.TextSecondary);

        door.nearestChip = FilterChip(filters, "Nearest", "find.nearest", false);

        // --- how many
        door.countLabel = UIFactory.Text("Count", card, Caption + 3f, ThemeRole.TextMuted,
                                         TextAlignmentOptions.BottomLeft, null);
        UIFactory.Size(door.countLabel.gameObject, 44f);
        door.countLabel.text = string.Empty;

        // --- the list, taking whatever height is left
        RectTransform listHolder = UIFactory.Rect("Body", card);
        LayoutElement bodySize = UIFactory.Size(listHolder.gameObject, 300f);
        bodySize.preferredHeight = -1f;
        bodySize.flexibleHeight = 1f;

        RectTransform content = UIFactory.ScrollList("Scroll", listHolder, 22f);
        door.scroll = listHolder.GetComponentInChildren<ScrollRect>();

        door.waitingState = StateNote(content, "Waiting", "trails.loading", null);
        door.failedState = StateNote(content, "Failed", "trails.failed", null,
                                     out door.failedLabel);

        // Nothing matches: the sentence, and the way back to everything.
        RectTransform empty = UIFactory.Rect("Empty", content);
        VerticalLayoutGroup emptyStack = UIFactory.VerticalStack(empty, 28f, false);
        emptyStack.padding = new RectOffset(0, 0, 60, 20);
        emptyStack.childAlignment = TextAnchor.UpperCenter;
        door.emptyState = empty.gameObject;

        door.emptyLabel = UIFactory.Text("Label", empty, Body, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.Center, null);
        door.emptyLabel.text = string.Empty;
        // It repeats what the reader typed; a "<b>" typed there is text, not markup.
        door.emptyLabel.richText = false;
        UIFactory.FitsItsText(door.emptyLabel);

        RectTransform clearRow = UIFactory.Rect("ClearRow", empty);
        UIFactory.Size(clearRow.gameObject, 121f).flexibleHeight = 0f;
        HorizontalLayoutGroup clearLayout = UIFactory.HorizontalStack(clearRow, 0f);
        clearLayout.childAlignment = TextAnchor.MiddleCenter;
        clearLayout.childForceExpandHeight = true;

        RectTransform clear = UIFactory.Rect("Clear", clearRow);
        door.clearButton = UIFactory.Button(clear, ThemeRole.Surface, 0f, UIShape.Pill);
        PillEdge(clear, ThemeRole.AccentText);
        HorizontalLayoutGroup clearPad = UIFactory.HorizontalStack(clear, 0f);
        clearPad.padding = new RectOffset(60, 60, 0, 0);
        clearPad.childAlignment = TextAnchor.MiddleCenter;
        clearPad.childForceExpandHeight = true;
        TextMeshProUGUI clearLabel = UIFactory.Text("Label", clear, Body, ThemeRole.AccentText,
                                                    TextAlignmentOptions.Center, "find.clear");
        clearLabel.textWrappingMode = TextWrappingModes.NoWrap;
        empty.gameObject.SetActive(false);

        // The white card the trails sit in.
        RectTransform list = UIFactory.Rect("List", content);
        UIFactory.Fill(list, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = false;
        Outline(list);
        UIFactory.VerticalStack(list, 0f, false);
        door.listCard = list.gameObject;
        door.listParent = list;
        door.rowTemplate = TrailListRow(list);

        // --- what to do about it ---
        door.joinButton = PrimaryAction(card, "Join", "trails.join", flow.OpenCode,
                                        "Open code entry");

        CardClose(card, flow.Close, "Close trails");

        BuildCityMenu(panel, door);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>
    /// A trail not joined yet, before starting it: a schematic map of its stops
    /// over the top of the screen, and a sheet with what it is, how long, how it
    /// works and where it starts.
    ///
    ///   (‹)       ①- - -②
    ///                   ③- - -④   ●
    ///   ╭───────────────────────────────╮
    ///   │ ▣ Туризъм · Ямбол              │
    ///   │ Ямбол: сърцето на града        │
    ///   │ [5 спирки][~20 мин][1,4 км]    │
    ///   │ По ред · … Без отбори и точки. │
    ///   │ Началото е при …, на 350 м.    │
    ///   │ (      Започни маршрута      ) │
    ///   │ (   Заведи ме до началото    ) │
    ///   ╰───────────────────────────────╯
    /// </summary>
    static GameObject BuildTrailPreview(Transform parent, DoorFlow flow, out TrailPreviewView view)
    {
        RectTransform panel = UIFactory.Rect("Preview", parent);
        UIFactory.Stretch(panel);

        // The map's ground, edge to edge, and a raycast target: nothing behind
        // this screen is pressable while it is up.
        UIFactory.Fill(panel, ThemeRole.SurfaceAlt).raycastTarget = true;
        AddFade(panel.gameObject, 22f);

        view = panel.gameObject.AddComponent<TrailPreviewView>();
        view.categoryIcons = CategoryIcons();

        // --- the schematic map
        RectTransform map = UIFactory.Rect("Map", panel);
        UIFactory.Stretch(map);
        view.mapArea = map;

        // A few streets, so the ground reads as a map and not as an empty page.
        // Not the real streets - the stops are placed in proportion, and that is
        // all this picture claims.
        RectTransform streets = UIFactory.Rect("Streets", map);
        UIFactory.Stretch(streets);
        float[][] roads =
        {
            //          x,      y,     length, width, angle
            new[] { 540f,  1500f, 2400f, 26f,  -8f },
            new[] { 300f,  1100f, 2400f, 18f,  62f },
            new[] { 760f,  1250f, 2400f, 18f,  98f },
            new[] { 540f,   880f, 2400f, 22f,   4f },
            new[] { 180f,  1650f, 1400f, 12f,  35f },
            new[] { 900f,  1700f, 1400f, 12f, -40f },
        };
        for (int i = 0; i < roads.Length; i++)
        {
            RectTransform road = UIFactory.Rect("Street" + i, streets);
            road.anchorMin = road.anchorMax = new Vector2(0f, 0f);
            road.pivot = new Vector2(0.5f, 0.5f);
            road.anchoredPosition = new Vector2(roads[i][0], roads[i][1]);
            road.sizeDelta = new Vector2(roads[i][2], roads[i][3]);
            road.localRotation = Quaternion.Euler(0f, 0f, roads[i][4]);
            UIFactory.Fill(road, ThemeRole.Surface).raycastTarget = false;
        }

        RectTransform legs = UIFactory.Rect("Legs", map);
        UIFactory.Stretch(legs);
        view.legsParent = legs;

        RectTransform pins = UIFactory.Rect("Pins", map);
        UIFactory.Stretch(pins);
        view.pinsParent = pins;

        // One leg of the route: the guidance line's dashes, in the accent.
        RectTransform leg = UIFactory.Rect("Leg", legs);
        UIFactory.Corner(leg, new Vector2(0.5f, 0.5f), new Vector2(0f, 17f), Vector2.zero);
        Image dash = UIFactory.Fill(leg, ThemeRole.Accent);
        dash.sprite = UISprites.Dash;
        dash.type = Image.Type.Tiled;
        dash.pixelsPerUnitMultiplier = 2f;
        dash.raycastTarget = false;
        leg.gameObject.SetActive(false);
        view.legTemplate = leg;

        // One stop: a dark numbered circle in a ring of the surface colour.
        RectTransform pin = UIFactory.Rect("Pin", pins);
        UIFactory.Corner(pin, new Vector2(0.5f, 0.5f), new Vector2(86f, 86f), Vector2.zero);
        UIFactory.Fill(pin, ThemeRole.Surface, 1f, UIShape.Circle).raycastTarget = false;
        RectTransform pinFace = UIFactory.Rect("Face", pin);
        UIFactory.Corner(pinFace, new Vector2(0.5f, 0.5f), new Vector2(72f, 72f), Vector2.zero);
        UIFactory.Fill(pinFace, ThemeRole.TextPrimary, 1f, UIShape.Circle).raycastTarget = false;
        TextMeshProUGUI pinNumber = UIFactory.Text("Number", pinFace, Secondary, ThemeRole.Surface,
                                                   TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(pinNumber.rectTransform);
        pinNumber.textWrappingMode = TextWrappingModes.NoWrap;
        pinNumber.text = "1";
        pin.gameObject.SetActive(false);
        view.pinTemplate = pin;

        // The reader: the map's blue dot, with its faint halo.
        RectTransform you = UIFactory.Rect("You", pins);
        UIFactory.Corner(you, new Vector2(0.5f, 0.5f), new Vector2(60f, 60f), Vector2.zero);
        RectTransform halo = UIFactory.Rect("Halo", you);
        UIFactory.Corner(halo, new Vector2(0.5f, 0.5f), new Vector2(160f, 160f), Vector2.zero);
        UIFactory.Fill(halo, ThemeRole.Accent, 0.16f, UIShape.Circle).raycastTarget = false;
        RectTransform ring = UIFactory.Rect("Ring", you);
        UIFactory.Stretch(ring);
        UIFactory.Fill(ring, ThemeRole.Surface, 1f, UIShape.Circle).raycastTarget = false;
        RectTransform core = UIFactory.Rect("Core", you);
        UIFactory.Corner(core, new Vector2(0.5f, 0.5f), new Vector2(44f, 44f), Vector2.zero);
        UIFactory.Fill(core, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;
        you.gameObject.SetActive(false);
        view.userDot = you;

        RectTransform safe = SafeContent(panel, padBottom: true);

        // --- back, round, top left
        RectTransform back = UIFactory.Rect("Back", safe);
        UIFactory.Corner(back, new Vector2(0f, 1f), new Vector2(122f, 122f), new Vector2(Margin, -Margin));
        Button backButton = UIFactory.Button(back, ThemeRole.Surface, 1f, UIShape.Circle);
        Wire(backButton.onClick, view.Back, "Leave the trail preview");
        PillEdge(back, ThemeRole.Line);
        Icon(back, AppIcon.ChevronLeft, 48f, ThemeRole.TextPrimary);
        back.gameObject.AddComponent<WayOut>();

        // --- the sheet
        RectTransform card = Card(safe, "Sheet", 28f);
        view.sheet = card;

        // No grabber: this sheet does not drag, and a handle promises it does.
        Transform grabber = card.Find("Grabber");
        if (grabber != null)
            Object.DestroyImmediate(grabber.gameObject);

        RectTransform meta = UIFactory.Rect("Meta", card);
        UIFactory.Size(meta.gameObject, 52f).flexibleHeight = 0f;
        HorizontalLayoutGroup metaRow = UIFactory.HorizontalStack(meta, 14f);
        metaRow.childForceExpandHeight = true;
        view.categoryIcon = Icon(meta, AppIcon.Dot, 40f, ThemeRole.TextMuted);
        view.metaLabel = UIFactory.Text("Label", meta, Secondary, ThemeRole.TextMuted,
                                        TextAlignmentOptions.Left, null);
        view.metaLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.metaLabel.overflowMode = TextOverflowModes.Ellipsis;
        view.metaLabel.richText = false;   // the city comes from the database
        UIFactory.Flexible(view.metaLabel.gameObject);

        view.nameLabel = UIFactory.Text("Name", card, Display, ThemeRole.TextPrimary,
                                        TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.FitsItsText(view.nameLabel);
        view.nameLabel.richText = false;   // whatever the trail's author typed
        view.nameLabel.text = string.Empty;

        // Three grey tiles: stops, minutes on foot, length.
        RectTransform tiles = UIFactory.Rect("Tiles", card);
        UIFactory.Size(tiles.gameObject, 180f).flexibleHeight = 0f;
        HorizontalLayoutGroup tileRow = UIFactory.HorizontalStack(tiles, 22f, true);
        tileRow.childForceExpandHeight = true;
        view.tilesRow = tiles.gameObject;

        PreviewTile(tiles, "Stops", "preview.stops", out view.stopsValue, out view.stopsLabel);
        view.minutesTile = PreviewTile(tiles, "Minutes", "preview.walk",
                                       out view.minutesValue, out TextMeshProUGUI _);
        view.lengthTile = PreviewTile(tiles, "Length", "preview.total",
                                      out view.lengthValue, out TextMeshProUGUI _);

        view.howLabel = UIFactory.Text("How", card, Body, ThemeRole.TextSecondary,
                                       TextAlignmentOptions.TopLeft, null);
        UIFactory.FitsItsText(view.howLabel);
        view.howLabel.text = string.Empty;

        view.startLabel = UIFactory.Text("Start", card, Body, ThemeRole.TextPrimary,
                                         TextAlignmentOptions.TopLeft, null);
        UIFactory.FitsItsText(view.startLabel);
        view.startLabel.richText = false;   // carries a stop's title
        view.startLabel.text = string.Empty;

        // Set by the view (loading, or failed), so no LocalizedLabel to fight it.
        view.statusLabel = UIFactory.Text("Status", card, Body, ThemeRole.TextSecondary,
                                          TextAlignmentOptions.TopLeft, null);
        UIFactory.FitsItsText(view.statusLabel);
        view.statusLabel.text = string.Empty;

        view.goButton = PrimaryAction(card, "Go", "preview.go", view.StartTrail, "Start the trail");

        // The second way in: the same pill, outlined.
        RectTransform lead = UIFactory.Rect("Lead", card);
        UIFactory.Size(lead.gameObject, Btn);
        view.leadButton = UIFactory.Button(lead, ThemeRole.Surface, 1f, UIShape.Pill);
        Wire(view.leadButton.onClick, view.LeadToStart, "Take me to the start");
        PillEdge(lead, ThemeRole.AccentText);
        TextMeshProUGUI leadLabel = UIFactory.Text("Label", lead, Body, ThemeRole.AccentText,
                                                   TextAlignmentOptions.Center, "preview.lead");
        UIFactory.Stretch(leadLabel.rectTransform);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    /// <summary>One grey tile: the number large, what it counts under it.</summary>
    static GameObject PreviewTile(RectTransform row, string name, string key,
                                  out TextMeshProUGUI value, out TextMeshProUGUI label)
    {
        RectTransform tile = UIFactory.Rect(name, row);
        UIFactory.Fill(tile, ThemeRole.Fill, 1f, UIShape.Small).raycastTarget = false;

        // Equal thirds whatever the numbers say.
        LayoutElement size = tile.gameObject.AddComponent<LayoutElement>();
        size.minWidth = 0f;
        size.preferredWidth = 1f;
        size.flexibleWidth = 1f;

        VerticalLayoutGroup stack = UIFactory.VerticalStack(tile, 2f, false);
        stack.padding = new RectOffset(36, 24, 0, 0);
        stack.childAlignment = TextAnchor.MiddleLeft;

        value = UIFactory.Text("Value", tile, Title, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(value.gameObject, 70f);
        value.textWrappingMode = TextWrappingModes.NoWrap;
        value.enableAutoSizing = true;
        value.fontSizeMin = Secondary;
        value.fontSizeMax = Title;
        value.text = string.Empty;

        label = UIFactory.Text("Label", tile, Secondary, ThemeRole.TextSecondary,
                               TextAlignmentOptions.Left, key);
        UIFactory.Size(label.gameObject, 48f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return tile.gameObject;
    }

    const float ListSide = 39f;
    const float ListChip = 121f;
    const float ListText = ListSide + ListChip + 33f;

    /// <summary>
    /// One trail in the list:
    ///   [▣]  Name                       350 m
    ///        City · 5 stops             Leave
    /// </summary>
    static RectTransform TrailListRow(RectTransform list)
    {
        RectTransform row = UIFactory.Rect("Row", list);
        UIFactory.Size(row.gameObject, 190f);
        UIFactory.Button(row, ThemeRole.Fill, 0f);

        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        rule.offsetMin = new Vector2(ListText, rule.offsetMin.y);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);

        RectTransform chip = UIFactory.Rect("Chip", row);
        UIFactory.Corner(chip, new Vector2(0f, 0.5f), new Vector2(ListChip, ListChip),
                         new Vector2(ListSide, 0f));
        UIFactory.Fill(chip, ThemeRole.Fill, 1f, UIShape.Small).raycastTarget = false;
        Icon(chip, AppIcon.Dot, 62f, ThemeRole.TextSecondary);

        RectTransform side = UIFactory.Rect("Side", row);
        side.anchorMin = new Vector2(1f, 0f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(1f, 0.5f);
        side.sizeDelta = new Vector2(230f, 0f);
        side.anchoredPosition = new Vector2(-ListSide, 0f);
        VerticalLayoutGroup sideStack = UIFactory.VerticalStack(side, 0f, false);
        sideStack.childAlignment = TextAnchor.MiddleRight;

        TextMeshProUGUI distance = UIFactory.Text("Distance", side, Secondary, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Right);
        UIFactory.Size(distance.gameObject, 52f);
        distance.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform leave = UIFactory.Rect("Leave", side);
        UIFactory.Size(leave.gameObject, 70f);
        UIFactory.Button(leave, ThemeRole.Surface, 0f);
        TextMeshProUGUI leaveLabel = UIFactory.Text("Label", leave, Caption + 3f, ThemeRole.TextMuted,
                                                    TextAlignmentOptions.Right, "trails.leave");
        UIFactory.Stretch(leaveLabel.rectTransform);
        leaveLabel.textWrappingMode = TextWrappingModes.NoWrap;
        leave.gameObject.SetActive(false);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.Stretch(text);
        text.offsetMin = new Vector2(ListText, 0f);
        text.offsetMax = new Vector2(-ListSide - 240f, 0f);
        UIFactory.VerticalStack(text, 4f, false).childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI name = UIFactory.Text("Name", text, Body, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(name.gameObject, 58f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.richText = false;   // a trail's name is whatever its author typed

        TextMeshProUGUI sub = UIFactory.Text("Sub", text, Secondary, ThemeRole.TextSecondary,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(sub.gameObject, 48f);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;

        row.gameObject.SetActive(false);
        return row;
    }

    /// <summary>
    /// A pill that is a filter: white with a hairline, as wide as its words.
    /// TrailsDoor turns the selected one ink (or the accent tint, for Nearest).
    /// </summary>
    static TrailsDoor.Chip FilterChip(RectTransform parent, string name, string key, bool glyph)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        TrailsDoor.Chip chip = new TrailsDoor.Chip();

        chip.button = UIFactory.Button(rt, ThemeRole.Surface, 1f, UIShape.Pill);
        chip.fill = rt.GetComponent<Image>();
        chip.outline = PillEdge(rt, ThemeRole.Line).gameObject;

        HorizontalLayoutGroup pad = UIFactory.HorizontalStack(rt, 14f);
        pad.padding = new RectOffset(44, 44, 0, 0);
        pad.childAlignment = TextAnchor.MiddleCenter;

        if (glyph)
        {
            chip.icon = Icon(rt, AppIcon.Dot, 40f, ThemeRole.Surface);
            chip.icon.gameObject.SetActive(false);
        }

        chip.label = UIFactory.Text("Label", rt, Secondary, ThemeRole.TextPrimary,
                                    TextAlignmentOptions.Center, key);
        chip.label.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Size(chip.label.gameObject, 60f);

        return chip;
    }

    /// <summary>The hairline round a pill, outside its layout.</summary>
    static Image PillEdge(RectTransform pill, ThemeRole role)
    {
        RectTransform rim = UIFactory.Rect("Outline", pill);
        UIFactory.Stretch(rim);
        Image edge = UIFactory.Fill(rim, role);
        edge.sprite = UISprites.PillOutline;
        edge.type = Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1f;
        edge.raycastTarget = false;
        rim.gameObject.AddComponent<PillShape>();
        Ignore(rim);
        return edge;
    }

    /// <summary>
    /// The city picker: a plain list over a dimmed sheet. "All cities" first,
    /// then the cities present, then "No city" when a trail has none.
    /// </summary>
    static void BuildCityMenu(RectTransform panel, TrailsDoor door)
    {
        RectTransform root = UIFactory.Rect("CityMenu", panel);
        UIFactory.Stretch(root);
        door.cityMenu = root.gameObject;

        RectTransform scrimRect = UIFactory.Rect("Scrim", root);
        UIFactory.Stretch(scrimRect);
        Image dim = UIFactory.Fill(scrimRect, ThemeRole.Scrim, 0.35f);
        dim.raycastTarget = true;
        Button scrim = scrimRect.gameObject.AddComponent<Button>();
        scrim.targetGraphic = dim;
        scrim.transition = Selectable.Transition.None;
        Wire(scrim.onClick, door.CloseCities, "Close city picker");
        scrimRect.gameObject.AddComponent<WayOut>();

        RectTransform card = UIFactory.Rect("Card", root);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(860f, 400f);
        UIFactory.Fill(card, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;

        // The card's own outline, at the card's radius (Outline() is the
        // smaller rounded one and showed as a second edge in the corners).
        RectTransform edge = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(edge);
        Image line = UIFactory.Fill(edge, ThemeRole.Line);
        line.sprite = UISprites.CardOutline;
        line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 1f;
        line.raycastTarget = false;
        Ignore(edge);
        VerticalLayoutGroup stack = UIFactory.VerticalStack(card, 0f);
        stack.padding = new RectOffset(0, 0, 14, 14);

        // Rows in a scroll whose height TrailsDoor sets: its rows, up to 60% of
        // the screen. Twenty cities must not run the card off the top and bottom.
        RectTransform listHolder = UIFactory.Rect("List", card);
        door.cityListSize = UIFactory.Size(listHolder.gameObject, 140f);
        RectTransform rows = UIFactory.ScrollList("Scroll", listHolder, 0f);
        door.cityScroll = listHolder.GetComponentInChildren<ScrollRect>();
        door.cityRowsParent = rows;

        RectTransform row = UIFactory.Rect("Row", rows);
        UIFactory.Size(row.gameObject, 140f);
        UIFactory.Button(row, ThemeRole.Fill, 0f);

        RectTransform rule = UIFactory.Rect("Rule", row);
        UIFactory.TopStretch(rule, 3f);
        rule.offsetMin = new Vector2(MenuSide, rule.offsetMin.y);
        UIFactory.Fill(rule, ThemeRole.Line).raycastTarget = false;
        Ignore(rule);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left);
        UIFactory.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(MenuSide, 0f);
        label.rectTransform.offsetMax = new Vector2(-MenuSide - 80f, 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        Image tick = Icon(row, AppIcon.Check, 46f, ThemeRole.AccentText, out RectTransform tickRect);
        UIFactory.Corner(tickRect, new Vector2(1f, 0.5f), new Vector2(46f, 46f),
                         new Vector2(-MenuSide - 23f, 0f));
        tick.gameObject.name = "Check";

        door.cityRowTemplate = row;
        row.gameObject.SetActive(false);
        root.gameObject.SetActive(false);
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

        SheetTitle(card, "code.title");

        TextMeshProUGUI codeBody = UIFactory.Text("Body", card, Body, ThemeRole.TextSecondary,
                                                  TextAlignmentOptions.TopLeft, "code.body");
        UIFactory.FitsItsText(codeBody);

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
                                      TextAlignmentOptions.Top);
        door.message.gameObject.SetActive(false);

        RectTransform actions = UIFactory.Rect("Actions", card);
        UIFactory.VerticalStack(actions, 10f, false);

        door.joinButton = PrimaryAction(actions, "Join", "code.join", door.Join,
                                        "Join with code");
        door.joinLabel = door.joinButton.GetComponentInChildren<TextMeshProUGUI>();

        // Cancel is the way out, and it is the only one.
        //
        // There was a mark in the corner as well, doing the same job by a
        // different route. Two controls for one action on a card with three
        // controls on it, and the mark is the one that has to be guessed at while
        // the word says what it does - and it sits where the thumb already is,
        // next to Join. The trails card still has its mark; its worded exit says
        // "Not now", which is a different sentence from "close this".
        door.cancelButton = QuietAction(actions, "Cancel", "code.cancel", door.Cancel,
                                        "Cancel code entry");
        door.cancelButton.gameObject.AddComponent<WayOut>();
        Swipeable(card, door.cancelButton);

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

        // A plain door is a whole screen of its own and keeps its margins. The
        // rest are Daylight sheets: edge to edge, up from the bottom, with the
        // same room above them a sheet at full height leaves.
        if (plain)
        {
            inner.offsetMin = new Vector2(Margin, Margin);
            inner.offsetMax = new Vector2(-Margin, -Margin);
        }
        else
        {
            inner.offsetMin = Vector2.zero;
            inner.offsetMax = new Vector2(0f, -SheetGap);
        }

        return panel;
    }

    // ------------------------------------------------------------------- cards

    /// <summary>Corner radius aside, everything a card is made of.</summary>
    const float CardPad = SheetPad;
    const float CardTop = 44f;
    const float CardBottom = 44f;

    /// <summary>How far the shadow reaches past the card, and how far it falls.</summary>
    const float ShadowSpread = 40f;
    const float ShadowDrop = 14f;

    const float BoxWidth = 122f;
    const float BoxHeight = 150f;
    const float BoxGap = 22f;

    /// <summary>No split: six boxes in one even row, as Daylight draws them.</summary>
    const float BoxSplit = 0f;

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

        // No shadow. Daylight keeps its one shadow for the thing standing in the
        // street; a card is lifted off what is behind it by its own surface and
        // a hairline.
        // A sheet's face: 24px corners at the top, run on past the bottom of the
        // screen so its lower corners never show.
        RectTransform face = UIFactory.Rect("Face", card);
        UIFactory.Stretch(face);
        face.offsetMin = new Vector2(0f, -160f);

        // A raycast target, so a tap that lands on the card does not fall through
        // to the map behind a screen the reader has not finished with.
        UIFactory.Fill(face, ThemeRole.Surface, 1f, UIShape.Card).raycastTarget = true;
        Ignore(face);

        RectTransform grab = UIFactory.Rect("Grabber", card);
        UIFactory.Corner(grab, new Vector2(0.5f, 1f), new Vector2(100f, 14f), new Vector2(0f, -17f));
        Image grip = UIFactory.Fill(grab, ThemeRole.FillStrong, 1f, UIShape.Pill);
        grip.raycastTarget = false;
        PillFit(grip, 14f);
        Ignore(grab);

        RectTransform edge = UIFactory.Rect("Outline", card);
        UIFactory.Stretch(edge);
        edge.offsetMin = new Vector2(0f, -160f);
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
    /// A tap on the dimmed space above a sheet closes it, as a scrim does.
    /// </summary>
    static void TapOutside(RectTransform panel, UnityEngine.Events.UnityAction call, string what)
    {
        // A sibling behind the sheet, not the panel the sheet is inside: a click
        // bubbles up to the nearest handler, so a button on the panel was also
        // pressed by every tap on the sheet's own words.
        Image own = panel.GetComponent<Image>();
        if (own != null)
            own.raycastTarget = false;

        RectTransform area = UIFactory.Rect("Outside", panel);
        UIFactory.Stretch(area);
        area.SetAsFirstSibling();

        Image catcher = area.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        Button outside = area.gameObject.AddComponent<Button>();
        outside.targetGraphic = catcher;
        outside.transition = Selectable.Transition.None;
        Wire(outside.onClick, call, what);
    }

    /// <summary>
    /// A door's title, as a sheet has it: 17px bold in the middle of a 48px bar,
    /// with the close circle at its right end.
    /// </summary>
    static TextMeshProUGUI SheetTitle(Transform card, string key)
    {
        RectTransform bar = UIFactory.Rect("TitleBar", card);
        UIFactory.Size(bar.gameObject, 133f);

        TextMeshProUGUI title = UIFactory.Text("Title", bar, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Center, key, FontStyles.Bold);
        UIFactory.Stretch(title.rectTransform);
        title.margin = new Vector4(150f, 0f, 150f, 0f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        return title;
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
        // A section header in the grouped-list manner: small, grey, capitals.
        // It was the accent colour, spaced wide, above every card's title -
        // a second heading saying what the first one already said.
        TextMeshProUGUI label = UIFactory.Text("Eyebrow", parent, Secondary, ThemeRole.TextMuted,
                                               TextAlignmentOptions.TopLeft, key);
        return label;
    }

    /// <summary>The one filled button on a card.</summary>
    static Button PrimaryAction(Transform parent, string name, string key,
                                UnityEngine.Events.UnityAction call, string what)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Size(rt.gameObject, Btn);

        Button button = UIFactory.Button(rt, ThemeRole.Accent, 1f, UIShape.Pill);
        Wire(button.onClick, call, what);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Center, key);
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
        UIFactory.Size(rt.gameObject, Tap);

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0f);
        Wire(button.onClick, call, what);

        TextMeshProUGUI label = UIFactory.Text("Label", rt, Body, ThemeRole.TextSecondary,
                                               TextAlignmentOptions.Center, key);
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
        UIFactory.Corner(rt, new Vector2(1f, 1f), new Vector2(122f, 122f),
                         new Vector2(-33f, -49f));
        Ignore(rt);

        Button button = UIFactory.Button(rt, ThemeRole.Fill, 1f, UIShape.Circle);
        Wire(button.onClick, call, what);
        Icon(rt, AppIcon.Close, 44f, ThemeRole.TextPrimary);

        // Every card corner close is a way out, so back agrees with it.
        button.gameObject.AddComponent<WayOut>();

        Swipeable(card, button);
        return button;
    }

    /// <summary>
    /// A sheet or card that swipes down to close, the way its grabber says it
    /// does. Dismissing presses the given way out, so whatever that button
    /// cleans up is cleaned up the same way.
    /// </summary>
    static void Swipeable(RectTransform sheet, Button wayOut)
    {
        SheetDragHandle swipe = sheet.gameObject.AddComponent<SheetDragHandle>();
        swipe.sheetRect = sheet;
        swipe.dismissDistance = 170f;
        swipe.pressOnDismiss = wayOut;
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
