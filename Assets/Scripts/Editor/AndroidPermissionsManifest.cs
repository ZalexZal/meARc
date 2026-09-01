using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Stops Unity asking for every permission the moment the app launches.
///
/// Unity's player requests each dangerous permission declared in the manifest
/// during startup, before any of our code runs. On this project that is the
/// camera and the location, and it is why the app closed itself on a fresh
/// install: the dialog arrives while the window is still settling, the activity
/// is paused, and it never comes back. Moving the ARSession's start later did not
/// help, because ARCore was never the thing asking.
///
/// `unityplayer.SkipPermissionsDialog` turns that off and hands the decision to
/// us, which is where it belongs anyway - a permission asked for at the moment it
/// is needed, with the app on screen and the reason visible, is a question. The
/// same permission asked for during a splash screen is a toll gate.
///
/// Injected into the generated manifest rather than kept as a hand-written one:
/// a full AndroidManifest.xml in the project has to be maintained against every
/// package that contributes to it, and this needs one line.
/// </summary>
public class AndroidPermissionsManifest : IPostGenerateGradleAndroidProject
{
    // After ARCore Extensions, which also edits the manifest.
    public int callbackOrder { get { return 100; } }

    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");

        if (!File.Exists(manifest))
        {
            Debug.LogWarning("[Manifest] Not where it was expected: " + manifest);
            return;
        }

        XmlDocument document = new XmlDocument();
        document.Load(manifest);

        XmlNode application = document.SelectSingleNode("/manifest/application");
        if (application == null)
        {
            Debug.LogWarning("[Manifest] No <application> to add to.");
            return;
        }

        const string Name = "unityplayer.SkipPermissionsDialog";

        // Idempotent: a rebuild over an existing generated project must not end up
        // with the same meta-data twice, which is a manifest merger failure.
        foreach (XmlNode node in application.ChildNodes)
        {
            if (node.Name != "meta-data" || node.Attributes == null)
                continue;

            XmlAttribute existing = node.Attributes["name", AndroidNs];
            if (existing != null && existing.Value == Name)
                return;
        }

        XmlElement meta = document.CreateElement("meta-data");
        meta.SetAttribute("name", AndroidNs, Name);
        meta.SetAttribute("value", AndroidNs, "true");
        application.AppendChild(meta);

        document.Save(manifest);
        Debug.Log("[Manifest] Startup permission dialogs are ours to ask for now.");
    }
}
