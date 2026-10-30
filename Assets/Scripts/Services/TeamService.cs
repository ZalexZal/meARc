using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Who you are walking with, and how everybody is doing.
///
/// The server has had all of this since 0005 and 0009 - teams, a four-member
/// cap, one team per trail, invite codes, a leaderboard - and the phone has
/// never been able to reach any of it. ensure_team quietly made every walker a
/// team of one on their first answer, so teams existed and nobody could form
/// one.
///
/// Everything here is a read or a single RPC. The rules live in the database,
/// including the ones this class could get wrong: the size cap and the
/// one-team-per-trail rule are enforced by a trigger, and a competitive trail
/// refusing anonymous walkers is enforced by assert_may_play. This asks and
/// reports what it is told.
/// </summary>
public class TeamService : MonoBehaviour
{
    public static TeamService Instance { get; private set; }

    /// <summary>Raised when the team or the board changes, so screens repaint.</summary>
    public static event Action OnTeamChanged;

    [Serializable]
    public class Member
    {
        public string name;
        public bool is_me;

        // Since 0058: who it is (for the captain's controls), whether they are
        // the captain, and whether their phone said location works (null, not
        // checked yet, reads as false here).
        public string id;
        public bool is_captain;
        public bool gps;
    }

    /// <summary>
    /// One pull of a moderator's lever, as the team sees it.
    ///
    /// The row has been readable by the team it was written about since 0032 -
    /// the policy allows it - and until 0033 nothing asked. A deduction nobody
    /// can see the reason for is just a score that went wrong.
    /// </summary>
    [Serializable]
    public class Adjustment
    {
        public int points;
        public string reason;
    }

    [Serializable]
    public class Detail
    {
        public bool on_team;
        public string id;
        public string name;
        public string invite_code;
        public Member[] members;
        public int solved;

        /// <summary>
        /// What the standings say: walked for, plus whatever a moderator moved,
        /// never below nothing. Deliberately the same arithmetic as the board,
        /// because a card reading 100 beside a board reading 75 is worse than
        /// either number on its own.
        /// </summary>
        public int points;

        /// <summary>The part of it they actually walked for.</summary>
        public int earned;

        public Adjustment[] adjustments;

        public bool disqualified;
        public string disqualified_reason;

        /// <summary>Since 0058: this phone's person leads the team.</summary>
        public bool i_am_captain;

        /// <summary>Since 0058: the captain gave up for everybody.</summary>
        public bool gave_up;

        public Member Me
        {
            get
            {
                if (members != null)
                    for (int i = 0; i < members.Length; i++)
                        if (members[i] != null && members[i].is_me)
                            return members[i];
                return null;
            }
        }

        public bool Moved
        {
            get { return adjustments != null && adjustments.Length > 0; }
        }

        /// <summary>
        /// The trail's own cap, since 0027 - the creator sets it, so this class
        /// cannot know it without being told. Four only for a reply written
        /// before the column existed.
        /// </summary>
        public int max_size;

        public int Size { get { return members != null ? members.Length : 0; } }

        public int Cap { get { return max_size > 0 ? max_size : 4; } }

        /// <summary>The trigger's number, repeated here only to grey a button.</summary>
        public bool IsFull { get { return Size >= Cap; } }
    }

    [Serializable]
    public class Standing
    {
        public int rank;
        public string team_id;
        public string team_name;
        public int points;
        public int stops_solved;
        public int elapsed_seconds;
        public int pending;
        public bool is_mine;
    }

    [Serializable]
    class StandingList
    {
        public List<Standing> items;
    }

    public Detail Team { get; private set; }
    public List<Standing> Board { get; private set; } = new List<Standing>();

    /// <summary>What went wrong last, for a screen to show. Null when fine.</summary>
    public string LastError { get; private set; }

    public bool Busy { get; private set; }

    /// <summary>
    /// True only while a change is on its way - making, joining or leaving.
    ///
    /// Separate from Busy on purpose. The team card re-reads the board every
    /// fifteen seconds, and Busy was up for every one of those reads; the buttons
    /// checked Busy and returned without a word. On school wifi or mobile data a
    /// read can hang for many seconds, so Make a team looked and behaved dead -
    /// on every trail - for as long as the network was slow. A read in flight is
    /// no reason to refuse a write: the write reloads everything when it lands.
    /// </summary>
    public bool Writing { get; private set; }

    string _world;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>The trail the team and board were last asked for.</summary>
    public string World { get { return _world; } }

    /// <summary>Reads the team and the board for a trail, in that order.</summary>
    public void Load(string worldId)
    {
        if (string.IsNullOrEmpty(worldId))
            return;

        // Another trail's team is not this trail's team.
        //
        // Team was kept across a switch, so the card for the new trail drew once
        // with the old trail's team in it - on a team, form hidden, Make a team
        // switched off - and when the reload said "no team here" the form came
        // back with the button still off. Being on a team anywhere made every
        // other trail's team card impossible to use.
        if (_world != worldId)
        {
            Team = null;
            Board = null;
        }

        _world = worldId;
        StartCoroutine(LoadBoth());
    }

    public void Create(string name, Action<string> done)
    {
        StartCoroutine(Rpc("create_team",
            "{\"p_world\":\"" + Escape(_world) + "\",\"p_name\":\"" + Escape(name) + "\"}", done));
    }

    public void Join(string inviteCode, Action<string> done)
    {
        StartCoroutine(Rpc("join_team",
            "{\"p_code\":\"" + Escape(inviteCode) + "\"}", done));
    }

    public void Leave(Action<string> done)
    {
        if (Team == null || !Team.on_team)
        {
            if (done != null)
                done(null);

            return;
        }

        StartCoroutine(Rpc("leave_team", "{\"p_team\":\"" + Escape(Team.id) + "\"}", done));
    }

