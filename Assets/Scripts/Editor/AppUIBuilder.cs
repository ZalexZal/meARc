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
/// Builds the whole three-tab interface into the open scene and wires every
/// control to its controller.
///
/// Safe to re-run: anything it generated is identified by name, removed, and built
/// again. It never touches the AR side of the scene - no anchors, no prefabs, no
/// session configuration - and it leaves the existing InfoPanel's own references
/// alone, only adding what's missing.
/// </summary>
public static class AppUIBuilder
{
    const float Nav = 160f;
    const float SheetHeight = 820f;
    const float Margin = 44f;

    const string PrefabFolder = "Assets/Prefabs";
    const string RowPrefabPath = PrefabFolder + "/BuildingRow.prefab";
    const string PillPrefabPath = PrefabFolder + "/FilterPill.prefab";
    const string MarkerPrefabPath = PrefabFolder + "/BuildingMarker.prefab";

    // Everything the builder owns. Removed and rebuilt on every run.
    static readonly string[] GeneratedNames =
    {
        "NearbyButton", "VpsIndicator", "NearbyPanel", "LiveARView", "ReadyStateView",
        "SearchContent", "SettingsContent", "AppServices"
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

        Transform root = canvas.transform;

        RectTransform cameraPanel = Find(root, "CameraPanel");
        RectTransform searchPanel = Find(root, "SearchPanel");
        RectTransform settingsPanel = Find(root, "SettingsPanel");
        RectTransform bottomNav = Find(root, "BottomNav");
        RectTransform infoPanel = Find(root, "InfoPanel");

        if (cameraPanel == null || searchPanel == null || settingsPanel == null)
        {
            EditorUtility.DisplayDialog("Build App UI",
                "Missing one of CameraPanel / SearchPanel / SettingsPanel.\n\n" +
                "Run Tools > UI > Lay Out App UI first.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Build App UI");
        int group = Undo.GetCurrentGroup();

        try
        {
            BuildAll(root, cameraPanel, searchPanel, settingsPanel, bottomNav, infoPanel);
        }
        catch (System.Exception exception)
        {
            // Everything this tool owns is destroyed before it is rebuilt, so a
            // failure half way through would otherwise leave the scene stripped.
            // Rolling the undo group back restores what was there.
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
            ? "Built the Camera, Search and Settings tabs and wired every control.\n\n" +
              "Press Ctrl+S, then Play."
            : "Built, with " + _notes.Count + " note(s):\n\n" + string.Join("\n", _notes);

        Debug.Log("[App UI] " + report);
        EditorUtility.DisplayDialog("Build App UI", report, "OK");
    }

    static void BuildAll(Transform root, RectTransform cameraPanel, RectTransform searchPanel,
                         RectTransform settingsPanel, RectTransform bottomNav,
                         RectTransform infoPanel)
    {
        RemoveGenerated(root);
        EnsureFolder();

        BuildingRowView rowPrefab = CreateRowPrefab();
        FilterPillView pillPrefab = CreatePillPrefab();

        UserLocationService location = BuildServices();

        RectTransform liveView = CreateLiveView(cameraPanel);

        InfoPanelController info = UpgradeInfoPanel(infoPanel, liveView);
        NearbyPanelController nearby = BuildNearbyPanel(liveView, location, rowPrefab);
        VpsIndicatorController vps = BuildVpsIndicator(liveView, location);
        ReadyStateView ready = BuildReadyState(cameraPanel, location, rowPrefab);

        CameraTabController camera = WireCameraTab(
            cameraPanel, liveView, ready, location, nearby, info, vps);

        GateTapInput(liveView);
        OrderLiveView(nearby, info);

        SearchTabController search = BuildSearchTab(searchPanel, location, rowPrefab, pillPrefab);

        // Serialized references, not an Action - Action is not a serializable type,
        // so a callback assigned here would be discarded when the scene saves and
        // tapping a result would silently do nothing.
        if (search != null)
        {
            search.cameraTab = camera;
            search.tabs = Object.FindFirstObjectByType<TabNavigationController>(
                FindObjectsInactive.Include);
            EditorUtility.SetDirty(search);
        }

        BuildSettingsTab(settingsPanel);
        ThemeShell(searchPanel, settingsPanel, bottomNav, infoPanel);

        AddFade(searchPanel.gameObject);
        AddFade(settingsPanel.gameObject);
        if (ready != null)
            AddFade(ready.gameObject, 18f);
        LocalizeNav(bottomNav);
    }

    // ----------------------------------------------------------------- services

    static UserLocationService BuildServices()
    {
        GameObject go = new GameObject("AppServices");
        Undo.RegisterCreatedObjectUndo(go, "Build App UI");

        // OnApplicationPause never fires on a disabled object, and the Camera tab
        // is disabled whenever the reader is on another tab - so this lives here.
        go.AddComponent<AppLifecycleWatcher>();

        // Buildings come from the published sheet now, not from assets in the build.
        go.AddComponent<RemoteBuildingSource>();

        UserLocationService location = go.AddComponent<UserLocationService>();
        location.earthManager = Object.FindFirstObjectByType<AREarthManager>(FindObjectsInactive.Include);

        if (location.earthManager == null)
        {
            _notes.Add("No AREarthManager found - distances and the calibration dot " +
                       "will stay inactive until one exists on the XR Origin.");
        }

        BuildMarkerSpawner(go, location);
        BuildOcclusion(go);

        return location;
    }

    /// <summary>
    /// Markers are created from coordinates at runtime, so nothing has to be placed
    /// in the Editor any more. Sits on the services object so it survives tab
    /// switches - anchors should keep resolving while the reader is in Search.
    /// </summary>
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

        // A marker placed by hand in the Editor would now be joined by a second one
        // spawned from the sheet, giving every such building two markers.
        TappableObject leftover =
            Object.FindFirstObjectByType<TappableObject>(FindObjectsInactive.Include);

        if (leftover != null)
        {
            _notes.Add($"\"{leftover.name}\" is a hand-placed marker still in the scene. " +
                       "Delete it, or that building will show two markers.");
        }
    }

    /// <summary>
    /// Streetscape meshes become invisible colliders that markers are tested
    /// against, plus the depth subsystem for anything close to the camera.
    /// </summary>
    static void BuildOcclusion(GameObject services)
    {
        OcclusionService occlusion = services.AddComponent<OcclusionService>();

        occlusion.streetscapeManager = EnsureStreetscapeManager();

        // The depth manager belongs on the AR camera, next to the background renderer.
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

    /// <summary>
    /// Adds the streetscape manager back to the XR Origin and switches the mode on
    /// in the session config. Both are needed: the manager surfaces the meshes, and
    /// the config decides whether ARCore produces any.
    /// </summary>
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
        view.billboard = false; // a cube reads the same from every angle

        temp.AddComponent<TappableObject>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, MarkerPrefabPath);
        Object.DestroyImmediate(temp);

        return saved;
    }

