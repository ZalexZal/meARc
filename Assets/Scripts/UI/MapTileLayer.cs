using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Draws OpenStreetMap raster tiles underneath the markers.
///
/// The tiles are the background; nothing else about the map changes. Pins, the
/// reader's dot and the guidance ray are still placed from metres through
/// <see cref="MapView.Project"/>, and this layer places tiles the same way - by
/// projecting each tile's own corners. That is what keeps them aligned: both are
/// answering the same question in the same units.
///
/// Every tile is cached to disk on first sight, so a street walked once works with
/// no signal afterwards, and a pan back over ground already seen costs nothing.
///
/// On the tile server: <see cref="urlTemplate"/> points at OpenStreetMap's own
/// tiles, which is fine for development at this volume and is NOT fine for a
/// released app - their tile usage policy forbids it. Shipping means pointing this
/// at a provider (MapTiler, Thunderforest, Stadia) or a self-hosted renderer. It is
/// one string, deliberately.
/// </summary>
public class MapTileLayer : MonoBehaviour
{
    [Header("References")]
    public MapView map;

    [Tooltip("Tiles are parented here, under everything else on the map.")]
    public RectTransform tileParent;

    [Header("Source")]
    [Tooltip("{z}, {x} and {y} are replaced. See the note on this class before release.")]
    public string urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    [Tooltip("Sent with every request. OpenStreetMap rejects anonymous clients, and " +
             "being identifiable is the least a tile consumer owes a free service.")]
    public string userAgent = "ARTrail/0.2 (+https://github.com/ZalexZal/AR-Trails)";

    [Header("Limits")]
    [Tooltip("More than a handful in flight gets a client throttled, and the tiles " +
             "arrive no faster.")]
    public int maxConcurrentRequests = 4;

    [Tooltip("Textures held in memory. Beyond this the least recently used are " +
             "dropped - they stay on disk, so getting them back is not a download.")]
    // Comfortably more than one screenful. At 96 a single tall view could want
    // more tiles than the cache holds, so every pan evicted textures that were
    // still on screen and immediately asked for them again.
    public int maxCachedTextures = 220;

    [Tooltip("OSM has no tiles past this, and asking wastes a request.")]
    public int maxZoom = 19;

    public int minZoom = 3;

    const int TilePixels = 256;
    const double EarthCircumference = 40075016.686d;

    /// <summary>
    /// The most tiles one view may ask for before the zoom is stepped out.
    ///
    /// A tall phone at the worst rounding of ZoomFor needs about seventy-seven, so
    /// this is that with room to spare rather than a number chosen to feel safe.
    /// It is a guard against a pathological view, not a rendering budget.
    /// </summary>
    const int MaxTilesOnScreen = 140;

    class Tile
    {
        public RawImage image;
        public int z, x, y;
    }

    readonly Dictionary<long, Tile> _live = new Dictionary<long, Tile>();
    readonly Stack<RawImage> _pool = new Stack<RawImage>();

    readonly Dictionary<long, Texture2D> _textures = new Dictionary<long, Texture2D>();
    readonly List<long> _recent = new List<long>();

    readonly HashSet<long> _fetching = new HashSet<long>();
    readonly Queue<long> _queued = new Queue<long>();

    // Downloaded but not yet turned into a texture. Decoding a PNG and inverting
    // sixty-five thousand pixels costs a couple of milliseconds, and four of them
    // landing in the same frame is a dropped frame - which happens every time the
    // reader pans, exactly when the map should feel smooth.
    readonly Queue<long> _toBuild = new Queue<long>();
    readonly Dictionary<long, byte[]> _raw = new Dictionary<long, byte[]>();

    readonly List<long> _wanted = new List<long>();
    readonly List<long> _spent = new List<long>();

    int _inFlight;
    int _lastZoom = -1;
    int _lastVersion = -1;
    bool _dark;

    string CacheFolder
    {
        get { return Path.Combine(Application.persistentDataPath, "tiles"); }
    }

