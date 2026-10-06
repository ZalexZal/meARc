using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps glTFast's shaders in the player, for creators' own models.
///
/// glTFast makes its materials at runtime, so nothing in the project points at
/// its shaders and a build strips them: a model that looks right in the editor
/// comes out magenta on a phone, or not at all. Its documentation's fix is a
/// material for every keyword combination you expect, in a Resources folder,
/// and that is what this writes - once, then BuildApk checks they are there.
///
/// The combinations are the ones glTFast sets in URP: occlusion, emission and
/// texture transform on the lit shader, each opaque, cut out or blended.
/// Transmission and the old specular-glossiness workflow get the shader alone,
/// so they render, approximately.
///
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod ModelShaders.Make -quit
/// </summary>
public static class ModelShaders
{
    const string Folder = "Assets/Resources/ModelShaders";

    static readonly string[][] Modes =
    {
        new string[0],
        new[] { "_ALPHATEST_ON" },
        new[] { "_SURFACE_TYPE_TRANSPARENT", "_DISABLE_SSR_TRANSPARENT", "_ENABLE_FOG_ON_TRANSPARENT" },
    };

    [MenuItem("Tools/Build/Model Shaders")]
    public static void Make()
    {
        Directory.CreateDirectory(Folder);
        int made = 0;

        made += Write("glTF-pbrMetallicRoughness", new[] { "_OCCLUSION", "_EMISSIVE", "_TEXTURE_TRANSFORM" }, null);
        made += Write("glTF-unlit", new[] { "_TEXTURE_TRANSFORM" }, null);
        made += Write("glTF-pbrSpecularGlossiness", new string[0], "_SPECULAR_SETUP");

        AssetDatabase.SaveAssets();
        Debug.Log("[ModelShaders] " + made + " new material(s) in " + Folder);
    }

    /// <summary>One material per subset of the optional keywords, in each mode.</summary>
    static int Write(string shaderName, string[] optional, string always)
    {
        Shader shader = Shader.Find("Shader Graphs/" + shaderName);
        if (shader == null)
            throw new System.Exception("glTFast's " + shaderName + " shader is missing - is the package installed?");

        int made = 0;
        for (int mask = 0; mask < 1 << optional.Length; mask++)
        {
            for (int mode = 0; mode < Modes.Length; mode++)
            {
                var keywords = new List<string>(Modes[mode]);
                if (always != null)
                    keywords.Add(always);
                for (int i = 0; i < optional.Length; i++)
                    if ((mask & (1 << i)) != 0)
                        keywords.Add(optional[i]);

                string path = Folder + "/" + shaderName + "-" + mask + "-" + mode + ".mat";
                if (File.Exists(path))
                    continue;

                Material material = new Material(shader);
                foreach (string keyword in keywords)
                    material.EnableKeyword(keyword);
                AssetDatabase.CreateAsset(material, path);
                made++;
            }
        }

        return made;
    }
}
