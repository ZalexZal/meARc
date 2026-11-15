using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// The launcher icon: the meARc mark (flag and ring) on white, and the
/// stacked meARc lockup on the Unity splash screen.
///
/// There was none once. Every build shipped Unity's default icon, which is the
/// first thing a Play reviewer and every tester would have seen. The artwork
/// comes from the meARc logo files (rebrand, 16 Nov 2026) in Assets/Art/Icon
/// and Assets/Art/Brand, and is assigned here, on every build, so a settings
/// file that lost it cannot quietly put the default back.
///
/// Three kinds, because Android launchers disagree: adaptive (a background
/// and a foreground layer the launcher masks to its own shape), round, and
/// legacy for the ones that do neither.
/// </summary>
public static class AppIcons
{
    const string Folder = "Assets/Art/Icon/";

    [MenuItem("Tools/Build/Apply app icons")]
    public static void Apply()
    {
        Texture2D background = Load("adaptive-background.png");
        Texture2D foreground = Load("adaptive-foreground.png");
        Texture2D round = Load("round.png");
        Texture2D legacy = Load("legacy.png");

        if (background == null || foreground == null || round == null || legacy == null)
        {
            Debug.LogWarning("[Icons] Artwork missing from " + Folder + "; the default icon stays.");
            return;
        }

        NamedBuildTarget android = NamedBuildTarget.Android;

        PlatformIcon[] adaptive = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Adaptive);
        foreach (PlatformIcon icon in adaptive)
            icon.SetTextures(background, foreground);   // background first, then foreground
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Adaptive, adaptive);

        PlatformIcon[] rounded = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Round);
        foreach (PlatformIcon icon in rounded)
            icon.SetTexture(round);
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Round, rounded);

        PlatformIcon[] plain = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Legacy);
        foreach (PlatformIcon icon in plain)
            icon.SetTexture(legacy);
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Legacy, plain);

        Debug.Log("[Icons] Adaptive, round and legacy icons set.");

        Splash();
    }

    /// <summary>
    /// The stacked lockup in white on the splash's ink background (#231f20,
    /// the logo's own ink), shown before Unity's own logo line.
    /// </summary>
    static void Splash()
    {
        const string LockupPath = "Assets/Art/Brand/meARc-stacked-dark.png";
        Sprite lockup = AssetDatabase.LoadAssetAtPath<Sprite>(LockupPath);

        if (lockup == null)
        {
            Debug.LogWarning("[Icons] " + LockupPath + " is not a Sprite; the splash keeps no app logo.");
            return;
        }

        PlayerSettings.SplashScreen.backgroundColor = new Color32(0x23, 0x1f, 0x20, 0xff);
        PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, lockup) };
        Debug.Log("[Icons] Splash logo set.");
    }

    static Texture2D Load(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + file);
    }
}