    // --------------------------------------------------------------- info panel

    static InfoPanelController UpgradeInfoPanel(RectTransform infoPanel, RectTransform liveView)
    {
        if (infoPanel == null)
        {
            _notes.Add("No InfoPanel found - the info sheet was left untouched.");
            return null;
        }

        InfoPanelController controller = infoPanel.GetComponent<InfoPanelController>();
        if (controller == null)
        {
            _notes.Add("InfoPanel has no InfoPanelController.");
            return null;
        }

        // The sheet slides from below the nav bar, which is where Lay Out App UI
        // already parked it.
        SheetAnimator animator = infoPanel.GetComponent<SheetAnimator>();
        if (animator == null)
            animator = Undo.AddComponent<SheetAnimator>(infoPanel.gameObject);

        animator.openY = Nav;
        animator.duration = 0.25f;
        controller.sheet = animator;

        RectTransform oldGrab = Find(infoPanel, "Grabber");
        if (oldGrab != null)
            Undo.DestroyObjectImmediate(oldGrab.gameObject);

        RectTransform oldDrag = Find(infoPanel, "DragArea");
        if (oldDrag != null)
            Undo.DestroyObjectImmediate(oldDrag.gameObject);

        SheetDragHandle infoDrag = AddDragArea(infoPanel, animator);
        Wire(infoDrag.onDismissed,
             new UnityEngine.Events.UnityAction(controller.ClosePanel), "Info sheet drag");

        // The info sheet belongs to the live view, so it disappears with it when
        // the tab drops back to the Ready State.
        if (liveView != null && infoPanel.parent != liveView)
            Undo.SetTransformParent(infoPanel, liveView, "Build App UI");

        // The animator hides the sheet by moving it, not by disabling it, so the
        // object has to stay enabled or ShowPanel has nothing to reveal.
        infoPanel.gameObject.SetActive(true);
        infoPanel.anchoredPosition = new Vector2(0f, Nav - infoPanel.rect.height - 40f);

        // The sheet's two buttons come from the original scene and were still
        // hard-edged rectangles, because nothing had given them a sprite.
        RoundSheetButton(infoPanel, "LearnMoreButton", ThemeRole.Accent);
        RoundSheetButton(infoPanel, "CloseButton", ThemeRole.SurfaceAlt);

        controller.scrimButton = CreateScrim("InfoScrim", liveView, out GameObject infoScrim);
        controller.scrim = infoScrim;

        controller.learnMoreLabel = FindLabelIn(infoPanel, "LearnMoreButton", "info.learnMore");
        controller.closeLabel = FindLabelIn(infoPanel, "CloseButton", "info.close");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void RoundSheetButton(Transform parent, string childName, ThemeRole role)
    {
        RectTransform button = Find(parent, childName);
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        if (image == null)
            image = Undo.AddComponent<Image>(button.gameObject);

        UIFactory.ApplyShape(image, UIShape.Rounded);
        image.color = ThemeManager.Get(role);

        ThemedElement themed = button.GetComponent<ThemedElement>();
        if (themed == null)
            themed = Undo.AddComponent<ThemedElement>(button.gameObject);

        themed.role = role;
        themed.Apply();
    }

    static TextMeshProUGUI FindLabelIn(Transform parent, string childName, string key)
    {
        RectTransform child = Find(parent, childName);
        if (child == null)
            return null;

        TextMeshProUGUI label = child.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            return null;

        LocalizedLabel localized = label.GetComponent<LocalizedLabel>();
        if (localized == null)
            localized = Undo.AddComponent<LocalizedLabel>(label.gameObject);

        localized.key = key;
        label.text = AppStrings.Get(key);
        return label;
    }

    // -------------------------------------------------------------- camera tab

    /// <summary>
    /// Transparent container for everything that sits over the live feed. Grouping
    /// it means the Ready State can hide the whole live view in one call.
    /// </summary>
    static RectTransform CreateLiveView(RectTransform cameraPanel)
    {
        RectTransform rt = UIFactory.Rect("LiveARView", cameraPanel);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);
        return rt;
    }

    /// <summary>
    /// Points tap detection at the live view rather than the whole tab, so taps do
    /// nothing while the Ready State is covering the feed.
    /// </summary>
    static void GateTapInput(RectTransform liveView)
    {
        CubeTapManager tap =
            Object.FindFirstObjectByType<CubeTapManager>(FindObjectsInactive.Include);

        if (tap == null)
        {
            _notes.Add("No CubeTapManager found - marker taps are not gated to the live view.");
            return;
        }

        tap.cameraPanel = liveView.gameObject;
        EditorUtility.SetDirty(tap);
    }

    static ReadyStateView BuildReadyState(RectTransform cameraPanel,
        UserLocationService location, BuildingRowView rowPrefab)
    {
        RectTransform rt = UIFactory.Rect("ReadyStateView", cameraPanel);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);

        // Opaque on purpose: this is what hides the camera feed. The AR session
        // keeps running underneath so localization finishes while the reader waits.
        UIFactory.Fill(rt, ThemeRole.Background);

        VerticalLayoutGroup layout = UIFactory.VerticalStack(rt, 22f, false);

