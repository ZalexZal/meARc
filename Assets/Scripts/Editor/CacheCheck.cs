using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Proves the two offline copies actually round-trip.
///
/// Both were written to be read only when the network has failed, which is the
/// one condition a batchmode run cannot produce on demand - so this drives the
/// private halves directly rather than pretending to lose signal.
/// </summary>
public static class CacheCheck
{
    public static void Run()
    {
        int failures = 0;

        failures += Picture();
        failures += StopDetail();

        Debug.Log(failures == 0 ? "CACHE CHECK PASSED" : "CACHE CHECK FAILED: " + failures);

        if (Application.isBatchMode)
            EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static int Picture()
    {
        const string url = "https://example.test/a/b/1788000000-photo.jpg";

        MethodInfo toDisk = typeof(MediaPlayerView)
            .GetMethod("ToDisk", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo fromDisk = typeof(MediaPlayerView)
            .GetMethod("FromDisk", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo pathFor = typeof(MediaPlayerView)
            .GetMethod("PathFor", BindingFlags.NonPublic | BindingFlags.Static);

        if (toDisk == null || fromDisk == null || pathFor == null)
        {
            Debug.LogError("  picture: the cache methods are not where the check expects them");
            return 1;
        }

        // A picture with two distinguishable halves, so a wrong read is visible
        // rather than a plausible grey.
        Texture2D made = new Texture2D(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                made.SetPixel(x, y, x < 4 ? Color.red : Color.blue);
        made.Apply();

        string path = (string)pathFor.Invoke(null, new object[] { url });
        if (File.Exists(path))
            File.Delete(path);

        toDisk.Invoke(null, new object[] { url, made.EncodeToPNG() });

        if (!File.Exists(path))
        {
            Debug.LogError("  picture: nothing was written");
            return 1;
        }

        Texture2D back = (Texture2D)fromDisk.Invoke(null, new object[] { url });

        if (back == null)
        {
            Debug.LogError("  picture: could not be read back");
            return 1;
        }

        bool same = back.width == 8 && back.height == 8
                    && back.GetPixel(1, 1).r > 0.9f && back.GetPixel(6, 1).b > 0.9f;

        // A URL that was never fetched must not resolve to somebody else's file.
        Texture2D stranger = (Texture2D)fromDisk.Invoke(
            null, new object[] { "https://example.test/a/b/1788000001-other.jpg" });

        File.Delete(path);

        if (!same)
        {
            Debug.LogError("  picture: read back, but not the same picture");
            return 1;
        }

        if (stranger != null)
        {
            Debug.LogError("  picture: a different URL found a cached file");
            return 1;
        }

        Debug.Log("  picture: written, read back intact, and keyed by its own URL");
        return 0;
    }

    static int StopDetail()
    {
        MethodInfo write = typeof(MarkerDetailService)
            .GetMethod("WriteToDisk", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo read = typeof(MarkerDetailService)
            .GetMethod("ReadFromDisk", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo fileFor = typeof(MarkerDetailService)
            .GetMethod("FileFor", BindingFlags.NonPublic | BindingFlags.Static);

        if (write == null || read == null || fileFor == null)
        {
            Debug.LogError("  stop: the cache methods are not where the check expects them");
            return 1;
        }

        const string id = "00000000-0000-0000-0000-0000000000aa";

        // The shape marker_detail actually returns, so this goes through the same
        // parser the live reply does.
        string json = "{\"task\":{\"kind\":\"exact\",\"points\":40," +
                      "\"prompt\":{\"bg\":\"Каква е формата?\"}}," +
                      "\"hints\":[],\"media\":[]}";

        write.Invoke(null, new object[] { id, json });

        string path = (string)fileFor.Invoke(null, new object[] { id });

        if (!File.Exists(path))
        {
            Debug.LogError("  stop: nothing was written");
            return 1;
        }

        object back = read.Invoke(null, new object[] { id });
        File.Delete(path);

        if (back == null)
        {
            Debug.LogError("  stop: written but unreadable");
            return 1;
        }

        object missing = read.Invoke(
            null, new object[] { "00000000-0000-0000-0000-0000000000bb" });

        if (missing != null)
        {
            Debug.LogError("  stop: a stop that was never fetched read something");
            return 1;
        }

        Debug.Log("  stop: question written and parsed back through the live parser");
        return 0;
    }
}
