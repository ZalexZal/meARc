using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// The pure half of the teacher tools: reading world_teams, world_players and
/// trail_start as PostgREST sends them, building the roster, and ranking the
/// scoreboard (TeacherService).
/// </summary>
public class TeacherTests
{
    const string TeamsJson =
        "[{\"id\":\"t1\",\"name\":\"Green\",\"invite_code\":\"ABC123\",\"started_at\":null," +
        "\"members\":[{\"name\":\"Maria\",\"joined_at\":\"2026-11-11T09:00:00+00:00\",\"user_id\":\"u1\",\"gps_ok\":true}," +
        "{\"name\":\"Ivan\",\"joined_at\":\"2026-11-11T09:01:00+00:00\",\"user_id\":\"u2\",\"gps_ok\":false}]," +
        "\"solved\":3,\"points\":120,\"adjusted\":-20,\"disqualified_at\":null,\"disqualified_reason\":null," +
        "\"warnings\":{}}," +
        "{\"id\":\"t2\",\"name\":\"Blue\",\"invite_code\":\"XYZ789\",\"started_at\":null," +
        "\"members\":[{\"name\":\"Petya\",\"joined_at\":\"2026-11-11T09:02:00+00:00\",\"user_id\":\"u3\",\"gps_ok\":true}]," +
        "\"solved\":0,\"points\":0,\"adjusted\":0,\"disqualified_at\":\"2026-11-11T10:00:00+00:00\"," +
        "\"disqualified_reason\":\"cheating\",\"warnings\":{\"spoof\":2}}]";

    const string PlayersJson =
        "[{\"user_id\":\"u1\",\"name\":\"Maria\",\"team_id\":\"t1\",\"gps_ok\":true,\"joined_at\":\"2026-11-11T09:00:00+00:00\"}," +
        "{\"user_id\":\"u4\",\"name\":\"Elena\",\"team_id\":null,\"gps_ok\":false,\"joined_at\":\"2026-11-11T09:05:00+00:00\"}," +
        "{\"user_id\":\"me\",\"name\":\"Teacher\",\"team_id\":null,\"gps_ok\":false,\"joined_at\":\"2026-11-11T08:00:00+00:00\"}]";

    // ------------------------------------------------------------- parsing

    [Test]
    public void ParseTeams_ReadsMembersAndDisqualified()
    {
        List<TeacherService.TeamRow> teams = TeacherService.ParseTeams(TeamsJson);

        Assert.AreEqual(2, teams.Count);
        Assert.AreEqual("t1", teams[0].id);
        Assert.AreEqual("Green", teams[0].name);
        Assert.AreEqual(2, teams[0].members.Length);
        Assert.AreEqual("u1", teams[0].members[0].user_id);
        Assert.IsTrue(teams[0].members[0].gps_ok);
        Assert.IsFalse(teams[0].members[1].gps_ok);
        Assert.AreEqual(120, teams[0].points);
        Assert.AreEqual(-20, teams[0].adjusted);
        Assert.AreEqual(3, teams[0].solved);
        Assert.IsFalse(teams[0].Disqualified);
        Assert.IsTrue(teams[1].Disqualified);
    }

    [Test]
    public void ParseTeams_NullGps_ReadsFalse()
    {
        List<TeacherService.TeamRow> teams = TeacherService.ParseTeams(
            "[{\"id\":\"t1\",\"name\":\"A\",\"members\":[{\"name\":\"X\",\"user_id\":\"u1\",\"gps_ok\":null}]}]");

        Assert.AreEqual(1, teams[0].members.Length);
        Assert.IsFalse(teams[0].members[0].gps_ok);
    }

    [Test]
    public void ParsePlayers_NullTeamIsEmpty()
    {
        List<TeacherService.Player> players = TeacherService.ParsePlayers(PlayersJson);

        Assert.AreEqual(3, players.Count);
        Assert.AreEqual("t1", players[0].team_id);
        Assert.IsTrue(string.IsNullOrEmpty(players[1].team_id));
        Assert.AreEqual("Elena", players[1].name);
    }

