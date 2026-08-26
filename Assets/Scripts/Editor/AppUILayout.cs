using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Applies a consistent layout to the app's existing Canvas. This never creates or
/// deletes GameObjects and never touches component references, so everything wired
/// in the Inspector - panel fields, OnClick handlers, InfoPanelController's text
/// references - survives untouched. It only sets RectTransform values, colours and
/// font sizes, all of which are undoable with Ctrl+Z.
/// </summary>
public static class AppUILayout
{
    // Everything below is expressed in this reference resolution, and the
    // CanvasScaler maps it onto whatever the real device screen is.
    const float ReferenceWidth = 1080f;
    const float ReferenceHeight = 1920f;

    const float NavHeight = 160f;
    const float SheetHeight = 820f;
    const float SideMargin = 50f;

    static readonly Color SheetBackground = new Color(0.09f, 0.10f, 0.12f, 0.97f);
    static readonly Color NavBackground = new Color(0.07f, 0.08f, 0.10f, 1f);
    static readonly Color PanelBackground = new Color(0.10f, 0.11f, 0.13f, 1f);
    static readonly Color PrimaryButton = new Color(0.16f, 0.45f, 0.85f, 1f);
    static readonly Color SecondaryButton = new Color(0.28f, 0.30f, 0.34f, 1f);
    static readonly Color TabButtonTint = new Color(1f, 1f, 1f, 0.06f);
    static readonly Color BodyText = new Color(0.93f, 0.94f, 0.96f, 1f);
    static readonly Color MutedText = new Color(0.68f, 0.72f, 0.78f, 1f);

    const string UndoLabel = "Lay Out App UI";

    static List<string> _notes;

    [MenuItem("Tools/UI/Lay Out App UI")]
    public static void LayOutAppUI()
    {
        _notes = new List<string>();

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(UndoLabel,
                "No Canvas found in the open scene.", "OK");
            return;
        }

        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        Transform root = canvas.transform;

        ConfigureCanvas(canvas);
        LayOutCameraPanel(root);
        LayOutTabPanel(root, "SearchPanel");
        LayOutTabPanel(root, "SettingsPanel");
        LayOutBottomNav(root);
        LayOutInfoPanel(root);
        SetDefaultTabState(root);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        string report = _notes.Count == 0
            ? "Layout applied. Everything was found and positioned.\n\n" +
              "Press Ctrl+Z to undo if you don't like it."
            : "Layout applied, with " + _notes.Count + " note(s):\n\n" +
              string.Join("\n", _notes);

