using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Photographs the marker prefab, from the command line, with no editor open.
///
/// UIPreview exists because the person building the screens could not see them.
/// This exists for the same reason and a worse case: a marker is a 3D object
/// that will be looked at from a standing eye height, at distance, outdoors, and
/// none of that can be judged from a mesh in a file.
///
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod MarkerPreview.Shoot
///             -logFile - -quit
///
/// Not a substitute for seeing it on a street. It is a substitute for shipping
/// a shape nobody has looked at.
/// </summary>
public static class MarkerPreview
{
    const string Output = "MarkerShots";
    const int Width = 900;
    const int Height = 1100;

    public static void Shoot()
    {
        // The scene first: the builder works on the Canvas in it and refuses
        // without one, which is a clearer failure than a prefab quietly not
        // being rebuilt.
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        // Rebuilt, so this photographs what the builder makes rather than
        // whatever happened to be saved last.
        AppUIBuilder.BuildForPreview();
        AssetDatabase.Refresh();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/BuildingMarker.prefab");

        if (prefab == null)
            throw new System.Exception("No BuildingMarker prefab after the build.");

        Directory.CreateDirectory(Output);

        GameObject stage = new GameObject("Stage");

        try
        {
            // A ground plane, to catch the shadows.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform);
            ground.transform.localScale = Vector3.one * 4f;
            // Coloured through a property block rather than the material asset.
            // Setting _BaseColor on a freshly created .mat did not survive to the
            // render - the same route MarkerView uses does, and a white ring on a
            // white ground tells me nothing about whether the ring reads.
            Renderer groundRenderer = ground.GetComponent<Renderer>();
            groundRenderer.sharedMaterial =
                MarkerShapes.Surface("PreviewGround", new Color(0.62f, 0.60f, 0.57f), 0.05f);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.55f, 0.53f, 0.50f));
            block.SetColor("_Color", new Color(0.55f, 0.53f, 0.50f));
            groundRenderer.SetPropertyBlock(block);

            // Lit the way a phone is lit, nothing added here. This used to bring
            // its own light, so it showed coloured shapes while the phone showed
            // black ones. The editor works the ambient light out for itself and
            // a phone does not, so start from none, as a phone does, and let
            // the app's own code supply it.
            RenderSettings.ambientProbe = new UnityEngine.Rendering.SphericalHarmonicsL2();
            MarkerSpawner.LightFromAmbientColour();
            if (RenderSettings.ambientProbe[0, 0] <= 0.01f)
                throw new System.Exception("The scene has no ambient light: every side the sun misses will render black.");

