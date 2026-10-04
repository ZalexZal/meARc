using System;
using UnityEngine;

/// <summary>
/// Records how long the app spent in the background.
///
/// This lives on the always-active services object rather than on the Camera tab,
/// because <c>OnApplicationPause</c> never fires on a component whose GameObject is
/// disabled - and the Camera tab is disabled whenever the reader is on Search or
/// Settings, which is exactly when they are most likely to background the app.
/// </summary>
public class AppLifecycleWatcher : MonoBehaviour
{
    /// <summary>Seconds spent backgrounded on the most recent resume, or 0.</summary>
    public static float LastBackgroundDuration { get; private set; }

    /// <summary>Raised on resume, carrying the duration of the background spell.</summary>
    public static event Action<float> OnResumed;

    float _pausedAt = -1f;

    void Start()
    {
        ThemeManager.RefreshSystem();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            _pausedAt = Time.realtimeSinceStartup;
            return;
        }

        // The phone may have gone to night mode while the app was away.
        ThemeManager.RefreshSystem();

        if (_pausedAt < 0f)
            return;

        LastBackgroundDuration = Time.realtimeSinceStartup - _pausedAt;
        _pausedAt = -1f;

        if (OnResumed != null)
            OnResumed(LastBackgroundDuration);
    }

    /// <summary>
    /// Reads the pending duration and clears it, so a tab that was disabled during
    /// the background can still react the first time it comes back - and only once.
    /// </summary>
    public static float ConsumeBackgroundDuration()
    {
        float duration = LastBackgroundDuration;
        LastBackgroundDuration = 0f;
        return duration;
    }
}
