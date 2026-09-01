using System;
using System.Collections;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Asks Android for a permission without using Unity's result callback.
///
/// The callback is the thing that crashes. Answering any permission dialog killed
/// the app with a SIGSEGV at address zero on the Unity main thread, and the stack
/// is Java handleMessage into libunity into libil2cpp and then a jump to null -
/// Unity delivering the result into managed code. Not our code: it did this
/// before this project contained a single permission call, and it survived
/// turning off both managed and engine code stripping, switching the application
/// entry point, and moving who asks and when.
///
/// So this does not register a callback. It asks, then watches the permission
/// itself. Android's own state is the answer, and reading it needs nothing to be
/// called back into.
///
/// The result is decided when the app has the screen again: while the dialog is
/// up the app is not focused, so regaining focus is the signal that the reader
/// has answered - whichever way they answered.
/// </summary>
public static class AndroidPermissions
{
    /// <summary>True when the permission is already ours, or the platform has none.</summary>
    public static bool Has(string permission)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(permission);
#else
        return true;
#endif
    }

    /// <summary>
    /// Asks, and calls back with the answer once the reader has given one.
    ///
    /// <paramref name="done"/> is a plain delegate we invoke ourselves from this
    /// coroutine, so nothing native ever holds a pointer to it.
    /// </summary>
    public static IEnumerator Ask(string permission, Action<bool> done)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(permission))
        {
            if (done != null) done(true);
            yield break;
        }

        // The overload with no PermissionCallbacks. That is the whole point.
        Permission.RequestUserPermission(permission);

        // The dialog takes a moment to arrive; until it does the app still has
        // focus, and treating that as an answer would decide before it was asked.
        float waited = 0f;
        while (Application.isFocused && waited < 5f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // Now wait for it to go away again. A reader who wanders off is not a
        // reader who said no, so this waits rather than assuming.
        waited = 0f;
        while (!Application.isFocused && waited < 300f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // Android has written its decision down by the time the app is back.
        yield return null;

        bool granted = Permission.HasUserAuthorizedPermission(permission);
        if (done != null) done(granted);
#else
        // Nothing to ask on a desktop player or in the editor.
        if (done != null) done(true);
        yield break;
#endif
    }
}
