using System.Collections.Generic;

/// <summary>Strings for the misc feature, merged into AppStrings (see AppStrings.cs).</summary>
public static partial class AppStrings
{
    static readonly Dictionary<string, string> MiscEn = new Dictionary<string, string>
    {
        // The trail menu: trails the reader made, and the teacher's way in.
        { "misc.myTrails", "My trails" },
        { "misc.teacherPanel", "Teacher panel" },
    };

    static readonly Dictionary<string, string> MiscBg = new Dictionary<string, string>
    {
        { "misc.myTrails", "Моите маршрути" },
        { "misc.teacherPanel", "Учителски пулт" },
    };
}