        // Extra bottom padding: with the nav bar hidden this screen runs to the
        // edge of the display, where a phone's gesture bar lives.
        layout.padding = new RectOffset((int)Margin, (int)Margin, 80, 72);

        ReadyStateView view = rt.gameObject.AddComponent<ReadyStateView>();
        view.locationService = location;
        view.rowPrefab = rowPrefab;
        view.radiusMetres = 500d;
        view.closestCount = 3;

        // Greeting
        RectTransform greeting = UIFactory.Rect("Greeting", rt);
        UIFactory.VerticalStack(greeting, 6f, false);
        UIFactory.Size(greeting.gameObject, 170f);

        view.greetingText = UIFactory.Text("Line", greeting, 34f, ThemeRole.TextMuted,
                                           TextAlignmentOptions.Left);
        UIFactory.Size(view.greetingText.gameObject, 44f);

        view.headlineText = UIFactory.Text("Headline", greeting, 62f, ThemeRole.TextPrimary,
                                           TextAlignmentOptions.TopLeft, null, FontStyles.Bold);
        UIFactory.Size(view.headlineText.gameObject, 120f);

        // Closest
        RectTransform closest = UIFactory.Rect("ClosestCard", rt);
        UIFactory.Card(closest, 30f, 12f);
        view.closestCard = closest.gameObject;
        view.closestLabel = SectionLabel(closest, "ready.closest");

        RectTransform closestList = UIFactory.Rect("List", closest);
        UIFactory.VerticalStack(closestList, 0f, false);
        view.closestParent = closestList;

        // Status
        RectTransform status = UIFactory.Rect("StatusCard", rt);
        UIFactory.Card(status, 30f, 10f);

        RectTransform head = UIFactory.Rect("Head", status);
        UIFactory.Size(head.gameObject, 50f);
        UIFactory.HorizontalStack(head, 16f);

