using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// A creator's own 3D models (migration 0055), fetched once and kept on the
/// phone.
///
/// markers_near says a stop is a model ("model" style) and nothing more, so a
/// trail with no models costs no extra request. When the list holds one, this
/// asks trail_models for the files - with exactly markers_near's visibility -
/// and downloads each to persistentDataPath/models. A file is named by its
/// marker and its updated_at, so a model the creator replaces is a new file
/// and the old one is deleted on the next look.
///
/// Until a file is on the phone the stop stands as a pin, which is also what
/// it falls back to if the model will not load.
/// </summary>
public class ModelCache : MonoBehaviour
{
    [Serializable]
    public class Model
    {
        public string marker_id;
        public string path;
        public long bytes;
        public string updated_at;
        public float height_m;
        public int heading_deg;
        public bool animated;
        public string credit_source;
        public string credit_author;
        public string credit_licence;
        public string credit_link;
    }

    [Serializable]
    class ModelList
    {
        public Model[] items;
    }

    static ModelCache _instance;

    /// <summary>A model's file has arrived; its stop can stand as the model now.</summary>
    public static event Action<string> OnModelReady;

    readonly Dictionary<string, Model> _models = new Dictionary<string, Model>();
    readonly HashSet<string> _broken = new HashSet<string>();
    bool _fetching;
    bool _again;

    static string Folder
    {
        get { return Path.Combine(Application.persistentDataPath, "models"); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        GameObject host = new GameObject("ModelCache");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<ModelCache>();
    }

    void OnEnable()
    {
        BuildingDatabase.OnBuildingsChanged += Refresh;
    }

    void OnDisable()
    {
        BuildingDatabase.OnBuildingsChanged -= Refresh;
    }

    /// <summary>The stop's model, or null when it has none (or it is not known yet).</summary>
    public static Model For(string markerId)
    {
        return _instance != null && markerId != null &&
               _instance._models.TryGetValue(markerId, out Model model)
            ? model
            : null;
    }

    /// <summary>Where the stop's model is on the phone, or null until it is there.</summary>
    public static string FileFor(string markerId)
    {
        Model model = For(markerId);
        if (model == null || _instance._broken.Contains(markerId))
            return null;

        string file = PathFor(model);
        return File.Exists(file) ? file : null;
    }

    /// <summary>A file that would not load stays a pin until the app restarts.</summary>
    public static void MarkBroken(string markerId)
    {
        if (_instance != null && markerId != null)
            _instance._broken.Add(markerId);
    }

    /// <summary>
    /// The line a licence asks for, under the stop's text. Empty for a model
    /// the creator made, or a stop without one.
    /// </summary>
    public static string CreditFor(string markerId)
    {
        Model model = For(markerId);
        if (model == null || model.credit_source != "other")
            return "";

        return string.Format(AppStrings.Get("model.credit"),
                             model.credit_author, model.credit_licence, model.credit_link);
    }

    static string PathFor(Model model)
    {
        // updated_at carries colons, which a file name cannot.
        string stamp = Hash128.Compute(model.path + "|" + model.updated_at).ToString();
        return Path.Combine(Folder, model.marker_id + "-" + stamp + ".glb");
    }

    void Refresh()
    {
        bool any = false;
        foreach (BuildingData building in BuildingDatabase.AllBuildings)
        {
            if (building != null && building.markerStyle == "model")
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            _models.Clear();
            return;
        }

        if (_fetching)
        {
            _again = true;
            return;
        }

        StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        TrailService trails = TrailService.Instance;
        string world = trails != null ? trails.SelectedId : null;

        if (config == null || string.IsNullOrEmpty(world))
            yield break;

        _fetching = true;
        string endpoint = config.url.TrimEnd('/');
        Model[] rows = null;

        using (UnityWebRequest request = SupabaseAuth.Post(
                   endpoint + "/rest/v1/rpc/trail_models", "{\"p_world\":\"" + world + "\"}", null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // JsonUtility can't parse a bare top-level array, so it is wrapped.
                rows = JsonUtility.FromJson<ModelList>("{\"items\":" + request.downloadHandler.text + "}").items;
            }
            else
            {
                // The stops stay pins; the next marker refresh asks again.
                Debug.LogWarning("[Models] trail_models failed: " + request.error);
            }
        }

        if (rows != null)
        {
            _models.Clear();
            foreach (Model model in rows)
                _models[model.marker_id] = model;

            Directory.CreateDirectory(Folder);
            Tidy();

            foreach (Model model in rows)
            {
                string file = PathFor(model);
                if (File.Exists(file))
                    continue;

                yield return Download(endpoint + "/storage/v1/object/public/marker-media/" + model.path,
                                      file, model);
            }
        }

        _fetching = false;

        if (_again)
        {
            _again = false;
            Refresh();
        }
    }

    IEnumerator Download(string url, string file, Model model)
    {
        string partial = file + ".part";

        using (UnityWebRequest request = new UnityWebRequest(url, "GET"))
        {
            request.downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true };
            request.timeout = 120;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Models] Couldn't download " + model.path + ": " + request.error);
                yield break;
            }
        }

        // Renamed only once complete, so a file that exists is a whole file.
        File.Move(partial, file);
        Debug.Log("[Models] " + model.marker_id + " ready, " + model.bytes + " bytes.");

        if (OnModelReady != null)
            OnModelReady(model.marker_id);
    }

    /// <summary>
    /// Deletes every file that is not a current model of this trail: replaced
    /// models, half downloads, and another trail's models.
    /// </summary>
    void Tidy()
    {
        var keep = new HashSet<string>();
        foreach (Model model in _models.Values)
            keep.Add(Path.GetFileName(PathFor(model)));

        // ponytail: models of a trail left behind are re-downloaded on coming
        // back; keep them per trail if walkers switch trails often.
        foreach (string file in Directory.GetFiles(Folder))
        {
            if (keep.Contains(Path.GetFileName(file)))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Models] Couldn't delete " + file + ": " + exception.Message);
            }
        }
    }
}
