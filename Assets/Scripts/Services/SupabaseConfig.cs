using UnityEngine;

/// <summary>
/// Where the backend is, in one place.
///
/// The URL and the anon key were baked into RemoteMarkerSource because it was the
/// only thing that talked to Supabase. Auth, membership and the marker fetch all
/// need them now, and three copies of a key is three places to miss when it
/// rotates.
///
/// The anon key is public by design - it ships inside the APK and identifies the
/// project, not the person. The service key is not here and must never be: it
/// bypasses every row-level policy in the database.
/// </summary>
public class SupabaseConfig : MonoBehaviour
{
    public static SupabaseConfig Instance { get; private set; }

    [Tooltip("Project URL, no trailing slash.")]
    public string url = "https://tajbebyhmvppqlswyjiq.supabase.co";

    [Tooltip("The anon key. Public by design - it says which project, not who.")]
    [TextArea(3, 6)]
    public string anonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRhamJlYnlobXZwcHFsc3d5amlxIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc3NDg0OTQsImV4cCI6MjEwMzMyNDQ5NH0.e2-l3SbkBIRBCEOfbNrvQC0i2M_FbQxZ5Lwyg3zOHLo";

    [Header("Terms")]
    [Tooltip("Bumping this makes every reader accept again. It is written to " +
             "profiles.terms_version, so it is also the record of what they agreed to.")]
    public string termsVersion = "2026-09-29";

    [Tooltip("The website, no trailing slash. The app links to its terms, privacy " +
             "policy and account page, in the language the app is set to.")]
    public string siteUrl = "https://artrails.netlify.app";

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>A REST or auth path joined to the project URL.</summary>
    public string Endpoint(string path)
    {
        return url.TrimEnd('/') + path;
    }
}
