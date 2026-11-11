using System.Collections.Generic;

/// <summary>Strings for the auth feature, merged into AppStrings (see AppStrings.cs).</summary>
public static partial class AppStrings
{
    static readonly Dictionary<string, string> AuthEn = new Dictionary<string, string>
    {
        // Sign-in and sign-up are two screens; each links to the other.
        { "auth.toSignUp", "No account? Sign up" },
        { "auth.toSignIn", "Have an account? Sign in" },
        { "auth.signInBody", "Sign in to the account you made in the app or on the website." },
    };

    static readonly Dictionary<string, string> AuthBg = new Dictionary<string, string>
    {
        { "auth.toSignUp", "Нямаш профил? Регистрирай се" },
        { "auth.toSignIn", "Вече имаш профил? Вход" },
        { "auth.signInBody", "Влез с профила, който си направил в приложението или в сайта." },
    };
}
