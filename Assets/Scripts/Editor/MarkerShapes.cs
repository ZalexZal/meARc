using UnityEditor;
using UnityEngine;

/// <summary>
/// The geometry a marker is made of, generated rather than modelled.
///
/// There is no artist on this project, so the choice was a grey primitive or
/// shapes built from arithmetic. A cube floating over a pavement is the single
/// thing most likely to read as unfinished to somebody trying this for the first
/// time, and a pin is a few dozen lines.
/// </summary>
public static class MarkerShapes
{
    const string Folder = "Assets/Generated";

    /// <summary>
    /// The pin: one smooth surface turned on a lathe, a ball whose sides run
    /// straight down to a point.
    ///
    /// The straight sides leave the ball exactly where they touch it, and the
    /// normals carry across that line, so it reads as a single drop. The pin
    /// before this was a sphere with a cone pushed into it, and looked like
    /// two shapes glued together.
    /// </summary>
    public static Mesh Pin(float headRadius, float height, int segments = 40, int arcSteps = 24)
    {
        Mesh mesh = new Mesh { name = "MarkerPin" };

        // The outline in (distance from the axis, height), tip at the origin.
        // A line from the tip touches the ball at angle `a` below its middle,
        // where sin a = radius / (distance from tip to the ball's centre).
        float centre = height - headRadius;
        float a = Mathf.Asin(headRadius / centre);

        var outline = new System.Collections.Generic.List<Vector2>();
        var facing = new System.Collections.Generic.List<Vector2>();

        // The straight part in short bands: one band from the tip right up to
        // the ball shades in stripes, its triangles are too long and thin.
        Vector2 touch = new Vector2(headRadius * Mathf.Cos(a), centre - headRadius * Mathf.Sin(a));
        for (int i = 0; i < 8; i++)
        {
            outline.Add(touch * (i / 8f));
            facing.Add(new Vector2(Mathf.Cos(a), -Mathf.Sin(a)));
        }

        for (int i = 0; i <= arcSteps; i++)
        {
            float phi = Mathf.Lerp(-a, Mathf.PI * 0.5f, (float)i / arcSteps);
            Vector2 n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
            outline.Add(new Vector2(0f, centre) + n * headRadius);
            facing.Add(n);
        }

        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        for (int j = 0; j < outline.Count; j++)
        {
            for (int k = 0; k <= segments; k++)
            {
                float theta = (float)k / segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(theta), sin = Mathf.Sin(theta);
                verts.Add(new Vector3(outline[j].x * cos, outline[j].y, outline[j].x * sin));
                normals.Add(new Vector3(facing[j].x * cos, facing[j].y, facing[j].x * sin));
            }
        }

        // Wound so the surface faces out (as the cone below).
        for (int j = 0; j < outline.Count - 1; j++)
        {
            for (int k = 0; k < segments; k++)
            {
                int v = j * (segments + 1) + k;
                int up = v + segments + 1;

                tris.Add(v); tris.Add(up); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(up); tris.Add(up + 1);
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// A cone standing on its base, like a traffic cone, point up.
    ///
    /// The side's normals are worked out from the slope rather than averaged by
    /// Unity: averaging runs them through the apex, and a cone lit that way
    /// looks like a crumpled paper hat. Each side quad gets its own apex vertex
    /// for the same reason. The base is a separate disc facing down, so the
    /// seam between side and base is a hard edge, as it is on a real cone.
    /// </summary>
    public static Mesh Cone(float radius, float height, int segments = 32)
    {
        Mesh mesh = new Mesh { name = "MarkerCone" };

        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        // Outward and up in proportion to the slope: for a cone of this radius
        // and height, (cos, r/h, sin) normalised is perpendicular to the side.
        float rise = radius / height;

        for (int x = 0; x < segments; x++)
        {
            float a0 = (float)x / segments * Mathf.PI * 2f;
            float a1 = (float)(x + 1) / segments * Mathf.PI * 2f;
            float mid = (a0 + a1) * 0.5f;

            int start = verts.Count;

            verts.Add(new Vector3(0f, height, 0f));
            normals.Add(new Vector3(Mathf.Cos(mid), rise, Mathf.Sin(mid)).normalized);

            verts.Add(new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius));
            normals.Add(new Vector3(Mathf.Cos(a0), rise, Mathf.Sin(a0)).normalized);

            verts.Add(new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius));
            normals.Add(new Vector3(Mathf.Cos(a1), rise, Mathf.Sin(a1)).normalized);

            // Wound so the side faces out (the same care the pin's cone needed).
            tris.Add(start);
            tris.Add(start + 2);
            tris.Add(start + 1);
        }

        // The base, facing the ground.
        int centre = verts.Count;
        verts.Add(Vector3.zero);
        normals.Add(Vector3.down);

        int ring = verts.Count;
        for (int x = 0; x <= segments; x++)
        {
            float a = (float)x / segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            normals.Add(Vector3.down);
        }

        for (int x = 0; x < segments; x++)
        {
            tris.Add(centre);
            tris.Add(ring + x);
            tris.Add(ring + x + 1);
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>Writes a mesh into the project so a prefab can reference it.</summary>
    public static Mesh Save(Mesh mesh, string name)
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Generated");

        string path = Folder + "/" + name + ".asset";

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            // Overwritten in place rather than replaced, so every prefab already
            // pointing at it keeps pointing at it.
            existing.Clear();
            existing.vertices = mesh.vertices;
            existing.triangles = mesh.triangles;
            existing.normals = mesh.normals;
            existing.uv = mesh.uv;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    /// <summary>
    /// A material that tints. URP's Lit if it is there, whatever the project
    /// falls back to if it is not.
    /// </summary>
    public static Material Surface(string name, Color colour, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Generated");

        string path = Folder + "/" + name + ".mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = name;

        // Created first, then coloured, then saved. Setting the properties on a
        // Material before CreateAsset writes them to an object the asset system
        // has not adopted yet, and the version that reaches disk is the shader's
        // default - which is why the ground and the ring both came out white.
        AssetDatabase.CreateAsset(material, path);

        material.SetColor("_BaseColor", colour);
        material.SetColor("_Color", colour);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        Debug.Log("[MarkerShapes] " + name + " on " + shader.name);
        return material;
    }
}