    [Test]
    public void Parse_BlankAndNull_AreEmpty()
    {
        Assert.AreEqual(0, TeacherService.ParseTeams(null).Count);
        Assert.AreEqual(0, TeacherService.ParseTeams("").Count);
        Assert.AreEqual(0, TeacherService.ParseTeams("[]").Count);
        Assert.AreEqual(0, TeacherService.ParsePlayers("null").Count);
        Assert.IsNull(TeacherService.ParseStart("null"));
        Assert.IsNull(TeacherService.ParseStart(""));
    }

    [Test]
    public void ParseStart_ManualNotStarted_CanStartAndWaits()
    {
        TeacherService.StartState start = TeacherService.ParseStart(
            "{\"start_mode\":\"manual\",\"starts_at\":null,\"started_at\":null,\"started\":false,\"ended\":false}");

        Assert.AreEqual("manual", start.start_mode);
        Assert.IsTrue(start.CanStart);
        Assert.IsTrue(start.WaitsForTeacher);
    }

    [Test]
    public void ParseStart_StartedOrOtherModes_DoNotWait()
    {
        TeacherService.StartState started = TeacherService.ParseStart(
            "{\"start_mode\":\"manual\",\"started_at\":\"2026-11-11T10:00:00+00:00\",\"started\":true,\"ended\":false}");
        Assert.IsFalse(started.CanStart);
        Assert.IsFalse(started.WaitsForTeacher);
        Assert.AreEqual("2026-11-11T10:00:00+00:00", started.started_at);

        TeacherService.StartState auto = TeacherService.ParseStart(
            "{\"start_mode\":\"auto\",\"starts_at\":\"2026-11-11T12:00:00+00:00\",\"started\":false,\"ended\":false}");
        Assert.IsFalse(auto.CanStart);

        TeacherService.StartState none = TeacherService.ParseStart(
            "{\"start_mode\":\"none\",\"started\":true,\"ended\":false}");
        Assert.IsFalse(none.WaitsForTeacher);
    }

    // --------------------------------------------------------------- lobby

    [Test]
    public void Lobby_TeamsThenUnteamed_MeLeftOut()
    {
        TeacherService.Lobby lobby = TeacherService.BuildLobby(
            TeacherService.ParseTeams(TeamsJson), TeacherService.ParsePlayers(PlayersJson), "me");

        Assert.AreEqual(2, lobby.teams.Count);
        Assert.AreEqual(2, lobby.teams[0].players.Count);
        Assert.AreEqual("u2", lobby.teams[0].players[1].userId);
        Assert.IsTrue(lobby.teams[1].disqualified);

        Assert.AreEqual(1, lobby.unteamed.Count);
        Assert.AreEqual("u4", lobby.unteamed[0].userId);
    }

    [Test]
    public void Lobby_CountsPhones_OnlyTeamsStillPlaying()
    {
        TeacherService.Lobby lobby = TeacherService.BuildLobby(
            TeacherService.ParseTeams(TeamsJson), null, null);

        // Green: two phones, one with GPS. Blue is disqualified and not counted.
        Assert.AreEqual(2, lobby.phones);
        Assert.AreEqual(1, lobby.ready);
        Assert.AreEqual(0, lobby.unteamed.Count);
    }

    [Test]
    public void Lobby_MemberWithoutId_MatchedFromPlayers()
    {
        var teams = new List<TeacherService.TeamRow>
        {
            new TeacherService.TeamRow
            {
                id = "t1", name = "Old server",
                members = new[] { new TeacherService.TeamMember { name = "Maria" } }
            }
        };
        var players = new List<TeacherService.Player>
        {
            new TeacherService.Player { user_id = "u1", name = "Maria", team_id = "t1", gps_ok = true }
        };

        TeacherService.Lobby lobby = TeacherService.BuildLobby(teams, players, null);

        Assert.AreEqual("u1", lobby.teams[0].players[0].userId);
        Assert.IsTrue(lobby.teams[0].players[0].gps);
        Assert.AreEqual(0, lobby.unteamed.Count);
    }

