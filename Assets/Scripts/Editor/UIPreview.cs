using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders every screen to a PNG, from the command line, with no editor open.
///
/// This exists because the person building this UI could not see it. Contrast can
/// be measured and geometry can be checked arithmetically, but spacing, balance
/// and "does that look finished" cannot - and shipping visual changes unseen is
/// how a screen ends up with a camera icon that looks like an apple.
///
/// Run it with:
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod UIPreview.CaptureAll
///             -logFile - -quit
///
/// What it captures is the built layout, not the running app: nothing in the scene
/// is playing, so text that a controller fills in at runtime is stood in for here,
/// and the map has mock pins rather than live ones. Everything structural - sizes,
/// spacing, icons, borders, colours, whether anything overlaps or is clipped - is
/// exactly what the device will draw.
/// </summary>
public static class UIPreview
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OutputFolder = "PreviewShots";

    const int Width = 1080;
    const int Height = 1920;

    public static void CaptureAll()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        AppUIBuilder.BuildForPreview();
        AssetDatabase.Refresh();

        Canvas canvas = AppUIBuilder.FindRootCanvas();
        if (canvas == null)
            throw new System.Exception("No Canvas after the build.");

        // The first run of this harness captured the scene exactly as it was saved,
        // because the builder had hit a confirmation dialog that returns false in
        // batch mode and returned without doing anything. Ten screenshots of the
        // wrong thing is worse than none, so the build now has to prove it ran.
        if (canvas.transform.Find("MapPanel/MapSurface/Tiles") == null)
        {
            throw new System.Exception(
                "The build did not run - MapPanel/MapSurface/Tiles is missing. " +
                "These screenshots would have been of the previously saved scene.");
        }

        Directory.CreateDirectory(OutputFolder);

        StandIn(canvas.transform);
        MockPins(canvas.transform);
        MockRadar(canvas.transform);

        Rig rig = Rig.Create(canvas);

        // After the rig, and after a rebuild. The first version dumped before any
        // layout had run and reported every layout-driven rect as 100x100 at the
        // origin, which sent me looking for a bug that was not there.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
        Canvas.ForceUpdateCanvases();

        // The canvas has to be the size the screens were designed for, or every
        // picture below is of a layout nobody will ever see. Screen Space - Camera
        // sizes the canvas from the screen, and batch mode invents a 640x480 one -
        // which is what these shots were, silently, for several rounds.
        Rect canvasRect = ((RectTransform)canvas.transform).rect;
        if (Mathf.Abs(canvasRect.width - Width) > 1f ||
            Mathf.Abs(canvasRect.height - Height) > 1f)
        {
            throw new System.Exception(
                "The canvas is " + canvasRect.width.ToString("0") + "x" +
                canvasRect.height.ToString("0") + ", not " + Width + "x" + Height +
                ". Every screenshot would be of the wrong layout.");
        }

        Dump(canvas.transform, 0);
        BigGraphics(canvas);

        Transform nav = canvas.transform.Find("BottomNav");
        if (nav != null)
        {
            Graphic[] inNav = nav.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < inNav.Length; i++)
            {
                Graphic g = inNav[i];
                Color c = g.color;
                CanvasGroup grp = g.GetComponentInParent<CanvasGroup>();

                Debug.Log("[Nav] " + g.transform.parent.name + "/" + g.name +
                          "  " + g.GetType().Name +
                          "  rgba(" + (int)(c.r * 255) + "," + (int)(c.g * 255) + "," +
                          (int)(c.b * 255) + "," + c.a.ToString("0.00") + ")" +
                          (g.enabled ? "" : " DISABLED") +
                          (g.gameObject.activeInHierarchy ? "" : " INACTIVE") +
                          (g.canvas == null ? " NO-CANVAS" : "") +
                          (grp != null ? " group=" + grp.alpha.ToString("0.00") : "") +
                          (g.canvasRenderer != null && g.canvasRenderer.cull ? " CULLED" : ""));
            }
        }

        try
        {
            Shoot(canvas, rig, "01-map", "MapPanel");
            Shoot(canvas, rig, "02-camera", "CameraPanel");
            Shoot(canvas, rig, "03-browse", "BrowsePanel");
            Shoot(canvas, rig, "04-settings", "SettingsPanel");

            // The sheet and the walkthrough sit over whatever is behind them.
            Shoot(canvas, rig, "05-marker-sheet", "MapPanel", "InfoSheet/Safe/Sheet", "InfoSheet/InfoScrim");
            Shoot(canvas, rig, "06-tutorial", "MapPanel", "Tutorial/Overlay");
            Shoot(canvas, rig, "07-toast", "MapPanel", "ProximityToast/Safe/CardHolder");
            Shoot(canvas, rig, "08-state-card", "MapPanel", "MapPanel/Safe/StateCard");

            // Light mode, where the contrast decisions are easiest to get wrong.
            SetTheme(false);
            Shoot(canvas, rig, "09-settings-light", "SettingsPanel");
            Shoot(canvas, rig, "10-map-light", "MapPanel");
            SetTheme(true);
        }
        finally
        {
            rig.Dispose();
        }

        Debug.Log("[Preview] Wrote screenshots to " + Path.GetFullPath(OutputFolder));
    }

    /// <summary>
    /// Logs what is actually in the scene and how big it is.
    ///
    /// Added after a screen came back blank and three plausible explanations all
    /// turned out to be wrong. A rect that is zero-wide or parked off screen looks
    /// exactly like one that was never built.
    /// </summary>
    static void Dump(Transform t, int depth)
    {
        if (depth > 4)
            return;

        RectTransform rt = t as RectTransform;
        string size = rt != null
            ? rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") +
              " @" + rt.anchoredPosition.x.ToString("0") + "," +
              rt.anchoredPosition.y.ToString("0")
            : "(no rect)";

        Debug.Log("[Tree] " + new string(' ', depth * 2) + t.name +
                  "  " + size + (t.gameObject.activeSelf ? "" : "  OFF"));

        for (int i = 0; i < t.childCount; i++)
            Dump(t.GetChild(i), depth + 1);
    }

    /// <summary>
    /// Lists every graphic large enough to cover the screen, with its colour and
    /// where it sits in the hierarchy.
    ///
    /// The map came back as a single flat colour with only the mock pins on it,
    /// and three plausible culprits were all wrong. Rather than guess a fourth,
    /// this asks the scene which object is actually big enough to have done it.
    /// </summary>
    static void BigGraphics(Canvas canvas)
    {
        Graphic[] all = Object.FindObjectsByType<Graphic>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            Graphic g = all[i];
            Rect r = g.rectTransform.rect;

            if (r.width * r.height < Width * Height * 0.7f)
                continue;

            Color c = g.color;
            string path = g.name;
            for (Transform t = g.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;

            Debug.Log("[Big] " + path + "  " + r.width.ToString("0") + "x" +
                      r.height.ToString("0") +
                      "  rgba(" + (int)(c.r * 255) + "," + (int)(c.g * 255) + "," +
                      (int)(c.b * 255) + "," + c.a.ToString("0.00") + ")" +
                      (g.gameObject.activeInHierarchy ? "  ON" : "  off") +
                      (g.enabled ? "" : "  disabled"));
        }
    }

    // ------------------------------------------------------------------ capture

    /// <summary>
    /// A camera and a render texture pointed at the canvas.
    ///
    /// The canvas has to leave Screen Space - Overlay to be captured at all: an
    /// overlay canvas is composited after every camera and cannot be rendered into
    /// a texture. World space at exactly 1080x1920 also gives the reference
    /// resolution the whole UI was designed against, rather than whatever window
    /// size batch mode invents.
    /// </summary>
    class Rig
    {
        public Camera camera;
        public RenderTexture texture;

        RenderMode _mode;
        Vector2 _size;
        Vector3 _position;
        bool _scaler;
        CanvasScaler _scalerComponent;

        public static Rig Create(Canvas canvas)
        {
            Rig rig = new Rig();

            RectTransform rt = (RectTransform)canvas.transform;
            rig._mode = canvas.renderMode;
            rig._size = rt.sizeDelta;
            rig._position = rt.position;

            rig._scalerComponent = canvas.GetComponent<CanvasScaler>();
            if (rig._scalerComponent != null)
            {
                rig._scaler = rig._scalerComponent.enabled;
                rig._scalerComponent.enabled = false;
            }

            GameObject go = new GameObject("PreviewCamera");
            rig.camera = go.AddComponent<Camera>();
            rig.camera.orthographic = true;
            rig.camera.orthographicSize = Height * 0.5f;
            rig.camera.nearClipPlane = 0.1f;
            rig.camera.farClipPlane = 5000f;
            rig.camera.clearFlags = CameraClearFlags.SolidColor;
            rig.camera.backgroundColor = ThemeManager.Get(ThemeRole.Background);
            go.transform.position = new Vector3(0f, 0f, -1000f);
            go.transform.rotation = Quaternion.identity;

            // The target texture goes on first, and that ordering is the whole
            // trick. A Screen Space - Camera canvas sizes itself from the camera's
            // pixel rect, and a camera with no target texture reports the screen -
            // which in batch mode is an invented 640x480. Hooking the canvas up
            // first meant every screenshot was of a layout for a screen nobody
            // owns, at the right resolution, looking entirely plausible.
            rig.texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rig.texture.antiAliasing = 4;
            rig.camera.targetTexture = rig.texture;

            // Screen Space - Camera, not World Space.
            //
            // World space was the obvious way to point a camera at a canvas, and it
            // lied a different way: only nested canvases drew, so the map came back
            // with its pins and none of its chrome.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = rig.camera;
            canvas.planeDistance = 100f;

            Canvas.ForceUpdateCanvases();

            return rig;
        }

        public void Dispose()
        {
            if (camera != null)
                Object.DestroyImmediate(camera.gameObject);

            if (texture != null)
                Object.DestroyImmediate(texture);

            Canvas canvas = AppUIBuilder.FindRootCanvas();
            if (canvas != null)
                canvas.renderMode = _mode;

            if (_scalerComponent != null)
                _scalerComponent.enabled = _scaler;
        }
    }

    /// <summary>
    /// Turns on exactly the panels named, renders, and writes the file. Panels are
    /// given by path so an overlay can be raised over a tab without the tab
    /// controller, which is not running.
    /// </summary>
    static void Shoot(Canvas canvas, Rig rig, string name, params string[] visible)
    {
        var wanted = new HashSet<string>(visible);

        // Top-level panels: only the ones asked for.
        for (int i = 0; i < canvas.transform.childCount; i++)
        {
            Transform child = canvas.transform.GetChild(i);

            // The nav bar is on every screen. Everything else is on only when the
            // shot asks for it, by name or as the root of a requested path.
            bool on = child.name == "BottomNav" || wanted.Contains(child.name);

            for (int p = 0; p < visible.Length && !on; p++)
                on = visible[p].StartsWith(child.name + "/");

            child.gameObject.SetActive(on);
        }

        // Overlays, addressed by path.
        var raised = new List<GameObject>();
        for (int i = 0; i < visible.Length; i++)
        {
            if (!visible[i].Contains("/"))
                continue;

            Transform found = canvas.transform.Find(visible[i]);
            if (found == null)
            {
                Debug.LogWarning("[Preview] No object at \"" + visible[i] + "\".");
                continue;
            }

            if (found.gameObject.activeSelf)
                continue;

            found.gameObject.SetActive(true);
            raised.Add(found.gameObject);
        }

        Highlight(canvas.transform,
            name.Contains("camera") ? "CameraTabButton" :
            name.Contains("settings") ? "SettingsTabButton" : "MapTabButton");

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
        Canvas.ForceUpdateCanvases();

        rig.camera.backgroundColor = ThemeManager.Get(ThemeRole.Background);
        rig.camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rig.texture;

        Texture2D shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
        shot.Apply();

        RenderTexture.active = previous;

        File.WriteAllBytes(Path.Combine(OutputFolder, name + ".png"), shot.EncodeToPNG());
        Object.DestroyImmediate(shot);

        for (int i = 0; i < raised.Count; i++)
            raised[i].SetActive(false);

        Debug.Log("[Preview] " + name);
    }

    static void SetTheme(bool dark)
    {
        ThemeManager.SetDark(dark);

        // Nothing is playing, so the elements are not subscribed to the change.
        ThemedElement[] themed = Object.FindObjectsByType<ThemedElement>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < themed.Length; i++)
            themed[i].Apply();
    }

    // -------------------------------------------------------------- stand-ins

    /// <summary>
    /// Fills in the text a controller would write at runtime. Without it half the
    /// screen reads "Name", "Distance", "Count" - which tells me nothing about
    /// whether a real name fits in the space it has.
    /// </summary>
    static void StandIn(Transform root)
    {
        Set(root, "MapPanel/Safe/Panel/Nearest/Row/Text/Name", "Безистен");
        Set(root, "MapPanel/Safe/Panel/Nearest/Row/Distance", "180 m");

        Set(root, "CameraPanel/LiveARView/Safe/Panel/Dock/Row/Text/Name", "Часовниковата кула");
        Set(root, "CameraPanel/LiveARView/Safe/Panel/Dock/Row/Distance", "62 m");
        Hide(root, "CameraPanel/LiveARView/Safe/Panel/Dock/Row/Empty");

        Set(root, "InfoSheet/Safe/Sheet/Body/Head/TitleText", "Безистен");
        Set(root, "InfoSheet/Safe/Sheet/Body/Distance/Value", "180 m");
        Set(root, "InfoSheet/Safe/Sheet/Body/MetaText", "15 век  ·  Неизвестен  ·  Търговски");
        Set(root, "InfoSheet/Safe/Sheet/Body/DescriptionText",
            "Ямболският безистен е един от малкото запазени покрити пазари на " +
            "Балканския полуостров. Построен е през XV век и дълго време е бил " +
            "търговското сърце на града.");

        Set(root, "SettingsPanel/Safe/SettingsContent/Root/Scroll/Viewport/Content/" +
                  "PrivacyCard/Row/Count", "2 hidden");
        Set(root, "SettingsPanel/Safe/SettingsContent/Root/Scroll/Viewport/Content/" +
                  "Version/Label", "Version 0.2.0");

        Set(root, "ProximityToast/Safe/CardHolder/Card/Row/Text/Name", "Часовниковата кула");
        Set(root, "ProximityToast/Safe/CardHolder/Card/Row/Distance", "48 m");

        Set(root, "MapPanel/Safe/Scale/Label", "100 m");
    }

    static void Set(Transform root, string path, string text)
    {
        Transform found = root.Find(path);
        if (found == null)
        {
            Debug.LogWarning("[Preview] stand-in missed: " + path);
            return;
        }

        TMP_Text label = found.GetComponent<TMP_Text>();
        if (label != null)
            label.text = text;
    }

    static void Hide(Transform root, string path)
    {
        Transform found = root.Find(path);
        if (found != null)
            found.gameObject.SetActive(false);
    }

    /// <summary>
    /// Three pins on the map. MapView places the real ones at runtime from live
    /// coordinates, so these are stood at fixed offsets purely to show what a pin
    /// looks like next to everything else.
    /// </summary>
    static void MockPins(Transform root)
    {
        Transform content = root.Find("MapPanel/MapSurface/Content");
        if (content == null)
        {
            Debug.LogWarning("[Preview] No map content to put mock pins in.");
            return;
        }

        MapPinView prefab = AssetDatabase.LoadAssetAtPath<MapPinView>("Assets/Prefabs/MapPin.prefab");
        if (prefab == null)
        {
            Debug.LogWarning("[Preview] No MapPin prefab.");
            return;
        }

        string[] names = { "Безистен", "ПМГ", "Часовникът" };
        Color[] colours =
        {
            new Color32(0x45, 0xC4, 0xB0, 0xFF),
            new Color32(0x00, 0x33, 0xFF, 0xFF),
            new Color32(0x72, 0x00, 0x7A, 0xFF)
        };
        Vector2[] at = { new Vector2(-150f, 260f), new Vector2(210f, -80f), new Vector2(-60f, -330f) };

        for (int i = 0; i < 3; i++)
        {
            MapPinView pin = (MapPinView)PrefabUtility.InstantiatePrefab(prefab, content);
            ((RectTransform)pin.transform).anchoredPosition = at[i];

            if (pin.bulb != null)
                pin.bulb.color = colours[i];

            if (pin.ring != null)
                pin.ring.color = MarkerPalette.RingFor(colours[i]);

            if (pin.label != null)
                pin.label.text = names[i];

            pin.SetLabelAllowed(true);
            pin.SetSelected(i == 0);
        }

        // The reader, and the ring that says how far.
        Show(content, "UserDot");
        Show(content, "RingNear");
        Show(content, "RingFar");
    }

    /// <summary>
    /// Three dots on the radar. CameraRadar places the real ones from a live
    /// compass, which a scene that is not playing does not have.
    /// </summary>
    static void MockRadar(Transform root)
    {
        Transform radar = root.Find("CameraPanel/LiveARView/Safe/Panel/RadarRow/Radar");
        Transform dots = radar != null ? radar.Find("Dots") : null;

        // Point the cone somewhere believable, since nothing is driving it here.
        Transform facing = radar != null ? radar.Find("Facing") : null;
        if (facing != null)
            facing.localRotation = Quaternion.Euler(0f, 0f, -42f);

        if (dots == null)
        {
            Debug.LogWarning("[Preview] No radar to put mock dots in.");
            return;
        }

        Color[] colours =
        {
            new Color32(0x45, 0xC4, 0xB0, 0xFF),
            new Color32(0x00, 0x33, 0xFF, 0xFF),
            new Color32(0x72, 0x00, 0x7A, 0xFF)
        };

        // True bearings, north up. The cone is what turns.
        float[] angles = { 18f, 128f, 286f };
        float[] reach = { 58f, 124f, 92f };

        for (int i = 0; i < 3; i++)
        {
            GameObject go = new GameObject("MockDot" + i, typeof(RectTransform));
            go.layer = dots.gameObject.layer;
            go.transform.SetParent(dots, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(26f, 26f);

            float radians = angles[i] * Mathf.Deg2Rad;
            rt.anchoredPosition = new Vector2(Mathf.Sin(radians) * reach[i],
                                              Mathf.Cos(radians) * reach[i]);

            Image ring = go.AddComponent<Image>();
            ring.sprite = UISprites.Circle;
            ring.color = MarkerPalette.RingFor(colours[i]);
            ring.raycastTarget = false;

            GameObject inner = new GameObject("Fill", typeof(RectTransform));
            inner.layer = go.layer;
            inner.transform.SetParent(rt, false);

            RectTransform innerRect = (RectTransform)inner.transform;
            innerRect.anchorMin = new Vector2(0.5f, 0.5f);
            innerRect.anchorMax = new Vector2(0.5f, 0.5f);
            innerRect.pivot = new Vector2(0.5f, 0.5f);
            innerRect.sizeDelta = new Vector2(19f, 19f);

            Image fill = inner.AddComponent<Image>();
            fill.sprite = UISprites.Circle;
            fill.color = colours[i];
            fill.raycastTarget = false;
        }
    }

    /// <summary>
    /// Lights the tab this screenshot is of. TabNavigationController does it at
    /// runtime, and nothing in a still scene calls it.
    /// </summary>
    static void Highlight(Transform root, string tab)
    {
        string[] all = { "MapTabButton", "CameraTabButton", "SettingsTabButton" };

        for (int i = 0; i < all.Length; i++)
        {
            Transform button = root.Find("BottomNav/Row/" + all[i]);
            if (button == null)
                continue;

            bool active = all[i] == tab;
            Color colour = ThemeManager.Get(active ? ThemeRole.Accent : ThemeRole.TextMuted);

            Transform indicator = button.Find("Indicator");
            if (indicator != null)
            {
                Image pill = indicator.GetComponent<Image>();
                if (pill != null)
                {
                    Color fill = ThemeManager.Get(ThemeRole.Accent);
                    fill.a = active ? 0.16f : 0f;
                    pill.color = fill;
                }
            }

            Transform label = button.Find("Label");
            if (label != null)
            {
                TMP_Text text = label.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.color = colour;
                    text.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            Transform icon = button.Find("Icon/Icon");
            if (icon != null)
            {
                Graphic g = icon.GetComponent<Graphic>();
                if (g != null)
                    g.color = colour;
            }
        }
    }

    static void Show(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null)
            found.gameObject.SetActive(true);
    }
}
