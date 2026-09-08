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
        { "tutorial.2.title", "A trail starts with a code" },
        { "tutorial.2.body", "Six characters, given to you by whoever is running it. Type it under Trails and the trail is yours to walk. Without one you are just looking at a map." },
        { "tutorial.3.title", "Walking with other people" },
        { "tutorial.3.body", "Under Team you can make one and read its code out, or type somebody else's. Careful: a team code is not the trail code. The trail code puts everybody in the same game; the team code puts a few of you on the same side." },
        { "tutorial.4.title", "The map first, the camera when you arrive" },
        { "tutorial.4.body", "The map shows what is around you and how far. Open the camera when you get there - tapping the marker standing on the spot is what counts as having been. Opening a stop from the map does not." },
        { "tutorial.5.title", "Guide me there" },
        { "tutorial.5.body", "Pick a marker and an arrow follows you, on the map and through the camera, until you arrive or you stop it." },
        { "tutorial.6.title", "Your map, your rules" },
        { "tutorial.6.body", "Hide this marker takes one off your own map straight away, and Settings brings it back. Report tells the people running the trail, which is the one to use for somewhere unsafe or somebody's garden." },

        { "tab.map", "Map" },

        { "map.nearest", "Nearest to you" },
        { "strip.here", "You are here" },
        { "strip.found", "Found" },
        { "strip.stopFound", "Stop {0} \u00b7 found" },
        { "strip.stopOf", "Stop {0} of {1} \u00b7 next" },
        { "strip.closest", "Closest \u00b7 not opened yet" },
        { "strip.foundOf", "{0} of {1} found" },
        { "strip.nothingYet", "Nothing found yet" },
        { "strip.allFound", "All {0} found" },
        { "strip.complete", "Trail complete" },
        { "strip.loading", "Looking for markers" },
        { "map.browse", "Browse all" },
        { "map.backToMap", "Back to map" },
        { "map.noLocation", "Location unknown" },
        { "map.recentre", "Recentre" },

        { "guide.start", "Guide me there" },
        { "guide.stop", "Stop" },
        { "guide.arrived", "You have arrived" },

        { "marker.hide", "Hide" },
        { "marker.report", "Report" },
        { "report.title", "What is wrong with this stop?" },
        { "report.titleTrail", "What is wrong with this trail?" },
        { "report.reason.private_property", "It is on private property" },
        { "report.reason.unsafe", "Getting to it is unsafe" },
        { "report.reason.offensive", "The content is offensive" },
        { "report.reason.wrong_place", "It is in the wrong place" },
        { "report.reason.spam", "It is spam" },
        { "report.reason.other", "Something else" },
        { "report.cancel", "Never mind" },
        { "report.sending", "Sending…" },
        { "report.sent", "Sent. Somebody will look." },
        { "report.offline", "Could not reach the server." },
        { "report.failed", "That did not send." },
        { "camera.noMarkers", "No markers nearby" },
        { "camera.inView", "In view" },
        { "camera.nearest", "Nearest" },

        { "state.loading.title", "Loading markers" },
        { "state.loading.body", "Fetching what is around you." },
        { "state.locating.title", "Finding your position" },
        { "state.locating.body", "Step into the open and this will be quicker." },
        { "state.empty.title", "No trail open" },
        { "state.empty.body", "You have not opened a trail yet. Open one and its markers appear on the map." },
        { "state.far.title", "Nothing within {0}" },
        { "state.far.body", "The trail’s markers are further off. Keep walking — the card below points at the next one." },
        { "state.failed.title", "Couldn't load the markers" },
        { "state.failed.body", "The server didn't answer. Anything downloaded earlier is still here." },
        { "state.offline.title", "No connection" },
        { "state.offline.body", "Check your connection. Markers downloaded earlier still work." },
        { "state.retry", "Try again" },
        { "state.widen", "Open a trail" },

        { "settings.privacy", "Privacy" },
        { "settings.hidden", "Hidden markers" },
        { "settings.hiddenNone", "None hidden" },
        { "settings.hiddenCount", "{0} hidden" },
        { "settings.unhideAll", "Show all again" },

        { "tab.camera", "Camera" },
        { "tab.search", "Search" },
        { "tab.settings", "Settings" },

        { "camera.refused", "No camera access, so there is nothing to look " +
                            "through. The map still works - you can allow the " +
                            "camera in Settings if you want the AR view." },
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
        { "vps.none.title", "Cannot calibrate here" },
        { "vps.none.body", "No visual positioning here. Markers use GPS only and may be well off." },
        { "vps.accuracy", "±{0} m" },
        { "vps.unavailable", "Unavailable" },

        { "info.learnMore", "Learn more" },

        { "search.placeholder", "Search markers" },
        { "search.closest", "Closest to you" },
        { "search.suggestions", "Suggestions" },
        { "search.empty", "No markers found" },
        { "buildings.offline", "Couldn't load markers. Check your connection." },
        { "search.discovered", "markers explored" },
        { "search.title", "Progress" },
        { "graph.empty", "The trail appears here once its markers load." },
        { "graph.scored", "+{0}" },
        { "graph.shut", "Not open yet" },
        { "search.points", "points" },
        { "search.standing", "on the board" },
        { "search.noTeam", "walking alone" },
        { "search.rank", "#{0}" },
        { "search.discoveredNote", "* A stop counts once you tap its marker in the camera — or open it from the map while you are standing at it." },

        { "team.close", "Close" },
        { "team.eyebrow", "Your team" },
        { "team.title", "Team" },
        { "team.none", "You are walking alone" },
        { "team.needed", "This game is played in teams" },
        { "team.neededBody", "Make one — one person is a team — or type somebody else’s code to join theirs." },
        { "team.noneBody", "Make a team and share its code, or type somebody else's to join theirs." },
        { "team.capacity", "{0} of {1} people" },
        { "team.capacityNone", "Teams here hold up to {0} people" },
        { "team.name", "Team name" },
        { "team.create", "Make a team" },
        { "team.code", "Their team code" },
        { "team.join", "Join" },
        { "team.invite", "Team code" },
        { "team.inviteHint", "Give this to the people walking with you. It is not the trail code." },
        { "team.leave", "Leave the team" },
        { "team.you", "(you)" },
        { "team.meta", "{0} stops \u00b7 {1} points" },
        { "task.worth", "Worth {0} points" },
        { "team.moved", "A moderator changed your points" },
        { "team.movedRow", "{0} \u2014 {1}" },
        { "team.earned", "{0} walked for, {1} from the moderator" },
        { "team.out", "Out of the standings" },
        { "team.board", "Standings" },
        { "team.boardEmpty", "Nobody has answered anything yet" },
        { "team.needName", "Give the team a name." },
        { "team.needCode", "Type the team code." },
        { "team.working", "One moment\u2026" },
        { "team.offline", "Could not reach the server." },
        { "team.failed", "That did not work." },
        { "settings.account", "Account" },
        { "account.none", "You have no account yet" },
        { "account.noneBody", "Your walking is saved on this phone only. Make an account and it follows you to another one, and to the website." },
        { "account.name", "Your name" },
        { "account.nameHint", "What the leaderboard calls you." },
        { "account.saveName", "Save" },
        { "account.email", "Email" },
        { "account.password", "Password" },
        { "account.create", "Create account" },
        { "account.signIn", "Sign in" },
        { "account.signOut", "Sign out" },
        { "account.signedInAs", "Signed in as" },
        { "account.mergeWarning", "Signing in to an account you already have does not carry over what you walked on this phone." },
        { "account.working", "One moment\u2026" },
        { "account.needBoth", "Fill in both an email and a password." },
        { "auth.failed", "That did not go through. Try again." },
        { "auth.noSession", "No connection to the server yet." },
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
        { "terms.1.title", "Watch where you are going" },
        { "terms.1.body", "This is not a navigation app. It draws a straight line to a marker, not a route, and knows nothing about roads or traffic. Look up." },
        { "terms.2.title", "Where you are" },
        { "terms.2.body", "Your location is used to find markers near you and to place them in the camera. It stays on your phone and reaches us only as a \"what is near this point\" question. We keep no record of where you have been." },
        { "terms.3.title", "What other people leave" },
        { "terms.3.body", "Anyone can place a marker and we do not check them first. If one bothers you, Hide this marker takes it off your map at once, and nobody has to agree with you." },
        { "terms.4.title", "Your account" },
        { "terms.4.body", "Your account remembers which trails you have joined and what your team has answered. You can delete it whenever you like, from Settings." },
        { "terms.footer", "Accepting records the date and the version against your account." },

        { "trails.title", "My trails" },
        { "trails.eyebrow", "YOUR TRAILS" },
        { "trails.subtitle", "Pick one to walk it." },
        { "trails.choose", "Choose a trail" },
        { "trails.label", "TRAIL" },
        { "trails.notNow", "Not now" },
        { "trails.leave", "Leave" },
        { "trails.leaveConfirm", "Confirm" },
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
        { "piece.look", "Look" },
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
        { "hint.title", "Hints" },
        { "hint.button", "Hints ({0})" },
        { "hint.nth", "Hint {0}" },
        { "hint.free", "Free" },
        { "hint.cost", "−{0} points" },
        { "hint.unlock", "Unlock" },
        { "hint.locked", "Locked — unlock to read it" },
        { "hint.previous", "Unlock the earlier hints first" },
        { "hint.none", "No hints on this marker." },
        { "hint.opening", "Unlocking…" },
        { "hint.failed", "That did not unlock." },
        { "hint.spent", "{0} points spent on hints" },
        { "task.checking", "Checking…" },
        { "task.openHint", "Type your answer…" },
        { "task.offline", "Couldn’t reach the server, so that was not sent. Move a few steps and try again." },
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
        { "tutorial.2.title", "Маршрутът започва с код" },
        { "tutorial.2.body", "Шест знака, които ви дава този, който води маршрута. Напишете го в Маршрути и маршрутът е ваш. Без код гледате просто карта." },
        { "tutorial.3.title", "Върви с други хора" },
        { "tutorial.3.body", "В Отбор можете да направите отбор и да прочетете кода му на глас или да напишете чужд. Внимание: кодът на отбора не е кодът на маршрута. Кодът на маршрута вкарва всички в една игра; кодът на отбора вкарва няколко души в един отбор." },
        { "tutorial.4.title", "Първо картата, камерата — когато стигнете" },
        { "tutorial.4.body", "Картата показва какво има около вас и колко е далеч. Отворете камерата, когато пристигнете — натискането на маркера, който стои на самото място, е това, което брои за посетена спирка. Отварянето от картата не брои." },
        { "tutorial.5.title", "Заведи ме там" },
        { "tutorial.5.body", "Изберете маркер и стрелката ви следва — на картата и през камерата — докато не пристигнете или не я спрете." },
        { "tutorial.6.title", "Вашата карта, вашите правила" },
        { "tutorial.6.body", "„Скрий“ маха маркера от вашата карта веднага, а Настройки го връщат. „Сигнал“ казва на хората, които водят маршрута — това е бутонът за опасно място или частен двор." },

        { "tab.map", "Карта" },

        { "map.nearest", "Най-близо до вас" },
        { "strip.here", "\u0412\u0438\u0435 \u0441\u0442\u0435 \u0442\u0443\u043a" },
        { "strip.found", "\u041d\u0430\u043c\u0435\u0440\u0435\u043d" },
        { "strip.stopFound", "\u0421\u043f\u0438\u0440\u043a\u0430 {0} \u00b7 \u043d\u0430\u043c\u0435\u0440\u0435\u043d\u0430" },
        { "strip.stopOf", "\u0421\u043f\u0438\u0440\u043a\u0430 {0} \u043e\u0442 {1} \u00b7 \u0441\u043b\u0435\u0434\u0432\u0430\u0449\u0430" },
        { "strip.closest", "\u041d\u0430\u0439-\u0431\u043b\u0438\u0437\u043a\u0438\u044f\u0442 \u00b7 \u043d\u0435\u043e\u0442\u0432\u043e\u0440\u0435\u043d" },
        { "strip.foundOf", "{0} \u043e\u0442 {1} \u043d\u0430\u043c\u0435\u0440\u0435\u043d\u0438" },
        { "strip.nothingYet", "\u041e\u0449\u0435 \u043d\u0438\u0449\u043e \u043d\u0430\u043c\u0435\u0440\u0435\u043d\u043e" },
        { "strip.allFound", "\u0412\u0441\u0438\u0447\u043a\u0438 {0} \u0441\u0430 \u043d\u0430\u043c\u0435\u0440\u0435\u043d\u0438" },
        { "strip.complete", "\u041c\u0430\u0440\u0448\u0440\u0443\u0442\u044a\u0442 \u0435 \u0437\u0430\u0432\u044a\u0440\u0448\u0435\u043d" },
        { "strip.loading", "\u0422\u044a\u0440\u0441\u0438\u043c \u043c\u0430\u0440\u043a\u0435\u0440\u0438" },
        { "map.browse", "Всички маркери" },
        { "map.backToMap", "Към картата" },
        { "map.noLocation", "Неизвестно местоположение" },
        { "map.recentre", "Центрирай" },

        { "guide.start", "Заведи ме там" },
        { "guide.stop", "Спри" },
        { "guide.arrived", "Пристигнахте" },

        { "marker.hide", "Скрий" },
        { "marker.report", "Сигнал" },
        { "report.title", "Какво не е наред с тази спирка?" },
        { "report.titleTrail", "Какво не е наред с този маршрут?" },
        { "report.reason.private_property", "В частен имот е" },
        { "report.reason.unsafe", "Стигането до нея е опасно" },
        { "report.reason.offensive", "Съдържанието е обидно" },
        { "report.reason.wrong_place", "На грешно място е" },
        { "report.reason.spam", "Спам е" },
        { "report.reason.other", "Друго" },
        { "report.cancel", "Няма значение" },
        { "report.sending", "Изпраща се…" },
        { "report.sent", "Изпратено. Някой ще погледне." },
        { "report.offline", "Няма връзка със сървъра." },
        { "report.failed", "Не се изпрати." },
        { "camera.noMarkers", "Няма маркери наблизо" },
        { "camera.inView", "Пред вас" },
        { "camera.nearest", "Най-близо" },

        { "state.loading.title", "Зареждане на маркери" },
        { "state.loading.body", "Изтегляне на това, което е около вас." },
        { "state.locating.title", "Определяне на позицията" },
        { "state.locating.body", "Излезте на открито и това ще стане по-бързо." },
        { "state.empty.title", "Няма отворен маршрут" },
        { "state.empty.body", "Още не си отворил маршрут. Отвори един и маркерите му ще се появят на картата." },
        { "state.far.title", "Няма нищо в радиус от {0}" },
        { "state.far.body", "Маркерите на маршрута са по-нататък. Продължавай — картичката отдолу сочи следващия." },
        { "state.failed.title", "Маркерите не се заредиха" },
        { "state.failed.body", "Сървърът не отговори. Вече изтегленото остава достъпно." },
        { "state.offline.title", "Няма връзка" },
        { "state.offline.body", "Проверете връзката си. Изтеглените маркери продължават да работят." },
        { "state.retry", "Опитай отново" },
        { "state.widen", "Отвори маршрут" },

        { "settings.privacy", "Поверителност" },
        { "settings.hidden", "Скрити маркери" },
        { "settings.hiddenNone", "Няма скрити" },
        { "settings.hiddenCount", "{0} скрити" },
        { "settings.unhideAll", "Покажи всички отново" },

        { "tab.camera", "Камера" },
        { "tab.search", "Търсене" },
        { "tab.settings", "Настройки" },

        { "camera.refused", "Няма достъп до камерата, затова няма какво " +
                            "да се види. Картата работи - можете да разрешите " +
                            "камерата от Настройки." },
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
        { "vps.none.title", "Тук не може да се калибрира" },
        { "vps.none.body", "Няма визуално позициониране тук. Маркерите използват само GPS и може да са неточни." },
        { "vps.accuracy", "±{0} м" },
        { "vps.unavailable", "Недостъпно" },

        { "info.learnMore", "Научи повече" },

        { "search.placeholder", "Търсене на маркери" },
        { "search.closest", "Най-близо до вас" },
        { "search.suggestions", "Предложения" },
        { "search.empty", "Няма намерени маркери" },
        { "buildings.offline", "Неуспешно зареждане на маркерите. Проверете връзката си." },
        { "search.discovered", "разгледани маркера" },
        { "search.title", "Напредък" },
        { "graph.empty", "Маршрутът се появява тук, щом маркерите му се заредят." },
        { "graph.scored", "+{0}" },
        { "graph.shut", "Още не е отключен" },
        { "search.points", "точки" },
        { "search.standing", "в класацията" },
        { "search.noTeam", "вървиш сам" },
        { "search.rank", "#{0}" },
        { "search.discoveredNote", "* Спирката се отчита, когато натиснеш маркера в камерата — или я отвориш от картата, докато стоиш при нея." },

        { "team.close", "Затвори" },
        { "team.eyebrow", "Твоят отбор" },
        { "team.title", "Отбор" },
        { "team.none", "Вървиш сам" },
        { "team.needed", "Тази игра се играе в отбор" },
        { "team.neededBody", "Направи си — и сам човек е отбор — или напиши чужд код, за да влезеш в техния." },
        { "team.noneBody", "Направи отбор и дай кода му, или напиши чужд, за да влезеш в техния." },
        { "team.capacity", "{0} от {1} души" },
        { "team.capacityNone", "Отборите тук са до {0} души" },
        { "team.name", "Име на отбора" },
        { "team.create", "Направи отбор" },
        { "team.code", "Код на техния отбор" },
        { "team.join", "Влез" },
        { "team.invite", "Код на отбора" },
        { "team.inviteHint", "Дай го на хората, с които вървиш. Не е кодът на маршрута." },
        { "team.leave", "Напусни отбора" },
        { "team.you", "(ти)" },
        { "team.meta", "{0} спирки \u00b7 {1} точки" },
        { "task.worth", "Носи {0} точки" },
        { "team.moved", "Модератор промени точките ви" },
        { "team.movedRow", "{0} \u2014 {1}" },
        { "team.earned", "{0} извървени, {1} от модератора" },
        { "team.out", "Извън класирането" },
        { "team.board", "Класиране" },
        { "team.boardEmpty", "Още никой не е отговорил" },
        { "team.needName", "Дай име на отбора." },
        { "team.needCode", "Напиши кода на отбора." },
        { "team.working", "Момент\u2026" },
        { "team.offline", "Няма връзка със сървъра." },
        { "team.failed", "Не се получи." },
        { "settings.account", "Профил" },
        { "account.none", "Още нямаш профил" },
        { "account.noneBody", "Извървяното се пази само на този телефон. Направи профил и то ще те следва на друг телефон и в сайта." },
        { "account.name", "Твоето име" },
        { "account.nameHint", "Така те изписва класацията." },
        { "account.saveName", "Запази" },
        { "account.email", "Имейл" },
        { "account.password", "Парола" },
        { "account.create", "Направи профил" },
        { "account.signIn", "Вход" },
        { "account.signOut", "Изход" },
        { "account.signedInAs", "Влязъл си като" },
        { "account.mergeWarning", "Влизането в профил, който вече имаш, не пренася извървяното на този телефон." },
        { "account.working", "Момент\u2026" },
        { "account.needBoth", "Попълни и имейл, и парола." },
        { "auth.failed", "Не се получи. Опитай пак." },
        { "auth.noSession", "Още няма връзка със сървъра." },
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
        { "terms.1.title", "Гледайте къде вървите" },
        { "terms.1.body", "Това не е навигация. Чертае права линия до маркера, а не маршрут, и не знае нищо за улици и движение. Вдигнете поглед." },
        { "terms.2.title", "Къде се намирате" },
        { "terms.2.body", "Местоположението ви служи, за да намерим маркерите близо до вас и да ги поставим през камерата. Остава на телефона и стига до нас само като въпрос „какво има близо до тази точка“. Не пазим история къде сте били." },
        { "terms.3.title", "Какво оставят другите" },
        { "terms.3.body", "Всеки може да постави маркер и ние не ги проверяваме предварително. Ако някой ви пречи, „Скрий този маркер“ го маха от картата ви веднага и никой не трябва да се съгласява с вас." },
        { "terms.4.title", "Профилът ви" },
        { "terms.4.body", "Профилът помни към кои маршрути сте се присъединили и какво е отговорил отборът ви. Може да го изтриете когато поискате, от Настройки." },
        { "terms.footer", "Приемането записва датата и версията към профила ви." },

        { "trails.title", "Моите маршрути" },
        { "trails.eyebrow", "ВАШИТЕ МАРШРУТИ" },
        { "trails.subtitle", "Изберете един, за да го извървите." },
        { "trails.choose", "Изберете маршрут" },
        { "trails.label", "МАРШРУТ" },
        { "trails.notNow", "Не сега" },
        { "trails.leave", "Напусни" },
        { "trails.leaveConfirm", "Потвърди" },
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
        { "piece.look", "Виж" },
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
        { "hint.title", "Подсказки" },
        { "hint.button", "Подсказки ({0})" },
        { "hint.nth", "Подсказка {0}" },
        { "hint.free", "Безплатна" },
        { "hint.cost", "−{0} точки" },
        { "hint.unlock", "Отключи" },
        { "hint.locked", "Заключена — отключи, за да я прочетеш" },
        { "hint.previous", "Отключи първо предишните" },
        { "hint.none", "Този маркер няма подсказки." },
        { "hint.opening", "Отключва се…" },
        { "hint.failed", "Не се отключи." },
        { "hint.spent", "Похарчени {0} точки за подсказки" },
        { "task.checking", "Проверяване…" },
        { "task.openHint", "Напиши отговора си…" },
        { "task.offline", "Сървърът не отговори, така че това не е изпратено. Направи няколко крачки и опитай пак." },
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
