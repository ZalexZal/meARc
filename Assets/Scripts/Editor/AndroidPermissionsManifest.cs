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

        SkipStartupDialog(document, application);
        AcceptJoinLinks(document, application);

        document.Save(manifest);
    }

    static void SkipStartupDialog(XmlDocument document, XmlNode application)
    {
        const string Name = "unityplayer.SkipPermissionsDialog";

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
        Debug.Log("[Manifest] Startup permission dialogs are ours to ask for now.");
    }

    /// <summary>
    /// artrail://join/&lt;trail id&gt; opens the app on that trail. The website's
    /// "Open in the app" button sends it, which is what a trail's QR code leads to.
    /// </summary>
    static void AcceptJoinLinks(XmlDocument document, XmlNode application)
    {
        foreach (XmlNode activity in application.SelectNodes("activity"))
        {
            bool launcher = false;

            foreach (XmlNode filter in activity.SelectNodes("intent-filter"))
            {
                if (filter.InnerXml.Contains("android.intent.category.LAUNCHER"))
                    launcher = true;

                if (filter.InnerXml.Contains("artrail"))
                    return;
            }

            if (!launcher)
                continue;

            XmlElement links = document.CreateElement("intent-filter");
            links.AppendChild(Named(document, "action", "android.intent.action.VIEW"));
            links.AppendChild(Named(document, "category", "android.intent.category.DEFAULT"));
            links.AppendChild(Named(document, "category", "android.intent.category.BROWSABLE"));

            XmlElement data = document.CreateElement("data");
            data.SetAttribute("scheme", AndroidNs, "artrail");
            data.SetAttribute("host", AndroidNs, "join");
            links.AppendChild(data);

            activity.AppendChild(links);
            Debug.Log("[Manifest] artrail://join links open the app.");
            return;
        }

        Debug.LogWarning("[Manifest] No launcher activity, so join links will not open the app.");
    }

    static XmlElement Named(XmlDocument document, string tag, string name)
    {
        XmlElement element = document.CreateElement(tag);
        element.SetAttribute("name", AndroidNs, name);
        return element;
    }
}
