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
        { "alert.caption", "Nearby" },

        { "tutorial.skip", "Skip" },
        { "tutorial.next", "Next" },
        { "tutorial.done", "Start exploring" },
        { "tutorial.1.title", "Markers stand where they were left" },
        { "tutorial.1.body", "Every marker is pinned to a real spot on a real street. Walk to it and it will be there, in the colour whoever made the trail chose." },
        { "tutorial.2.title", "The map first, the camera when you arrive" },
        { "tutorial.2.body", "The map shows what is around you and how far. Open the camera when you get there, or before, if you just want to look. It is never locked." },
        { "tutorial.3.title", "Guide me there" },
        { "tutorial.3.body", "Pick a marker and an arrow follows you, on the map and through the camera, until you arrive or you stop it." },
        { "tutorial.4.title", "Your map, your rules" },
        { "tutorial.4.body", "Anyone can leave a marker. Hide this marker takes one off your map straight away, and Settings brings it back." },

        { "tab.map", "Map" },

        { "map.nearest", "Nearest to you" },
        { "map.browse", "Browse all" },
        { "map.backToMap", "Back to map" },
        { "map.noLocation", "Location unknown" },
        { "map.recentre", "Recentre" },

        { "guide.start", "Guide me there" },
        { "guide.stop", "Stop" },
        { "guide.arrived", "You have arrived" },

        { "marker.hide", "Hide this marker" },
        { "camera.noMarkers", "No markers nearby" },

        { "state.loading.title", "Loading markers" },
        { "state.loading.body", "Fetching what is around you." },
        { "state.locating.title", "Finding your position" },
        { "state.locating.body", "Step into the open and this will be quicker." },
        { "state.empty.title", "Nothing here yet" },
        { "state.empty.body", "No markers within {0}. Browse everything, or zoom out." },
        { "state.failed.title", "Couldn't load the markers" },
        { "state.failed.body", "The server didn't answer. Anything downloaded earlier is still here." },
        { "state.offline.title", "No connection" },
        { "state.offline.body", "Check your connection. Markers downloaded earlier still work." },
        { "state.retry", "Try again" },
        { "state.widen", "Browse all" },

        { "settings.privacy", "Privacy" },
        { "settings.hidden", "Hidden markers" },
        { "settings.hiddenNone", "None hidden" },
        { "settings.hiddenCount", "{0} hidden" },
        { "settings.unhideAll", "Show all again" },

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
        { "settings.nearbyBuildings", "Alert me about markers nearby" },
        { "settings.nearbyNote", "While the app is open. Markers you have already read about are not announced." },
        { "settings.help", "Help" },
        { "settings.replayTutorial", "Replay tutorial" },
        { "settings.dataSources", "Data and sources" },
        { "settings.about", "About" },
        { "settings.version", "Version {0}" },
        { "settings.back", "Back" },

        { "sub.sources.body", "Building information is researched and written by hand, one entry at a time. Where a fact comes from a published source it is credited here." },
        { "sub.about.body", "An augmented reality guide to the architecture around you." },
        { "live.warning", "Marker positions may be inaccurate" },

        { "unit.metres", "{0} m" },
        { "unit.kilometres", "{0} km" }
    };

    // -------------------------------------------------------------- Bulgarian
    // Placeholder translations - to be reviewed by a native speaker before release.

    static readonly Dictionary<string, string> Bg = new Dictionary<string, string>
    {
        { "alert.caption", "Наблизо" },

        { "tutorial.skip", "Пропусни" },
        { "tutorial.next", "Напред" },
        { "tutorial.done", "Започни" },
        { "tutorial.1.title", "Маркерите стоят там, където са оставени" },
        { "tutorial.1.body", "Всеки маркер е закачен за истинско място на истинска улица. Отидете до него и ще го намерите там, в цвета, който създателят на маршрута е избрал." },
        { "tutorial.2.title", "Първо картата, камерата — когато стигнете" },
        { "tutorial.2.body", "Картата показва какво има около вас и колко е далеч. Отворете камерата, когато пристигнете — или по-рано, ако искате да погледнете. Никога не е заключена." },
        { "tutorial.3.title", "Заведи ме там" },
        { "tutorial.3.body", "Изберете маркер и стрелката ви следва — на картата и през камерата — докато не пристигнете или не я спрете." },
        { "tutorial.4.title", "Вашата карта, вашите правила" },
        { "tutorial.4.body", "Всеки може да остави маркер. „Скрий този маркер“ го маха от вашата карта веднага, а Настройки го връщат." },

        { "tab.map", "Карта" },

        { "map.nearest", "Най-близо до вас" },
        { "map.browse", "Всички маркери" },
        { "map.backToMap", "Към картата" },
        { "map.noLocation", "Неизвестно местоположение" },
        { "map.recentre", "Центрирай" },

        { "guide.start", "Заведи ме там" },
        { "guide.stop", "Спри" },
        { "guide.arrived", "Пристигнахте" },

        { "marker.hide", "Скрий този маркер" },
        { "camera.noMarkers", "Няма маркери наблизо" },

        { "state.loading.title", "Зареждане на маркери" },
        { "state.loading.body", "Изтегляне на това, което е около вас." },
        { "state.locating.title", "Определяне на позицията" },
        { "state.locating.body", "Излезте на открито и това ще стане по-бързо." },
        { "state.empty.title", "Тук няма маркери" },
        { "state.empty.body", "Няма маркери в радиус от {0}. Разгледайте всички или отдалечете картата." },
        { "state.failed.title", "Маркерите не се заредиха" },
        { "state.failed.body", "Сървърът не отговори. Вече изтегленото остава достъпно." },
        { "state.offline.title", "Няма връзка" },
        { "state.offline.body", "Проверете връзката си. Изтеглените маркери продължават да работят." },
        { "state.retry", "Опитай отново" },
        { "state.widen", "Всички маркери" },

        { "settings.privacy", "Поверителност" },
        { "settings.hidden", "Скрити маркери" },
        { "settings.hiddenNone", "Няма скрити" },
        { "settings.hiddenCount", "{0} скрити" },
        { "settings.unhideAll", "Покажи всички отново" },

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
        { "settings.nearbyBuildings", "Известявай ме за маркери наблизо" },
        { "settings.nearbyNote", "Докато приложението е отворено. Маркери, които вече сте разгледали, не се обявяват." },
        { "settings.help", "Помощ" },
        { "settings.replayTutorial", "Повтори обучението" },
        { "settings.dataSources", "Данни и източници" },
        { "settings.about", "За приложението" },
        { "settings.version", "Версия {0}" },
        { "settings.back", "Назад" },

        { "sub.sources.body", "Информацията за сградите е проучена и написана ръчно, запис по запис. Когато даден факт идва от публикуван източник, той е посочен тук." },
        { "sub.about.body", "Приложение с добавена реалност за архитектурата около вас." },
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
