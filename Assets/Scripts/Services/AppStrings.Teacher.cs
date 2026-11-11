using System.Collections.Generic;

/// <summary>Strings for the teacher feature, merged into AppStrings (see AppStrings.cs).</summary>
public static partial class AppStrings
{
    static readonly Dictionary<string, string> TeacherEn = new Dictionary<string, string>
    {
        // The panel
        { "teacher.title", "Teacher panel" },
        { "teacher.eyebrow", "Teacher panel" },
        { "teacher.tabLobby", "Lobby" },
        { "teacher.tabBoard", "Scoreboard" },
        { "teacher.state.manual", "Not started. Press Start when everybody is ready." },
        { "teacher.state.startsAt", "Starts by itself at {0}" },
        { "teacher.state.started", "Started" },
        { "teacher.state.startedAt", "Started at {0}" },
        { "teacher.state.ended", "This game has ended" },
        { "teacher.state.open", "No start: the trail is open" },
        { "teacher.ready", "{0} of {1} phones have GPS" },
        { "teacher.start", "Start" },
        { "teacher.startTitle", "Start the game?" },
        { "teacher.startBody", "Every team sees the stops at once, and points start to count. This cannot be undone." },
        { "teacher.started", "The game has started." },
        { "teacher.cancel", "Cancel" },

        // Lobby
        { "teacher.noTeam", "No team" },
        { "teacher.player", "Player" },
        { "teacher.people", "{0} people" },
        { "teacher.teamMeta", "{0} people · {1} with GPS" },
        { "teacher.dqTag", "Disqualified" },
        { "teacher.lobbyEmpty", "Nobody has joined yet" },
        { "teacher.lobbyEmptyBody", "Give the students the trail code. They show up here as they join." },
        { "teacher.kick", "Remove" },
        { "teacher.kickTitle", "Remove {0}?" },
        { "teacher.kickBody", "They leave the trail and their team. They can join again with the code." },
        { "teacher.kicked", "{0} was removed." },
        { "teacher.ban", "Block" },
        { "teacher.banTitle", "Block {0}?" },
        { "teacher.banBody", "They leave the trail and their team, and cannot join this trail again." },
        { "teacher.banned", "{0} was blocked." },
        { "teacher.dq", "Disqualify" },
        { "teacher.dqTitle", "Disqualify {0}?" },
        { "teacher.dqBody", "This is final. The team leaves the scoreboard and its points no longer count." },
        { "teacher.dqReason", "Disqualified by the teacher" },
        { "teacher.disqualified", "{0} was disqualified." },

        // Scoreboard
        { "teacher.boardEmpty", "No teams yet" },
        { "teacher.boardEmptyBody", "Teams show up here as soon as they are made." },
        { "teacher.stops", "{0} stops" },
        { "teacher.stopsOne", "1 stop" },
        { "teacher.points", "{0} pts" },

        // Errors
        { "teacher.offline", "Could not reach the server." },
        { "teacher.err.notYours", "Only the trail's owner can do that." },
        { "teacher.err.noTeam", "That team no longer exists." },
        { "teacher.err.noPlayer", "That player is no longer here." },
        { "teacher.err.owner", "The owner cannot be removed." },
        { "teacher.err.banned", "You can't join this trail." },
        { "server.banned", "You can't join this trail." },

        // The students' waiting screen
        { "wait.title", "Waiting for the teacher to start" },
        { "wait.body", "The stops appear as soon as the teacher presses Start. Keep the app open." },
        { "wait.team", "Team “{0}”" },
        { "wait.noTeam", "You are not on a team yet" },
        { "wait.noTeamBody", "Make a team or join one with its code before the start." },
        { "wait.openTeam", "Team" },
        { "wait.makeTeam", "Make or join a team" },
        { "wait.later", "To the map" },
        { "wait.started", "The game has started. Good luck!" },
    };

