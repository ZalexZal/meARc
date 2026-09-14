using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Guards one rule that is very easy to break by accident: anything the phone
/// stores per person goes through <see cref="AccountScope"/>.
///
/// Four settings were written straight into PlayerPrefs under a bare name, and
/// every one of them leaked from one walker to the next on a shared phone - the
/// discovery flags handing over a finished trail and its route, the cached
/// answers, the chosen trail, and the terms acceptance, which is a consent
/// record rather than a preference. They were fixed together, and the only
/// thing stopping a fifth from being added the same way is this check.
///
/// Run from the menu, or in batch mode:
///   Unity -batchmode -quit -executeMethod AccountScopeCheck.Verify
/// </summary>
public static class AccountScopeCheck
{
    const string Root = "Assets/Scripts";

    /// <summary>
    /// A PlayerPrefs call naming one of the account-scoped key constants
    /// directly, rather than passing it through AccountScope.Key.
    /// </summary>
    static readonly Regex Unscoped = new Regex(
        @"PlayerPrefs\.\w+\(\s*(AcceptedKey|RecordedKey|SelectedKey|ProgressKey)\b",
        RegexOptions.Compiled);

    /// <summary>The discovery flags, which build their key from a prefix.</summary>
    static readonly Regex UnscopedPrefix = new Regex(
        @"PlayerPrefs\.\w+\(\s*KeyPrefix\s*\+",
        RegexOptions.Compiled);

    [MenuItem("Tools/Check/Account scoping")]
    public static void Verify()
    {
        List<string> found = new List<string>();

        foreach (string path in Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                if (Unscoped.IsMatch(lines[i]) || UnscopedPrefix.IsMatch(lines[i]))
                    found.Add(path + ":" + (i + 1) + "  " + lines[i].Trim());
            }
        }

        if (found.Count == 0)
        {
            Debug.Log("[Scope] Every account-scoped setting goes through AccountScope.");
            return;
        }

        foreach (string line in found)
            Debug.LogError("[Scope] Stored per phone, not per person: " + line);

        Debug.LogError("[Scope] " + found.Count + " unscoped use(s). Wrap the key in " +
                       "AccountScope.Key(...) - see the class comment for what this costs.");

        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
