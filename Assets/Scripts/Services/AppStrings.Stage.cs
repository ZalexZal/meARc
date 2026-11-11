using System.Collections.Generic;

/// <summary>Strings for the stage feature, merged into AppStrings (see AppStrings.cs).</summary>
public static partial class AppStrings
{
    // The stage's signs and moment, verbatim from the website's role.ts stageFor.
    // The tag, head and sub of each stage are in AppStrings.cs.
    static readonly Dictionary<string, string> StageEn = new Dictionary<string, string>
    {
        { "stage.teacher.sign1", "Outdoor lessons" },
        { "stage.teacher.sign2", "Teams" },
        { "stage.teacher.sign3", "Live leaderboard" },
        { "stage.teacher.moment", "Team North · +10 pts" },
        { "stage.guide.sign1", "Routes" },
        { "stage.guide.sign2", "Stories and sights" },
        { "stage.guide.sign3", "AR guide" },
        { "stage.guide.moment", "Nearby · the Eastern Gate" },
        { "stage.broker.sign1", "Properties" },
        { "stage.broker.sign2", "3D on site" },
        { "stage.broker.sign3", "Model from a photo" },
        { "stage.broker.moment", "The client is on the plot" },
        { "stage.other.sign1", "All tools" },
        { "stage.other.sign2", "Any kind of trail" },
        { "stage.other.sign3", "Switch any time" },
        { "stage.other.moment", "Stop 2 of 5" },
        { "stage.viewer.sign1", "Public trails" },
        { "stage.viewer.sign2", "Join with a code" },
        { "stage.viewer.sign3", "AR camera" },
        { "stage.viewer.moment", "Next · 180 m" },
    };

    static readonly Dictionary<string, string> StageBg = new Dictionary<string, string>
    {
        { "stage.teacher.sign1", "Уроци на открито" },
        { "stage.teacher.sign2", "Отбори" },
        { "stage.teacher.sign3", "Класиране на живо" },
        { "stage.teacher.moment", "Отбор Север · +10 т." },
        { "stage.guide.sign1", "Маршрути" },
        { "stage.guide.sign2", "Истории и обекти" },
        { "stage.guide.sign3", "AR гид" },
        { "stage.guide.moment", "Наблизо · Източната порта" },
        { "stage.broker.sign1", "Имоти" },
        { "stage.broker.sign2", "3D на място" },
        { "stage.broker.sign3", "Модел от снимка" },
        { "stage.broker.moment", "Клиентът е на парцела" },
        { "stage.other.sign1", "Всички инструменти" },
        { "stage.other.sign2", "Всякакви маршрути" },
        { "stage.other.sign3", "Сменяте по всяко време" },
        { "stage.other.moment", "Спирка 2 от 5" },
        { "stage.viewer.sign1", "Обществени маршрути" },
        { "stage.viewer.sign2", "Влизане с код" },
        { "stage.viewer.sign3", "AR камера" },
        { "stage.viewer.moment", "Следваща · 180 м" },
    };
}