    [Test]
    public void Lobby_Nothing_IsEmpty()
    {
        TeacherService.Lobby lobby = TeacherService.BuildLobby(null, null, null);

        Assert.AreEqual(0, lobby.teams.Count);
        Assert.AreEqual(0, lobby.unteamed.Count);
        Assert.AreEqual(0, lobby.phones);
    }

    // --------------------------------------------------------------- board

    static TeacherService.TeamRow T(string id, string name, int points, int solved,
                                    int adjusted = 0, bool out_ = false)
    {
        return new TeacherService.TeamRow
        {
            id = id, name = name, points = points, solved = solved, adjusted = adjusted,
            disqualified_at = out_ ? "2026-11-11T10:00:00Z" : null
        };
    }

    [Test]
    public void Board_SortsByPointsThenStopsThenName()
    {
        var board = TeacherService.BuildBoard(new List<TeacherService.TeamRow>
        {
            T("a", "Zeta", 50, 2),
            T("b", "Alpha", 100, 4),
            T("c", "beta", 50, 3),
            T("d", "Gamma", 50, 3)
        }, null);

        Assert.AreEqual(4, board.Count);
        Assert.AreEqual("b", board[0].teamId);
        Assert.AreEqual("c", board[1].teamId);   // beta before Gamma, ignoring case
        Assert.AreEqual("d", board[2].teamId);
        Assert.AreEqual("a", board[3].teamId);
    }

    [Test]
    public void Board_TiesShareARank()
    {
        var board = TeacherService.BuildBoard(new List<TeacherService.TeamRow>
        {
            T("a", "A", 50, 3),
            T("b", "B", 50, 3),
            T("c", "C", 10, 1)
        }, null);

        Assert.AreEqual(1, board[0].rank);
        Assert.AreEqual(1, board[1].rank);
        Assert.AreEqual(3, board[2].rank);
    }

    [Test]
    public void Board_EveryTeamFromTheStart_AtZero()
    {
        var board = TeacherService.BuildBoard(new List<TeacherService.TeamRow>
        {
            T("a", "A", 0, 0),
            T("b", "B", 0, 0)
        }, null);

        Assert.AreEqual(2, board.Count);
        Assert.AreEqual(0, board[0].points);
        Assert.AreEqual(1, board[1].rank);
    }

    [Test]
    public void Board_AddsAdjustment_FloorsAtZero()
    {
        var board = TeacherService.BuildBoard(new List<TeacherService.TeamRow>
        {
            T("a", "A", 120, 3, -20),
            T("b", "B", 40, 1, -100)
        }, null);

        Assert.AreEqual(100, board[0].points);
        Assert.AreEqual(0, board[1].points);
    }

    [Test]
    public void Board_LeavesOutDisqualified_AndJustDisqualified()
    {
        var board = TeacherService.BuildBoard(new List<TeacherService.TeamRow>
        {
            T("a", "A", 100, 3),
            T("b", "B", 200, 5, 0, true),
            T("c", "C", 300, 6)
        }, new HashSet<string> { "c" });

        Assert.AreEqual(1, board.Count);
        Assert.AreEqual("a", board[0].teamId);
        Assert.AreEqual(1, board[0].rank);
    }

    [Test]
    public void Board_Null_IsEmpty()
    {
        Assert.AreEqual(0, TeacherService.BuildBoard(null, null).Count);
    }

    [Test]
    public void IsBanned_MatchesServerSentence()
    {
        Assert.IsTrue(TeacherService.IsBanned("banned"));
        Assert.IsTrue(TeacherService.IsBanned("{\"code\":\"P0001\",\"message\":\"banned\"}"));
        Assert.IsFalse(TeacherService.IsBanned("no trail with that code"));
        Assert.IsFalse(TeacherService.IsBanned(null));
    }
}
