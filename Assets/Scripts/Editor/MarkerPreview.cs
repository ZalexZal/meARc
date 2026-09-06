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
            // A ground plane, because the ring only means anything against one.
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

            // Batch mode starts with no ambient light and no skybox, so every
            // surface the sun does not hit directly renders black - which made
            // the ring look like a painted line rather than a lit object. The
            // real app has the camera feed and ARCore's estimated lighting; this
            // is the nearest honest stand-in.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.54f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.32f, 0.30f);

            GameObject sun = new GameObject("Sun");
            sun.transform.SetParent(stage.transform);
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

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
        GameObject[] pins = new GameObject[colours.Length];

        for (int i = 0; i < colours.Length; i++)
        {
            pins[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            pins[i].transform.position = new Vector3((i - 1) * 1.5f, 0f, 0f);
            pins[i].transform.localScale = Vector3.one * 1.5f;

            MarkerView view = pins[i].GetComponent<MarkerView>();
            if (view != null)
                view.SetColour(colours[i]);

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

        cam.transform.position = from;
        cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);

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

        for (int i = 0; i < pins.Length; i++)
            Object.DestroyImmediate(pins[i]);
    }
}
