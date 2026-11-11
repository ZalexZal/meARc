using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The organiser's levers on a live trail: start it, see who is in it, take a
/// player out (or keep them out), take a team off the board, and read the board.
///
/// Every rule lives in the database (0032, 0058, 0071): who may moderate, that
/// the owner cannot be kicked, that a banned player cannot come back. This only
/// asks and reports what it is told, the same way TeamService does.
///
/// A static class of coroutines rather than a component: the two screens that
/// use it run them on themselves, so a request never outlives the screen that
/// asked for it. The parsing and the sorting are plain functions so they can be
/// tested without a network (Tests/Editor/TeacherTests.cs).
/// </summary>
public static class TeacherService
{
    // ------------------------------------------------------------------ models

    /// <summary>trail_start (0058), with started_at, which TrailService.StartInfo leaves out.</summary>
    [Serializable]
    public class StartState
    {
        public string start_mode;
        public string starts_at;
        public string started_at;
        public bool started;
        public bool ended;

        /// <summary>The organiser's Start applies: a manual start that has not happened.</summary>
        public bool CanStart { get { return start_mode == "manual" && !started && !ended; } }

        /// <summary>Students wait on this screen: manual, not started, not over.</summary>
        public bool WaitsForTeacher { get { return start_mode == "manual" && !started && !ended; } }
    }

    /// <summary>One member inside world_teams' members jsonb (user_id and gps_ok since 0071).</summary>
    [Serializable]
    public class TeamMember
    {
        public string user_id;
        public string name;
        public bool gps_ok;
        public string joined_at;
    }

    /// <summary>One row of world_teams (0045, members widened in 0071).</summary>
    [Serializable]
    public class TeamRow
    {
        public string id;
        public string name;
        public string invite_code;
        public TeamMember[] members;
        public int solved;
        public int points;
        public int adjusted;
        public string disqualified_at;
        public string disqualified_reason;

        public bool Disqualified { get { return !string.IsNullOrEmpty(disqualified_at); } }
    }

    /// <summary>One row of world_players (0071): every member of the trail, with their team or none.</summary>
    [Serializable]
    public class Player
    {
        public string user_id;
        public string name;
        public string team_id;
        public bool gps_ok;
        public string joined_at;
    }

    /// <summary>A person as the lobby draws them.</summary>
    public class LobbyPlayer
    {
        public string userId;
        public string name;
        public bool gps;
    }

    /// <summary>A team as the lobby draws it.</summary>
    public class LobbyTeam
    {
        public string id;
        public string name;
        public bool disqualified;
        public List<LobbyPlayer> players = new List<LobbyPlayer>();
    }

    /// <summary>The roster: teams in the order they were made, then everybody in no team.</summary>
    public class Lobby
    {
        public List<LobbyTeam> teams = new List<LobbyTeam>();
        public List<LobbyPlayer> unteamed = new List<LobbyPlayer>();

        /// <summary>Phones on a team still in the game.</summary>
        public int phones;

        /// <summary>Of those, the ones that said location works.</summary>
        public int ready;
    }

    [Serializable] class TeamList { public List<TeamRow> items; }
    [Serializable] class PlayerList { public List<Player> items; }

    // ----------------------------------------------------------------- parsing

    static bool Blank(string json)
    {
        if (string.IsNullOrEmpty(json))
            return true;

        string trimmed = json.Trim();
        return trimmed.Length == 0 || trimmed == "null";
    }

    /// <summary>
    /// A top-level array, wrapped: JsonUtility will not take one bare. The same
    /// trick TeamService and RemoteMarkerSource use.
    /// </summary>
    static string Wrap(string json)
    {
        return "{\"items\":" + json + "}";
    }

    public static List<TeamRow> ParseTeams(string json)
    {
        if (Blank(json))
            return new List<TeamRow>();

        TeamList list = JsonUtility.FromJson<TeamList>(Wrap(json));
        return list != null && list.items != null ? list.items : new List<TeamRow>();
    }

    public static List<Player> ParsePlayers(string json)
    {
        if (Blank(json))
            return new List<Player>();

        PlayerList list = JsonUtility.FromJson<PlayerList>(Wrap(json));
        return list != null && list.items != null ? list.items : new List<Player>();
    }

    /// <summary>trail_start answers one object, or null for a trail the caller cannot see.</summary>
    public static StartState ParseStart(string json)
    {
        if (Blank(json))
            return null;

        return JsonUtility.FromJson<StartState>(json);
    }

    // ------------------------------------------------------------- the lobby

