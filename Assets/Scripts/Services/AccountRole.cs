using System;
using UnityEngine;

/// <summary>
/// How a person uses meARc, as profiles.role holds it: a creator makes
/// routes, a viewer browses and walks them, and null is somebody who has never
/// been asked (every account made before the question existed).
///
/// Like LineOfWork, the app only collects and stores it. A viewer sends no
/// line of work; a creator sends one of the four.
/// </summary>
public static class AccountRole
{
    public const string Creator = "creator";
    public const string Viewer = "viewer";

    public static bool IsValid(string role)
    {
        return role == Creator || role == Viewer;
    }

    /// <summary>
    /// Reads the reply to GET profiles?select=role,work.
    ///
    /// False when there is nothing to go on - no row, an error body, not JSON -
    /// because a read that failed must not be taken for "never answered" and put
    /// a question in front of somebody who answered long ago. True with a null
    /// role is the one case that means "ask"; the work may be null on its own.
    /// </summary>
    public static bool TryRead(string reply, out string role, out string work)
    {
        role = null;
        work = null;

        if (string.IsNullOrWhiteSpace(reply) || !reply.TrimStart().StartsWith("["))
            return false;

        Rows rows;

        try
        {
            rows = JsonUtility.FromJson<Rows>("{\"rows\":" + reply + "}");
        }
        catch (Exception)
        {
            return false;
        }

        if (rows == null || rows.rows == null || rows.rows.Length == 0 || rows.rows[0] == null)
            return false;

        string r = rows.rows[0].role;
        string w = rows.rows[0].work;

        // A value the app does not know is treated as unanswered.
        role = IsValid(r) ? r : null;
        work = LineOfWork.IsValid(w) ? w : null;
        return true;
    }

    [Serializable]
    class Rows
    {
        public Row[] rows;
    }

    [Serializable]
    class Row
    {
        public string role;
        public string work;
    }
}