            GameObject camera = new GameObject("Camera");
            camera.transform.SetParent(stage.transform);
            Camera cam = camera.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.80f);
            cam.fieldOfView = 55f;

            // Three colours, because the whole point of the pin is that a
            // creator's colour is what tells two markers apart.
            Color[] colours =
            {
                new Color(0.27f, 0.77f, 0.69f),   // the app's teal
                new Color(0.45f, 0.00f, 0.48f),   // the purple marker A uses
                new Color(0.90f, 0.42f, 0.20f),   // something warm
            };

            // Eye height, a couple of paces back: what a person actually sees.
            Shoot(cam, prefab, colours, new Vector3(0f, 1.6f, -4.5f), 5f, "eye");

            // And from further off, where a marker is small and the ring is the
            // thing still doing the work.
            Shoot(cam, prefab, colours, new Vector3(0f, 1.7f, -9f), 6f, "far");

            // Looking down, as somebody holding a phone at chest height does.
            Shoot(cam, prefab, colours, new Vector3(0f, 2.2f, -3.0f), 22f, "down");

            // Every shape a creator can pick, side by side in the colours they
            // would be given, and the sign close enough to read.
            string[] keys = { "Sphere", "Cone", "Cube", "Sign" };
            GameObject[] all = new GameObject[keys.Length + 1];
            all[0] = prefab;
            for (int i = 0; i < keys.Length; i++)
            {
                all[i + 1] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Marker" + keys[i] + ".prefab");
                if (all[i + 1] == null)
                    throw new System.Exception("No Marker" + keys[i] + " prefab after the build.");
            }

            // The shapes are designed to one width so none outweighs the rest;
            // the sign is meant to be wider. A mesh of the wrong native size
            // (the old 2 m sphere) fails here rather than on a street.
            for (int i = 0; i < 4; i++)
            {
                MarkerView view = all[i].GetComponent<MarkerView>();
                Bounds b = view.tintedRenderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
                float width = b.size.x * view.tintedRenderers[0].transform.localScale.x;
                Debug.Log("[MarkerPreview] " + all[i].name + " width=" + width.ToString("F2"));
                if (width > 0.7f)
                    throw new System.Exception(all[i].name + " is " + width.ToString("F2") + " wide; the shapes are meant to be at most 0.7.");
            }

            Color[] five =
            {
                colours[0], colours[1], colours[2],
                new Color(0.20f, 0.45f, 0.85f), new Color(0.85f, 0.25f, 0.35f),
            };

            ShootRow(cam, all, five, 1.55f, new Vector3(0f, 1.6f, -9.5f), 6f, "shapes");
            ShootRow(cam, new[] { all[4] }, new[] { five[4] }, 0f, new Vector3(0f, 1.7f, -3.2f), 4f, "sign");
        }
        finally
        {
            Object.DestroyImmediate(stage);
        }

        Debug.Log("[MarkerPreview] Wrote " + Path.GetFullPath(Output));
    }

    static void Shoot(Camera cam, GameObject prefab, Color[] colours,
                      Vector3 from, float pitch, string name)
    {
        GameObject[] same = new GameObject[colours.Length];
        for (int i = 0; i < same.Length; i++)
            same[i] = prefab;

        ShootRow(cam, same, colours, 1.5f, from, pitch, name);
    }

    /// <summary>A row of markers, one prefab and one colour each, photographed from one spot.</summary>
    static void ShootRow(Camera cam, GameObject[] prefabs, Color[] colours, float spacing,
                         Vector3 from, float pitch, string name)
    {
        GameObject[] pins = new GameObject[colours.Length];

        for (int i = 0; i < colours.Length; i++)
        {
            pins[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
            pins[i].transform.position = new Vector3((i - (colours.Length - 1) * 0.5f) * spacing, 0f, 0f);
            pins[i].transform.localScale = Vector3.one * 1.5f;

            MarkerView view = pins[i].GetComponent<MarkerView>();
            if (view != null)
            {
                view.SetColour(colours[i]);
                if (view.signLabel != null)
                    view.SetSign("Безистенът · 1509–1510");
            }

            // Says what is actually there. A shot of an empty field and a shot
            // of three pins that failed to load look identical.
            if (i == 0)
            {
                foreach (MeshFilter mf in pins[i].GetComponentsInChildren<MeshFilter>())
                {
                    Renderer r = mf.GetComponent<Renderer>();
                    Debug.Log("[MarkerPreview] " + mf.name +
                              " mesh=" + (mf.sharedMesh == null ? "NULL"
                                          : mf.sharedMesh.name + " verts=" + mf.sharedMesh.vertexCount) +
                              " mat=" + (r == null || r.sharedMaterial == null ? "NULL"
                                         : r.sharedMaterial.name) +
                              " enabled=" + (r != null && r.enabled) +
                              " active=" + mf.gameObject.activeInHierarchy +
                              " pos=" + pins[i].transform.position +
                              " scale=" + pins[i].transform.lossyScale);
                }
            }
        }

        Photograph(cam, from, pitch, name);

        for (int i = 0; i < pins.Length; i++)
            Object.DestroyImmediate(pins[i]);
    }

    /// <summary>
    /// Creators' own models, loaded through the same ModelView the phone uses,
    /// standing on the ground at the height and heading the website saved.
    /// Runs without -quit, because glTFast loads asynchronously; it exits by
    /// itself, 0 when every model loaded.
    ///
    ///   set ARTRAIL_MODELS=&lt;folder with hot-air-balloon.glb and Fox.glb&gt;
    ///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod MarkerPreview.ShootModels -logFile -
    /// </summary>
    public static async void ShootModels()
    {
        int code = 1;
        GameObject stage = null;

        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            stage = new GameObject("Stage");
            Directory.CreateDirectory(Output);
            string folder = System.Environment.GetEnvironmentVariable("ARTRAIL_MODELS");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform);
            ground.transform.localScale = Vector3.one * 4f;
            ground.GetComponent<Renderer>().sharedMaterial =
                MarkerShapes.Surface("PreviewGround", new Color(0.62f, 0.60f, 0.57f), 0.05f);

            RenderSettings.ambientProbe = new UnityEngine.Rendering.SphericalHarmonicsL2();
            MarkerSpawner.LightFromAmbientColour();

            Camera cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.80f);
            cam.fieldOfView = 55f;

            // The balloon at the height the e2e test saved; the Fox at the one
            // the builder offered; both facing the camera, which stands north.
            var models = new (string file, float height, float x)[]
            {
                ("hot-air-balloon.glb", 3.1f, -1.4f),
                ("Fox.glb", 0.8f, 1.3f),
            };

            bool all = true;
            foreach (var m in models)
            {
                GameObject holder = new GameObject(m.file);
                holder.transform.SetParent(stage.transform);
                holder.transform.position = new Vector3(m.x, 0f, 0f);

                bool ok = await holder.AddComponent<ModelView>().Show(Path.Combine(folder, m.file), m.height, 0);
                all &= ok;

                Renderer[] renderers = holder.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(holder.transform.position, Vector3.zero);
                foreach (Renderer r in renderers)
                    b.Encapsulate(r.bounds);
                Animation anim = holder.GetComponentInChildren<Animation>();
                Debug.Log("[MarkerPreview] " + m.file + " loaded=" + ok +
                          " renderers=" + renderers.Length +
                          " bottom=" + b.min.y.ToString("F2") + " top=" + b.max.y.ToString("F2") +
                          " width=" + b.size.x.ToString("F2") +
                          " shader=" + (renderers.Length > 0 && renderers[0].sharedMaterial != null
                                        ? renderers[0].sharedMaterial.shader.name : "none") +
                          " clip=" + (anim != null && anim.clip != null
                                      ? anim.clip.name + " " + anim.clip.length.ToString("F1") + "s loop=" + (anim.wrapMode == WrapMode.Loop)
                                      : "none"));
            }

            // In batch mode a material made in code never has its colours
            // uploaded to the SRP batcher before a one-off Render - the same
            // reason the ground above is coloured through a property block -
            // so every colour factor rendered as white. Textures were fine.
            // The materials themselves are right (logged once: envelope blue
            // 0.16/0.59/1.0); the batcher is off for these shots only.
            var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            bool batcher = urp.useSRPBatcher;
            urp.useSRPBatcher = false;
            try
            {
                Photograph(cam, new Vector3(0f, 1.6f, -7f), 4f, "models");
                Photograph(cam, new Vector3(1.3f, 1.1f, -2.4f), 14f, "models-fox");
            }
            finally
            {
                urp.useSRPBatcher = batcher;
            }
            code = all ? 0 : 1;
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (stage != null)
                Object.DestroyImmediate(stage);
            EditorApplication.Exit(code);
        }
    }

    /// <summary>What the camera sees from here, written to MarkerShots/name.png.</summary>
    static void Photograph(Camera cam, Vector3 from, float pitch, string name)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);

        // The app's sun rides on its camera; stand that camera where this one
        // is, so the light falls the way it does on the phone.
        Camera app = Camera.main;
        if (app != null && app != cam)
            app.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

        RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 8,
        };

        cam.targetTexture = target;
        cam.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;

        Texture2D shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();

        RenderTexture.active = previous;
        cam.targetTexture = null;

        File.WriteAllBytes(Output + "/" + name + ".png", shot.EncodeToPNG());
        Debug.Log("[MarkerPreview] " + name);

        Object.DestroyImmediate(shot);
        target.Release();
        Object.DestroyImmediate(target);
    }
}
