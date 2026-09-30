using UnityEditor;
using UnityEngine;

/// <summary>
/// The geometry a marker is made of, generated rather than modelled.
///
/// There is no artist on this project, so the choice was a grey primitive or
/// shapes built from arithmetic. A cube floating over a pavement is the single
/// thing most likely to read as unfinished to somebody trying this for the first
/// time, and a pin is a few dozen lines.
///
/// One shape for now, done properly. Flag, plaque and post are the same
/// machinery with different numbers and can follow.
/// </summary>
public static class MarkerShapes
{
    const string Folder = "Assets/Generated";

    /// <summary>
    /// A ring lying flat on the ground.
    ///
    /// This is the part that says "the marker is HERE" rather than "somewhere
    /// over there": it touches the pavement, so a reader reads its position off
    /// the ground rather than guessing at the depth of a floating object. It is
    /// also the part that survives bad localisation - a pin a metre out still
    /// has a ring around roughly the right paving slab.
    /// </summary>
    public static Mesh Torus(float radius, float tube, int around = 40, int through = 12)
    {
        Mesh mesh = new Mesh { name = "MarkerRing" };

        Vector3[] vertices = new Vector3[(around + 1) * (through + 1)];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];

        for (int i = 0; i <= around; i++)
        {
            float u = (float)i / around * Mathf.PI * 2f;
            Vector3 centre = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u)) * radius;

            for (int j = 0; j <= through; j++)
            {
                float v = (float)j / through * Mathf.PI * 2f;

                // The tube's own circle, swept around the ring. Its "up" is the
                // world's up and its "out" is the direction away from the centre,
                // which is what keeps the ring lying flat rather than standing.
                Vector3 outward = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                Vector3 offset = outward * (Mathf.Cos(v) * tube) + Vector3.up * (Mathf.Sin(v) * tube);

                int index = i * (through + 1) + j;
                vertices[index] = centre + offset;
                normals[index] = offset.normalized;
                uv[index] = new Vector2((float)i / around, (float)j / through);
            }
        }

        int[] triangles = new int[around * through * 6];
        int t = 0;

        for (int i = 0; i < around; i++)
        {
            for (int j = 0; j < through; j++)
            {
                int a = i * (through + 1) + j;
                int b = a + through + 1;

                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = a + 1;

                triangles[t++] = a + 1;
                triangles[t++] = b;
                triangles[t++] = b + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// The pin's body: a sphere with a cone under it, tip down.
    ///
    /// Built as one mesh rather than two objects so the whole head tints as a
    /// single surface. Two renderers would tint in two draws and show a seam
    /// wherever the shading differs.
    /// </summary>
    public static Mesh Pin(float headRadius, float neck, float height, int segments = 28)
    {
        Mesh mesh = new Mesh { name = "MarkerPin" };

        // The sphere sits at the top; the cone runs from the sphere's lower
        // tangent down to a point at the origin. `neck` is how much of the
        // sphere the cone meets, which is what stops the join looking pinched.
        float centreY = height - headRadius;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        // ---- the head, a UV sphere
        int rings = segments / 2;

        for (int y = 0; y <= rings; y++)
        {
            float phi = (float)y / rings * Mathf.PI;

            for (int x = 0; x <= segments; x++)
            {
                float theta = (float)x / segments * Mathf.PI * 2f;

                verts.Add(new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(theta) * headRadius,
                    centreY + Mathf.Cos(phi) * headRadius,
                    Mathf.Sin(phi) * Mathf.Sin(theta) * headRadius));
            }
        }

        for (int y = 0; y < rings; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int a = y * (segments + 1) + x;
                int b = a + segments + 1;

                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        }

        // ---- the cone, from a circle on the sphere down to the ground
        int tip = verts.Count;
        verts.Add(Vector3.zero);

        int first = verts.Count;
        float ringY = centreY - headRadius * neck;
        float ringR = headRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - neck * neck));

        for (int x = 0; x <= segments; x++)
        {
            float theta = (float)x / segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(theta) * ringR, ringY, Mathf.Sin(theta) * ringR));
        }

        // Wound so the normals face out. The other way round renders the cone's
        // inside: backface culling removes the wall between the camera and the
        // sphere, and the pin looks like a scoop with a ball sitting in it.
        for (int x = 0; x < segments; x++)
        {
            tris.Add(tip);
            tris.Add(first + x);
            tris.Add(first + x + 1);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
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