    static readonly Dictionary<string, string> TeacherBg = new Dictionary<string, string>
    {
        // The panel
        { "teacher.title", "Учителски пулт" },
        { "teacher.eyebrow", "Учителски пулт" },
        { "teacher.tabLobby", "Лоби" },
        { "teacher.tabBoard", "Класиране" },
        { "teacher.state.manual", "Не е започнал. Натисни „Старт“, когато всички са готови." },
        { "teacher.state.startsAt", "Започва сам в {0}" },
        { "teacher.state.started", "Започна" },
        { "teacher.state.startedAt", "Започна в {0}" },
        { "teacher.state.ended", "Играта приключи" },
        { "teacher.state.open", "Без старт: маршрутът е отворен" },
        { "teacher.ready", "{0} от {1} телефона са с GPS" },
        { "teacher.start", "Старт" },
        { "teacher.startTitle", "Да започне ли играта?" },
        { "teacher.startBody", "Всички отбори виждат спирките веднага и точките започват да се броят. Това не може да се отмени." },
        { "teacher.started", "Играта започна." },
        { "teacher.cancel", "Отказ" },

        // Lobby
        { "teacher.noTeam", "Без отбор" },
        { "teacher.player", "Играч" },
        { "teacher.people", "{0} души" },
        { "teacher.teamMeta", "{0} души · {1} с GPS" },
        { "teacher.dqTag", "Дисквалифициран" },
        { "teacher.lobbyEmpty", "Още никой не е влязъл" },
        { "teacher.lobbyEmptyBody", "Дай на учениците кода на маршрута. Ще се появяват тук, щом влязат." },
        { "teacher.kick", "Изгони" },
        { "teacher.kickTitle", "Да изгониш ли {0}?" },
        { "teacher.kickBody", "Излиза от маршрута и от отбора си. Може да влезе отново с кода." },
        { "teacher.kicked", "{0} е изгонен." },
        { "teacher.ban", "Блокирай" },
        { "teacher.banTitle", "Да блокираш ли {0}?" },
        { "teacher.banBody", "Излиза от маршрута и от отбора си и не може да влезе отново в този маршрут." },
        { "teacher.banned", "{0} е блокиран." },
        { "teacher.dq", "Дисквалифицирай" },
        { "teacher.dqTitle", "Да дисквалифицираш ли {0}?" },
        { "teacher.dqBody", "Това е окончателно. Отборът излиза от класирането и точките му не се броят." },
        { "teacher.dqReason", "Дисквалифициран от учителя" },
        { "teacher.disqualified", "{0} е дисквалифициран." },

        // Scoreboard
        { "teacher.boardEmpty", "Още няма отбори" },
        { "teacher.boardEmptyBody", "Отборите се появяват тук, щом бъдат направени." },
        { "teacher.stops", "{0} спирки" },
        { "teacher.stopsOne", "1 спирка" },
        { "teacher.points", "{0} т." },

        // Errors
        { "teacher.offline", "Няма връзка със сървъра." },
        { "teacher.err.notYours", "Само собственикът на маршрута може да прави това." },
        { "teacher.err.noTeam", "Този отбор вече го няма." },
        { "teacher.err.noPlayer", "Този играч вече го няма." },
        { "teacher.err.owner", "Собственикът не може да бъде изгонен." },
        { "teacher.err.banned", "Не можеш да влезеш в този маршрут." },
        { "server.banned", "Не можеш да влезеш в този маршрут." },

        // The students' waiting screen
        { "wait.title", "Чакаме учителя да започне" },
        { "wait.body", "Спирките се появяват, щом учителят натисне „Старт“. Не затваряй приложението." },
        { "wait.team", "Отбор „{0}“" },
        { "wait.noTeam", "Още не си в отбор" },
        { "wait.noTeamBody", "Направи отбор или влез в нечий с кода му преди старта." },
        { "wait.openTeam", "Отбор" },
        { "wait.makeTeam", "Направи или влез в отбор" },
        { "wait.later", "Към картата" },
        { "wait.started", "Играта започна. Успех!" },
    };
}
