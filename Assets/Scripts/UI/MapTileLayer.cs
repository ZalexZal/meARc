using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
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
/// The map is never blank. A tile not yet loaded shows its parent (or
/// grandparent) stretched over it, a zoom keeps the previous level's tiles
/// under the new ones until the new ones have arrived, and behind all of it is
/// the map's own ground colour rather than the app's - which in the dark theme
/// was black, and is what people meant by "a black screen while the map loads".
///
/// Tiles are unpacked off the main thread. They used to be decoded with
/// LoadImage, one per frame, on the main thread: a screen is about sixty
/// 512-pixel PNGs, so refilling one took a second or two even with every tile
/// already on the phone, and stuttered while it did.
///
/// Kept on the phone for seven days, which is what Stadia's terms allow on the
/// free plan for client-side caching (longer, offline caching needs a paid one).
/// </summary>
public class MapTileLayer : MonoBehaviour
{
    [Header("References")]
    public MapView map;

    [Tooltip("Tiles are parented here, under everything else on the map.")]
    public RectTransform tileParent;

    [Tooltip("What shows where no tile has arrived yet: the map's own ground, " +
             "tinted like the tiles, never the app's background.")]
    public Graphic ground;

    // ------------------------------------------------------------- the source
    //
    // Stadia Outdoors, in both themes.
    //
    // Alidade Smooth came first and is the prettier map, but it hides points of
    // interest by design - the opposite of what somebody standing in a street
    // needs. Outdoors names the cafes, the banks, the library and the
    // playground, and those are what a person navigates by: "past the bank, then
    // left" is a direction; "past the unlabelled grey block" is not.
    //
    // It is a light style and there is no dark variant that keeps the labels, so
    // the dark theme gets the same tiles. A bright map under dark chrome is a
    // real cost and it is the smaller one: these trails are walked in daylight,
    // where a dark map is the harder thing to read.
    //
    // @2x asks for 512-pixel tiles covering the same ground as a 256-pixel one,
    // so the view carries twice the linear resolution. Nothing else changes for
    // it: tiles are placed by projecting their own corners, and ZoomFor still
    // reasons in logical 256-pixel tiles, so this buys sharpness rather than a
    // different zoom.
    //
    // Zoom 20, where Esri's Gray Canvas stopped at 16 over Bulgaria and
    // everything past it was one z16 tile stretched.

    [Header("Source")]
    [Tooltip("Stadia Maps API key, on its own property. Stadia offers no way to " +
             "restrict a key - rotate it if it is abused. Empty falls back to " +
             "Esri, which works but is not sharp past zoom 16.")]
    public string stadiaApiKey = "";

    [Tooltip("Light theme. {z}, {x} and {y} are replaced.")]
    public string urlTemplateLight =
        "https://tiles.stadiamaps.com/tiles/outdoors/{z}/{x}/{y}@2x.png";

    [Tooltip("Dark theme. The same style: Outdoors has no dark variant that keeps " +
             "its labels, and the labels are why it is here.")]
    public string urlTemplateDark =
        "https://tiles.stadiamaps.com/tiles/outdoors/{z}/{x}/{y}@2x.png";

    // Esri's Gray Canvas. No key, capped at zoom 16, used only when no Stadia key
    // is set. A soft map beats no map on a walk, and OnEnable says which is in
    // use so a soft one is never a mystery.
    const string FallbackLight =
        "https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Light_Gray_Base/MapServer/tile/{z}/{y}/{x}";
    const string FallbackDark =
        "https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}";

    public bool HasKey { get { return !string.IsNullOrWhiteSpace(stadiaApiKey); } }

    /// <summary>Whichever the theme is asking for, from whichever server we may use.</summary>
    public string urlTemplate
    {
        get
        {
            if (!HasKey)
                return _dark ? FallbackDark : FallbackLight;

            return (_dark ? urlTemplateDark : urlTemplateLight) + "?api_key=" + stadiaApiKey;
        }
    }

