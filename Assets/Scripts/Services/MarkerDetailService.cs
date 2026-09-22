using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Fetches what a stop actually holds - its media, its question, its hints -
/// the moment somebody opens it.
///
/// This exists because the two halves of this project were built to contracts
/// written months apart and never compared. The app read media and task out of
/// the marker's content jsonb, which is what MARKER-CONTENT.md specifies. The
/// website writes rows: marker_media for uploads, marker_answers for questions,
/// marker_hints for hints. Neither was wrong on its own terms; a creator
/// attached a video and a question, and the phone showed neither, because the
/// two never met.
///
/// The tables are the source of truth and this is the app learning to read them,
/// through one RPC, marker_detail. Mirroring back into content would duplicate
/// state that drifts the first time somebody edits one side, and it would put
/// every question in a trail on every phone in radius - which is the leak
/// marker_answers exists to prevent.
///
/// So markers_near keeps carrying only what is needed to DRAW a marker, and this
/// carries what is needed to READ one. A stop nobody opens costs nothing.
/// </summary>
public class MarkerDetailService : MonoBehaviour
{
    public static MarkerDetailService Instance { get; private set; }

    /// <summary>
    /// What has already been fetched, by marker id.
    ///
    /// Kept for the session. There is a copy on disk as well, but it is only ever
    /// READ when the network has failed - a creator editing a trail while
    /// somebody walks it is a real case, and a stale question served in
    /// preference to a live one would be a question nobody can answer. Offline,
    /// a stale question beats no question, which is the whole of the trade.
    /// </summary>
    readonly Dictionary<string, Detail> _cache = new Dictionary<string, Detail>();

    /// <summary>
    /// Where the copies live. One file per stop, named by its id, because the
    /// alternative is one file rewritten on every fetch and JsonUtility cannot
    /// serialise a dictionary anyway.
    /// </summary>
    static string Folder
    {
        get { return Path.Combine(Application.persistentDataPath, "stops"); }
    }

    static string FileFor(string id)
    {
        return Path.Combine(Folder, id + ".json");
    }

    readonly HashSet<string> _inFlight = new HashSet<string>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Fills in the marker's media and task, then calls back.
    ///
    /// The callback runs even when the fetch fails, and it runs immediately when
    /// the answer is already cached - the caller opens a sheet either way and
    /// should not have to care which happened. What it must not do is wait: a
    /// stop with nothing attached is a perfectly good stop, and the text is
    /// already on screen.
    /// </summary>
    public void Fill(BuildingData marker, Action done)
    {
        // The demo's stops carry everything already and exist nowhere else.
        if (marker == null || string.IsNullOrEmpty(marker.buildingId) || DemoTrail.Owns(marker))
        {
            if (done != null)
                done();

            return;
        }

        if (_cache.TryGetValue(marker.buildingId, out Detail cached))
        {
            Apply(marker, cached);

            if (done != null)
                done();

            return;
        }

        StartCoroutine(Fetch(marker, done));
    }

    /// <summary>Drops what was fetched, so a reopened trail asks again.</summary>
    public void Forget()
    {
        _cache.Clear();
        _inFlight.Clear();
    }

    IEnumerator Fetch(BuildingData marker, Action done)
    {
        string id = marker.buildingId;
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || _inFlight.Contains(id))
        {
            if (done != null)
                done();

            yield break;
        }

        _inFlight.Add(id);

        string body = "{\"p_marker\":\"" + id.Replace("\"", "\\\"") + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/marker_detail"), body, null))
        {
            yield return request.SendWebRequest();

            _inFlight.Remove(id);

            if (request.result != UnityWebRequest.Result.Success)
            {
                // A 404 means 0017 has not been applied. A connection error means
                // somebody has walked behind a building. The first cannot be
                // helped; the second is what the copy on disk is for.
                Debug.LogWarning("[Detail] marker_detail failed (" + request.responseCode +
                                 "): " + request.error);

                Detail saved = ReadFromDisk(id);

                if (saved != null)
                {
                    _cache[id] = saved;
                    Apply(marker, saved);
                }

                if (done != null)
                    done();

                yield break;
            }

            Detail detail = Read(request.downloadHandler.text);

            if (detail != null)
            {
                _cache[id] = detail;
                Apply(marker, detail);
                WriteToDisk(id, request.downloadHandler.text);
            }
        }

        if (done != null)
            done();
    }

    /// <summary>
    /// Keeps the reply exactly as it arrived, so reading it back later goes
    /// through the same parser and cannot drift from it.
    /// </summary>
    static void WriteToDisk(string id, string json)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FileFor(id), json);
        }
        catch (Exception exception)
        {
            // Not worth failing a sheet that has already been filled in.
            Debug.LogWarning("[Detail] Could not keep a copy of " + id + ": " + exception.Message);
        }
    }

    static Detail ReadFromDisk(string id)
    {
        try
        {
            string path = FileFor(id);

            if (!File.Exists(path))
                return null;

            return Read(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Detail] Could not read the copy of " + id + ": " + exception.Message);
            return null;
        }
    }

    static Detail Read(string json)
    {
        try
        {
            return JsonUtility.FromJson<Detail>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Detail] Unreadable reply: " + exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Writes the fetched pieces onto the marker.
    ///
    /// A task that arrives malformed is dropped rather than half-shown, which is
    /// the same rule the content path used: IsUsable is what the sheet asks, and
    /// a question with no prompt is a question nobody can read. That case is
    /// real - the builder had no field for the prompt at all until this round.
    /// </summary>
    static void Apply(BuildingData marker, Detail detail)
    {
        marker.media = detail.media;
        marker.task = detail.task != null && detail.task.IsUsable ? detail.task : null;
        marker.hints = detail.hints;
    }

    /// <summary>What marker_detail returns. Named for the wire, not for us.</summary>
    [Serializable]
    class Detail
    {
        public MarkerMedia[] media;
        public MarkerHint[] hints;
        public MarkerTask task;
    }
}
