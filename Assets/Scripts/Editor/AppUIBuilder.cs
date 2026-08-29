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

    // Everything the builder owns under the Canvas. Removed and rebuilt every run.
    // Old names are kept in the list so a scene built by an earlier version is
    // cleaned up rather than left with two of everything.
    static readonly string[] GeneratedNames =
    {
        "MapPanel", "CameraPanel", "BrowsePanel", "SearchPanel", "SettingsPanel",
        "InfoPanel", "InfoSheet", "BottomNav",
        "NearbyButton", "VpsIndicator", "NearbyPanel", "LiveARView", "ReadyStateView",
        "SearchContent", "SettingsContent"
    };

    static List<string> _notes;

    [MenuItem("Tools/UI/Build App UI")]
    public static void Build()
    {
        _notes = new List<string>();

        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Build App UI", "No Canvas in the open scene.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Build App UI",
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

        UserLocationService location = BuildServices();

        // Order in the hierarchy is draw order: panels first, then the sheet that
        // has to float over any of them, then the nav bar on top of everything.
        RectTransform mapPanel = Panel(root, "MapPanel");
        RectTransform cameraPanel = Panel(root, "CameraPanel", transparent: true);
        RectTransform browsePanel = Panel(root, "BrowsePanel");
        RectTransform settingsPanel = Panel(root, "SettingsPanel");

        InfoPanelController info = BuildInfoSheet(root, location);

        MapTabController mapTab = BuildMapTab(mapPanel, location, pinPrefab, info);
        CameraTabController cameraTab = BuildCameraTab(cameraPanel, location, rowPrefab, info);
        SearchTabController browseTab = BuildBrowseTab(browsePanel, location, rowPrefab, pillPrefab);
        SettingsTabController settingsTab = BuildSettingsTab(settingsPanel);

        // BuildNav returns the padded row the buttons go in. The bar itself is its
        // parent, and that is what the tab controller shows and hides.
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

        // Markers come from the Supabase RPC, not from a spreadsheet and not from
        // assets in the build. RemoteBuildingSource is the old path and is
        // deliberately not added.
        go.AddComponent<RemoteMarkerSource>();

        UserLocationService location = go.AddComponent<UserLocationService>();
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
    static InfoPanelController BuildInfoSheet(Transform root, UserLocationService location)
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

        RectTransform sheet = UIFactory.Rect("Sheet", sheetSafe);
        UIFactory.BottomStretch(sheet, 1040f, Nav);
        UIFactory.Fill(sheet, ThemeRole.Surface, 1f, UIShape.Rounded).raycastTarget = true;
        controller.panelRoot = sheet.gameObject;

        SheetAnimator animator = sheet.gameObject.AddComponent<SheetAnimator>();
        animator.openY = Nav;
        animator.duration = 0.25f;
        controller.sheet = animator;

        SheetDragHandle drag = AddDragArea(sheet, animator);
        Wire(drag.onDismissed, controller.ClosePanel, "Marker sheet drag");

        RectTransform body = UIFactory.Rect("Body", sheet);
        UIFactory.Stretch(body);
        body.offsetMin = new Vector2(Margin, Margin);
        body.offsetMax = new Vector2(-Margin, -110f);
        UIFactory.VerticalStack(body, 18f, false);

        // Identity: the marker's own colour beside its name, so this sheet and the
        // thing standing in the street are recognisably the same object.
        RectTransform head = UIFactory.Rect("Head", body);
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
        RectTransform distanceRow = UIFactory.Rect("Distance", body);
        UIFactory.Size(distanceRow.gameObject, 54f);
        UIFactory.HorizontalStack(distanceRow, 14f);

        controller.bearingArrow = Arrow(distanceRow, location, BearingArrow.Frame.NorthUp, 40f);

        controller.distanceText = UIFactory.Text("Value", distanceRow, Body, ThemeRole.Accent,
                                                 TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.distanceText.gameObject, 54f);
        UIFactory.Flexible(controller.distanceText.gameObject);

        controller.metaText = UIFactory.Text("MetaText", body, Secondary, ThemeRole.TextSecondary,
                                             TextAlignmentOptions.Left);
        UIFactory.Size(controller.metaText.gameObject, 46f);

        controller.descriptionText = UIFactory.Text("DescriptionText", body, Body,
                                                    ThemeRole.TextSecondary,
                                                    TextAlignmentOptions.TopLeft);
        LayoutElement descriptionSize = UIFactory.Size(controller.descriptionText.gameObject, 280f);
        descriptionSize.flexibleHeight = 1f;

        // Guide is the primary action and gets the full width to itself. The rest
        // are a quieter row underneath.
        RectTransform guide = UIFactory.Rect("GuideButton", body);
        UIFactory.Size(guide.gameObject, Tap);
        controller.guideButton = UIFactory.Button(guide, ThemeRole.Accent, 1f, UIShape.Rounded);
        Unthemed(guide.gameObject);

        RectTransform guideInner = UIFactory.Rect("Inner", guide);
        UIFactory.Stretch(guideInner);
        HorizontalLayoutGroup guideLayout = UIFactory.HorizontalStack(guideInner, 16f);
        guideLayout.childAlignment = TextAnchor.MiddleCenter;

        RectTransform guideIcon = UIFactory.Rect("Icon", guideInner);
        LayoutElement guideIconSize = UIFactory.Size(guideIcon.gameObject, 40f, 40f);
        guideIconSize.flexibleWidth = 0f;
        UIFactory.Plain(guideIcon, ThemeManager.Get(ThemeRole.OnAccent), UIShape.Triangle)
            .raycastTarget = false;

        controller.guideLabel = UIFactory.Text("Label", guideInner, Body, ThemeRole.OnAccent,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        LayoutElement guideLabelSize = UIFactory.Size(controller.guideLabel.gameObject, Tap, 420f);
        guideLabelSize.flexibleWidth = 0f;
        Unthemed(controller.guideLabel.gameObject);
        controller.guideLabel.text = AppStrings.Get("guide.start");

        RectTransform actions = UIFactory.Rect("Actions", body);
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
        UIFactory.Cross(close, ThemeRole.TextSecondary, 34f, 4f);

        // The animator hides the sheet by moving it, not by disabling it, so this
        // object stays enabled or ShowPanel would have nothing to reveal.
        sheet.anchoredPosition = new Vector2(0f, Nav - sheet.rect.height - 40f);

        EditorUtility.SetDirty(controller);
        return controller;
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

        MapView map = surface.gameObject.AddComponent<MapView>();
        map.locationService = location;
        map.pinPrefab = pinPrefab;
        map.metresAcross = 400f;
        map.minMetresAcross = 60f;
        map.maxMetresAcross = 4000f;
        controller.map = map;

        RectTransform content = UIFactory.Rect("Content", surface);
        UIFactory.Stretch(content);
        map.contentParent = content;

        // Rings first, so everything else draws over them.
        map.distanceRings = new[]
        {
            RingImage(content, "RingNear"),
            RingImage(content, "RingFar")
        };

        RectTransform accuracy = UIFactory.Rect("Accuracy", content);
        UIFactory.Corner(accuracy, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), Vector2.zero);
        map.accuracyCircle = UIFactory.Plain(accuracy, ThemeManager.Get(ThemeRole.Accent),
                                             UIShape.Circle);
        map.accuracyCircle.raycastTarget = false;

        RectTransform ray = UIFactory.Rect("GuidanceRay", content);
        UIFactory.Corner(ray, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), Vector2.zero);
        UIFactory.Plain(ray, ThemeManager.Get(ThemeRole.Accent), UIShape.Pill).raycastTarget = false;
        map.guidanceRay = ray;

        // The reader's own dot is drawn last, so it is never buried under a pin.
        RectTransform dot = UIFactory.Rect("UserDot", content);
        UIFactory.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(52f, 52f), Vector2.zero);
        UIFactory.Fill(dot, ThemeRole.Background, 1f, UIShape.Circle).raycastTarget = false;

        RectTransform dotInner = UIFactory.Rect("Inner", dot);
        UIFactory.Corner(dotInner, new Vector2(0.5f, 0.5f), new Vector2(34f, 34f), Vector2.zero);
        UIFactory.Fill(dotInner, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;
        map.userDot = dot;

        RectTransform safe = SafeContent(panel);

        // --- top bar ---
        RectTransform top = UIFactory.Rect("TopBar", safe);
        UIFactory.TopStretch(top, 170f);

        controller.titleText = UIFactory.Text("Title", top, Title, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, "tab.map", FontStyles.Bold);
        UIFactory.Corner(controller.titleText.rectTransform, new Vector2(0f, 0.5f),
                         new Vector2(500f, 70f), new Vector2(Margin, -14f));

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

        RectTransform recentreGlyph = UIFactory.Rect("Glyph", recentreRow);
        LayoutElement recentreGlyphSize = UIFactory.Size(recentreGlyph.gameObject, 40f, 40f);
        recentreGlyphSize.flexibleWidth = 0f;
        UIFactory.Fill(recentreGlyph, ThemeRole.Accent, 1f, UIShape.Ring).raycastTarget = false;

        RectTransform recentreCore = UIFactory.Rect("Core", recentreGlyph);
        UIFactory.Corner(recentreCore, new Vector2(0.5f, 0.5f), new Vector2(14f, 14f), Vector2.zero);
        UIFactory.Fill(recentreCore, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;

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
        controller.browseLabel = UIFactory.Text("Label", browse, Secondary, ThemeRole.TextPrimary,
                                                TextAlignmentOptions.Center, "map.browse",
                                                FontStyles.Bold);
        UIFactory.Stretch(controller.browseLabel.rectTransform);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static Image RingImage(Transform parent, string name)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Corner(rt, new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), Vector2.zero);

        Image image = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Line), UIShape.Ring);
        image.raycastTarget = false;
        return image;
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

        RectTransform tip = UIFactory.Rect("Tip", holder);
        UIFactory.Corner(tip, new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);
        Image glyph = UIFactory.Plain(tip, ThemeManager.Get(ThemeRole.Accent), UIShape.Triangle);
        glyph.raycastTarget = false;

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

        StateCardView view = rt.gameObject.AddComponent<StateCardView>();
        view.root = rt.gameObject;

        RectTransform iconRect = UIFactory.Rect("Icon", rt);
        LayoutElement iconSize = UIFactory.Size(iconRect.gameObject, 56f, 56f);
        iconSize.flexibleWidth = 0f;
        view.icon = UIFactory.Plain(iconRect, ThemeManager.Get(ThemeRole.TextMuted), UIShape.Ring);
        view.icon.raycastTarget = false;

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

        RectTransform safe = SafeContent(live);

        controller.nearbyPanel = BuildNearbyPanel(live, location, rowPrefab);
        controller.vpsIndicator = BuildVpsIndicator(safe, location);

        // Everything over the feed sits on a solid surface, never on glass. A camera
        // feed is the one background the app does not control, and translucent
        // chrome over it is unreadable roughly half the time.
        RectTransform nearby = UIFactory.Rect("NearbyButton", safe);
        UIFactory.Corner(nearby, new Vector2(0f, 1f), new Vector2(360f, Tap),
                         new Vector2(Margin, -Margin));
        controller.nearbyButton = UIFactory.Button(nearby, ThemeRole.Surface, 1f, UIShape.Pill);

        RectTransform pin = UIFactory.Rect("Pin", nearby);
        UIFactory.Corner(pin, new Vector2(0f, 0.5f), new Vector2(24f, 24f), new Vector2(32f, 0f));
        UIFactory.Fill(pin, ThemeRole.Accent, 1f, UIShape.Circle).raycastTarget = false;

        controller.nearbyCountText = UIFactory.Text("Count", nearby, Secondary,
                                                    ThemeRole.TextPrimary,
                                                    TextAlignmentOptions.Left);
        UIFactory.Stretch(controller.nearbyCountText.rectTransform);
        controller.nearbyCountText.rectTransform.offsetMin = new Vector2(70f, 0f);
        controller.nearbyCountText.rectTransform.offsetMax = new Vector2(-24f, 0f);
        controller.nearbyCountText.text = string.Format(AppStrings.Get("camera.nearbyCount"), 0);

        BuildWarningBanner(safe, controller);
        BuildEdgePills(safe, location, controller);

        // --- bottom stack: guidance above the dock, both above the nav bar ---
        RectTransform dockArea = UIFactory.Rect("Panel", safe);
        UIFactory.BottomStretch(dockArea, 0f, Nav + 20f);
        dockArea.sizeDelta = new Vector2(-Margin * 2f, 0f);
        VerticalLayoutGroup dockStack = UIFactory.VerticalStack(dockArea, 16f);
        dockStack.childAlignment = TextAnchor.LowerCenter;

        controller.guidance = BuildGuidanceBanner(dockArea, location,
                                                  BearingArrow.Frame.DeviceRelative);

        RectTransform dock = UIFactory.Rect("Dock", dockArea);
        UIFactory.Size(dock.gameObject, 150f);
        controller.dockButton = UIFactory.Button(dock, ThemeRole.Surface, 1f, UIShape.Rounded);
        controller.dock = dock.gameObject;

        RectTransform dockRow = UIFactory.Rect("Row", dock);
        UIFactory.Stretch(dockRow);
        dockRow.offsetMin = new Vector2(28f, 0f);
        dockRow.offsetMax = new Vector2(-28f, 0f);
        UIFactory.HorizontalStack(dockRow, 22f);

        controller.dockRing = Disc(dockRow, 52f, ThemeRole.Line, UIShape.Ring,
                                   out RectTransform dockRingRect);
        controller.dockDot = Disc(dockRingRect, 32f, ThemeRole.Accent, UIShape.Circle,
                                  out RectTransform _);

        controller.dockName = UIFactory.Text("Name", dockRow, Body, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(controller.dockName.gameObject, 62f);
        UIFactory.Flexible(controller.dockName.gameObject);

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
        banner.anchoredPosition = new Vector2(0f, -(Margin + Tap + 20f));
        UIFactory.Fill(banner, ThemeRole.Surface, 1f, UIShape.Rounded);

        // A coloured stripe rather than a coloured background: the message has to
        // stay readable, and status is carried by shape as well as by hue.
        RectTransform stripe = UIFactory.Rect("Stripe", banner);
        UIFactory.Corner(stripe, new Vector2(0f, 0.5f), new Vector2(10f, 80f),
                         new Vector2(18f, 0f));
        UIFactory.Fill(stripe, ThemeRole.StatusFair, 1f, UIShape.Pill).raycastTarget = false;

        controller.warningLabel = UIFactory.Text("Label", banner, Secondary, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Left);
        UIFactory.Stretch(controller.warningLabel.rectTransform);
        controller.warningLabel.rectTransform.offsetMin = new Vector2(48f, 0f);
        controller.warningLabel.rectTransform.offsetMax = new Vector2(-(Tap + 10f), 0f);
        controller.warningLabel.text = AppStrings.Get("live.warning");

        RectTransform dismiss = UIFactory.Rect("Dismiss", banner);
        UIFactory.Corner(dismiss, new Vector2(1f, 0.5f), new Vector2(Tap, Tap),
                         new Vector2(-6f, 0f));
        controller.warningDismissButton =
            UIFactory.Button(dismiss, ThemeRole.Surface, 0f, UIShape.Circle);
        UIFactory.Cross(dismiss, ThemeRole.TextSecondary, 30f, 4f);

        controller.warningBanner = banner.gameObject;
        banner.gameObject.SetActive(false);
    }

    static void BuildEdgePills(RectTransform parent, UserLocationService location,
                               CameraTabController controller)
    {
        RectTransform holder = UIFactory.Rect("EdgePills", parent);
        UIFactory.Stretch(holder);

        CameraEdgePills pills = holder.gameObject.AddComponent<CameraEdgePills>();
        pills.locationService = location;
        pills.left = EdgePill(holder, "Left", true);
        pills.right = EdgePill(holder, "Right", false);

        controller.edgePills = pills;
    }

    /// <summary>
    /// One side tab, anchored to the middle of its edge where a thumb reaches
    /// without the hand leaving the phone.
    /// </summary>
    static CameraEdgePills.Pill EdgePill(RectTransform parent, string name, bool onLeft)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Corner(rt, new Vector2(onLeft ? 0f : 1f, 0.5f),
                         new Vector2(380f, Tap), Vector2.zero);

        CameraEdgePills.Pill pill = new CameraEdgePills.Pill();
        pill.root = rt.gameObject;
        pill.button = UIFactory.Button(rt, ThemeRole.Surface, 1f, UIShape.Rounded);

        RectTransform row = UIFactory.Rect("Row", rt);
        UIFactory.Stretch(row);
        row.offsetMin = new Vector2(onLeft ? 26f : 18f, 0f);
        row.offsetMax = new Vector2(onLeft ? -18f : -26f, 0f);
        HorizontalLayoutGroup layout = UIFactory.HorizontalStack(row, 14f);
        layout.reverseArrangement = !onLeft;

        pill.ring = Disc(row, 42f, ThemeRole.Line, UIShape.Ring, out RectTransform ringRect);
        pill.dot = Disc(ringRect, 24f, ThemeRole.Accent, UIShape.Circle, out RectTransform _);

        RectTransform text = UIFactory.Rect("Text", row);
        UIFactory.Size(text.gameObject, 100f);
        UIFactory.Flexible(text.gameObject);
        UIFactory.VerticalStack(text, 0f, false);

        TextAlignmentOptions align = onLeft ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;

        pill.label = UIFactory.Text("Name", text, Secondary, ThemeRole.TextPrimary, align,
                                    null, FontStyles.Bold);
        UIFactory.Size(pill.label.gameObject, 50f);

        pill.distance = UIFactory.Text("Distance", text, Caption, ThemeRole.TextMuted, align);
        UIFactory.Size(pill.distance.gameObject, 42f);

        rt.gameObject.SetActive(false);
        return pill;
    }

    static VpsIndicatorController BuildVpsIndicator(RectTransform parent,
                                                    UserLocationService location)
    {
        RectTransform rt = UIFactory.Rect("VpsIndicator", parent);
        UIFactory.Stretch(rt);

        RectTransform dotButton = UIFactory.Rect("DotButton", rt);
        UIFactory.Corner(dotButton, new Vector2(1f, 1f), new Vector2(Tap, Tap),
                         new Vector2(-Margin, -Margin));
        Button button = UIFactory.Button(dotButton, ThemeRole.Surface, 1f, UIShape.Circle);

        RectTransform dot = UIFactory.Rect("Dot", dotButton);
        UIFactory.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(44f, 44f), Vector2.zero);
        Image dotImage = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.StatusNone),
                                         UIShape.Circle);
        dotImage.raycastTarget = false;

        // The bubble sits under the button rather than taking the screen - the
        // reader is holding the phone up at a street while reading it.
        RectTransform bubble = UIFactory.Rect("Bubble", rt);
        bubble.anchorMin = new Vector2(0f, 1f);
        bubble.anchorMax = new Vector2(1f, 1f);
        bubble.pivot = new Vector2(0.5f, 1f);
        bubble.sizeDelta = new Vector2(-(200f + Margin), 0f);
        bubble.anchoredPosition = new Vector2((200f - Margin) * 0.5f, -(Margin + Tap + 16f));
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
        UIFactory.Cross(closeRect, ThemeRole.TextSecondary, 32f, 4f);

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
        UIFactory.Card(appearance);
        controller.themeRowLabel = RowLabel(appearance, "settings.theme",
                                            out RectTransform themeRow);
        controller.themeToggle = UIFactory.Switch("Toggle", themeRow);
        controller.occlusionRowLabel = RowLabel(appearance, "settings.occlusion",
                                                out RectTransform occlusionRow);
        controller.occlusionToggle = UIFactory.Switch("Toggle", occlusionRow);

        controller.languageSectionLabel = SectionLabel(content, "settings.language");
        RectTransform language = UIFactory.Rect("LanguageCard", content);
        UIFactory.Card(language);
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
        UIFactory.Card(privacy);
        controller.hiddenRowLabel = RowLabel(privacy, "settings.hidden",
                                             out RectTransform hiddenRow);
        controller.hiddenCountLabel = UIFactory.Text("Count", hiddenRow, Secondary,
                                                     ThemeRole.TextMuted,
                                                     TextAlignmentOptions.Right);
        LayoutElement hiddenCountSize = UIFactory.Size(controller.hiddenCountLabel.gameObject,
                                                        84f, 260f);
        hiddenCountSize.flexibleWidth = 0f;

        RectTransform unhide = UIFactory.Rect("UnhideAll", privacy);
        UIFactory.Size(unhide.gameObject, Tap);
        controller.unhideAllButton = UIFactory.Button(unhide, ThemeRole.SurfaceAlt, 1f,
                                                      UIShape.Rounded);
        controller.unhideAllLabel = UIFactory.Text("Label", unhide, Secondary,
                                                   ThemeRole.TextPrimary,
                                                   TextAlignmentOptions.Center,
                                                   "settings.unhideAll", FontStyles.Bold);
        UIFactory.Stretch(controller.unhideAllLabel.rectTransform);

        controller.notificationsLabel = SectionLabel(content, "settings.notifications");
        RectTransform notifications = UIFactory.Rect("NotificationsCard", content);
        UIFactory.Card(notifications);
        controller.nearbyRowLabel = RowLabel(notifications, "settings.nearbyBuildings",
                                             out RectTransform nearbyRow);
        controller.nearbyToggle = UIFactory.Switch("Toggle", nearbyRow);

        controller.helpLabel = SectionLabel(content, "settings.help");
        RectTransform help = UIFactory.Rect("HelpCard", content);
        UIFactory.Card(help);
        controller.tutorialButton = HelpRow(help, "settings.replayTutorial",
                                            out controller.tutorialRowLabel);
        controller.sourcesButton = HelpRow(help, "settings.dataSources",
                                           out controller.sourcesRowLabel);
        controller.aboutButton = HelpRow(help, "settings.about", out controller.aboutRowLabel);

        RectTransform versionRow = UIFactory.Rect("Version", content);
        UIFactory.Size(versionRow.gameObject, Tap);
        controller.versionButton = UIFactory.Button(versionRow, ThemeRole.Background, 0f);
        controller.versionLabel = UIFactory.Text("Label", versionRow, Caption,
                                                 ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Center);
        UIFactory.Stretch(controller.versionLabel.rectTransform);

        controller.tutorialPage = SubPage(rt, "TutorialPage", "settings.replayTutorial",
                                          "sub.tutorial.body");
        controller.sourcesPage = SubPage(rt, "SourcesPage", "settings.dataSources",
                                         "sub.sources.body");
        controller.aboutPage = SubPage(rt, "AboutPage", "settings.about", "sub.about.body");

        navigator.pages = new List<GameObject>
        {
            controller.tutorialPage, controller.sourcesPage, controller.aboutPage
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

    static TextMeshProUGUI RowLabel(Transform card, string key, out RectTransform row)
    {
        row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, 88f);
        UIFactory.HorizontalStack(row, 18f, false);

        TextMeshProUGUI label = UIFactory.Text("Label", row, Body, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Size(label.gameObject, 88f);
        UIFactory.Flexible(label.gameObject);
        return label;
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

        // A drawn chevron, not a typed one: the font atlas has no arrows, and a
        // glyph it does not have renders as an empty box on device.
        RectTransform chevron = UIFactory.Rect("Chevron", row);
        UIFactory.Corner(chevron, new Vector2(1f, 0.5f), new Vector2(24f, 24f),
                         new Vector2(-12f, 0f));
        UIFactory.Fill(chevron, ThemeRole.TextMuted, 1f, UIShape.Triangle).raycastTarget = false;
        chevron.localRotation = Quaternion.Euler(0f, 0f, -90f);

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

        RectTransform line = UIFactory.Rect("TopLine", nav);
        UIFactory.TopStretch(line, 2f);
        UIFactory.Fill(line, ThemeRole.Line).raycastTarget = false;

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
                               out tabs.mapLabel, out tabs.mapIcon);
        Button camera = NavButton(navRow, "CameraTabButton", "tab.camera", NavGlyph.Camera,
                                  out tabs.cameraLabel, out tabs.cameraIcon);
        Button settings = NavButton(navRow, "SettingsTabButton", "tab.settings",
                                    NavGlyph.Settings,
                                    out tabs.settingsLabel, out tabs.settingsIcon);

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

    static Button NavButton(RectTransform navRow, string name, string key, NavGlyph glyph,
                            out TMP_Text label, out Graphic[] icon)
    {
        RectTransform rt = UIFactory.Rect(name, navRow);

        // Invisible fill: the nav bar behind provides the surface, but the button
        // still needs a graphic to receive taps across its whole width.
        Image background = UIFactory.Plain(rt, new Color(1f, 1f, 1f, 0f));

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        UIFactory.Feedback(button);

        RectTransform iconRect = UIFactory.Rect("Icon", rt);
        UIFactory.Corner(iconRect, new Vector2(0.5f, 1f), new Vector2(58f, 58f),
                         new Vector2(0f, -14f));

        icon = DrawGlyph(iconRect, glyph);

        TextMeshProUGUI text = UIFactory.Text("Label", rt, Caption, ThemeRole.TextMuted,
                                              TextAlignmentOptions.Center, key);
        UIFactory.Corner(text.rectTransform, new Vector2(0.5f, 0f), new Vector2(240f, 44f),
                         new Vector2(0f, 20f));

        // TabNavigationController colours this from which tab is open, so it must
        // not also be themed automatically - the two would overwrite each other.
        Unthemed(text.gameObject);

        label = text;
        return button;
    }

    /// <summary>
    /// Nav icons drawn from primitives rather than typed from an icon font. The
    /// project's font atlas is Latin and Cyrillic only, so a Material Icons glyph
    /// renders as an empty box on device.
    ///
    /// Every piece is handed back, because the active-tab colour has to reach all
    /// of them - a UI Image does not inherit its parent's colour.
    /// </summary>
    static Graphic[] DrawGlyph(RectTransform holder, NavGlyph glyph)
    {
        switch (glyph)
        {
            case NavGlyph.Map:
            {
                // A pin: a ring over a downward point.
                RectTransform ring = UIFactory.Rect("Ring", holder);
                UIFactory.Corner(ring, new Vector2(0.5f, 1f), new Vector2(40f, 40f), Vector2.zero);
                Image ringImage = UIFactory.Plain(ring, Color.white, UIShape.Ring);
                ringImage.raycastTarget = false;

                RectTransform tip = UIFactory.Rect("Tip", holder);
                UIFactory.Corner(tip, new Vector2(0.5f, 0f), new Vector2(24f, 24f), Vector2.zero);
                Image tipImage = UIFactory.Plain(tip, Color.white, UIShape.Triangle);
                tipImage.raycastTarget = false;
                tip.localRotation = Quaternion.Euler(0f, 0f, 180f);

                return new Graphic[] { ringImage, tipImage };
            }

            case NavGlyph.Camera:
            {
                RectTransform bodyRect = UIFactory.Rect("Body", holder);
                UIFactory.Corner(bodyRect, new Vector2(0.5f, 0.5f), new Vector2(54f, 42f),
                                 new Vector2(0f, -4f));
                Image bodyImage = UIFactory.Plain(bodyRect, Color.white, UIShape.Rounded);
                bodyImage.raycastTarget = false;

                RectTransform lens = UIFactory.Rect("Lens", holder);
                UIFactory.Corner(lens, new Vector2(0.5f, 0.5f), new Vector2(22f, 22f),
                                 new Vector2(0f, -4f));
                Image lensImage = UIFactory.Plain(lens, Color.white, UIShape.Ring);
                lensImage.raycastTarget = false;

                RectTransform bump = UIFactory.Rect("Bump", holder);
                UIFactory.Corner(bump, new Vector2(0.5f, 1f), new Vector2(24f, 12f),
                                 new Vector2(-9f, 0f));
                Image bumpImage = UIFactory.Plain(bump, Color.white, UIShape.Pill);
                bumpImage.raycastTarget = false;

                return new Graphic[] { bodyImage, lensImage, bumpImage };
            }

            default:
            {
                // Three sliders, which reads as settings without needing a gear.
                Graphic[] bars = new Graphic[3];
                float[] widths = { 48f, 34f, 42f };

                for (int i = 0; i < 3; i++)
                {
                    RectTransform bar = UIFactory.Rect("Bar" + i, holder);
                    UIFactory.Corner(bar, new Vector2(0.5f, 0.5f), new Vector2(widths[i], 8f),
                                     new Vector2(0f, 16f - i * 16f));

                    Image image = UIFactory.Plain(bar, Color.white, UIShape.Pill);
                    image.raycastTarget = false;
                    bars[i] = image;
                }

                return bars;
            }
        }
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