        Debug.Log("[App UI] " + report);
        EditorUtility.DisplayDialog(UndoLabel, report, "OK");
    }

    // ------------------------------------------------------------------ pieces

    static void ConfigureCanvas(Canvas canvas)
    {
        Undo.RecordObject(canvas, UndoLabel);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);

        Undo.RecordObject(scaler, UndoLabel);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
    }

    /// <summary>
    /// Leaves the scene showing what the app actually shows on launch. Without this
    /// every panel stays enabled at edit time, they stack on top of each other, and
    /// the Game view shows whichever one happens to be last in the hierarchy - which
    /// looks like a bug but isn't, since TabNavigationController.Start() sorts it out
    /// the moment you press Play.
    /// </summary>
    static void SetDefaultTabState(Transform root)
    {
        SetActive(root, "CameraPanel", true);
        SetActive(root, "SearchPanel", false);
        SetActive(root, "SettingsPanel", false);

        // InfoPanel is deliberately left enabled. Once it has a SheetAnimator it
        // hides by sitting offscreen rather than by being disabled, and a disabled
        // panel can never be opened by a tap.
        SetActive(root, "InfoPanel", true);
    }

    static void SetActive(Transform root, string name, bool active)
    {
        Transform found = FindDeep(root, name);
        if (found == null)
            return;

        Undo.RecordObject(found.gameObject, UndoLabel);
        found.gameObject.SetActive(active);
    }

    static void LayOutCameraPanel(Transform root)
    {
        RectTransform rt = FindRect(root, "CameraPanel");
        if (rt == null)
            return;

        Stretch(rt);

        // This panel sits directly over the AR camera feed, so it must stay fully
        // transparent - an Image here would hide the real world.
        if (rt.GetComponent<Image>() != null)
        {
            _notes.Add("CameraPanel has an Image component. Remove it, or it will " +
                       "cover the camera feed.");
        }
    }

    static void LayOutTabPanel(Transform root, string name)
    {
        RectTransform rt = FindRect(root, name);
        if (rt == null)
            return;

        Undo.RecordObject(rt, UndoLabel);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(0f, NavHeight); // stop above the nav bar
        rt.offsetMax = Vector2.zero;

        Image background = rt.GetComponent<Image>();
        if (background == null)
            background = Undo.AddComponent<Image>(rt.gameObject);

        Undo.RecordObject(background, UndoLabel);
        background.color = ThemeManager.Get(ThemeRole.Background);
        // Also acts as the raycast blocker that stops taps on this tab reaching
        // the AR world behind it.
        background.raycastTarget = true;

        Themed(rt.gameObject, ThemeRole.Background);

        TextMeshProUGUI placeholder = rt.GetComponentInChildren<TextMeshProUGUI>(true);
        if (placeholder != null)
        {
            Undo.RecordObject(placeholder.rectTransform, UndoLabel);
            Stretch(placeholder.rectTransform);

            Undo.RecordObject(placeholder, UndoLabel);
            placeholder.alignment = TextAlignmentOptions.Center;
            placeholder.fontSize = 40f;
            placeholder.color = MutedText;
        }
    }

    static void LayOutBottomNav(Transform root)
    {
        RectTransform rt = FindRect(root, "BottomNav");
        if (rt == null)
            return;

        Undo.RecordObject(rt, UndoLabel);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, NavHeight);
        rt.anchoredPosition = Vector2.zero;

        Image background = rt.GetComponent<Image>();
        if (background == null)
            background = Undo.AddComponent<Image>(rt.gameObject);

        Undo.RecordObject(background, UndoLabel);
        background.color = ThemeManager.Get(ThemeRole.Surface);
        Themed(rt.gameObject, ThemeRole.Surface);

        HorizontalLayoutGroup group = rt.GetComponent<HorizontalLayoutGroup>();
        if (group == null)
            group = Undo.AddComponent<HorizontalLayoutGroup>(rt.gameObject);

        Undo.RecordObject(group, UndoLabel);
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = true;
        group.spacing = 0f;
        group.padding = new RectOffset(0, 0, 0, 0);

        StyleTabButton(rt, "CameraTabButton", "Camera");
        StyleTabButton(rt, "SearchTabButton", "Search");
        StyleTabButton(rt, "SettingsTabButton", "Settings");
    }

    static void StyleTabButton(Transform nav, string name, string label)
    {
        RectTransform rt = FindRect(nav, name);
        if (rt == null)
            return;

        Image image = rt.GetComponent<Image>();
        if (image == null)
            image = Undo.AddComponent<Image>(rt.gameObject);

        Undo.RecordObject(image, UndoLabel);
        image.color = TabButtonTint;

        Button button = rt.GetComponent<Button>();
        if (button == null)
        {
            _notes.Add(name + " has no Button component, so it can't be tapped.");
        }
        else
        {
            Undo.RecordObject(button, UndoLabel);
            button.targetGraphic = image;
        }

        StyleButtonLabel(rt, label, 30f, BodyText);
    }

    static void LayOutInfoPanel(Transform root)
    {
        RectTransform panel = FindRect(root, "InfoPanel");
        if (panel == null)
            return;

        Undo.RecordObject(panel, UndoLabel);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(0f, SheetHeight);
        panel.anchoredPosition = new Vector2(0f, NavHeight); // rest on top of the nav

        Image background = panel.GetComponent<Image>();
        if (background == null)
            background = Undo.AddComponent<Image>(panel.gameObject);

        Undo.RecordObject(background, UndoLabel);
        background.color = ThemeManager.Get(ThemeRole.Surface);
        background.raycastTarget = true;
        Themed(panel.gameObject, ThemeRole.Surface);

        StyleText(panel, "TitleText", 56f, FontStyles.Bold, BodyText, 40f, 110f);
        StyleText(panel, "MetaText", 32f, FontStyles.Normal, MutedText, 165f, 60f);
        StyleText(panel, "DescriptionText", 34f, FontStyles.Normal, BodyText, 245f, 340f);

        StyleSheetButton(panel, "LearnMoreButton", "Learn More", PrimaryButton, -170f);
        StyleSheetButton(panel, "CloseButton", "Close", SecondaryButton, 170f);
    }

    static void StyleText(Transform parent, string name, float size, FontStyles style,
                          Color colour, float topOffset, float height)
    {
        RectTransform rt = FindRect(parent, name);
        if (rt == null)
            return;

        TextMeshProUGUI text = rt.GetComponent<TextMeshProUGUI>();
        if (text == null)
        {
            _notes.Add(name + " has no TextMeshPro - Text (UI) component.");
            return;
        }

        Undo.RecordObject(rt, UndoLabel);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-SideMargin * 2f, height);
        rt.anchoredPosition = new Vector2(0f, -topOffset);

        Undo.RecordObject(text, UndoLabel);
        text.fontSize = size;
        text.fontStyle = style;
        text.color = colour;
        text.alignment = TextAlignmentOptions.TopLeft;
    }

    static void StyleSheetButton(Transform parent, string name, string label,
                                 Color colour, float xOffset)
    {
        RectTransform rt = FindRect(parent, name);
        if (rt == null)
            return;

        Undo.RecordObject(rt, UndoLabel);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300f, 110f);
        rt.anchoredPosition = new Vector2(xOffset, 50f);

        Image image = rt.GetComponent<Image>();
        if (image != null)
        {
            Undo.RecordObject(image, UndoLabel);
            image.color = colour;
        }

        StyleButtonLabel(rt, label, 34f, Color.white);
    }

    static void StyleButtonLabel(RectTransform button, string label, float size, Color colour)
    {
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
        {
            _notes.Add(button.name + " has no text child to label.");
            return;
        }

        Undo.RecordObject(text.rectTransform, UndoLabel);
        Stretch(text.rectTransform);

        Undo.RecordObject(text, UndoLabel);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size;
        text.color = colour;
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Tags an object so the light/dark switch reaches it. Without this the shell
    /// keeps whatever colour it was painted while the cards inside it change.
    /// </summary>
    static void Themed(GameObject go, ThemeRole role)
    {
        ThemedElement themed = go.GetComponent<ThemedElement>();
        if (themed == null)
            themed = Undo.AddComponent<ThemedElement>(go);

        themed.role = role;
        themed.preserveAlpha = false;
        themed.Apply();
    }

    static void Stretch(RectTransform rt)
    {
        Undo.RecordObject(rt, UndoLabel);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static RectTransform FindRect(Transform root, string name)
    {
        Transform found = FindDeep(root, name);
        if (found == null)
        {
            _notes.Add("Could not find an object named \"" + name + "\".");
            return null;
        }

        RectTransform rt = found as RectTransform;
        if (rt == null)
            _notes.Add("\"" + name + "\" is not a UI object (it has no RectTransform).");

        return rt;
    }

    static Transform FindDeep(Transform current, string name)
    {
        if (current.name == name)
            return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindDeep(current.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
