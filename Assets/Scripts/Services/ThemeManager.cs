using System;
using UnityEngine;

/// <summary>
/// Holds the active palette and tells everything listening when it changes.
/// Static rather than a scene object so a panel can ask for a colour while
/// building itself, before any manager has had a chance to run.
/// </summary>
public static class ThemeManager
{
    const string PrefKey = "app_theme_dark";

    /// <summary>Raised after the palette swaps, so views can recolour themselves.</summary>
    public static event Action OnThemeChanged;

    static bool _loaded;
    static bool _isDark = true;

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

    public static void SetDark(bool dark)
    {
        Load();

        if (_isDark == dark)
            return;

        _isDark = dark;
        PlayerPrefs.SetInt(PrefKey, dark ? 1 : 0);
        PlayerPrefs.Save();

        if (OnThemeChanged != null)
            OnThemeChanged();
    }

    public static void Toggle()
    {
        SetDark(!IsDark);
    }

    static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;
        _isDark = PlayerPrefs.GetInt(PrefKey, 1) == 1;
    }
}
