using UnityEngine;

/// <summary>
/// The light taps under the thumb: a tick as the tab-bar lens passes a tab, a
/// confirm on a right answer, a reject on a wrong one.
///
/// Through the window's own performHapticFeedback rather than the vibrator.
/// It needs no permission, it is the same tick the system keyboard makes, and
/// it does nothing at all when the person has switched touch feedback off in
/// Android's settings - which is the setting they would expect it to follow.
/// Handheld.Vibrate is a 400 ms buzz and would have been the wrong instrument.
/// </summary>
public static class Haptics
{
    // HapticFeedbackConstants
    const int ClockTick = 4;
    const int VirtualKey = 1;
    const int ConfirmKey = 16;   // API 30
    const int RejectKey = 17;    // API 30

    public static void Tick() { Perform(ClockTick); }

    public static void Confirm() { Perform(Sdk >= 30 ? ConfirmKey : VirtualKey); }

    public static void Reject() { Perform(Sdk >= 30 ? RejectKey : VirtualKey); }

    static int _sdk = -1;

    public static int Sdk
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sdk < 0)
            {
                try
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdk = version.GetStatic<int>("SDK_INT");
                }
                catch
                {
                    _sdk = 0;
                }
            }
#endif
            return _sdk;
        }
    }

    static void Perform(int constant)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                    return;

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        using (var view = window.Call<AndroidJavaObject>("getDecorView"))
                            view.Call<bool>("performHapticFeedback", constant);
                    }
                    catch
                    {
                        // A phone without the motor, or a window mid-teardown.
                        // Nobody needs to hear about a missing tick.
                    }
                }));
            }
        }
        catch
        {
        }
#endif
    }
}
