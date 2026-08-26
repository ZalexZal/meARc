using System;
using System.Collections.Generic;
using UnityEngine;

public enum AppLanguage
{
    English = 0,
    Bulgarian = 1
}

/// <summary>
/// Every user-facing string in one place, in both languages.
///
/// This is deliberately a stand-in for Unity Localization, which isn't installed
/// yet. When it is, <see cref="Get"/> becomes a one-line wrapper over the string
/// table and the dictionaries below get deleted - nothing that calls Get has to
/// change. Keeping the lookup behind a key from day one is what makes that swap
/// cheap.
/// </summary>
public static class AppStrings
{
    const string PrefKey = "app_language";

    public static event Action OnLanguageChanged;

    static bool _loaded;
    static AppLanguage _language = AppLanguage.English;

    public static AppLanguage CurrentLanguage
    {
        get
        {
            Load();
            return _language;
        }
    }

    public static void SetLanguage(AppLanguage language)
    {
        Load();

        if (_language == language)
            return;

        _language = language;
        PlayerPrefs.SetInt(PrefKey, (int)language);
        PlayerPrefs.Save();

        if (OnLanguageChanged != null)
            OnLanguageChanged();
    }

    static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;
        _language = (AppLanguage)PlayerPrefs.GetInt(PrefKey, (int)AppLanguage.English);
    }

    /// <summary>
    /// Returns the key itself when a string is missing, which shows up loudly in
    /// the UI rather than rendering as an empty label.
    /// </summary>
    public static string Get(string key)
    {
        Dictionary<string, string> table =
            CurrentLanguage == AppLanguage.Bulgarian ? Bg : En;

        if (table.TryGetValue(key, out string value))
            return value;

        if (CurrentLanguage != AppLanguage.English && En.TryGetValue(key, out value))
            return value;

        return key;
    }

    // ---------------------------------------------------------------- English

    static readonly Dictionary<string, string> En = new Dictionary<string, string>
    {
        { "tab.camera", "Camera" },
        { "tab.search", "Search" },
        { "tab.settings", "Settings" },

        { "camera.nearbyCount", "{0} nearby" },
        { "nearby.title", "Nearby buildings" },
        { "nearby.empty", "No buildings within {0} m" },
        { "nearby.noLocation", "Waiting for location" },

        { "vps.good.title", "Well calibrated" },
        { "vps.good.body", "Markers are placed accurately." },
        { "vps.fair.title", "Partly calibrated" },
        { "vps.fair.body", "Markers may be slightly offset. Move around and point at nearby buildings to improve." },
        { "vps.poor.title", "Not calibrated" },
        { "vps.poor.body", "Marker positions are unreliable. Step into open space with a clear view of building facades." },
        { "vps.none.title", "VPS unavailable" },
        { "vps.none.body", "No visual positioning here. Markers use GPS only and may be well off." },
        { "vps.accuracy", "±{0} m" },
        { "vps.unavailable", "Unavailable" },

        { "info.learnMore", "Learn more" },
        { "info.close", "Close" },

        { "search.placeholder", "Search buildings" },
        { "search.closest", "Closest to you" },
        { "search.suggestions", "Suggestions" },
        { "search.byEra", "By era" },
        { "search.byType", "By type" },
        { "search.empty", "No buildings found" },
        { "buildings.offline", "Couldn't load buildings. Check your connection." },
        { "search.discovered", "buildings discovered" },
        { "search.discoveredNote", "* A building counts as explored once you open its details." },

        { "settings.appearance", "Appearance" },
        { "settings.theme", "Dark mode" },
        { "settings.occlusion", "Hide markers behind buildings" },
        { "settings.language", "Language" },
        { "settings.appLanguage", "App language" },
        { "settings.notifications", "Notifications" },
        { "settings.nearbyBuildings", "Nearby buildings" },
        { "settings.help", "Help" },
        { "settings.replayTutorial", "Replay tutorial" },
        { "settings.dataSources", "Data and sources" },
        { "settings.about", "About" },
        { "settings.version", "Version {0}" },
        { "settings.back", "Back" },

        { "sub.tutorial.body", "The first-run walkthrough will live here." },
        { "sub.sources.body", "Building information is researched and written by hand, one entry at a time. Where a fact comes from a published source it is credited here." },
        { "sub.about.body", "An augmented reality guide to the architecture around you." },

        { "ready.morning", "Good morning" },
        { "ready.afternoon", "Good afternoon" },
        { "ready.evening", "Good evening" },
        { "ready.countHeadline", "{0} buildings around you" },
        { "ready.finding", "Finding buildings nearby…" },
        { "ready.closest", "Closest" },
        { "ready.status.none.title", "Finding your position…" },
        { "ready.status.none.body", "Hold your phone up and look around" },
        { "ready.status.novps.title", "No visual positioning here" },
        { "ready.status.novps.body", "Your position comes from GPS, so distances are approximate and markers can't be placed. Everything else works." },
        { "ready.status.poor.title", "Still calibrating" },
        { "ready.status.poor.body", "Point at nearby building facades to improve accuracy" },
        { "ready.status.fair.title", "Almost ready" },
        { "ready.status.fair.body", "Accuracy is improving — a few more seconds" },
        { "ready.status.good.title", "Ready to explore" },
        { "ready.status.good.body", "Position accurate to about {0}" },
        { "ready.start", "Start exploring" },
        { "ready.preparing", "Preparing…" },
        { "ready.startAnyway", "Start anyway" },
        { "ready.cameraOff", "Camera stays off until you start" },
        { "live.warning", "Marker positions may be inaccurate" },

        { "unit.metres", "{0} m" },
        { "unit.kilometres", "{0} km" }
    };

    // -------------------------------------------------------------- Bulgarian
    // Placeholder translations - to be reviewed by a native speaker before release.

    static readonly Dictionary<string, string> Bg = new Dictionary<string, string>
    {
        { "tab.camera", "Камера" },
        { "tab.search", "Търсене" },
        { "tab.settings", "Настройки" },

        { "camera.nearbyCount", "{0} наблизо" },
        { "nearby.title", "Сгради наблизо" },
        { "nearby.empty", "Няма сгради в радиус от {0} м" },
        { "nearby.noLocation", "Изчакване на местоположение" },

        { "vps.good.title", "Добра калибрация" },
        { "vps.good.body", "Маркерите са позиционирани точно." },
        { "vps.fair.title", "Частична калибрация" },
        { "vps.fair.body", "Маркерите може да са леко отместени. Придвижете се и насочете камерата към сгради наблизо." },
        { "vps.poor.title", "Няма калибрация" },
        { "vps.poor.body", "Позициите на маркерите са ненадеждни. Излезте на открито с видимост към фасади." },
        { "vps.none.title", "VPS не е достъпен" },
        { "vps.none.body", "Няма визуално позициониране тук. Маркерите използват само GPS и може да са неточни." },
        { "vps.accuracy", "±{0} м" },
        { "vps.unavailable", "Недостъпно" },

        { "info.learnMore", "Научи повече" },
        { "info.close", "Затвори" },

        { "search.placeholder", "Търсене на сгради" },
        { "search.closest", "Най-близо до вас" },
        { "search.suggestions", "Предложения" },
        { "search.byEra", "По период" },
        { "search.byType", "По вид" },
        { "search.empty", "Няма намерени сгради" },
        { "buildings.offline", "Неуспешно зареждане на сградите. Проверете връзката си." },
        { "search.discovered", "открити сгради" },
        { "search.discoveredNote", "* Сградата се отчита като разгледана, когато отворите информацията за нея." },

        { "settings.appearance", "Външен вид" },
        { "settings.theme", "Тъмен режим" },
        { "settings.occlusion", "Скривай маркери зад сгради" },
        { "settings.language", "Език" },
        { "settings.appLanguage", "Език на приложението" },
        { "settings.notifications", "Известия" },
        { "settings.nearbyBuildings", "Сгради наблизо" },
        { "settings.help", "Помощ" },
        { "settings.replayTutorial", "Повтори обучението" },
        { "settings.dataSources", "Данни и източници" },
        { "settings.about", "За приложението" },
        { "settings.version", "Версия {0}" },
        { "settings.back", "Назад" },

        { "sub.tutorial.body", "Тук ще бъде въведението при първо стартиране." },
        { "sub.sources.body", "Информацията за сградите е проучена и написана ръчно, запис по запис. Когато даден факт идва от публикуван източник, той е посочен тук." },
        { "sub.about.body", "Приложение с добавена реалност за архитектурата около вас." },

        { "ready.morning", "Добро утро" },
        { "ready.afternoon", "Добър ден" },
        { "ready.evening", "Добър вечер" },
        { "ready.countHeadline", "{0} сгради около вас" },
        { "ready.finding", "Търсене на сгради наблизо…" },
        { "ready.closest", "Най-близо" },
        { "ready.status.none.title", "Определяне на позицията…" },
        { "ready.status.none.body", "Вдигнете телефона и се огледайте" },
        { "ready.status.novps.title", "Няма визуално позициониране тук" },
        { "ready.status.novps.body", "Позицията идва от GPS, затова разстоянията са приблизителни и маркери не могат да се поставят. Всичко останало работи." },
        { "ready.status.poor.title", "Все още се калибрира" },
        { "ready.status.poor.body", "Насочете към фасади наблизо за по-добра точност" },
        { "ready.status.fair.title", "Почти готово" },
        { "ready.status.fair.body", "Точността се подобрява — още няколко секунди" },
        { "ready.status.good.title", "Готово за разглеждане" },
        { "ready.status.good.body", "Точност на позицията около {0}" },
        { "ready.start", "Започни" },
        { "ready.preparing", "Подготовка…" },
        { "ready.startAnyway", "Започни въпреки това" },
        { "ready.cameraOff", "Камерата остава изключена, докато не започнете" },
        { "live.warning", "Позициите на маркерите може да са неточни" },

        { "unit.metres", "{0} м" },
        { "unit.kilometres", "{0} км" }
    };

    /// <summary>
    /// Distance formatting is locale-aware in more than the suffix - Bulgarian uses
    /// a comma as the decimal separator.
    /// </summary>
    public static string FormatDistance(double metres, bool approximate = false)
    {
        bool bulgarian = CurrentLanguage == AppLanguage.Bulgarian;

        if (metres >= 1000d)
        {
            string value = (metres / 1000d).ToString("0.0");
            if (bulgarian)
                value = value.Replace('.', ',');

            return Hedge(string.Format(Get("unit.kilometres"), value), approximate);
        }

        return Hedge(string.Format(Get("unit.metres"), Mathf.RoundToInt((float)metres)),
                     approximate);
    }

    /// <summary>
    /// Marks a distance derived from a GPS fix rather than a localized pose. The
    /// whole calibration UI is built on telling the truth about precision, and a
    /// plain "140 m" from a fifteen-metre fix would quietly break that.
    /// </summary>
    static string Hedge(string distance, bool approximate)
    {
        return approximate ? "~" + distance : distance;
    }
}