    void OnEnable()
    {
        _dark = ThemeManager.IsDark;
        ThemeManager.OnThemeChanged += HandleThemeChanged;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= HandleThemeChanged;
    }

    /// <summary>
    /// A light map behind dark chrome glares at night and washes out in sun, so the
    /// tiles are inverted for the dark theme. Doing it when the texture is built
    /// rather than in a shader keeps this to one file with nothing to compile.
    /// </summary>
    void HandleThemeChanged()
    {
        if (_dark == ThemeManager.IsDark)
            return;

        _dark = ThemeManager.IsDark;

        foreach (KeyValuePair<long, Texture2D> pair in _textures)
        {
            if (pair.Value != null)
                Destroy(pair.Value);
        }

        _textures.Clear();
        _recent.Clear();

        // Every live tile is now holding a texture that has been destroyed.
        foreach (KeyValuePair<long, Tile> pair in _live)
            Release(pair.Value);

        _live.Clear();
        _lastZoom = -1;
        _lastVersion = -1;

        // Anything waiting was decoded for the old theme.
        _toBuild.Clear();
        _raw.Clear();
    }

    void LateUpdate()
    {
        if (map == null || tileParent == null || !map.HasOrigin)
            return;

        // Placing tiles writes positions and sizes, so it only happens when the
        // map has moved. Fetching still runs every frame - it touches no graphics.
        if (map.Version != _lastVersion)
        {
            _lastVersion = map.Version;
            Layout();
        }

        Pump();
    }

    // ------------------------------------------------------------------- layout

