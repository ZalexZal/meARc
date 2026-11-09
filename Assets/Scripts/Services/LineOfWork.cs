using System;
using UnityEngine;

/// <summary>
/// What a person does for a living, as profiles.work holds it: broker, teacher,
/// guide or other, and null for somebody who has never been asked.
///
/// The app only collects and stores it. Nothing here hides anything: what a line
/// of work shows or leaves out is decided in the website's trail builder.
/// </summary>
public static class LineOfWork
{
    /// <summary>The four values the database accepts, in the order they are offered.</summary>
    public static readonly string[] Keys = { "broker", "teacher", "guide", "other" };

    public static bool IsValid(string work)
    {
        return work != null && Array.IndexOf(Keys, work) >= 0;
    }

    /// <summary>The value in the reader's language, or a dash when there is none.</summary>
    public static string Label(string work)
    {
        return IsValid(work) ? AppStrings.Get("work." + work) : "—";
    }

    /// <summary>
    /// Reads the reply to GET profiles?select=work.
    ///
    /// False when there is nothing to go on - no row, an error body, not JSON -
    /// because a read that failed must not be taken for "never answered" and put
    /// a question in front of somebody who answered long ago. True with a null
    /// work is the one case that means "ask".
    /// </summary>
    public static bool TryRead(string reply, out string work)
    {
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

        string value = rows.rows[0].work;
        work = IsValid(value) ? value : null;
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
        public string work;
    }
}
