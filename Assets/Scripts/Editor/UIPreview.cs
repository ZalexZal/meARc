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
        Pieces.Build(canvas.transform);

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
        DeadControls(canvas);

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
            // Both themes, the same screens, every time.
            //
            // Light mode used to get six of the twenty-five, chosen by guessing
            // which ones the contrast was likeliest to go wrong on. It went
            // wrong on the map, which was one of the six, and on the pin labels
            // drawn over it, which no shot at this size would have shown - so
            // the guess was wrong twice over. Shooting both is cheap; deciding
            // in advance which screens can afford not to be looked at is not.
            Shots(canvas, rig, "dark");

            SetTheme(false);
            Stranded(canvas);
            Shots(canvas, rig, "light");
            SetTheme(true);
        }
        finally
        {
            rig.Dispose();
        }

        Debug.Log("[Preview] Wrote screenshots to " + Path.GetFullPath(OutputFolder));
    }

    /// <summary>One pass over every screen, in whichever theme is currently set.</summary>
    static void Shots(Canvas canvas, Rig rig, string theme)
    {
        string t = "-" + theme;

        Pieces.Strip(canvas.transform, "Mid");
        Shoot(canvas, rig, "01-map" + t, "MapPanel");
        Shoot(canvas, rig, "02-camera" + t, "CameraPanel");
        Shoot(canvas, rig, "03-browse" + t, "BrowsePanel");
        Shoot(canvas, rig, "04-settings" + t, "SettingsPanel");

        // The sheet and the walkthrough sit over whatever is behind them.
        Shoot(canvas, rig, "05-marker-sheet" + t, "MapPanel", "InfoSheet/Safe/Sheet", "InfoSheet/InfoScrim");
        Shoot(canvas, rig, "06-tutorial" + t, "MapPanel", "Tutorial/Overlay");
        Shoot(canvas, rig, "07-toast" + t, "MapPanel", "ProximityToast/Safe/CardHolder");
        Shoot(canvas, rig, "08-state-card" + t, "MapPanel", "MapPanel/Safe/StateCard");

        // The map with the strip at the start of a trail, where the previous row
        // is switched off and the card is at its shortest.
        Pieces.Strip(canvas.transform, "Start");
        Shoot(canvas, rig, "09-map-start" + t, "MapPanel");
        Pieces.Strip(canvas.transform, "Mid");

        // --- the way in ---
        Pieces.TrailsState(canvas.transform, "Rows");
        Shoot(canvas, rig, "10-welcome" + t, "Doors/Welcome");
        Shoot(canvas, rig, "11-terms" + t, "Doors/Terms");
        Shoot(canvas, rig, "12-trails" + t, "MapPanel", "Doors/Backdrop", "Doors/Trails");

        Pieces.CodeState(canvas.transform, "", null);
        Shoot(canvas, rig, "13-code" + t, "MapPanel", "Doors/Backdrop", "Doors/Code");

        // The team card as somebody sees it before they have a team, which is
        // the state every walker starts in.
        Shoot(canvas, rig, "26-team" + t, "MapPanel", "Doors/Backdrop", "Doors/Team");

        // The two states of the list that are not a list. The third, waiting, is
        // the empty one with different words.
        Pieces.TrailsState(canvas.transform, "Empty");
        Shoot(canvas, rig, "14-trails-empty" + t, "MapPanel", "Doors/Backdrop", "Doors/Trails");
        Pieces.TrailsState(canvas.transform, "Rows");

        // Half a code in, and a code the server refused. Between them they show a
        // filled box beside an empty one, where the caret sits, and what the card
        // looks like once it has to say something went wrong.
        Pieces.CodeState(canvas.transform, "T97", null);
        Shoot(canvas, rig, "15-code-typing" + t, "MapPanel", "Doors/Backdrop", "Doors/Code");

        Pieces.CodeState(canvas.transform, "T97S25", "code.unknown");
        Shoot(canvas, rig, "16-code-wrong" + t, "MapPanel", "Doors/Backdrop", "Doors/Code");

        // --- inside a marker ---
        const string SheetPath = "InfoSheet/Safe/Sheet";

        Pieces.Page(canvas.transform, Pieces.Which.Overview);
        Shoot(canvas, rig, "17-sheet-pieces" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Page(canvas.transform, Pieces.Which.Video);
        Shoot(canvas, rig, "18-video" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Page(canvas.transform, Pieces.Which.Audio);
        Shoot(canvas, rig, "19-audio" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Page(canvas.transform, Pieces.Which.Question);
        Shoot(canvas, rig, "20-question" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        // The four endings a creator can choose between, and the one they cannot:
        // the server not answering at all.
        Pieces.Verdict(canvas.transform, Pieces.Ending.Retry);
        Shoot(canvas, rig, "21-wrong-retry" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Verdict(canvas.transform, Pieces.Ending.Lock);
        Shoot(canvas, rig, "22-wrong-lock" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Verdict(canvas.transform, Pieces.Ending.Deduct);
        Shoot(canvas, rig, "23-wrong-deduct" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Verdict(canvas.transform, Pieces.Ending.Skip);
        Shoot(canvas, rig, "24-wrong-skip" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        Pieces.Verdict(canvas.transform, Pieces.Ending.Right);
        Shoot(canvas, rig, "25-correct" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim");

        // The chooser sits over the sheet, so both are named. It ships switched
        // off, which is why it has to be asked for by name here.
        Pieces.Page(canvas.transform, Pieces.Which.Overview);
        Shoot(canvas, rig, "27-report" + t, "MapPanel", SheetPath, "InfoSheet/InfoScrim",
              "InfoSheet/Safe/ReportChooser");
    }

    /// <summary>
    /// Everything still holding a dark-theme colour after the switch to light.
    ///
    /// UIFactory has two ways to colour a graphic and only one of them follows
    /// the theme: Fill and Text attach a ThemedElement, Plain bakes whatever
    /// Color it is handed. Handing Plain a ThemeManager.Get(...) therefore
    /// freezes the editor's current theme into the asset, and the result is
    /// invisible in the theme it was built in - which is why the map pins
    /// shipped with the dark Surface baked into their label plates and nobody
    /// saw it until the app was opened in daylight.
    ///
    /// Roles whose two palettes agree are skipped: there is nothing to strand.
    /// A colour a component paints at runtime will show up here too, because
    /// nothing is running - so this is a list to read, not a list of faults.
    /// </summary>
    static void Stranded(Canvas canvas)
    {
        Palette dark = AppTheme.Dark();
        Palette light = AppTheme.Light();
        ThemeRole[] roles = (ThemeRole[])System.Enum.GetValues(typeof(ThemeRole));

        Graphic[] all = canvas.GetComponentsInChildren<Graphic>(true);
        int found = 0;

        for (int i = 0; i < all.Length; i++)
        {
            Graphic g = all[i];

            // A ThemedElement means the graphic repaints itself, and it does so
            // in OnEnable as well as on the event - so one on an inactive panel
            // is not stranded, it is merely waiting. Counting those was the
            // difference between a list of 120 and a list worth reading: every
            // primary button in the app was on the long one, and all of them
            // were measured correct on the phone.
            if (g.GetComponent<ThemedElement>() != null)
                continue;

            for (int r = 0; r < roles.Length; r++)
            {
                if (Same(dark.Get(roles[r]), light.Get(roles[r])))
                    continue;

                if (!Same(g.color, dark.Get(roles[r])))
                    continue;

                Debug.LogWarning("[Stranded] " + NamePath(g.transform) + "  (" +
                                 g.GetType().Name + ") still holds Dark." + roles[r]);
                found++;
                break;
            }
        }

        Debug.Log("[Stranded] " + found + " graphic(s) kept a dark colour in the light theme.");
    }

    /// <summary>Colour equality on the three channels. Alpha is applied separately by Fill.</summary>
    static bool Same(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.004f &&
               Mathf.Abs(a.g - b.g) < 0.004f &&
               Mathf.Abs(a.b - b.b) < 0.004f;
    }

    /// <summary>Where a graphic lives, so a warning names something findable.</summary>
    static string NamePath(Transform t)
    {
        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
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


    /// <summary>
    /// Names every control that cannot possibly do anything.
    ///
    /// Two ways a button is alive: a persistent listener written by the builder, or
    /// a reference held by some component that will wire it in Awake. Anything with
    /// neither is a control the reader can press with nothing at the other end -
    /// which is exactly what "are there buttons that do nothing" means, and is not
    /// visible in a screenshot.
    /// </summary>
    static void DeadControls(Canvas canvas)
    {
        Selectable[] controls = canvas.GetComponentsInChildren<Selectable>(true);
        MonoBehaviour[] components =
            Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                    FindObjectsSortMode.None);

        // Everything any component in the scene points at. One pass, because doing
        // it per control is quadratic and this runs over a few thousand fields.
        var referenced = new HashSet<Object>();

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
                continue;

            SerializedObject serialized = new SerializedObject(components[i]);
            SerializedProperty property = serialized.GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue != null)
                {
                    referenced.Add(property.objectReferenceValue);
                }
            }
        }

        int dead = 0;

        for (int i = 0; i < controls.Length; i++)
        {
            Selectable control = controls[i];

            Button button = control as Button;
            Toggle toggle = control as Toggle;
            TMP_InputField field = control as TMP_InputField;

            bool wired = field != null;   // a field's work is its own text

            if (button != null && button.onClick.GetPersistentEventCount() > 0)
                wired = true;

            if (toggle != null && toggle.onValueChanged.GetPersistentEventCount() > 0)
                wired = true;

            // Held by something that will wire it when the app runs.
            if (!wired && (referenced.Contains(control) ||
                           referenced.Contains(control.gameObject)))
            {
                wired = true;
            }

            // A drag handle is a control whose whole job is the gesture on it.
            if (!wired && control.GetComponent<SheetDragHandle>() != null)
                wired = true;

            if (wired)
                continue;

            dead++;

            string path = control.name;
            for (Transform t = control.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;

            Debug.LogWarning("[Dead] " + path + " (" + control.GetType().Name +
                             ") has no listener and nothing refers to it.");
        }

        Debug.Log("[Dead] " + dead + " of " + controls.Length +
                  " controls have nothing at the other end.");
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
    /// <summary>Where a sheet was parked before a shot moved it, so it goes back.</summary>
    static readonly Dictionary<RectTransform, Vector2> _parked =
        new Dictionary<RectTransform, Vector2>();

    static void Shoot(Canvas canvas, Rig rig, string name, params string[] visible)
    {
        var wanted = new HashSet<string>(visible);

        // Top-level panels: only the ones asked for.
        for (int i = 0; i < canvas.transform.childCount; i++)
        {
            Transform child = canvas.transform.GetChild(i);

            // The nav bar is on every screen except a way-in screen, which covers
            // it - DoorFlow switches it off at runtime for the same reason.
            bool on = (child.name == "BottomNav" && !IsDoorShot(visible)) ||
                      wanted.Contains(child.name);

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

            // A bottom sheet is hidden by being parked below the screen, not by
            // being disabled - so enabling one photographs an empty map. This is
            // what 05-marker-sheet was: a scrim, and a sheet sitting 890 units
            // under the bottom edge, for every run of this harness so far.
            SheetAnimator sheet = found.GetComponent<SheetAnimator>();
            if (sheet != null)
            {
                RectTransform sheetRect = (RectTransform)found;
                _parked[sheetRect] = sheetRect.anchoredPosition;
                sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x,
                                                         sheet.openY);
            }

            if (found.gameObject.activeSelf)
                continue;

            found.gameObject.SetActive(true);
            raised.Add(found.gameObject);
        }

        // The map's bar, scale, attribution and dock go down behind a door, exactly
        // as DoorFlow takes them down before the blur is captured. Without this the
        // harness photographs a composition the app never draws - and it did: every
        // picture of these two screens had the map's dock sitting legibly under the
        // card, so half the controls on screen were ones the reader cannot press.
        Transform mapChrome = canvas.transform.Find("MapPanel/Safe");
        bool chromeWas = mapChrome != null && mapChrome.gameObject.activeSelf;

        if (mapChrome != null && IsDoorShot(visible))
            mapChrome.gameObject.SetActive(false);

        Highlight(canvas.transform,
            name.Contains("camera") ? "CameraTabButton" :
            name.Contains("settings") ? "SettingsTabButton" : "MapTabButton");

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
        Canvas.ForceUpdateCanvases();

        // Now that the door is actually up. The trail card measures its own list
        // and shrinks to it, and a layout group on a switched-off object reports
        // nothing - so doing this any earlier measures an empty screen and gives
        // the card a list of no height at all. DoorFlow reveals the panel before
        // it calls Refresh for the same reason.
        TrailsDoor sizing = canvas.GetComponentInChildren<TrailsDoor>(false);
        if (sizing != null)
        {
            sizing.SizeList();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
            Canvas.ForceUpdateCanvases();
        }

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

        if (mapChrome != null)
            mapChrome.gameObject.SetActive(chromeWas);

        for (int i = 0; i < raised.Count; i++)
            raised[i].SetActive(false);

        foreach (KeyValuePair<RectTransform, Vector2> pair in _parked)
            pair.Key.anchoredPosition = pair.Value;

        _parked.Clear();

        Debug.Log("[Preview] " + name);
    }

    static bool IsDoorShot(string[] visible)
    {
        for (int i = 0; i < visible.Length; i++)
        {
            if (visible[i].StartsWith("Doors/"))
                return true;
        }

        return false;
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

        const string Sheet = "InfoSheet/Safe/Sheet/Overview/Body/Scroll/Viewport/Content/";

        Set(root, Sheet + "Head/TitleText", "Безистен");
        Set(root, Sheet + "Distance/Value", "180 m");
        Set(root, Sheet + "MetaText", "15 век  ·  Неизвестен  ·  Търговски");
        Set(root, Sheet + "DescriptionText",
            "Ямболският безистен е един от малкото запазени покрити пазари на " +
            "Балканския полуостров. Построен е през XV век и дълго време е бил " +
            "търговското сърце на града.");

        Set(root, "MapPanel/Safe/TopBar/TrailChip/Row/Name", "Ямбол — център");
        Set(root, "MapPanel/Safe/TopBar/TrailChip/Row/Mode/Label", "Свободен");

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
    /// A marker with something in it, and the switchboard for photographing every
    /// state its sheet can be in.
    ///
    /// None of this runs the real thing. Nothing in the scene is playing, so there
    /// is no MarkerTaskService to hold progress and no VideoPlayer to prepare a
    /// file - what these do is put the views into the state the server would have
    /// put them in, which is what a picture can check. The logic that decides
    /// between those states is checked by the compiler and by reading it; the
    /// drawing is the part that cannot be checked any other way.
    /// </summary>
    static class Pieces
    {
        public enum Which { Overview, Video, Audio, Question }

        public enum Ending { Retry, Lock, Deduct, Skip, Right }

        static MarkerPieceController _controller;
        static BuildingData _marker;

        public static void Build(Transform root)
        {
            Transform sheet = root.Find("InfoSheet/Safe/Sheet");
            if (sheet == null)
            {
                Debug.LogWarning("[Preview] No marker sheet to fill.");
                return;
            }

            _controller = sheet.GetComponent<MarkerPieceController>();
            if (_controller == null)
            {
                Debug.LogWarning("[Preview] The sheet has no piece controller.");
                return;
            }

            _marker = ScriptableObject.CreateInstance<BuildingData>();
            _marker.buildingId = "preview-marker";
            _marker.buildingName = "The Bezisten";
            _marker.buildingNameBg = "Безистен";

            _marker.media = new[]
            {
                new MarkerMedia
                {
                    kind = "video",
                    path = "worlds/preview/m/1/inside.mp4",
                    seconds = 134,
                    bytes = 26L * 1024L * 1024L,
                    title = new LocalisedText
                    {
                        en = "Inside the Bezisten",
                        bg = "Безистенът отвътре"
                    }
                },
                new MarkerMedia
                {
                    kind = "audio",
                    path = "worlds/preview/m/1/keeper.m4a",
                    seconds = 220,
                    bytes = 2L * 1024L * 1024L,
                    title = new LocalisedText
                    {
                        en = "The keeper's account",
                        bg = "Разказът на пазача"
                    }
                }
            };

            _marker.task = new MarkerTask
            {
                kind = "choice",
                points = 10,
                on_wrong = "retry",
                lock_seconds = 300,
                deduct = 5,
                prompt = new LocalisedText
                {
                    en = "Which century was the Bezisten built in?",
                    bg = "През кой век е построен Безистенът?"
                },
                options = new[]
                {
                    new LocalisedText { en = "14th century", bg = "XIV век" },
                    new LocalisedText { en = "15th century", bg = "XV век" },
                    new LocalisedText { en = "16th century", bg = "XVI век" },
                    new LocalisedText { en = "17th century", bg = "XVII век" }
                }
            };

            _controller.Bind(_marker);
        }

        /// <summary>Which half of the sheet is showing, and what is on the page.</summary>
        public static void Page(Transform root, Which which)
        {
            if (_controller == null)
                return;

            _controller.Bind(_marker);   // back to the overview, list redrawn

            if (which == Which.Overview)
                return;

            Show(_controller.overview, false);
            Show(_controller.page, true);

            bool question = which == Which.Question;
            Show(_controller.media != null ? _controller.media.gameObject : null, !question);
            Show(_controller.question != null ? _controller.question.gameObject : null, question);

            if (question)
            {
                if (_controller.pageTitle != null)
                    _controller.pageTitle.text = AppStrings.Get("piece.answer");

                _controller.question.Show(_marker);
                return;
            }

            MediaPlayerView media = _controller.media;
            MarkerMedia piece = which == Which.Video ? _marker.media[0] : _marker.media[1];

            if (_controller.pageTitle != null)
            {
                _controller.pageTitle.text = AppStrings.Get(
                    piece.Kind == MarkerMediaKind.Audio ? "piece.listen" : "piece.watch");
            }

            // Not media.Show: that reaches for a VideoPlayer that Awake never made,
            // because nothing in this scene is playing. The chrome is set by hand.
            Show(media.screenHolder, which == Which.Video);
            Show(media.noticeHolder, false);
            Show(media.dataHolder, false);

            if (media.titleLabel != null)
                media.titleLabel.text = piece.Title;

            if (media.timeLabel != null)
                media.timeLabel.text = "0:37 / " + piece.Duration;

            if (media.progressFill != null)
            {
                media.progressFill.anchorMax =
                    new Vector2(37f / Mathf.Max(1f, piece.seconds), 1f);
            }
        }

        /// <summary>
        /// One of the endings, drawn as the server would have left it.
        /// </summary>
        public static void Verdict(Transform root, Ending ending)
        {
            Page(root, Which.Question);

            MarkerQuestionView view = _controller != null ? _controller.question : null;
            if (view == null)
                return;

            bool right = ending == Ending.Right;

            // The reader picked the second option. Only that one is judged - marking
            // the others would be marking the right answer, which is the thing this
            // whole design exists to avoid.
            AnswerOptionView[] options =
                view.optionsParent.GetComponentsInChildren<AnswerOptionView>(true);

            // Retry and Deduct leave the untried options answerable; Lock and Skip
            // do not. Same split the view itself makes.
            bool carriesOn = ending == Ending.Retry || ending == Ending.Deduct;

            for (int i = 0; i < options.Length; i++)
            {
                options[i].Set(i == 1
                    ? (right ? AnswerOptionView.State.Right : AnswerOptionView.State.Wrong)
                    : (carriesOn ? AnswerOptionView.State.Idle
                                 : AnswerOptionView.State.Muted));
            }

            Show(view.ruleHolder, ending == Ending.Lock || ending == Ending.Deduct ||
                                  ending == Ending.Skip);

            if (view.ruleLabel != null)
            {
                view.ruleLabel.text = AppStrings.Get(
                    ending == Ending.Lock ? "task.lockCap" :
                    ending == Ending.Deduct ? "task.dedCap" : "task.skipCap");
            }

            Show(view.lockHolder, ending == Ending.Lock);
            if (ending == Ending.Lock && view.lockLabel != null)
                view.lockLabel.text = string.Format(AppStrings.Get("task.lockMsg"), "4:32");

            Show(view.scoreHolder, ending == Ending.Deduct);
            if (ending == Ending.Deduct && view.scoreLabel != null)
                view.scoreLabel.text = "20 " + AppStrings.Get("task.points");

            Show(view.finishedHolder, ending == Ending.Skip);
            if (ending == Ending.Skip)
            {
                if (view.finishedTitle != null)
                    view.finishedTitle.text = AppStrings.Get("task.skipMsg");

                if (view.finishedBody != null)
                    view.finishedBody.text = AppStrings.Get("task.skipBody");

                if (view.finishedButtonLabel != null)
                    view.finishedButtonLabel.text = AppStrings.Get("task.backToMap");
            }

            Bubble(view, ending);

            // Where the app puts it after a verdict. Without this the picture is of
            // the top of a page whose reply is below the fold.
            if (view.scroll != null)
                view.scroll.verticalNormalizedPosition = 0f;
        }

        static void Bubble(MarkerQuestionView view, Ending ending)
        {
            if (ending == Ending.Skip)
            {
                Show(view.bubbleHolder, false);
                return;
            }

            Show(view.bubbleHolder, true);

            string text =
                ending == Ending.Right
                    ? AppStrings.Get("task.correct") + "  " +
                      string.Format(AppStrings.Get("task.awarded"), 10)
                : ending == Ending.Deduct
                    ? string.Format(AppStrings.Get("task.dedMsg"), 5)
                : ending == Ending.Lock
                    ? AppStrings.Get("task.wrong")
                    : "Not quite. Look at the date on the plaque by the entrance.";

            if (view.bubbleLabel != null)
                view.bubbleLabel.text = text;

            Color colour = ThemeManager.Get(ending == Ending.Right
                ? ThemeRole.StatusGood
                : ThemeRole.StatusPoor);

            if (view.bubbleEdge != null)
                view.bubbleEdge.color = colour;

            if (view.bubbleFill != null)
            {
                Color wash = colour;
                wash.a = 0.14f;
                view.bubbleFill.color = wash;
            }
        }

        /// <summary>
        /// Two trails in the list, or the screen a first run actually sees.
        /// </summary>
        public static void TrailsState(Transform root, string state)
        {
            Transform door = root.Find("Doors/Trails");
            if (door == null)
                return;

            TrailsDoor trails = door.GetComponent<TrailsDoor>();
            Transform content = Dig(door, "Card/Body/Scroll/Viewport/Content");

            if (trails == null || content == null)
            {
                Debug.LogWarning("[Preview] No trails list to fill.");
                return;
            }

            bool rows = state == "Rows";

            // The card says something different with nothing in it, and the door
            // sets that from the service - which is not running here.
            if (trails.subtitle != null)
            {
                trails.subtitle.text = AppStrings.Get("trails.subtitle");
                trails.subtitle.gameObject.SetActive(rows);
            }

            Show(trails.emptyState, !rows);
            Show(trails.waitingState, false);
            Show(trails.failedState, false);

            TrailRowView[] existing = content.GetComponentsInChildren<TrailRowView>(true);

            if (existing.Length == 0 && rows)
            {
                string[] names = { "Ямбол — център", "ПМГ — училищен маршрут" };
                string[] roles = { "viewer", "editor" };
                string[] modes = { "freefall", "ctf" };

                for (int i = 0; i < names.Length; i++)
                {
                    TrailRowView row = Object.Instantiate(trails.rowPrefab, content);
                    row.Bind(new TrailService.Trail
                    {
                        id = "preview-" + i,
                        name = names[i],
                        role = roles[i],
                        mode = modes[i]
                    }, i == 0);
                }

                return;
            }

            for (int i = 0; i < existing.Length; i++)
                existing[i].gameObject.SetActive(rows);
        }

        /// <summary>
        /// Puts characters in the boxes, and optionally the line that comes back
        /// when the server says no.
        ///
        /// The boxes have only ever been photographed empty, which is the one state
        /// that cannot show whether a typed character lands in the right box, what
        /// a filled box looks like next to an empty one, or where the caret is.
        /// </summary>
        public static void CodeState(Transform root, string typed, string messageKey)
        {
            Transform door = root.Find("Doors/Code");
            if (door == null)
                return;

            CodeDoor code = door.GetComponent<CodeDoor>();
            if (code == null || code.boxes == null || code.boxes.cells == null)
            {
                Debug.LogWarning("[Preview] No code boxes to fill.");
                return;
            }

            CodeBoxes boxes = code.boxes;

            // Straight at the cells rather than through the field: the input field
            // does nothing outside play mode, and what is being photographed is how
            // the boxes draw, not how TMP stores a string.
            for (int i = 0; i < boxes.cells.Length; i++)
            {
                CodeBoxes.Cell cell = boxes.cells[i];
                if (cell == null)
                    continue;

                bool filled = i < typed.Length;
                bool here = i == typed.Length;

                if (cell.letter != null)
                    cell.letter.text = filled ? typed[i].ToString() : string.Empty;

                if (cell.fill != null)
                {
                    cell.fill.color = ThemeManager.Get(filled ? ThemeRole.Line
                                                              : ThemeRole.SurfaceAlt);
                }

                if (cell.outline != null)
                {
                    cell.outline.color = ThemeManager.Get(here ? ThemeRole.Accent
                                                                : ThemeRole.Line);
                }

                if (cell.caret != null)
                    cell.caret.gameObject.SetActive(here);
            }

            if (code.message != null)
            {
                bool say = !string.IsNullOrEmpty(messageKey);
                code.message.text = say ? AppStrings.Get(messageKey) : string.Empty;
                code.message.gameObject.SetActive(say);
            }
        }

        /// <summary>
        /// Finds a child by path, trying the safe-area wrapper as well. Whether
        /// SafeContent inserts a level depends on the device, and a hard-coded path
        /// that silently misses is a preview of nothing.
        /// </summary>
        static Transform Dig(Transform door, string tail)
        {
            Transform found = door.Find("Inner/" + tail);
            if (found != null)
                return found;

            return door.Find("Safe/Inner/" + tail);
        }

        /// <summary>
        /// Puts a believable route in the strip: one stop behind, one ahead.
        ///
        /// Straight at the labels rather than through NextStopStrip, because the
        /// component reads DiscoveryTracker and a live position, and neither exists
        /// in a batch-mode render.
        /// </summary>
        public static void Strip(Transform root, string state)
        {
            NextStopStrip strip = root.GetComponentInChildren<NextStopStrip>(true);

            if (strip == null)
            {
                Debug.LogWarning("[Preview] No strip on the map.");
                return;
            }

            bool started = state != "Start";
            bool done = state == "Done";

            Show(strip.previousRow, started);
            Show(strip.nextRow, !done);
            Show(strip.hereWire, !done);

            if (started && strip.previousName != null)
            {
                strip.previousName.text = "Часовниковата кула";
                strip.previousMeta.text = string.Format(AppStrings.Get("strip.stopFound"), 2);
            }

            if (done)
            {
                strip.hereName.text = string.Format(AppStrings.Get("strip.allFound"), 9);
                strip.hereMeta.text = AppStrings.Get("strip.complete");
                return;
            }

            strip.hereName.text = AppStrings.Get("strip.here");
            strip.hereMeta.text = started
                ? string.Format(AppStrings.Get("strip.foundOf"), 2, 9)
                : AppStrings.Get("strip.nothingYet");

            if (strip.nextName != null)
            {
                strip.nextName.text = "Градската градина";
                strip.nextMeta.text = string.Format(AppStrings.Get("strip.stopOf"), 3, 9);
            }

            if (strip.nextDistance != null)
                strip.nextDistance.text = AppStrings.FormatDistance(180d);

            if (strip.nextKnob != null)
                strip.nextKnob.color = MarkerPalette.ForKey("garden");
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null)
                go.SetActive(on);
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
