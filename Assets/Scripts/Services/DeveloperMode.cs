using System;
using UnityEngine;

/// <summary>
/// A hidden switch for authoring work, reached by tapping the version number in
/// Settings seven times - the same gesture Android uses for Developer Options, so
/// it is discoverable to anyone who needs it and invisible to everyone else.
///
/// Its only job today is revealing draft buildings, so a marker's placement can be
/// checked on site before the row is published.
/// </summary>
public static class DeveloperMode
{
    const string PrefKey = "developer_mode";
    const int TapsRequired = 7;

    public static event Action OnChanged;

    static bool _loaded;
    static bool _enabled;
    static int _taps;
    static float _lastTapTime;

    public static bool Enabled
    {
        get
        {
            Load();
            return _enabled;
        }
    }

    /// <summary>Drafts are only ever visible with developer mode on.</summary>
    public static bool ShowDrafts { get { return Enabled; } }

    public static void Set(bool enabled)
    {
        Load();

        if (_enabled == enabled)
            return;

        _enabled = enabled;
        PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        if (OnChanged != null)
            OnChanged();
    }

    /// <summary>
    /// Call on each tap of the version label. Returns how many more are needed, or
    /// 0 once it has fired. The run resets if the taps are more than two seconds
    /// apart, so an accidental double-tap never accumulates over a session.
    /// </summary>
    public static int RegisterTap()
    {
        if (Time.unscaledTime - _lastTapTime > 2f)
            _taps = 0;

        _lastTapTime = Time.unscaledTime;
        _taps++;

        if (_taps < TapsRequired)
            return TapsRequired - _taps;

        _taps = 0;
        Set(!Enabled);
        return 0;
    }

    static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;
        _enabled = PlayerPrefs.GetInt(PrefKey, 0) == 1;
    }
}