    /// <summary>
    /// How deep the current source actually has tiles.
    ///
    /// Esri answers past its ceiling with a 2,521-byte image reading "Map data
    /// not yet available", identically at every zoom and coordinate, so asking
    /// beyond it fills the screen with placeholders rather than failing. Capping
    /// turns the ceiling into an overzoom: a z16 tile covering a z18 view is
    /// simply drawn larger.
    /// </summary>
    public int EffectiveMaxZoom { get { return HasKey ? maxZoom : 16; } }

    /// <summary>
    /// Part of the cache filename: which picture, and which theme.
    ///
    /// Built from the whole template rather than the host. Keying on the host
    /// fixed the CARTO-to-Esri switch and then failed on the very next one:
    /// alidade_smooth_dark and outdoors are both tiles.stadiamaps.com, so the
    /// name did not change and the phone went on serving the old style from
    /// disk. The map came back looking exactly like the change had not been
    /// made - twice now, from the same mistake made one level too shallow.
    ///
    /// The api_key is stripped first, so rotating a key does not throw away
    /// every cached tile.
    /// </summary>
    string StyleKey
    {
        get
        {
            string template = urlTemplate;

            int query = template.IndexOf('?');
            if (query >= 0)
                template = template.Substring(0, query);

            // Not a security hash - a short, stable name for a string.
            unchecked
            {
                uint hash = 2166136261u;

                for (int i = 0; i < template.Length; i++)
                {
                    hash ^= template[i];
                    hash *= 16777619u;
                }

                // No theme in the name any more. The themes differ by tint,
                // not by picture, so a suffix here would keep two identical
                // copies of every tile on disk and make each switch refetch
                // the half it already had.
                return hash.ToString("x8");
            }
        }
    }

    [Tooltip("Sent with every request. OpenStreetMap rejects anonymous clients, and " +
             "being identifiable is the least a tile consumer owes a free service.")]
    public string userAgent = "ARTrail/0.6 (+https://github.com/ZalexZal/AR-Trails)";

    [Header("Limits")]
    [Tooltip("Tiles loading at once, from the phone or the network. Reading from " +
             "the phone is quick; more than this over the network gets a client " +
             "throttled and the tiles arrive no faster.")]
    public int maxConcurrentRequests = 6;

    [Tooltip("Textures held in memory. Beyond this the least recently used are " +
             "dropped - they stay on disk, so getting them back is not a download.")]
    // Comfortably more than one screenful. At 96 a single tall view could want
    // more tiles than the cache holds, so every pan evicted textures that were
    // still on screen and immediately asked for them again.
    public int maxCachedTextures = 220;

    [Tooltip("Days a tile is kept on the phone. Stadia's terms allow seven on the " +
             "free plan when the server sends no caching header.")]
    public int keepDays = 7;

    [Tooltip("Seconds the previous zoom's tiles may stay under the new ones while " +
             "those load. They go sooner once every new tile is in.")]
    public float fallbackSeconds = 4f;

    [Tooltip("OSM has no tiles past this, and asking wastes a request.")]
    // 16, because that is where Esri's Gray Canvas stops having tiles over
    // Bulgaria. Past it the service answers with a 2,521-byte image reading
    // "Map data not yet available", identically at every zoom and every
    // coordinate, so a check on the status code or the size sees a tile.
    //
    // Capping here rather than letting it ask is what turns the ceiling into an
    // overzoom: tiles are positioned by projecting their own corners, so a z16
    // tile asked to cover a z18 view is simply drawn four times larger. Softer
    // close in, and continuous - which beats a wall of placeholders.
    // What Stadia serves. The Esri fallback caps itself lower through
    // EffectiveMaxZoom.
    public int maxZoom = 20;

    public int minZoom = 3;

    const int TilePixels = 256;
    const double EarthCircumference = 40075016.686d;

    /// <summary>How far up a missing tile looks for something to stand in for it.</summary>
    const int StandInDepth = 6;

    /// <summary>
    /// The land colour of the Outdoors style, the 245 the tint comment in
    /// MapInk measures from. Behind the tiles, so a gap reads as map.
    /// </summary>
    static readonly Color Land = new Color(0.957f, 0.953f, 0.945f, 1f);

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