    void Layout()
    {
        if (!map.TryGetViewCentre(out double centreLat, out double centreLon))
            return;

        Rect view = map.ViewRect;

        // The view corners in tile coordinates. Latitude runs the other way from y,
        // so the top of the screen is the smaller tile row.
        map.Unproject(new Vector2(view.xMin, view.yMax), out double northLat, out double westLon);
        map.Unproject(new Vector2(view.xMax, view.yMin), out double southLat, out double eastLon);

        int zoom = ZoomFor(centreLat);
        int n, minX, maxX, minY, maxY;

        // Step out until the grid fits.
        //
        // This used to return when the count went over its cap, which drew nothing
        // and left the previous zoom's tiles stranded on screen. Each step down
        // quarters the number of tiles, so this ends - at worst at minZoom, where
        // the whole world is a handful of them. Coarser than asked for beats empty.
        while (true)
        {
            n = 1 << zoom;

            minX = Mathf.FloorToInt((float)LonToTileX(westLon, n));
            maxX = Mathf.FloorToInt((float)LonToTileX(eastLon, n));
            minY = Mathf.FloorToInt((float)LatToTileY(northLat, n));
            maxY = Mathf.FloorToInt((float)LatToTileY(southLat, n));

            long count = (long)(maxX - minX + 1) * (maxY - minY + 1);

            if (count <= MaxTilesOnScreen || zoom <= minZoom)
                break;

            zoom--;
        }

        // A zoom change replaces every tile, so the old set goes at once rather
        // than fading out one at a time as they scroll off.
        if (zoom != _lastZoom)
        {
            _lastZoom = zoom;

            foreach (KeyValuePair<long, Tile> pair in _live)
                Release(pair.Value);

            _live.Clear();
        }

        _wanted.Clear();

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (y < 0 || y >= n)
                    continue;

                // Longitude wraps; a map dragged past the date line should keep
                // showing tiles rather than nothing.
                int wrapped = ((x % n) + n) % n;

                long key = Key(zoom, wrapped, y);
                _wanted.Add(key);

                if (_live.TryGetValue(key, out Tile existing))
                {
                    Place(existing, zoom, x, y, n);
                    continue;
                }

                Tile tile = Take();
                tile.z = zoom;
                tile.x = wrapped;
                tile.y = y;

                Place(tile, zoom, x, y, n);
                Apply(tile, key);

                _live.Add(key, tile);
            }
        }

        // Anything no longer on screen goes back in the pool.
        _spent.Clear();
        foreach (KeyValuePair<long, Tile> pair in _live)
        {
            if (!_wanted.Contains(pair.Key))
                _spent.Add(pair.Key);
        }

        for (int i = 0; i < _spent.Count; i++)
        {
            Release(_live[_spent[i]]);
            _live.Remove(_spent[i]);
        }
    }

    /// <summary>
    /// Places a tile by projecting its own north-west and south-east corners, the
    /// same call the pins go through. Nothing here knows about pixels per metre.
    /// </summary>
    void Place(Tile tile, int zoom, int x, int y, int n)
    {
        double westLon = TileXToLon(x, n);
        double eastLon = TileXToLon(x + 1, n);
        double northLat = TileYToLat(y, n);
        double southLat = TileYToLat(y + 1, n);

        Vector2 nw = map.Project(northLat, westLon);
        Vector2 se = map.Project(southLat, eastLon);

        RectTransform rt = tile.image.rectTransform;
        rt.anchoredPosition = (nw + se) * 0.5f;

        // A hairline of overlap. Adjacent tiles land on fractional pixels, and
        // without it a seam of background shows between them.
        rt.sizeDelta = new Vector2(Mathf.Abs(se.x - nw.x) + 1f, Mathf.Abs(nw.y - se.y) + 1f);
    }

    Tile Take()
    {
        RawImage image;

        if (_pool.Count > 0)
        {
            image = _pool.Pop();
            image.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = new GameObject("Tile", typeof(RectTransform));
            go.layer = tileParent.gameObject.layer;
            go.transform.SetParent(tileParent, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
        }

        return new Tile { image = image };
    }

    void Release(Tile tile)
    {
        if (tile == null || tile.image == null)
            return;

        tile.image.texture = null;
        tile.image.gameObject.SetActive(false);
        _pool.Push(tile.image);
    }

    /// <summary>Shows the tile if its texture is in memory, and asks for it if not.</summary>
    void Apply(Tile tile, long key)
    {
        if (_textures.TryGetValue(key, out Texture2D texture) && texture != null)
        {
            tile.image.texture = texture;
            tile.image.color = Color.white;

            Touch(key);
            return;
        }

        // Transparent rather than a placeholder colour: the map background shows
        // through, which is quieter than a grid of grey squares filling in.
        tile.image.texture = null;
        tile.image.color = new Color(1f, 1f, 1f, 0f);

        if (_fetching.Contains(key))
            return;

        _fetching.Add(key);
        _queued.Enqueue(key);
    }

    // -------------------------------------------------------------------- fetch

    void Pump()
    {
        while (_inFlight < maxConcurrentRequests && _queued.Count > 0)
            StartCoroutine(Load(_queued.Dequeue()));

        BuildOne();
    }

    /// <summary>One texture per frame, whatever is waiting.</summary>
    void BuildOne()
    {
        if (_toBuild.Count == 0)
            return;

        long key = _toBuild.Dequeue();

        if (!_raw.TryGetValue(key, out byte[] data))
            return;

        _raw.Remove(key);

        Texture2D texture = Build(data);
        if (texture == null)
            return;

        Remember(key, texture);

        // The tile may have scrolled off while it was in the air.
        if (_live.TryGetValue(key, out Tile tile) && tile.image != null)
        {
            tile.image.texture = texture;
            tile.image.color = Color.white;
        }
    }

    IEnumerator Load(long key)
    {
        _inFlight++;

        Unpack(key, out int z, out int x, out int y);

        string path = Path.Combine(CacheFolder, z + "_" + x + "_" + y + ".png");
        byte[] data = null;

        if (File.Exists(path))
        {
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Tiles] Could not read a cached tile: " + exception.Message);
            }
        }

        if (data == null)
        {
            string url = urlTemplate
                .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
                .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("User-Agent", userAgent);
                request.timeout = 15;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    data = request.downloadHandler.data;
                    Cache(path, data);
                }
                else
                {
                    // Quietly. A missing tile is a gap in the background, not an
                    // error the reader can do anything about.
                    Debug.LogWarning("[Tiles] " + z + "/" + x + "/" + y + ": " + request.error);
                }
            }
        }

        _fetching.Remove(key);
        _inFlight--;

        if (data == null)
            yield break;

        // Handed to the queue rather than decoded here, so several arriving at
        // once are spread over several frames instead of one long one.
        _raw[key] = data;
        _toBuild.Enqueue(key);
    }

    void Cache(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllBytes(path, data);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Tiles] Could not cache a tile: " + exception.Message);
        }
    }

    /// <summary>
    /// Decodes a tile, inverting it for the dark theme. The inversion turns an OSM
    /// tile into a passable dark map - light roads on a dark ground - and is done
    /// once per tile rather than per frame.
    /// </summary>
    Texture2D Build(byte[] data)
    {
        Texture2D texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, false);

        if (!texture.LoadImage(data, false))
        {
            Destroy(texture);
            return null;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        if (_dark)
        {
            Color32[] pixels = texture.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];

                // Inverted, then pulled back towards neutral so the result is a
                // muted dark map rather than a photographic negative.
                int r = 255 - c.r;
                int g = 255 - c.g;
                int b = 255 - c.b;

                int grey = (r * 30 + g * 59 + b * 11) / 100;

                pixels[i] = new Color32(
                    (byte)((r + grey * 2) / 3 * 82 / 100),
                    (byte)((g + grey * 2) / 3 * 84 / 100),
                    (byte)((b + grey * 2) / 3 * 88 / 100),
                    c.a);
            }

            texture.SetPixels32(pixels);
        }

        texture.Apply(false, true); // no longer readable: halves the memory
        return texture;
    }

    void Remember(long key, Texture2D texture)
    {
        _textures[key] = texture;
        Touch(key);

        while (_recent.Count > maxCachedTextures)
        {
            long oldest = _recent[0];
            _recent.RemoveAt(0);

            // Still on screen, so it goes to the back of the queue instead.
            if (_live.ContainsKey(oldest))
            {
                _recent.Add(oldest);
                continue;
            }

            if (_textures.TryGetValue(oldest, out Texture2D spent) && spent != null)
                Destroy(spent);

            _textures.Remove(oldest);
        }
    }

    void Touch(long key)
    {
        _recent.Remove(key);
        _recent.Add(key);
    }

    // --------------------------------------------------------------- tile maths

    /// <summary>
    /// The zoom whose tile pixels are closest to the map's own pixels. Anything
    /// else is either a blurred upscale or a download of detail nobody can see.
    /// </summary>
    int ZoomFor(double latitude)
    {
        float pixelsPerMetre = map.PixelsPerMetre;
        if (pixelsPerMetre <= 0f)
            return minZoom;

        double metresPerPixel = 1d / pixelsPerMetre;
        double groundAtZoomZero =
            EarthCircumference * Math.Cos(latitude * Math.PI / 180d) / TilePixels;

        int zoom = (int)Math.Round(Math.Log(groundAtZoomZero / metresPerPixel, 2d));
        return Mathf.Clamp(zoom, minZoom, maxZoom);
    }

    static double LonToTileX(double longitude, int n)
    {
        return (longitude + 180d) / 360d * n;
    }

    static double LatToTileY(double latitude, int n)
    {
        double radians = latitude * Math.PI / 180d;
        return (1d - Math.Log(Math.Tan(radians) + 1d / Math.Cos(radians)) / Math.PI) / 2d * n;
    }

    static double TileXToLon(int x, int n)
    {
        return (double)x / n * 360d - 180d;
    }

    static double TileYToLat(int y, int n)
    {
        double m = Math.PI * (1d - 2d * y / n);
        return Math.Atan(Math.Sinh(m)) * 180d / Math.PI;
    }

    static long Key(int z, int x, int y)
    {
        return ((long)z << 58) | ((long)x << 29) | (uint)y;
    }

    static void Unpack(long key, out int z, out int x, out int y)
    {
        z = (int)(key >> 58);
        x = (int)((key >> 29) & 0x1FFFFFFF);
        y = (int)(key & 0x1FFFFFFF);
    }
}