    // ------------------------------------------------- the captain (0058)

    /// <summary>Hands the captaincy to a teammate.</summary>
    public void TransferCaptain(string userId, Action<string> done)
    {
        if (Team == null || !Team.on_team) { if (done != null) done(null); return; }
        StartCoroutine(Rpc("transfer_captain",
            "{\"p_team\":\"" + Escape(Team.id) + "\",\"p_user\":\"" + Escape(userId) + "\"}", done));
    }

    /// <summary>Takes a player off the team. They may join again with the code.</summary>
    public void RemoveMember(string userId, Action<string> done)
    {
        if (Team == null || !Team.on_team) { if (done != null) done(null); return; }
        StartCoroutine(Rpc("remove_member",
            "{\"p_team\":\"" + Escape(Team.id) + "\",\"p_user\":\"" + Escape(userId) + "\"}", done));
    }

    /// <summary>The captain stops the game for the whole team.</summary>
    public void GiveUp(Action<string> done)
    {
        if (Team == null || !Team.on_team) { if (done != null) done(null); return; }
        StartCoroutine(Rpc("give_up_team", "{\"p_team\":\"" + Escape(Team.id) + "\"}", done));
    }

    string _gpsSentFor;

    /// <summary>
    /// Tells the team (and the organiser) that location works on this phone,
    /// once per team: whether it works, never where it is (0058).
    /// </summary>
    void ReportGps()
    {
        UserLocationService location = UserLocationService.Instance;
        if (Team == null || !Team.on_team || location == null || !location.HasValidPose)
            return;
        Member me = Team.Me;
        if (me == null || me.gps || _gpsSentFor == Team.id)
            return;
        _gpsSentFor = Team.id;
        StartCoroutine(Fire("set_gps_ready", "{\"p_team\":\"" + Escape(Team.id) + "\",\"p_ok\":true}"));
    }

    IEnumerator Fire(string rpc, string body)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;
        using (UnityWebRequest request = SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/" + rpc), body, null))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[Team] " + rpc + " failed (" + request.responseCode + "): " + Postgrest.Said(request));
        }
    }

    IEnumerator LoadBoth()
    {
        Busy = true;
        LastError = null;

        // Nothing is asked of the server before there is somebody to ask as.
        //
        // A phone logged "my_team_detail failed (401)" one second after launch:
        // the card opened, asked who was on the team, and the anonymous session
        // had not come back yet. The card then drew an empty board and an empty
        // team, which is indistinguishable from being on neither - and the walker
        // is shown a form for a thing they may already have.
        yield return SupabaseAuth.Ready();

        yield return Fetch("my_team_detail", "{\"p_world\":\"" + Escape(_world) + "\"}",
            json =>
            {
                try
                {
                    Team = JsonUtility.FromJson<Detail>(json);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Team] Unreadable team: " + exception.Message);
                    Team = null;
                }
            });

        yield return Fetch("world_leaderboard", "{\"p_world\":\"" + Escape(_world) + "\"}",
            json =>
            {
                try
                {
                    // JsonUtility will not take a top-level array, so the reply is
                    // wrapped. The same trick RemoteMarkerSource uses.
                    StandingList list = JsonUtility.FromJson<StandingList>(
                        "{\"items\":" + json + "}");

                    Board = list != null && list.items != null ? list.items : new List<Standing>();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Team] Unreadable board: " + exception.Message);
                    Board = new List<Standing>();
                }
            });

        Busy = false;

        ReportGps();

        if (OnTeamChanged != null)
            OnTeamChanged();
    }

    IEnumerator Fetch(string rpc, string body, Action<string> got)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            LastError = "team.offline";
            yield break;
        }

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/" + rpc), body, null))
        {
            yield return request.SendWebRequest();

            if (Postgrest.WorthRetrying(request))
            {
                // Same story as everywhere else: the session is a second old and
                // the database has not caught up with its own clock yet.
                yield return new WaitForSeconds(2f);
                yield return Fetch(rpc, body, got);
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                // A 404 here means the migration has not been applied. Either way
                // the screen says it could not ask rather than inventing a team.
                Debug.LogWarning("[Team] " + rpc + " failed (" + request.responseCode + "): " +
                                 request.error);
                LastError = "team.offline";
                yield break;
            }

            got(request.downloadHandler.text);
        }
    }

    /// <summary>
    /// A call that changes something, then a reload.
    ///
    /// The reload is not optimism-avoidance, it is the only way to be right: the
    /// size cap and the one-team rule are triggers, so the answer to "did that
    /// work" lives in the database and not in anything this class predicted.
    /// </summary>
    IEnumerator Rpc(string rpc, string body, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            if (done != null)
                done("team.offline");

            yield break;
        }

        Busy = true;
        Writing = true;

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/" + rpc), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Busy = false;
                Writing = false;

                string message = Postgrest.Said(request);
                Debug.LogWarning("[Team] " + rpc + " failed (" + request.responseCode + "): " + message);

                // The listeners first, then the sentence. The other way round
                // the card was told why it was refused and then immediately
                // redrawn from LastError - which is null, because only a reload
                // sets it - so every refusal was written and wiped in the same
                // frame. Tapping Make a team on a competitive trail as a walker
                // without an account did nothing at all on screen, while the log
                // held the answer: "a competitive trail needs a real account".
                if (OnTeamChanged != null)
                    OnTeamChanged();

                if (done != null)
                    done(message);

                yield break;
            }
        }

        Writing = false;

        yield return LoadBoth();

        if (done != null)
            done(null);
    }

    [Serializable]
    class PostgrestError
    {
        public string message;
        public string code;
    }

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
