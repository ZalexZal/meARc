using System;
using UnityEngine;

/// <summary>
/// Holds the active palette and tells everything listening when it changes.
/// Static rather than a scene object so a panel can ask for a colour while
/// building itself, before any manager has had a chance to run.
///
/// Three choices, as Daylight v2 has them: like the phone, light, or dark. Like
/// the phone is the default and follows Android's own night mode, asked again
/// whenever the app comes back to the front.
/// </summary>
public static class ThemeManager
{
    public enum Mode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    const string ModeKey = "app_theme_mode";

    // The switch this replaced stored a plain on/off. Read once, so somebody who
    // had chosen dark before keeps dark rather than being moved to the phone's.
    const string LegacyKey = "app_theme_dark";

    /// <summary>Raised after the palette swaps, so views can recolour themselves.</summary>
    public static event Action OnThemeChanged;

    static bool _loaded;
    static Mode _mode = Mode.System;
    static bool _systemDark;
    static bool _isDark;

    public static Mode CurrentMode
    {
        get
        {
            Load();
            return _mode;
        }
    }

    public static bool IsDark
    {
        get
        {
            Load();
            return _isDark;
        }
    }

    public static Palette Current
    {
        get { return IsDark ? AppTheme.Dark() : AppTheme.Light(); }
    }

    public static Color Get(ThemeRole role)
    {
        return Current.Get(role);
    }

    public static void SetMode(Mode mode)
    {
        Load();

        if (_mode == mode)
            return;

        _mode = mode;
        PlayerPrefs.SetInt(ModeKey, (int)mode);
        PlayerPrefs.Save();
        Resolve();
    }

    /// <summary>Kept for callers that only know light or dark.</summary>
    public static void SetDark(bool dark)
    {
        SetMode(dark ? Mode.Dark : Mode.Light);
    }

    public static void Toggle()
    {
        SetDark(!IsDark);
    }

    /// <summary>
    /// Asks the phone again. Called when the app returns to the front, which is
    /// when somebody who switched the phone to dark at sunset expects it to show.
    /// </summary>
    public static void RefreshSystem()
    {
        Load();
        _systemDark = ReadSystemDark();
        Resolve();
    }

    static void Resolve()
    {
        bool dark = _mode == Mode.Dark || (_mode == Mode.System && _systemDark);

        if (dark == _isDark)
            return;

        _isDark = dark;

        if (OnThemeChanged != null)
            OnThemeChanged();
    }

    static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;

        if (PlayerPrefs.HasKey(ModeKey))
            _mode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 2);
        else if (PlayerPrefs.HasKey(LegacyKey))
            _mode = PlayerPrefs.GetInt(LegacyKey, 1) == 1 ? Mode.Dark : Mode.Light;
        else
            _mode = Mode.System;

        _systemDark = ReadSystemDark();
        _isDark = _mode == Mode.Dark || (_mode == Mode.System && _systemDark);
    }

    /// <summary>
    /// Android's night mode, from the activity's configuration. Anywhere else -
    /// the editor, a batch render - there is no phone to ask, and dark is what
    /// the camera half of the app was designed against first.
    /// </summary>
    static bool ReadSystemDark()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resources = activity.Call<AndroidJavaObject>("getResources"))
            using (var config = resources.Call<AndroidJavaObject>("getConfiguration"))
            {
                int uiMode = config.Get<int>("uiMode");
                return (uiMode & 0x30) == 0x20;   // UI_MODE_NIGHT_MASK, UI_MODE_NIGHT_YES
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Theme] Could not read the phone's night mode: " + exception.Message);
            return false;
        }
#else
        return true;
#endif
    }
}
