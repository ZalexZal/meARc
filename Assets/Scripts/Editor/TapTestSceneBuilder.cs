using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Generates a plain, non-AR scene that exercises the whole tap -> raycast ->
/// info panel chain. AR Foundation's subsystems don't run in Editor Play Mode,
/// so the AR scene can only be tested on a device; this scene uses the exact same
/// scripts and can be verified by pressing Play.
/// </summary>
public static class TapTestSceneBuilder
{
    const string TestDataFolder = "Assets/_TapTest";
    const string ScenesFolder = "Assets/Scenes";
    const string ScenePath = ScenesFolder + "/TapTest.unity";

    [MenuItem("Tools/Building Data/Create Tap Test Scene")]
    public static void CreateTapTestScene()
    {
        // The AR scene usually has unsaved changes; never discard them silently.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // Open the empty scene before touching the test assets, so a previously
        // generated TapTest scene isn't still referencing them as they're deleted.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildingData[] testData = CreateTestData();

        BuildCamera();
        BuildLight();
        BuildMarkers(testData);

        RectTransform cameraPanel = BuildCanvas();
        InfoPanelController controller = BuildInfoPanel(cameraPanel);
        BuildEventSystem();
        BuildTapManager(controller, cameraPanel);

        if (!AssetDatabase.IsValidFolder(ScenesFolder))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog(
            "Tap Test Scene",
            "Created " + ScenePath + " and opened it.\n\n" +
            "Press Play, then click each cube. Each one should open the panel with " +
            "its own text. Cube C has no external link, so its Learn More button " +
            "should stay hidden. Clicking Close, then clicking empty space, should " +
            "both dismiss the panel without re-opening it.",
            "OK");
    }

    // ---------------------------------------------------------------- test data

    static BuildingData[] CreateTestData()
    {
        if (AssetDatabase.IsValidFolder(TestDataFolder))
        {
            AssetDatabase.DeleteAsset(TestDataFolder);
            AssetDatabase.Refresh();
        }

        AssetDatabase.CreateFolder("Assets", "_TapTest");

        return new[]
        {
            // Deliberately kept outside Assets/Resources so Resources.LoadAll never
            // picks this placeholder data up alongside the real buildings.
            MakeBuilding("Test Building A", "PLACEHOLDER TEXT. Tapping the left cube should " +
                "show this paragraph, and nothing from the other two cubes.",
                "1901", "Test Architect One", "Civic", "https://example.com"),

            MakeBuilding("Test Building B", "PLACEHOLDER TEXT. Tapping the middle cube should " +
                "replace the panel contents with this paragraph.",
                "1968", "", "Residential", "example.com"),

            MakeBuilding("Test Building C", "PLACEHOLDER TEXT. This one has no external link, " +
                "so the Learn More button should be hidden when it is shown.",
                "1935", "Test Architect Three", "Industrial", "")
        };
    }

    static BuildingData MakeBuilding(string name, string description, string year,
                                     string architect, string type, string url)
    {
        BuildingData data = ScriptableObject.CreateInstance<BuildingData>();
        data.buildingName = name;
        data.description = description;
        data.yearBuilt = year;
        data.architect = architect;
        data.buildingType = type;
        data.externalLinkURL = url;
        data.latitude = 0d;
        data.longitude = 0d;

        AssetDatabase.CreateAsset(data, TestDataFolder + "/" + name.Replace(" ", "_") + ".asset");
        return data;
    }

    // ------------------------------------------------------------------- scene

    static void BuildCamera()
    {
        GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        go.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -9f), Quaternion.identity);

        Camera cam = go.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.17f, 0.19f, 0.23f);
    }

    static void BuildLight()
    {
        GameObject go = new GameObject("Directional Light", typeof(Light));
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Light light = go.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
    }

    static void BuildMarkers(BuildingData[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Marker_" + data[i].buildingName.Replace(" ", "_");
            cube.transform.position = new Vector3((i - 1) * 3.5f, 1.2f, 0f);
            cube.AddComponent<TappableObject>().buildingData = data[i];
        }
    }

    static RectTransform BuildCanvas()
    {
        GameObject go = new GameObject("Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform cameraPanel = NewUI("CameraPanel", go.transform);
        Stretch(cameraPanel);
        return cameraPanel;
    }

    static InfoPanelController BuildInfoPanel(RectTransform cameraPanel)
    {
        RectTransform panel = NewUI("InfoPanel", cameraPanel);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(0f, 900f);
        panel.anchoredPosition = Vector2.zero;

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.10f, 0.11f, 0.13f, 0.96f);

        TextMeshProUGUI title = NewText("TitleText", panel, 56f, FontStyles.Bold);
        TopAnchored(title.rectTransform, 40f, 110f);

        TextMeshProUGUI meta = NewText("MetaText", panel, 32f, FontStyles.Normal);
        meta.color = new Color(0.72f, 0.75f, 0.80f);
        TopAnchored(meta.rectTransform, 165f, 60f);

        TextMeshProUGUI description = NewText("DescriptionText", panel, 34f, FontStyles.Normal);
        description.alignment = TextAlignmentOptions.TopLeft;
        TopAnchored(description.rectTransform, 245f, 440f);

        Button learnMore = NewButton("LearnMoreButton", panel, "Learn More",
            new Vector2(-170f, 60f), new Color(0.16f, 0.45f, 0.85f));
        Button close = NewButton("CloseButton", panel, "Close",
            new Vector2(170f, 60f), new Color(0.30f, 0.32f, 0.36f));

        InfoPanelController controller = panel.gameObject.AddComponent<InfoPanelController>();
        controller.panelRoot = panel.gameObject;
        controller.titleText = title;
        controller.metaText = meta;
        controller.descriptionText = description;
        controller.learnMoreButton = learnMore.gameObject;

        UnityEventTools.AddVoidPersistentListener(close.onClick,
            new UnityAction(controller.ClosePanel));
        UnityEventTools.AddVoidPersistentListener(learnMore.onClick,
            new UnityAction(controller.OpenExternalLink));

        return controller;
    }

    static void BuildEventSystem()
    {
        GameObject go = new GameObject("EventSystem", typeof(EventSystem));
        // Matches the AR scene, which also uses the Input System UI module. Using
        // the same module here is what makes the IsPointerOverGameObject behaviour
        // in this test representative of the real app.
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    static void BuildTapManager(InfoPanelController controller, RectTransform cameraPanel)
    {
        GameObject go = new GameObject("TapInputManager");

        CubeTapManager tap = go.AddComponent<CubeTapManager>();
        tap.infoPanel = controller;
        tap.cameraPanel = cameraPanel.gameObject;
        tap.maxTapDistance = 500f;
        tap.closeOnTapEmptySpace = true;
        tap.verboseLogging = true;
    }

    // ----------------------------------------------------------------- helpers

    static RectTransform NewUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void TopAnchored(RectTransform rt, float topOffset, float height)
    {
        const float sideMargin = 50f;

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-sideMargin * 2f, height);
        rt.anchoredPosition = new Vector2(0f, -topOffset);
    }

    static TextMeshProUGUI NewText(string name, Transform parent, float size, FontStyles style)
    {
        RectTransform rt = NewUI(name, parent);

        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.text = name;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    static Button NewButton(string name, Transform parent, string label,
                            Vector2 anchoredPosition, Color colour)
    {
        RectTransform rt = NewUI(name, parent);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300f, 110f);
        rt.anchoredPosition = anchoredPosition;

        Image image = rt.gameObject.AddComponent<Image>();
        image.color = colour;

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI text = NewText("Text (TMP)", rt, 34f, FontStyles.Normal);
        Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;

        return button;
    }
}