    /// <summary>
    /// The roster from world_teams and world_players.
    ///
    /// Team members come from world_teams. If a member came back without a
    /// user_id (a server from before 0071), it is matched from world_players by
    /// team and name, so Kick still has somebody to point at. Everybody in
    /// world_players with no team, and not already on a team, is "no team";
    /// <paramref name="me"/> - the organiser - is left out of that list, since
    /// the owner is a member of their own trail and cannot be kicked anyway.
    /// </summary>
    public static Lobby BuildLobby(IList<TeamRow> teams, IList<Player> players, string me)
    {
        Lobby lobby = new Lobby();
        HashSet<string> placed = new HashSet<string>();

        if (teams != null)
        {
            for (int t = 0; t < teams.Count; t++)
            {
                TeamRow row = teams[t];
                if (row == null)
                    continue;

                LobbyTeam team = new LobbyTeam
                {
                    id = row.id,
                    name = row.name,
                    disqualified = row.Disqualified
                };

                if (row.members != null)
                {
                    for (int m = 0; m < row.members.Length; m++)
                    {
                        TeamMember member = row.members[m];
                        if (member == null)
                            continue;

                        string id = member.user_id;
                        bool gps = member.gps_ok;

                        if (string.IsNullOrEmpty(id))
                        {
                            Player match = Find(players, row.id, member.name, placed);
                            if (match != null)
                            {
                                id = match.user_id;
                                gps = gps || match.gps_ok;
                            }
                        }

                        if (!string.IsNullOrEmpty(id))
                            placed.Add(id);

                        team.players.Add(new LobbyPlayer { userId = id, name = member.name, gps = gps });

                        if (!team.disqualified)
                        {
                            lobby.phones++;
                            if (gps)
                                lobby.ready++;
                        }
                    }
                }

                lobby.teams.Add(team);
            }
        }

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                if (player == null || string.IsNullOrEmpty(player.user_id))
                    continue;
                if (!string.IsNullOrEmpty(player.team_id) || placed.Contains(player.user_id))
                    continue;
                if (player.user_id == me)
                    continue;

                placed.Add(player.user_id);
                lobby.unteamed.Add(new LobbyPlayer { userId = player.user_id, name = player.name, gps = player.gps_ok });
            }
        }

        return lobby;
    }

    static Player Find(IList<Player> players, string teamId, string name, HashSet<string> placed)
    {
        if (players == null)
            return null;

        for (int i = 0; i < players.Count; i++)
        {
            Player p = players[i];
            if (p != null && p.team_id == teamId && p.name == name &&
                !string.IsNullOrEmpty(p.user_id) && !placed.Contains(p.user_id))
                return p;
        }

        return null;
    }

    // -------------------------------------------------------------- the board

    /// <summary>One line of the organiser's scoreboard.</summary>
    public class BoardEntry
    {
        public int rank;
        public string teamId;
        public string name;
        public int points;
        public int solved;
    }

    /// <summary>
    /// The scoreboard, from world_teams rather than world_leaderboard.
    ///
    /// world_leaderboard leaves out every team that has not solved anything,
    /// so at the start of a lesson the organiser's board would be empty. This
    /// shows every team from the start, at 0. Points are the board's own
    /// arithmetic: walked for plus whatever a moderator moved, never below
    /// nothing. Disqualified teams are gone, and so is any id in
    /// <paramref name="alsoGone"/> - a team just disqualified should not sit on
    /// the board until the next read. Sorted by points, then stops solved,
    /// then name; equal points and stops share a rank (1, 1, 3).
    /// </summary>
    public static List<BoardEntry> BuildBoard(IList<TeamRow> teams, ICollection<string> alsoGone)
    {
        List<BoardEntry> board = new List<BoardEntry>();

        if (teams == null)
            return board;

        for (int i = 0; i < teams.Count; i++)
        {
            TeamRow t = teams[i];
            if (t == null || t.Disqualified)
                continue;
            if (alsoGone != null && t.id != null && alsoGone.Contains(t.id))
                continue;

            board.Add(new BoardEntry
            {
                teamId = t.id,
                name = t.name,
                points = Math.Max(0, t.points + t.adjusted),
                solved = t.solved
            });
        }

        board.Sort(CompareEntry);

        for (int i = 0; i < board.Count; i++)
        {
            bool tied = i > 0 && board[i].points == board[i - 1].points &&
                        board[i].solved == board[i - 1].solved;
            board[i].rank = tied ? board[i - 1].rank : i + 1;
        }

        return board;
    }

    static int CompareEntry(BoardEntry a, BoardEntry b)
    {
        if (a.points != b.points)
            return b.points.CompareTo(a.points);

        if (a.solved != b.solved)
            return b.solved.CompareTo(a.solved);

        return string.Compare(a.name ?? string.Empty, b.name ?? string.Empty,
                              StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ banned

    /// <summary>
    /// Whether a refusal is the 0071 ban ("banned", P0001), as Postgrest.Said
    /// or a raw reply body carries it.
    /// </summary>
    public static bool IsBanned(string said)
    {
        if (string.IsNullOrEmpty(said))
            return false;

        string trimmed = said.Trim();
        return trimmed == "banned" || trimmed.Contains("\"message\":\"banned\"");
    }

    /// <summary>The toast a banned student sees when a join is refused.</summary>
    public static void SayBanned()
    {
        ProximityToast.Say(AppStrings.Get("teacher.err.banned"));
    }

    // --------------------------------------------------------------- words

    /// <summary>
    /// What to tell the organiser, in the app language. Null from the server
    /// means it never answered.
    /// </summary>
    public static string Words(string said)
    {
        if (string.IsNullOrWhiteSpace(said))
            return AppStrings.Get("teacher.offline");

        string trimmed = said.Trim();

        switch (trimmed)
        {
            case "banned": return AppStrings.Get("teacher.err.banned");
            case "not yours":
            case "not yours to moderate": return AppStrings.Get("teacher.err.notYours");
            case "only the organiser can start this game": return AppStrings.Get("teacher.err.notYours");
            case "no such team": return AppStrings.Get("teacher.err.noTeam");
            case "no such player": return AppStrings.Get("teacher.err.noPlayer");
            case "the owner cannot be removed": return AppStrings.Get("teacher.err.owner");
        }

        return Postgrest.InWords(trimmed);
    }

    // ------------------------------------------------------------- requests

    static string World(string worldId)
    {
        return "{\"p_world\":\"" + Postgrest.Json(worldId) + "\"}";
    }

    public static IEnumerator FetchStart(string worldId, Action<StartState, string> done)
    {
        yield return Call("trail_start", World(worldId), (json, error) =>
        {
            if (error != null) { done(null, error); return; }
            StartState state = null;
            try { state = ParseStart(json); }
            catch (Exception exception) { Debug.LogWarning("[Teacher] Unreadable start: " + exception.Message); }
            done(state, null);
        });
    }

    public static IEnumerator FetchTeams(string worldId, Action<List<TeamRow>, string> done)
    {
        yield return Call("world_teams", World(worldId), (json, error) =>
        {
            if (error != null) { done(null, error); return; }
            List<TeamRow> rows;
            try { rows = ParseTeams(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Teacher] Unreadable teams: " + exception.Message);
                rows = new List<TeamRow>();
            }
            done(rows, null);
        });
    }

    public static IEnumerator FetchPlayers(string worldId, Action<List<Player>, string> done)
    {
        yield return Call("world_players", World(worldId), (json, error) =>
        {
            if (error != null) { done(null, error); return; }
            List<Player> rows;
            try { rows = ParsePlayers(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Teacher] Unreadable players: " + exception.Message);
                rows = new List<Player>();
            }
            done(rows, null);
        });
    }

    /// <summary>moderator_start_game(p_world) (0058).</summary>
    public static IEnumerator StartGame(string worldId, Action<string> done)
    {
        yield return Call("moderator_start_game", World(worldId), (json, error) => done(error));
    }

    /// <summary>moderator_kick(p_world, p_user) (0071): out, free to come back.</summary>
    public static IEnumerator Kick(string worldId, string userId, Action<string> done)
    {
        yield return Call("moderator_kick",
            "{\"p_world\":\"" + Postgrest.Json(worldId) + "\",\"p_user\":\"" + Postgrest.Json(userId) + "\"}",
            (json, error) => done(error));
    }

    /// <summary>moderator_ban(p_world, p_user) (0071): out, and kept out.</summary>
    public static IEnumerator Ban(string worldId, string userId, Action<string> done)
    {
        yield return Call("moderator_ban",
            "{\"p_world\":\"" + Postgrest.Json(worldId) + "\",\"p_user\":\"" + Postgrest.Json(userId) + "\"}",
            (json, error) => done(error));
    }

    /// <summary>
    /// moderator_disqualify(p_team, p_reason, p_on) (0032). The server wants a
    /// reason ("say why"), so the panel sends its own fixed one.
    /// </summary>
    public static IEnumerator Disqualify(string teamId, string reason, Action<string> done)
    {
        yield return Call("moderator_disqualify",
            "{\"p_team\":\"" + Postgrest.Json(teamId) + "\",\"p_reason\":\"" + Postgrest.Json(reason) +
            "\",\"p_on\":true}",
            (json, error) => done(error));
    }

    /// <summary>
    /// One RPC. done(json, null) on success; done(null, words) on failure, where
    /// words are already in the app language and fit for a toast.
    /// </summary>
    static IEnumerator Call(string rpc, string body, Action<string, string> done)
    {
        yield return SupabaseAuth.Ready();

        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
        {
            done(null, AppStrings.Get("teacher.offline"));
            yield break;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (UnityWebRequest request =
                   SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/" + rpc), body, null))
            {
                yield return request.SendWebRequest();

                // The session is a second old and the database has not caught up
                // with its own clock: once more, in a moment.
                if (attempt == 0 && Postgrest.WorthRetrying(request))
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string said = Postgrest.Said(request);
                    Debug.LogWarning("[Teacher] " + rpc + " failed (" + request.responseCode + "): " + said);
                    done(null, Words(said));
                    yield break;
                }

                done(request.downloadHandler.text, null);
                yield break;
            }
        }
    }
}