        // 0: its own picture. d > 0: an ancestor d levels up, stretched over it.
        // -1: nothing yet - the ground shows through.
        public int standIn;
    }

    readonly Dictionary<long, Tile> _live = new Dictionary<long, Tile>();
    readonly Dictionary<long, Tile> _fallback = new Dictionary<long, Tile>();
    readonly Stack<RawImage> _pool = new Stack<RawImage>();

    readonly Dictionary<long, Texture2D> _textures = new Dictionary<long, Texture2D>();
    readonly List<long> _recent = new List<long>();

    // Three queues, in the order they are served: the coarse tiles that stand in
    // for missing ones, the tiles on screen, and the ring just off it.
    readonly HashSet<long> _fetching = new HashSet<long>();
    readonly HashSet<long> _fetchingAhead = new HashSet<long>();
    readonly Queue<long> _urgent = new Queue<long>();
    readonly Queue<long> _queued = new Queue<long>();
    readonly Queue<long> _ahead = new Queue<long>();

    // Requests in the air, so leaving the tab can call them off.
    readonly List<UnityWebRequest> _active = new List<UnityWebRequest>();

    readonly HashSet<long> _wanted = new HashSet<long>();
    readonly List<long> _spent = new List<long>();

    // Tiles on the phone, from a listing made off the main thread at start.
    // Null until that listing is done; the ring ahead waits for it.
    volatile Dictionary<long, DateTime> _onDisk;
    string _diskStyle;

    int _inFlight;
    int _aheadInFlight;
    int _lastZoom = -1;
    int _lastVersion = -1;
    bool _dark;
    float _fallbackSince;

    // For the log: how long a screen took to fill, and from where.
    float _fillStart = -1f;
    int _fromDisk, _fromNetwork;

    string CacheFolder
    {
        get { return Path.Combine(Application.persistentDataPath, "tiles"); }
    }

    void OnEnable()
    {
        if (!HasKey)
        {
            Debug.LogWarning("[Tiles] No Stadia key, so the map falls back to Esri " +
                             "and will not be sharp past zoom 16.");
        }

        // Handle the change on the way in rather than just recording it.
        //
        // This line used to read "_dark = ThemeManager.IsDark", which is the
        // same event swallowed silently. The map panel is switched off while
        // Settings is open, and Settings is the only screen carrying the theme
        // toggle - so this component is guaranteed to be disabled at the moment
        // the event fires and guaranteed to miss it. It then woke up, wrote down
        // the new theme as though it had always known, and left every tile on
        // screen at the old tint. The dark map came back cream.
        //
        // HandleThemeChanged already returns immediately when nothing moved, so
        // the ordinary enable costs one comparison.
        HandleThemeChanged();
        PaintGround();
        ThemeManager.OnThemeChanged += HandleThemeChanged;

        if (_onDisk == null || _diskStyle != StyleKey)
            ListDisk();

        Resume();
    }

    /// <summary>
    /// Leaving the map tab switches this panel off, and Unity stops every
    /// coroutine on it where it stands. The loads in the air never finished,
    /// so the count of them never came down - a few tab switches mid-load and
    /// the map had no requests left to make - and the tiles they were for were
    /// never asked for again. They are called off here and counted back to
    /// nothing, and Resume asks again on the way back in.
    /// </summary>
    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= HandleThemeChanged;

        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i] != null)
                _active[i].Abort();
        }

        _active.Clear();
        _inFlight = 0;
        _aheadInFlight = 0;
        _fetching.Clear();
        _fetchingAhead.Clear();
        _urgent.Clear();
        _queued.Clear();
        _ahead.Clear();
    }

    /// <summary>Asks again for every tile on screen still without its own picture.</summary>
    void Resume()
    {
        foreach (KeyValuePair<long, Tile> pair in _live)
        {
            Tile tile = pair.Value;
            if (tile.standIn == 0)
                continue;

            int d = Mathf.Min(2, tile.z - minZoom);
            if (tile.standIn < 0 && d > 0)
                Request(Key(tile.z - d, tile.x >> d, tile.y >> d), _urgent);

            Request(pair.Key, _queued);
        }
    }

    void PaintGround()
    {
        if (ground == null)
            return;

        Color tint = MapInk.TileTint;
        ground.color = new Color(Land.r * tint.r, Land.g * tint.g, Land.b * tint.b, 1f);
    }

    /// <summary>
    /// A theme change is usually only a change of tint.
    ///
    /// Both themes now draw the same Outdoors tiles and the dark one multiplies
    /// them down, so throwing every texture away and fetching them again would
    /// re-download the identical bytes to show the identical picture. Only a
    /// change of template - a different style, or the drop to the keyless Esri
    /// fallback - is a change of picture, and only that one needs the refetch.
    ///
    /// This also matters on the walk: retinting is instant, whereas the refetch
    /// left the map blank for as long as the network took.
    /// </summary>
    void HandleThemeChanged()
    {
        if (_dark == ThemeManager.IsDark)
            return;

        string before = urlTemplate;
        _dark = ThemeManager.IsDark;
        PaintGround();

        if (urlTemplate == before)
        {
            Retint(_live);
            Retint(_fallback);
            return;
        }

        // Every live tile is about to hold a texture that has been destroyed.
        ReleaseAll(_live);
        ReleaseAll(_fallback);

        foreach (KeyValuePair<long, Texture2D> pair in _textures)
        {
            if (pair.Value != null)
                Destroy(pair.Value);
        }

        _textures.Clear();
        _recent.Clear();
        _urgent.Clear();
        _queued.Clear();
        _ahead.Clear();
        _fetching.Clear();
        _fetchingAhead.Clear();
        _lastZoom = -1;
        _lastVersion = -1;
        ListDisk();
    }

    static void Retint(Dictionary<long, Tile> tiles)
    {
        foreach (KeyValuePair<long, Tile> live in tiles)
        {
            if (live.Value.image != null && live.Value.image.texture != null)
                live.Value.image.color = MapInk.TileTint;
        }
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
        TidyFallback();
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

        // A new zoom: the tiles on screen step back rather than vanish. They
        // stay under the new level until it has filled in, so a pinch never
        // shows an empty map. Only the last level is kept; a second change
        // before it filled lets the older one go.
        if (zoom != _lastZoom)
        {
            ReleaseAll(_fallback);

            foreach (KeyValuePair<long, Tile> pair in _live)
            {
                if (pair.Value.standIn == 0)
                    _fallback[pair.Key] = pair.Value;
                else
                    Release(pair.Value);
            }

            _live.Clear();
            _fallbackSince = Time.unscaledTime;
            _lastZoom = zoom;
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

        foreach (KeyValuePair<long, Tile> pair in _fallback)
        {
            Tile old = pair.Value;
            int on = 1 << old.z;
            Place(old, old.z, old.x, old.y, on);
        }

        LookAhead(zoom, n, minX, maxX, minY, maxY);
    }

    /// <summary>
    /// The ring of tiles just off each edge, fetched to the phone (not decoded)
    /// once the screen itself is done, so the next pan starts from the disk.
    /// Ordinary client-side caching, and the same seven days as the rest.
    /// </summary>
    void LookAhead(int zoom, int n, int minX, int maxX, int minY, int maxY)
    {
        Dictionary<long, DateTime> disk = _onDisk;
        if (disk == null)
            return;

        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            for (int y = minY - 1; y <= maxY + 1; y++)
            {
                bool edge = x == minX - 1 || x == maxX + 1 || y == minY - 1 || y == maxY + 1;
                if (!edge || y < 0 || y >= n)
                    continue;

                long key = Key(zoom, ((x % n) + n) % n, y);

                if (_textures.ContainsKey(key) || _fetching.Contains(key) ||
                    _fetchingAhead.Contains(key) || Fresh(disk, key))
                {
                    continue;
                }

                _fetchingAhead.Add(key);
                _ahead.Enqueue(key);
            }
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

        // At the back until it has its own picture: under the previous zoom's
        // sharp tiles, which are better than a stretched stand-in where they
        // overlap. Show() brings it to the front.
        image.transform.SetAsFirstSibling();

        return new Tile { image = image, standIn = -1 };
    }

    void Release(Tile tile)
    {
        if (tile == null || tile.image == null)
            return;

        tile.image.texture = null;
        tile.image.uvRect = new Rect(0f, 0f, 1f, 1f);
        tile.image.gameObject.SetActive(false);
        _pool.Push(tile.image);
    }

    void ReleaseAll(Dictionary<long, Tile> tiles)
    {
        foreach (KeyValuePair<long, Tile> pair in tiles)
            Release(pair.Value);

        tiles.Clear();
    }

    /// <summary>
    /// Shows the tile if its texture is in memory. If not, stands the nearest
    /// ancestor in memory in for it, and asks for both.
    /// </summary>
    void Apply(Tile tile, long key)
    {
        if (_textures.TryGetValue(key, out Texture2D texture) && texture != null)
        {
            Show(tile, texture);
            Touch(key);
            return;
        }

        if (_fillStart < 0f)
        {
            _fillStart = Time.unscaledTime;
            _fromDisk = 0;
            _fromNetwork = 0;
        }

        bool covered = false;

        for (int d = 1; d <= StandInDepth && tile.z - d >= minZoom; d++)
        {
            long up = Key(tile.z - d, tile.x >> d, tile.y >> d);

            if (_textures.TryGetValue(up, out Texture2D coarse) && coarse != null)
            {
                StandIn(tile, coarse, d);
                Touch(up);
                covered = true;
                break;
            }
        }

        if (!covered)
        {
            tile.standIn = -1;
            tile.image.texture = null;
            tile.image.color = new Color(1f, 1f, 1f, 0f);

            // Two levels up covers sixteen tiles with one download, so the
            // screen has something on it long before all of its own arrive.
            int d = Mathf.Min(2, tile.z - minZoom);
            if (d > 0)
                Request(Key(tile.z - d, tile.x >> d, tile.y >> d), _urgent);
        }

        Request(key, _queued);
    }

    void Request(long key, Queue<long> queue)
    {
        if (_fetching.Contains(key) || _textures.ContainsKey(key))
            return;

        _fetching.Add(key);
        queue.Enqueue(key);
    }

    void Show(Tile tile, Texture2D texture)
    {
        tile.image.texture = texture;
        tile.image.uvRect = new Rect(0f, 0f, 1f, 1f);
        tile.image.color = MapInk.TileTint;
        tile.standIn = 0;

        // In front of whatever was standing in for it.
        tile.image.transform.SetAsLastSibling();
    }

    /// <summary>The part of an ancestor's picture that covers this tile, stretched.</summary>
    static void StandIn(Tile tile, Texture2D coarse, int depth)
    {
        int mask = (1 << depth) - 1;
        float size = 1f / (1 << depth);

        // Tile rows count down from the north; texture v counts up from the bottom.
        float u = (tile.x & mask) * size;
        float v = 1f - ((tile.y & mask) + 1) * size;

        tile.image.texture = coarse;
        tile.image.uvRect = new Rect(u, v, size, size);
        tile.image.color = MapInk.TileTint;
        tile.standIn = depth;
    }

    /// <summary>
    /// The previous zoom goes once every tile of the new one has its own
    /// picture, or after a few seconds whatever happens.
    /// </summary>
    void TidyFallback()
    {
        if (_fallback.Count == 0)
            return;

        if (Time.unscaledTime - _fallbackSince < fallbackSeconds)
        {
            foreach (KeyValuePair<long, Tile> pair in _live)
            {
                if (pair.Value.standIn != 0)
                    return;
            }
        }

        ReleaseAll(_fallback);
    }

    // -------------------------------------------------------------------- fetch

    void Pump()
    {
        while (_inFlight < maxConcurrentRequests)
        {
            long key;

            if (_urgent.Count > 0)
            {
                key = _urgent.Dequeue();
            }
            else if (_queued.Count > 0)
            {
                key = _queued.Dequeue();

                // Scrolled off before it was started: not worth the request.
                if (!_live.ContainsKey(key))
                {
                    _fetching.Remove(key);
                    continue;
                }
            }
            else
            {
                break;
            }

            StartCoroutine(Load(key));
        }

        // The ring ahead only when nothing on screen is waiting, and two at a time.
        while (_urgent.Count == 0 && _queued.Count == 0 && _aheadInFlight < 2 && _ahead.Count > 0)
            StartCoroutine(Fetch(_ahead.Dequeue()));

        ReportFill();
    }

    string PathFor(int z, int x, int y)
    {
        return Path.Combine(CacheFolder, StyleKey + "_" + z + "_" + x + "_" + y + ".png");
    }

    string UrlFor(int z, int x, int y)
    {
        return urlTemplate
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));
    }

    bool Kept(string path)
    {
        try
        {
            return File.Exists(path) &&
                   (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays < keepDays;
        }
        catch (Exception)
        {
            return false;
        }
    }

    bool Fresh(Dictionary<long, DateTime> disk, long key)
    {
        return disk.TryGetValue(key, out DateTime written) &&
               (DateTime.UtcNow - written).TotalDays < keepDays;
    }

    /// <summary>
    /// A tile for the screen: from the phone if it is there and recent, from the
    /// network if not. Either way Unity unpacks the PNG on a worker thread, so
    /// several can arrive in one frame without stalling it.
    /// </summary>
    IEnumerator Load(long key)
    {
        _inFlight++;

        Unpack(key, out int z, out int x, out int y);
        string path = PathFor(z, x, y);
        bool onDisk = Kept(path);

        Texture2D texture = null;
        byte[] bytes = null;

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(
                   onDisk ? new Uri(path).AbsoluteUri : UrlFor(z, x, y), true))
        {
            if (!onDisk)
            {
                request.SetRequestHeader("User-Agent", userAgent);
                request.timeout = 15;
            }

            _active.Add(request);
            yield return request.SendWebRequest();
            _active.Remove(request);

            if (request.result == UnityWebRequest.Result.Success)
            {
                texture = DownloadHandlerTexture.GetContent(request);

                if (!onDisk)
                    bytes = request.downloadHandler.data;
            }
            else if (onDisk && isActiveAndEnabled)
            {
                // A file half written when the app was killed. Drop it; the next
                // layout asks the network. (Not one called off by leaving the tab.)
                TryDelete(path);
                Forget(key);
            }
            else
            {
                // Quietly. A missing tile is a gap in the background, not an
                // error the reader can do anything about.
                Debug.LogWarning("[Tiles] " + z + "/" + x + "/" + y + ": " + request.error);
            }
        }

        if (bytes != null)
            Cache(path, key, bytes);

        _fetching.Remove(key);
        _inFlight = Mathf.Max(0, _inFlight - 1);

        if (texture == null)
        {
            // Back in the queue if it is still on screen; the file is gone now.
            if (onDisk && _live.ContainsKey(key))
                Request(key, _queued);

            yield break;
        }

        if (onDisk) _fromDisk++; else _fromNetwork++;

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Remember(key, texture);
        Arrived(key, texture);
    }

    /// <summary>The ring ahead: to the phone only, not unpacked into memory.</summary>
    IEnumerator Fetch(long key)
    {
        _aheadInFlight++;

        Unpack(key, out int z, out int x, out int y);
        string path = PathFor(z, x, y);

        if (!Kept(path))
        {
            using (UnityWebRequest request = UnityWebRequest.Get(UrlFor(z, x, y)))
            {
                request.SetRequestHeader("User-Agent", userAgent);
                request.timeout = 15;

                _active.Add(request);
                yield return request.SendWebRequest();
                _active.Remove(request);

                if (request.result == UnityWebRequest.Result.Success)
                    Cache(path, key, request.downloadHandler.data);
            }
        }

        _fetchingAhead.Remove(key);
        _aheadInFlight = Mathf.Max(0, _aheadInFlight - 1);
    }

    /// <summary>
    /// Shows a texture that has just arrived: on its own tile, and as a better
    /// stand-in on any tile below it that is still waiting.
    /// </summary>
    void Arrived(long key, Texture2D texture)
    {
        if (_live.TryGetValue(key, out Tile own) && own.image != null)
            Show(own, texture);

        Unpack(key, out int az, out int ax, out int ay);

        foreach (KeyValuePair<long, Tile> pair in _live)
        {
            Tile tile = pair.Value;
            if (tile.standIn == 0 || tile.z <= az)
                continue;

            int d = tile.z - az;
            if (d > StandInDepth || (tile.x >> d) != ax || (tile.y >> d) != ay)
                continue;

            if (tile.standIn < 0 || d < tile.standIn)
                StandIn(tile, texture, d);
        }
    }

    void ReportFill()
    {
        if (_fillStart < 0f)
            return;

        foreach (KeyValuePair<long, Tile> pair in _live)
        {
            if (pair.Value.standIn != 0)
                return;
        }

        Debug.Log("[Tiles] screen filled in " +
                  Mathf.RoundToInt((Time.unscaledTime - _fillStart) * 1000f) + " ms: " +
                  _fromDisk + " from the phone, " + _fromNetwork + " downloaded");
        _fillStart = -1f;
    }

    void Cache(string path, long key, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllBytes(path, data);

            Dictionary<long, DateTime> disk = _onDisk;
            if (disk != null)
                disk[key] = DateTime.UtcNow;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Tiles] Could not cache a tile: " + exception.Message);
        }
    }

    void Forget(long key)
    {
        Dictionary<long, DateTime> disk = _onDisk;
        if (disk != null)
            disk.Remove(key);
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Gone already, or busy: either way the next write replaces it.
        }
    }

    /// <summary>
    /// Lists the tiles on the phone and deletes any older than the keep, on a
    /// worker thread: a few thousand files is a noticeable pause on the main one.
    /// Tiles of an old style go too - nothing will ever ask for them again.
    /// </summary>
    void ListDisk()
    {
        string folder = CacheFolder;
        string style = StyleKey;
        int days = keepDays;

        _diskStyle = style;
        _onDisk = null;

        Task.Run(() =>
        {
            Dictionary<long, DateTime> found = new Dictionary<long, DateTime>();

            try
            {
                if (!Directory.Exists(folder))
                    return found;

                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    string name = Path.GetFileNameWithoutExtension(file);
                    string[] parts = name.Split('_');

                    bool current = parts.Length == 4 && parts[0] == style &&
                                   int.TryParse(parts[1], out int z) &&
                                   int.TryParse(parts[2], out int x) &&
                                   int.TryParse(parts[3], out int y);

                    if (!current || (DateTime.UtcNow - written).TotalDays >= days)
                    {
                        TryDelete(file);
                        continue;
                    }

                    found[Key(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]))] = written;
                }
            }
            catch (Exception)
            {
                // A listing that failed leaves the ring ahead switched off, nothing worse.
            }

            return found;
        }).ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion && _diskStyle == style)
                _onDisk = task.Result;
        });
    }

    void Remember(long key, Texture2D texture)
    {
        _textures[key] = texture;
        Touch(key);

        int guard = _recent.Count;

        while (_recent.Count > maxCachedTextures && guard-- > 0)
        {
            long oldest = _recent[0];
            _recent.RemoveAt(0);

            // Still on screen, or standing in for something that is, so it goes
            // to the back of the queue instead.
            if (_live.ContainsKey(oldest) || _fallback.ContainsKey(oldest) ||
                InUse(_textures.TryGetValue(oldest, out Texture2D used) ? used : null))
            {
                _recent.Add(oldest);
                continue;
            }

            if (_textures.TryGetValue(oldest, out Texture2D spent) && spent != null)
                Destroy(spent);

            _textures.Remove(oldest);
        }
    }

    bool InUse(Texture2D texture)
    {
        if (texture == null)
            return false;

        foreach (KeyValuePair<long, Tile> pair in _live)
        {
            if (pair.Value.image != null && pair.Value.image.texture == texture)
                return true;
        }

        return false;
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
        return Mathf.Clamp(zoom, minZoom, EffectiveMaxZoom);
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