        RectTransform dot = UIFactory.Rect("Dot", head);
        UIFactory.Size(dot.gameObject, 30f, 30f);
        view.statusDot = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.StatusNone), UIShape.Circle);
        view.statusDot.raycastTarget = false;

        view.statusTitle = UIFactory.Text("Title", head, 42f, ThemeRole.TextPrimary,
                                          TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(view.statusTitle.gameObject, 50f);
        UIFactory.Flexible(view.statusTitle.gameObject);

        view.statusBody = UIFactory.Text("Body", status, 34f, ThemeRole.TextSecondary,
                                         TextAlignmentOptions.TopLeft);
        UIFactory.Size(view.statusBody.gameObject, 100f);

        // Pushes the buttons to the bottom whatever the cards above come to.
        RectTransform spacer = UIFactory.Rect("Spacer", rt);
        LayoutElement spacerElement = spacer.gameObject.AddComponent<LayoutElement>();
        spacerElement.flexibleHeight = 1f;
        spacerElement.minHeight = 24f;

        // Start
        RectTransform start = UIFactory.Rect("StartButton", rt);
        UIFactory.Size(start.gameObject, 116f);
        view.startButton = UIFactory.Button(start, ThemeRole.Accent, 1f, UIShape.Rounded);
        view.startButtonBackground = start.GetComponent<Image>();
        Unthemed(start.gameObject);

        view.startLabel = UIFactory.Text("Label", start, 44f, ThemeRole.OnAccent,
                                         TextAlignmentOptions.Center, null, FontStyles.Bold);
        UIFactory.Stretch(view.startLabel.rectTransform);
        Unthemed(view.startLabel.gameObject);

        // Escape hatch - live at every calibration state, so nobody is ever trapped.
        RectTransform anyway = UIFactory.Rect("StartAnywayButton", rt);
        UIFactory.Size(anyway.gameObject, 68f);
        view.startAnywayButton = UIFactory.Button(anyway, ThemeRole.Background, 0f);
        view.startAnywayLabel = UIFactory.Text("Label", anyway, 34f, ThemeRole.TextSecondary,
                                               TextAlignmentOptions.Center, "ready.startAnyway");
        UIFactory.Stretch(view.startAnywayLabel.rectTransform);

        view.captionText = UIFactory.Text("Caption", rt, 30f, ThemeRole.TextMuted,
                                          TextAlignmentOptions.Center, "ready.cameraOff");
        UIFactory.Size(view.captionText.gameObject, 44f);

        return view;
    }

    /// <summary>
    /// Sets the draw order inside the live view. Siblings render in hierarchy
    /// order, so the sheets have to be raised once everything else exists -
    /// otherwise the nearby button and the pause button sit on top of the open
    /// info sheet, and the sheet's dismiss catcher covers nothing.
    /// </summary>
    static void OrderLiveView(NearbyPanelController nearby, InfoPanelController info)
    {
        if (nearby != null)
        {
            if (nearby.scrim != null)
                nearby.scrim.transform.SetAsLastSibling();

            nearby.transform.SetAsLastSibling();
        }

        if (info == null)
            return;

        if (info.scrim != null)
            info.scrim.transform.SetAsLastSibling();

        info.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Builds the grab bar at the top of a sheet and makes it draggable. The bar
    /// was previously decorative, which promised a gesture that did nothing.
    /// </summary>
    static SheetDragHandle AddDragArea(RectTransform sheet, SheetAnimator animator)
    {
        RectTransform area = UIFactory.Rect("DragArea", sheet);
        UIFactory.TopStretch(area, 76f);

        // Transparent, but a raycast target - otherwise there is nothing to grab.
        Image catcher = area.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        RectTransform grab = UIFactory.Rect("Grabber", area);
        UIFactory.Corner(grab, new Vector2(0.5f, 1f), new Vector2(120f, 10f),
                         new Vector2(0f, -20f));
        UIFactory.Fill(grab, ThemeRole.Line, 1f, UIShape.Pill).raycastTarget = false;

        SheetDragHandle handle = area.gameObject.AddComponent<SheetDragHandle>();
        handle.sheet = animator;
        handle.sheetRect = sheet;
        handle.dismissDistance = 150f;

        return handle;
    }

    /// <summary>Strips the automatic theming from elements a controller recolours itself.</summary>
    static void Unthemed(GameObject go)
    {
        ThemedElement themed = go.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);
    }


    static CameraTabController WireCameraTab(RectTransform cameraPanel,
        RectTransform liveView, ReadyStateView ready, UserLocationService location,
        NearbyPanelController nearby, InfoPanelController info, VpsIndicatorController vps)
    {
        RectTransform rt = UIFactory.Rect("NearbyButton", liveView);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Corner(rt, new Vector2(0f, 1f), new Vector2(300f, 76f), new Vector2(Margin, -Margin));

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0.86f, UIShape.Pill);

        RectTransform pin = UIFactory.Rect("Pin", rt);
        UIFactory.Corner(pin, new Vector2(0f, 0.5f), new Vector2(20f, 20f), new Vector2(24f, 0f));
        UIFactory.Fill(pin, ThemeRole.Accent, 1f, UIShape.Circle);

        TextMeshProUGUI count = UIFactory.Text("Count", rt, 34f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left);
        UIFactory.Stretch(count.rectTransform);
        count.rectTransform.offsetMin = new Vector2(56f, 0f);
        count.rectTransform.offsetMax = new Vector2(-18f, 0f);
        count.text = string.Format(AppStrings.Get("camera.nearbyCount"), 0);

        CameraTabController controller = cameraPanel.GetComponent<CameraTabController>();
        if (controller == null)
            controller = Undo.AddComponent<CameraTabController>(cameraPanel.gameObject);

        controller.locationService = location;
        controller.nearbyPanel = nearby;
        controller.infoPanel = info;
        controller.vpsIndicator = vps;
        controller.nearbyButton = button;
        controller.nearbyCountText = count;

        RectTransform nav = Find(cameraPanel.root, "BottomNav");
        if (nav != null)
            controller.bottomNav = nav.gameObject;

        controller.readyStateView = ready != null ? ready.gameObject : null;
        controller.liveARView = liveView.gameObject;
        controller.readyState = ready;
        controller.backgroundResetSeconds = 120f;
        controller.arSession =
            Object.FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);

        BuildLiveExtras(liveView, controller);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>The pause button beside the calibration dot, and the accuracy banner.</summary>
    static void BuildLiveExtras(RectTransform liveView, CameraTabController controller)
    {
        RectTransform banner = UIFactory.Rect("WarningBanner", liveView);
        banner.anchorMin = new Vector2(0f, 1f);
        banner.anchorMax = new Vector2(1f, 1f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(-Margin * 2f, 96f);
        banner.anchoredPosition = new Vector2(0f, -(Margin + 104f));
        UIFactory.Fill(banner, ThemeRole.Surface, 0.96f, UIShape.Rounded);

        controller.warningLabel = UIFactory.Text("Label", banner, 32f, ThemeRole.TextPrimary,
                                                 TextAlignmentOptions.Left);
        UIFactory.Stretch(controller.warningLabel.rectTransform);
        controller.warningLabel.rectTransform.offsetMin = new Vector2(28f, 0f);
        controller.warningLabel.rectTransform.offsetMax = new Vector2(-84f, 0f);
        controller.warningLabel.text = AppStrings.Get("live.warning");

        RectTransform dismiss = UIFactory.Rect("Dismiss", banner);
        UIFactory.Corner(dismiss, new Vector2(1f, 0.5f), new Vector2(64f, 64f),
                         new Vector2(-14f, 0f));
        controller.warningDismissButton =
            UIFactory.Button(dismiss, ThemeRole.Surface, 0f, UIShape.Circle);

        UIFactory.Cross(dismiss, ThemeRole.TextMuted, 22f, 3f);

        controller.warningBanner = banner.gameObject;
        banner.gameObject.SetActive(false);
    }

    static VpsIndicatorController BuildVpsIndicator(RectTransform cameraPanel,
                                                    UserLocationService location)
    {
        RectTransform rt = UIFactory.Rect("VpsIndicator", cameraPanel);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);

        RectTransform dotButton = UIFactory.Rect("DotButton", rt);
        UIFactory.Corner(dotButton, new Vector2(1f, 1f), new Vector2(84f, 84f),
                         new Vector2(-Margin, -Margin));
        Button button = UIFactory.Button(dotButton, ThemeRole.Surface, 0.86f, UIShape.Circle);

        RectTransform dot = UIFactory.Rect("Dot", dotButton);
        UIFactory.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(34f, 34f), Vector2.zero);
        Image dotImage = UIFactory.Plain(dot, ThemeManager.Get(ThemeRole.StatusNone), UIShape.Circle);
        dotImage.raycastTarget = false;

        // The bubble sits under the button rather than taking over the screen -
        // the reader is holding the phone up at a building while reading it.
        RectTransform bubble = UIFactory.Rect("Bubble", rt);
        bubble.anchorMin = new Vector2(0f, 1f);
        bubble.anchorMax = new Vector2(1f, 1f);
        bubble.pivot = new Vector2(0.5f, 1f);
        // Width insets of 160 left and Margin right. Height is left to the content
        // fitter, so sizeDelta.y stays 0 and anchoredPosition only sets the drop.
        bubble.sizeDelta = new Vector2(-(160f + Margin), 0f);
        bubble.anchoredPosition = new Vector2((160f - Margin) * 0.5f, -(Margin + 96f));
        UIFactory.Card(bubble, 30f, 10f, true);

        RectTransform head = UIFactory.Rect("Head", bubble);
        UIFactory.Size(head.gameObject, 46f);
        UIFactory.HorizontalStack(head, 16f);

        RectTransform bubbleDotRect = UIFactory.Rect("Dot", head);
        UIFactory.Size(bubbleDotRect.gameObject, 30f, 30f);
        Image bubbleDot = UIFactory.Plain(bubbleDotRect, ThemeManager.Get(ThemeRole.StatusNone), UIShape.Circle);
        bubbleDot.raycastTarget = false;

        TextMeshProUGUI title = UIFactory.Text("Title", head, 40f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(title.gameObject, 46f);

        TextMeshProUGUI body = UIFactory.Text("Body", bubble, 34f, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft);
        UIFactory.Size(body.gameObject, 150f);

        TextMeshProUGUI accuracy = UIFactory.Text("Accuracy", bubble, 38f, ThemeRole.TextPrimary,
                                                  TextAlignmentOptions.Left);
        UIFactory.Size(accuracy.gameObject, 46f);

        // Index 0 keeps the catcher below the dot, so tapping the dot still
        // toggles while a tap anywhere else dismisses.
        Button scrimButton = CreateScrim("BubbleScrim", rt, out GameObject bubbleScrim);
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

    static NearbyPanelController BuildNearbyPanel(RectTransform cameraPanel,
        UserLocationService location, BuildingRowView rowPrefab)
    {
        RectTransform sheet = UIFactory.Rect("NearbyPanel", cameraPanel);
        Undo.RegisterCreatedObjectUndo(sheet.gameObject, "Build App UI");
        UIFactory.BottomStretch(sheet, SheetHeight, Nav);
        UIFactory.Fill(sheet, ThemeRole.Surface, 0.97f, UIShape.Rounded);

        RectTransform head = UIFactory.Rect("Header", sheet);
        UIFactory.TopStretch(head, 80f, 44f);
        head.sizeDelta = new Vector2(-Margin * 2f, 80f);

        TextMeshProUGUI title = UIFactory.Text("Title", head, 46f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, "nearby.title", FontStyles.Bold);
        UIFactory.Stretch(title.rectTransform);

        RectTransform closeRect = UIFactory.Rect("CloseButton", head);
        UIFactory.Corner(closeRect, new Vector2(1f, 0.5f), new Vector2(64f, 64f), Vector2.zero);
        Button close = UIFactory.Button(closeRect, ThemeRole.SurfaceAlt, 1f, UIShape.Circle);
        UIFactory.Cross(closeRect, ThemeRole.TextSecondary);

        RectTransform listArea = UIFactory.Rect("ListArea", sheet);
        UIFactory.Stretch(listArea);
        listArea.offsetMin = new Vector2(Margin, Margin);
        listArea.offsetMax = new Vector2(-Margin, -140f);

        RectTransform content = UIFactory.ScrollList("Scroll", listArea);

        TextMeshProUGUI empty = UIFactory.Text("Empty", listArea, 34f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center);
        UIFactory.Stretch(empty.rectTransform);

        NearbyPanelController controller = sheet.gameObject.AddComponent<NearbyPanelController>();
        controller.locationService = location;
        controller.sheet = sheet.gameObject.AddComponent<SheetAnimator>();
        controller.sheet.openY = Nav;

        controller.scrimButton = CreateScrim("NearbyScrim", cameraPanel, out GameObject nearbyScrim);
        controller.scrim = nearbyScrim;

        SheetDragHandle nearbyDrag = AddDragArea(sheet, controller.sheet);
        Wire(nearbyDrag.onDismissed,
             new UnityEngine.Events.UnityAction(controller.Close), "Nearby sheet drag");
        controller.listParent = content;
        controller.rowPrefab = rowPrefab;
        controller.titleText = title;
        controller.emptyText = empty;
        controller.closeButton = close;
        controller.radiusMetres = 500d;

        // Parked offscreen rather than disabled, so the controller on this same
        // object keeps running and the Game view isn't blocked at edit time.
        sheet.anchoredPosition = new Vector2(0f, Nav - SheetHeight - 40f);
        return controller;
    }

    // -------------------------------------------------------------- search tab

    static SearchTabController BuildSearchTab(RectTransform panel,
        UserLocationService location, BuildingRowView rowPrefab, FilterPillView pillPrefab)
    {
        RemoveLegacyPlaceholders(panel);

        RectTransform rt = UIFactory.Rect("SearchContent", panel);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(Margin, Margin);
        rt.offsetMax = new Vector2(-Margin, -Margin);

        SearchTabController controller = rt.gameObject.AddComponent<SearchTabController>();
        controller.locationService = location;
        controller.rowPrefab = rowPrefab;
        controller.pillPrefab = pillPrefab;

        RectTransform fieldRect = UIFactory.Rect("SearchField", rt);
        UIFactory.TopStretch(fieldRect, 96f);
        controller.searchField = UIFactory.InputField("Field", fieldRect,
            "search.placeholder", out TextMeshProUGUI placeholder);
        UIFactory.Stretch((RectTransform)controller.searchField.transform);
        controller.searchPlaceholder = placeholder;

        // One scroll for the whole tab. The filter rows live inside it alongside
        // the results, so selecting a filter never takes the pills off screen.
        RectTransform body = UIFactory.Rect("Body", rt);
        UIFactory.Stretch(body);
        body.offsetMax = new Vector2(0f, -124f);
        RectTransform content = UIFactory.ScrollList("Scroll", body, 26f);

        // --- always visible ---
        RectTransform eras = UIFactory.Rect("EraSection", content);
        UIFactory.VerticalStack(eras, 12f, false);
        controller.eraLabel = SectionLabel(eras, "search.byEra");
        controller.eraPillParent = PillRow(eras);

        RectTransform types = UIFactory.Rect("TypeSection", content);
        UIFactory.VerticalStack(types, 12f, false);
        controller.typeLabel = SectionLabel(types, "search.byType");
        controller.typePillParent = PillRow(types);

        // --- browse only ---
        RectTransform closest = UIFactory.Rect("ClosestSection", content);
        UIFactory.VerticalStack(closest, 12f, false);
        controller.closestSection = closest.gameObject;
        controller.closestLabel = SectionLabel(closest, "search.closest");
        RectTransform closestList = UIFactory.Rect("List", closest);
        UIFactory.VerticalStack(closestList, 0f, false);
        controller.closestParent = closestList;

        controller.progressCard = BuildProgressCard(content);
        controller.discoverySection = controller.progressCard.gameObject;

        // --- results only ---
        RectTransform results = UIFactory.Rect("ResultsSection", content);
        UIFactory.VerticalStack(results, 0f, false);
        controller.resultsSection = results.gameObject;

        TextMeshProUGUI empty = UIFactory.Text("Empty", results, 36f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Center, "search.empty");
        UIFactory.Size(empty.gameObject, 140f);
        controller.emptyText = empty;

        RectTransform resultsList = UIFactory.Rect("List", results);
        UIFactory.VerticalStack(resultsList, 0f, false);
        controller.resultsParent = resultsList;

        results.gameObject.SetActive(false);
        return controller;
    }

    static TextMeshProUGUI SectionLabel(Transform parent, string key)
    {
        TextMeshProUGUI label = UIFactory.Text("Label", parent, 30f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left, key, FontStyles.UpperCase);
        UIFactory.Size(label.gameObject, 38f);
        label.characterSpacing = 6f;
        return label;
    }

    static RectTransform PillRow(Transform parent)
    {
        RectTransform row = UIFactory.Rect("Pills", parent);

        FlowLayoutGroup flow = row.gameObject.AddComponent<FlowLayoutGroup>();
        flow.spacingX = 14f;
        flow.spacingY = 14f;

        return row;
    }

    static DiscoveryProgressCard BuildProgressCard(Transform parent)
    {
        RectTransform card = UIFactory.Rect("DiscoveryCard", parent);
        UIFactory.Card(card, 30f, 14f);

        TextMeshProUGUI count = UIFactory.Text("Count", card, 76f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(count.gameObject, 88f);

        RectTransform bar = UIFactory.Rect("Bar", card);
        UIFactory.Size(bar.gameObject, 18f);
        UIFactory.Fill(bar, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);

        RectTransform fill = UIFactory.Rect("Fill", bar);
        UIFactory.Stretch(fill);
        UIFactory.Fill(fill, ThemeRole.Accent, 1f, UIShape.Pill);

        TextMeshProUGUI label = UIFactory.Text("Label", card, 32f, ThemeRole.TextMuted,
                                               TextAlignmentOptions.Left, "search.discovered");
        UIFactory.Size(label.gameObject, 40f);

        TextMeshProUGUI note = UIFactory.Text("Note", card, 26f, ThemeRole.TextMuted,
                                              TextAlignmentOptions.TopLeft, "search.discoveredNote");
        UIFactory.Size(note.gameObject, 60f);

        DiscoveryProgressCard component = card.gameObject.AddComponent<DiscoveryProgressCard>();
        component.noteText = note;
        component.countText = count;
        component.labelText = label;
        component.barFill = fill;
        return component;
    }

    // ------------------------------------------------------------ settings tab

    static void BuildSettingsTab(RectTransform panel)
    {
        RemoveLegacyPlaceholders(panel);

        RectTransform rt = UIFactory.Rect("SettingsContent", panel);
        Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build App UI");
        UIFactory.Stretch(rt);

        SettingsTabController controller = rt.gameObject.AddComponent<SettingsTabController>();
        SubPanelNavigator navigator = rt.gameObject.AddComponent<SubPanelNavigator>();
        controller.navigator = navigator;

        RectTransform rootView = UIFactory.Rect("Root", rt);
        UIFactory.Stretch(rootView);
        rootView.offsetMin = new Vector2(Margin, Margin);
        rootView.offsetMax = new Vector2(-Margin, -Margin);
        RectTransform content = UIFactory.ScrollList("Scroll", rootView, 24f);
        navigator.rootView = rootView.gameObject;

        // Appearance
        controller.appearanceLabel = SectionLabel(content, "settings.appearance");
        RectTransform appearance = UIFactory.Rect("AppearanceCard", content);
        UIFactory.Card(appearance);
        controller.themeRowLabel = RowLabel(appearance, "settings.theme", out RectTransform themeRow);
        controller.themeToggle = UIFactory.Switch("Toggle", themeRow);
        controller.occlusionRowLabel = RowLabel(appearance, "settings.occlusion", out RectTransform occlusionRow);
        controller.occlusionToggle = UIFactory.Switch("Toggle", occlusionRow);

        // Language
        controller.languageSectionLabel = SectionLabel(content, "settings.language");
        RectTransform language = UIFactory.Rect("LanguageCard", content);
        UIFactory.Card(language);
        controller.languageRowLabel = RowLabel(language, "settings.appLanguage", out RectTransform langRow);

        RectTransform segment = UIFactory.Rect("Segment", langRow);
        LayoutElement segmentSize = UIFactory.Size(segment.gameObject, 62f, 200f);
        segmentSize.flexibleWidth = 0f;
        UIFactory.Plain(segment, ThemeManager.Get(ThemeRole.SurfaceAlt), UIShape.Pill);
        HorizontalLayoutGroup segmentLayout = UIFactory.HorizontalStack(segment, 0f, true);
        segmentLayout.padding = new RectOffset(4, 4, 4, 4);

        controller.englishButton = SegmentHalf(segment, "EN",
            out controller.englishBackground, out controller.englishLabel);
        controller.bulgarianButton = SegmentHalf(segment, "BG",
            out controller.bulgarianBackground, out controller.bulgarianLabel);

        // Notifications
        controller.notificationsLabel = SectionLabel(content, "settings.notifications");
        RectTransform notifications = UIFactory.Rect("NotificationsCard", content);
        UIFactory.Card(notifications);
        controller.nearbyRowLabel = RowLabel(notifications, "settings.nearbyBuildings", out RectTransform nearbyRow);
        controller.nearbyToggle = UIFactory.Switch("Toggle", nearbyRow);

        // Help
        controller.helpLabel = SectionLabel(content, "settings.help");
        RectTransform help = UIFactory.Rect("HelpCard", content);
        UIFactory.Card(help);
        controller.tutorialButton = HelpRow(help, "settings.replayTutorial", out controller.tutorialRowLabel);
        controller.sourcesButton = HelpRow(help, "settings.dataSources", out controller.sourcesRowLabel);
        controller.aboutButton = HelpRow(help, "settings.about", out controller.aboutRowLabel);

        RectTransform versionRow = UIFactory.Rect("Version", content);
        UIFactory.Size(versionRow.gameObject, 52f);
        controller.versionButton = UIFactory.Button(versionRow, ThemeRole.Background, 0f);

        controller.versionLabel = UIFactory.Text("Label", versionRow, 30f, ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Center);
        UIFactory.Stretch(controller.versionLabel.rectTransform);

        // Sub-pages
        controller.tutorialPage = SubPage(rt, "TutorialPage", "settings.replayTutorial", "sub.tutorial.body");
        controller.sourcesPage = SubPage(rt, "SourcesPage", "settings.dataSources", "sub.sources.body");
        controller.aboutPage = SubPage(rt, "AboutPage", "settings.about", "sub.about.body");

        navigator.pages = new List<GameObject>
        {
            controller.tutorialPage, controller.sourcesPage, controller.aboutPage
        };

        RectTransform backRect = UIFactory.Rect("BackButton", rt);
        UIFactory.Corner(backRect, new Vector2(0f, 1f), new Vector2(200f, 72f),
                         new Vector2(Margin, -Margin));
        navigator.backButton = UIFactory.Button(backRect, ThemeRole.SurfaceAlt, 1f, UIShape.Pill);
        navigator.backLabel = UIFactory.Text("Label", backRect, 32f, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.Center, "settings.back");
        UIFactory.Stretch(navigator.backLabel.rectTransform);
        backRect.gameObject.SetActive(false);

        navigator.ShowRoot();
    }

    static TextMeshProUGUI RowLabel(Transform card, string key, out RectTransform row)
    {
        row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, 68f);
        UIFactory.HorizontalStack(row, 16f, false);

        TextMeshProUGUI label = UIFactory.Text("Label", row, 38f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, key);
        UIFactory.Size(label.gameObject, 68f);
        UIFactory.Flexible(label.gameObject);
        return label;
    }

    static Button HelpRow(Transform card, string key, out TextMeshProUGUI label)
    {
        RectTransform row = UIFactory.Rect("Row", card);
        UIFactory.Size(row.gameObject, 72f);
        Button button = UIFactory.Button(row, ThemeRole.Surface, 0f);

        label = UIFactory.Text("Label", row, 38f, ThemeRole.TextPrimary,
                               TextAlignmentOptions.Left, key);
        UIFactory.Stretch(label.rectTransform);

        TextMeshProUGUI chevron = UIFactory.Text("Chevron", row, 40f, ThemeRole.TextMuted,
                                                 TextAlignmentOptions.Right);
        UIFactory.Stretch(chevron.rectTransform);
        chevron.text = "›";

        return button;
    }

    static Button SegmentHalf(Transform parent, string text, out Image background,
                              out TextMeshProUGUI label)
    {
        RectTransform rt = UIFactory.Rect(text, parent);
        UIFactory.Size(rt.gameObject, 54f, 96f);

        // Plain, not Fill: SettingsTabController drives this colour from which
        // language is active, and a ThemedElement would overwrite it.
        background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;

        label = UIFactory.Text("Label", rt, 30f, ThemeRole.TextMuted, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        label.text = text;

        // Same reason as the background: the controller sets this colour from the
        // active language, so it must not also be themed automatically.
        ThemedElement themed = label.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);

        return button;
    }

    static GameObject SubPage(Transform parent, string name, string titleKey, string bodyKey)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Stretch(rt);
        rt.offsetMin = new Vector2(Margin, Margin);
        rt.offsetMax = new Vector2(-Margin, -(Margin + 96f));

        RectTransform content = UIFactory.ScrollList("Scroll", rt, 24f);

        TextMeshProUGUI title = UIFactory.Text("Title", content, 52f, ThemeRole.TextPrimary,
                                               TextAlignmentOptions.Left, titleKey, FontStyles.Bold);
        UIFactory.Size(title.gameObject, 70f);

        TextMeshProUGUI body = UIFactory.Text("Body", content, 34f, ThemeRole.TextSecondary,
                                              TextAlignmentOptions.TopLeft, bodyKey);
        UIFactory.Size(body.gameObject, 300f);

        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    // ------------------------------------------------------------------- prefabs

    static BuildingRowView CreateRowPrefab()
    {
        GameObject temp = new GameObject("BuildingRow", typeof(RectTransform));
        RectTransform rt = (RectTransform)temp.transform;
        rt.sizeDelta = new Vector2(0f, 118f);
        UIFactory.Size(temp, 118f);

        Button button = UIFactory.Button(rt, ThemeRole.Surface, 0f);

        RectTransform main = UIFactory.Rect("Main", rt);
        UIFactory.Stretch(main);
        main.offsetMax = new Vector2(-160f, 0f);
        UIFactory.VerticalStack(main, 4f, false);

        TextMeshProUGUI name = UIFactory.Text("Name", main, 40f, ThemeRole.TextPrimary,
                                              TextAlignmentOptions.Left, null, FontStyles.Bold);
        UIFactory.Size(name.gameObject, 50f);

        TextMeshProUGUI subtitle = UIFactory.Text("Subtitle", main, 32f, ThemeRole.TextMuted,
                                                  TextAlignmentOptions.Left);
        UIFactory.Size(subtitle.gameObject, 40f);

        TextMeshProUGUI distance = UIFactory.Text("Distance", rt, 34f, ThemeRole.TextSecondary,
                                                  TextAlignmentOptions.Right);
        UIFactory.Corner(distance.rectTransform, new Vector2(1f, 0.5f),
                         new Vector2(150f, 50f), Vector2.zero);

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
        rt.sizeDelta = new Vector2(200f, 62f);

        // No ThemedElement on either of these: FilterPillView drives both colours
        // itself, because they depend on whether the pill is the active filter.
        Image background = UIFactory.Plain(rt, ThemeManager.Get(ThemeRole.Surface), UIShape.Pill);

        Button button = temp.AddComponent<Button>();
        button.targetGraphic = background;

        RectTransform labelRect = UIFactory.Rect("Label", rt);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 32f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = ThemeManager.Get(ThemeRole.TextSecondary);
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        UIFactory.Stretch(labelRect);

        // No ContentSizeFitter and no layout group here. FilterPillView measures
        // its own label and writes the result to this LayoutElement, which the
        // parent FlowLayoutGroup then reads - one measurement instead of nested
        // auto-sizing, which is what crashed the editor on play-mode exit.
        LayoutElement element = temp.AddComponent<LayoutElement>();
        element.preferredHeight = 62f;
        element.minHeight = 62f;

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
    /// Full-screen transparent catcher. An alpha-zero Image still receives
    /// raycasts, so it closes whatever is above it without being visible.
    /// </summary>
    static Button CreateScrim(string name, RectTransform parent, out GameObject scrim)
    {
        RectTransform rt = UIFactory.Rect(name, parent);
        UIFactory.Stretch(rt);

        Image image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        scrim = rt.gameObject;
        rt.gameObject.SetActive(false);
        return button;
    }

    /// <summary>Adds the short fade-and-rise used whenever a panel appears.</summary>
    static void AddFade(GameObject go, float rise = 14f)
    {
        if (go.GetComponent<CanvasGroup>() == null)
            Undo.AddComponent<CanvasGroup>(go);

        PanelFade fade = go.GetComponent<PanelFade>();
        if (fade == null)
            fade = Undo.AddComponent<PanelFade>(go);

        fade.duration = 0.18f;
        fade.rise = rise;
    }

    // ------------------------------------------------------------------- helpers

    /// <summary>
    /// Lay Out App UI painted the panels and nav bar with fixed colours, from
    /// before the theme system existed. Without a ThemedElement they stay dark
    /// while the cards inside them turn light, which is what makes light mode look
    /// half-applied.
    /// </summary>
    static void ThemeShell(RectTransform searchPanel, RectTransform settingsPanel,
                           RectTransform bottomNav, RectTransform infoPanel)
    {
        Theme(searchPanel, ThemeRole.Background);
        Theme(settingsPanel, ThemeRole.Background);
        Theme(bottomNav, ThemeRole.Surface);
        Theme(infoPanel, ThemeRole.Surface, UIShape.Rounded);

        // The tab buttons carry no fill of their own - the nav bar behind them
        // provides the surface, and the active tab is shown by its label.
        ClearTabBackground(bottomNav, "CameraTabButton");
        ClearTabBackground(bottomNav, "SearchTabButton");
        ClearTabBackground(bottomNav, "SettingsTabButton");
    }

    static void Theme(RectTransform rt, ThemeRole role, UIShape shape = UIShape.Rect)
    {
        if (rt == null)
            return;

        Image image = rt.GetComponent<Image>();
        if (image == null)
            image = Undo.AddComponent<Image>(rt.gameObject);

        UIFactory.ApplyShape(image, shape);
        image.color = ThemeManager.Get(role);

        ThemedElement themed = rt.GetComponent<ThemedElement>();
        if (themed == null)
            themed = Undo.AddComponent<ThemedElement>(rt.gameObject);

        themed.role = role;
        themed.preserveAlpha = false;
        themed.Apply();
    }

    static void ClearTabBackground(Transform nav, string buttonName)
    {
        RectTransform button = Find(nav, buttonName);
        if (button == null)
            return;

        ThemedElement themed = button.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = new Color(1f, 1f, 1f, 0f); // invisible, still tappable
    }

    static void LocalizeNav(RectTransform bottomNav)
    {
        if (bottomNav == null)
        {
            _notes.Add("No BottomNav found - tab labels were not localized.");
            return;
        }

        TMP_Text cameraLabel = Localize(bottomNav, "CameraTabButton", "tab.camera");
        TMP_Text searchLabel = Localize(bottomNav, "SearchTabButton", "tab.search");
        TMP_Text settingsLabel = Localize(bottomNav, "SettingsTabButton", "tab.settings");

        TabNavigationController tabs =
            Object.FindFirstObjectByType<TabNavigationController>(FindObjectsInactive.Include);

        if (tabs == null)
        {
            _notes.Add("No TabNavigationController found - the active tab won't be highlighted.");
            return;
        }

        tabs.cameraTab =
            Object.FindFirstObjectByType<CameraTabController>(FindObjectsInactive.Include);
        tabs.bottomNav = bottomNav.gameObject;
        tabs.settingsTab =
            Object.FindFirstObjectByType<SettingsTabController>(FindObjectsInactive.Include);
        tabs.cameraLabel = cameraLabel;
        tabs.searchLabel = searchLabel;
        tabs.settingsLabel = settingsLabel;
        EditorUtility.SetDirty(tabs);
    }

    static TextMeshProUGUI Localize(Transform nav, string buttonName, string key)
    {
        RectTransform button = Find(nav, buttonName);
        if (button == null)
            return null;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            return null;

        LocalizedLabel localized = label.GetComponent<LocalizedLabel>();
        if (localized == null)
            localized = Undo.AddComponent<LocalizedLabel>(label.gameObject);

        localized.key = key;
        label.text = AppStrings.Get(key);

        // No ThemedElement here: TabNavigationController colours these from which
        // tab is open, and the two would overwrite each other.
        ThemedElement themed = label.GetComponent<ThemedElement>();
        if (themed != null)
            Object.DestroyImmediate(themed);

        return label;
    }

    /// <summary>
    /// Deletes bare text children left directly on a tab panel - the
    /// "Search - coming soon" placeholders from before this UI existed. Generated
    /// content is a plain RectTransform, so it is never caught by this.
    /// </summary>
    static void RemoveLegacyPlaceholders(RectTransform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            Transform child = panel.GetChild(i);

            if (child.GetComponent<TMP_Text>() != null)
                Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    static void RemoveGenerated(Transform root)
    {
        // InfoPanel is a scene object that lives inside the generated LiveARView.
        // Move it back out before that wrapper is deleted, or a rebuild would take
        // the panel and all its Inspector wiring with it.
        RectTransform info = Find(root, "InfoPanel");
        RectTransform cameraPanel = Find(root, "CameraPanel");

        if (info != null && cameraPanel != null && info.parent != cameraPanel)
            Undo.SetTransformParent(info, cameraPanel, "Build App UI");

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
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    /// <summary>
    /// Adds a persistent listener, tolerating a UnityEvent that has not been
    /// instantiated yet. One null event used to abort the entire build.
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
