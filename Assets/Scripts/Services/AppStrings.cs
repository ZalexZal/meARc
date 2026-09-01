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
        { "camera.inView", "In view" },
        { "camera.nearest", "Nearest" },

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
        { "nearby.title", "Markers nearby" },
        { "nearby.empty", "No markers within {0} m" },
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

        { "search.placeholder", "Search markers" },
        { "search.closest", "Closest to you" },
        { "search.suggestions", "Suggestions" },
        { "search.byEra", "By era" },
        { "search.byType", "By kind" },
        { "search.empty", "No markers found" },
        { "buildings.offline", "Couldn't load markers. Check your connection." },
        { "search.discovered", "markers explored" },
        { "search.discoveredNote", "* A marker counts as explored once you open it." },

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

        { "sub.sources.body", "Marker text is written by hand, one entry at a time. Where a fact comes from a published source it is credited here." },
        { "sub.about.body", "Trails in augmented reality: markers left in real places, for other people to find." },
        { "live.warning", "Marker positions may be inaccurate" },

        { "unit.metres", "{0} m" },
        { "unit.kilometres", "{0} km" },

        // ------------------------------------------------------- the way in

        { "app.name", "AR Trails" },

        { "welcome.title", "Markers left in real places." },
        { "welcome.body", "Walk to them and see what somebody else left behind." },
        { "welcome.start", "Get started" },
        { "welcome.haveCode", "I have a code" },

        { "terms.title", "Before we start" },
        { "terms.accept", "I accept" },
        { "terms.1.title", "Where you are" },
        { "terms.1.body", "The app uses your location to show markers near you and to place them in the camera view. Your position stays on your phone and is sent to our server only as a \"what is near this point\" question. We do not keep a record of where you have been." },
        { "terms.2.title", "What you leave" },
        { "terms.2.body", "Markers you create are visible to everyone who can open that trail. Do not put a marker on private property, on someone’s home, or anywhere it would cause a nuisance to the people who actually live there." },
        { "terms.3.title", "What other people leave" },
        { "terms.3.body", "Anyone can place a marker. We do not check them before they appear. If one bothers you, Hide this marker removes it from your map immediately, and no one needs to agree with you first." },
        { "terms.4.title", "Watch where you are going" },
        { "terms.4.body", "This is not a navigation app. It shows a straight line to a marker, not a route, and it does not know about roads, traffic or anything else in the way. Look up." },
        { "terms.5.title", "Your account" },
        { "terms.5.body", "You can use the app without signing up. It still creates an anonymous identity so it can remember which trails you have joined. Uninstalling the app ends that identity and everything attached to it." },
        { "terms.6.title", "Getting rid of it" },
        { "terms.6.body", "Deleting your account deletes your trails and every marker in them. There is no undo." },
        { "terms.footer", "Accepting records the date and the version against your account." },

        { "trails.title", "My trails" },
        { "trails.eyebrow", "YOUR TRAILS" },
        { "trails.subtitle", "Pick one to walk it." },
        { "trails.choose", "Choose a trail" },
        { "trails.notNow", "Not now" },
        { "trails.join", "Join with a code" },
        { "trails.loading", "Looking for your trails" },
        { "trails.failed", "Couldn’t reach the server. Anything joined earlier still works." },
        { "trails.empty", "No trails yet" },
        { "trails.emptyBody", "Ask whoever made a trail for its code, and enter it here." },
        { "trails.markers", "{0} markers" },
        { "trails.role.viewer", "Viewer" },
        { "trails.role.editor", "Editor" },
        { "trails.role.owner", "Owner" },

        { "code.title", "Enter a code" },
        { "code.eyebrow", "JOIN A TRAIL" },
        { "code.body", "Six characters, from whoever made the trail." },
        { "code.join", "Join" },
        { "code.cancel", "Cancel" },
        { "code.joining", "Joining…" },
        { "code.unknown", "No trail with that code." },
        { "code.failed", "Couldn’t reach the server. Try again." },
        { "code.needsAccount", "Signing you in…" },

        { "mode.guide", "Guided" },
        { "mode.freefall", "Freefall" },
        { "mode.ctf", "Capture the flag" },
        { "mode.educational", "Questions" },

        // ------------------------------------------------- inside a marker

        { "piece.one", "1 piece" },
        { "piece.count", "{0} pieces" },
        { "piece.watch", "Watch" },
        { "piece.listen", "Listen" },
        { "piece.read", "Read" },
        { "piece.answer", "Answer" },
        { "piece.done", "Done" },
        { "piece.back", "Back" },

        { "media.play", "Play" },
        { "media.pause", "Pause" },
        { "media.loading", "Loading…" },
        { "media.failed", "Couldn’t play that." },
        { "media.needsConnection", "This needs a connection. The text above works offline." },
        { "media.dataWarning", "About {0} over mobile data." },
        { "media.dataPlay", "Play anyway" },

        { "task.check", "Check" },
        { "task.checking", "Checking…" },
        { "task.openHint", "Type your answer…" },
        { "task.offline", "Couldn’t check that. Try again." },
        { "task.correct", "Correct." },
        { "task.points", "points" },
        { "task.awarded", "+{0} points" },
        { "task.solved", "Solved" },
        { "task.wrong", "Wrong answer." },
        { "task.retry", "Try again" },
        { "task.lockCap", "Creator rule: wait" },
        { "task.lockMsg", "Locked. Try again in {0}" },
        { "task.dedCap", "Creator rule: lose points" },
        { "task.dedMsg", "Wrong answer. −{0} points." },
        { "task.skipCap", "Creator rule: one attempt" },
        { "task.skipMsg", "This marker is finished." },
        { "task.skipBody", "The creator allowed one attempt." },
        { "task.backToMap", "Back to map" },
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
        { "camera.inView", "Пред вас" },
        { "camera.nearest", "Най-близо" },

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
        { "nearby.title", "Маркери наблизо" },
        { "nearby.empty", "Няма маркери в радиус от {0} м" },
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

        { "search.placeholder", "Търсене на маркери" },
        { "search.closest", "Най-близо до вас" },
        { "search.suggestions", "Предложения" },
        { "search.byEra", "По период" },
        { "search.byType", "По вид" },
        { "search.empty", "Няма намерени маркери" },
        { "buildings.offline", "Неуспешно зареждане на маркерите. Проверете връзката си." },
        { "search.discovered", "разгледани маркера" },
        { "search.discoveredNote", "* Маркерът се отчита като разгледан, когато го отворите." },

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

        { "sub.sources.body", "Текстовете на маркерите са написани ръчно, запис по запис. Когато даден факт идва от публикуван източник, той е посочен тук." },
        { "sub.about.body", "Маршрути с добавена реалност: маркери, оставени на истински места, за да ги открият други." },
        { "live.warning", "Позициите на маркерите може да са неточни" },

        { "unit.metres", "{0} м" },
        { "unit.kilometres", "{0} км" },

        // ------------------------------------------------------- the way in

        { "app.name", "AR Маршрути" },

        { "welcome.title", "Маркери, оставени на истински места." },
        { "welcome.body", "Вървете до тях и вижте какво е оставил някой друг." },
        { "welcome.start", "Започни" },
        { "welcome.haveCode", "Имам код" },

        { "terms.title", "Преди да започнем" },
        { "terms.accept", "Приемам" },
        { "terms.1.title", "Къде се намирате" },
        { "terms.1.body", "Приложението използва местоположението ви, за да показва маркери близо до вас и да ги поставя през камерата. Позицията ви остава на телефона и се изпраща до сървъра само като въпрос „какво има близо до тази точка“. Не пазим история къде сте били." },
        { "terms.2.title", "Какво оставяте" },
        { "terms.2.body", "Маркерите, които създавате, са видими за всички, които имат достъп до този маршрут. Не поставяйте маркер върху частен имот, върху нечий дом или някъде, където би пречил на хората, които живеят там." },
        { "terms.3.title", "Какво оставят другите" },
        { "terms.3.body", "Всеки може да постави маркер. Не ги проверяваме, преди да се появят. Ако някой ви пречи, „Скрий този маркер“ го маха от вашата карта веднага и никой не трябва да се съгласява с вас." },
        { "terms.4.title", "Гледайте къде вървите" },
        { "terms.4.body", "Това не е навигация. Показва права линия до маркера, а не маршрут, и не знае нищо за улици, движение или каквото и да е по пътя. Вдигнете поглед." },
        { "terms.5.title", "Профилът ви" },
        { "terms.5.body", "Може да използвате приложението без регистрация. То пак създава анонимна самоличност, за да помни към кои маршрути сте се присъединили. Изтриването на приложението прекратява тази самоличност и всичко към нея." },
        { "terms.6.title", "Как да се откажете" },
        { "terms.6.body", "Изтриването на профила изтрива вашите маршрути и всички маркери в тях. Няма връщане назад." },
        { "terms.footer", "Приемането записва датата и версията към профила ви." },

        { "trails.title", "Моите маршрути" },
        { "trails.eyebrow", "ВАШИТЕ МАРШРУТИ" },
        { "trails.subtitle", "Изберете един, за да го извървите." },
        { "trails.choose", "Изберете маршрут" },
        { "trails.notNow", "Не сега" },
        { "trails.join", "Присъедини се с код" },
        { "trails.loading", "Търсим вашите маршрути" },
        { "trails.failed", "Сървърът не отговаря. Всичко присъединено по-рано пак работи." },
        { "trails.empty", "Още няма маршрути" },
        { "trails.emptyBody", "Поискайте кода от създателя на маршрута и го въведете тук." },
        { "trails.markers", "{0} маркера" },
        { "trails.role.viewer", "Гост" },
        { "trails.role.editor", "Редактор" },
        { "trails.role.owner", "Собственик" },

        { "code.title", "Въведи код" },
        { "code.eyebrow", "ПРИСЪЕДИНАВАНЕ" },
        { "code.body", "Шест знака, от създателя на маршрута." },
        { "code.join", "Присъедини се" },
        { "code.cancel", "Отказ" },
        { "code.joining", "Присъединяване…" },
        { "code.unknown", "Няма маршрут с този код." },
        { "code.failed", "Сървърът не отговаря. Опитайте пак." },
        { "code.needsAccount", "Влизане…" },

        { "mode.guide", "С водач" },
        { "mode.freefall", "Свободен" },
        { "mode.ctf", "Надпревара" },
        { "mode.educational", "Въпроси" },

        // ------------------------------------------------- inside a marker

        { "piece.one", "1 част" },
        { "piece.count", "{0} части" },
        { "piece.watch", "Гледай" },
        { "piece.listen", "Слушай" },
        { "piece.read", "Прочети" },
        { "piece.answer", "Отговори" },
        { "piece.done", "Готово" },
        { "piece.back", "Назад" },

        { "media.play", "Пусни" },
        { "media.pause", "Пауза" },
        { "media.loading", "Зареждане…" },
        { "media.failed", "Не можа да се възпроизведе." },
        { "media.needsConnection", "Това изисква интернет. Текстът по-горе работи и без него." },
        { "media.dataWarning", "Около {0} по мобилни данни." },
        { "media.dataPlay", "Все пак пусни" },

        { "task.check", "Провери" },
        { "task.checking", "Проверяване…" },
        { "task.openHint", "Напиши отговора си…" },
        { "task.offline", "Не можа да се провери. Опитай пак." },
        { "task.correct", "Вярно." },
        { "task.points", "точки" },
        { "task.awarded", "+{0} точки" },
        { "task.solved", "Решено" },
        { "task.wrong", "Грешен отговор." },
        { "task.retry", "Опитай пак" },
        { "task.lockCap", "Правило на създателя: изчакване" },
        { "task.lockMsg", "Заключено. Опитай отново след {0}" },
        { "task.dedCap", "Правило: минус точки" },
        { "task.dedMsg", "Грешен отговор. −{0} точки." },
        { "task.skipCap", "Правило: без втори опит" },
        { "task.skipMsg", "Този маркер е приключен." },
        { "task.skipBody", "Създателят е избрал един опит." },
        { "task.backToMap", "Към картата" },
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
