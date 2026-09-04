using System;
using UnityEngine;

/// <summary>
/// The pieces a marker can hold beyond its text: uploaded media, and a task.
///
/// These types are the wire format. They are deserialized straight out of the
/// content jsonb that markers_near returns, so the field names here are the field
/// names in the database and renaming one is a schema change.
///
/// The one thing that is deliberately absent is the answer. content is public -
/// it travels to every phone that can see the marker - so a correct answer stored
/// in it is a correct answer handed to anyone running a proxy, before they have
/// walked anywhere. The answer lives in marker_answers, which nothing may select
/// from, and is compared by the check_answer function. See MARKER-CONTENT.md.
/// </summary>
[Serializable]
public class LocalisedText
{
    public string en;
    public string bg;

    /// <summary>
    /// Bulgarian when it is both chosen and written, English otherwise. Same rule
    /// as BuildingData, and for the same reason: a half-translated trail should
    /// read oddly rather than render blank.
    /// </summary>
    public string Text
    {
        get
        {
            if (AppStrings.CurrentLanguage == AppLanguage.Bulgarian &&
                !string.IsNullOrWhiteSpace(bg))
            {
                return bg;
            }

            return string.IsNullOrWhiteSpace(en) ? bg : en;
        }
    }

    public bool HasText
    {
        get { return !string.IsNullOrWhiteSpace(en) || !string.IsNullOrWhiteSpace(bg); }
    }
}

public enum MarkerMediaKind
{
    Unknown,
    Video,
    Audio,

    /// <summary>
    /// A still. The builder has offered photo uploads all along and the app had
    /// no name for one, so every uploaded picture arrived as Unknown, failed
    /// IsUsable, and was dropped without a word.
    /// </summary>
    Image
}

/// <summary>One uploaded file attached to a marker.</summary>
[Serializable]
public class MarkerMedia
{
    [Tooltip("\"video\" or \"audio\". Anything else is ignored rather than guessed at.")]
    public string kind;

    [Tooltip("Path inside the media bucket, e.g. worlds/<id>/m/<id>/inside.mp4.")]
    public string path;

    [Tooltip("Length in seconds, written by the portal at upload time so the app can " +
             "show it without downloading the file first.")]
    public int seconds;

    [Tooltip("Size in bytes, for the mobile-data warning. Zero means unknown, and " +
             "the warning is then left off rather than invented.")]
    public long bytes;

    public LocalisedText title;

    public MarkerMediaKind Kind
    {
        get
        {
            if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
                return MarkerMediaKind.Video;

            if (string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
                return MarkerMediaKind.Audio;

            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
                return MarkerMediaKind.Image;

            // "link" is what the builder writes when a creator pastes a URL
            // instead of uploading, and the control that produces it is labelled
            // "add video". The path is already absolute, which MarkerMediaUrl
            // passes through untouched.
            if (string.Equals(kind, "link", StringComparison.OrdinalIgnoreCase))
                return MarkerMediaKind.Video;

            return MarkerMediaKind.Unknown;
        }
    }

    public bool IsUsable
    {
        get { return Kind != MarkerMediaKind.Unknown && !string.IsNullOrWhiteSpace(path); }
    }

    public string Title
    {
        get
        {
            string written = title != null ? title.Text : null;
            if (!string.IsNullOrWhiteSpace(written))
                return written;

            return AppStrings.Get(Kind == MarkerMediaKind.Audio ? "piece.listen" : "piece.watch");
        }
    }

    /// <summary>m:ss, or empty when the portal did not record a length.</summary>
    public string Duration
    {
        get
        {
            if (seconds <= 0)
                return string.Empty;

            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }
    }

    /// <summary>
    /// Rounded to whole megabytes. The number exists to answer "is this going to
    /// cost me", and a reader deciding that does not need a second decimal place.
    /// </summary>
    public string Size
    {
        get
        {
            if (bytes <= 0L)
                return string.Empty;

            if (bytes < 1024L * 1024L)
                return Mathf.Max(1, Mathf.RoundToInt(bytes / 1024f)) + " KB";

            return Mathf.Max(1, Mathf.RoundToInt(bytes / (1024f * 1024f))) + " MB";
        }
    }
}

/// <summary>
/// One hint on a marker, and whether this team has paid for it.
///
/// The body arrives only once it has been revealed. A hint that travels
/// unrevealed is a hint that cost nothing - anybody with a proxy would read the
/// whole ladder without spending a point.
/// </summary>
[Serializable]
public class MarkerHint
{
    public string id;
    public int ord;

    [Tooltip("Points this costs to reveal. Zero is a free hint, which is a " +
             "deliberate thing a creator may want for a first nudge.")]
    public int cost;

    public bool revealed;

    [Tooltip("Null until revealed.")]
    public LocalisedText body;

    public bool HasText
    {
        get { return revealed && body != null && body.HasText; }
    }
}

public enum MarkerTaskKind
{
    None,
    Choice,
    Open
}

/// <summary>
/// What happens when the reader gets it wrong. Chosen by whoever made the trail,
/// not by us - which is why it travels with the marker.
/// </summary>
public enum WrongAnswerRule
{
    /// <summary>Ask again. Costs nothing and needs nothing behind it.</summary>
    Retry,

    /// <summary>Locked out for a while. Needs a server-held clock.</summary>
    Lock,

    /// <summary>Points come off. Needs a server-held score.</summary>
    Deduct,

    /// <summary>One attempt, then the marker is done. Needs a server-held record.</summary>
    Skip
}

/// <summary>
/// The question on a marker: everything the phone needs in order to ask, and
/// nothing it could use to answer.
/// </summary>
[Serializable]
public class MarkerTask
{
    [Tooltip("\"choice\" for a set of options, \"open\" for a typed answer.")]
    public string kind;

    public LocalisedText prompt;

    [Tooltip("Choice tasks only. Order is the order they are shown in.")]
    public LocalisedText[] options;

    [Tooltip("Awarded on a correct answer. Zero is a question with no score, which " +
             "is normal on a guided trail.")]
    public int points;

    [Tooltip("retry | lock | deduct | skip.")]
    public string on_wrong;

    [Tooltip("Lock rule only: how long the reader waits.")]
    public int lock_seconds;

    [Tooltip("Deduct rule only: how many points come off.")]
    public int deduct;

    public MarkerTaskKind Kind
    {
        get
        {
            if (string.Equals(kind, "choice", StringComparison.OrdinalIgnoreCase))
                return MarkerTaskKind.Choice;

            // "exact" and "free" are the server's two typed-answer kinds and both
            // draw the same thing here: a box to type in and a button. What
            // separates them is who marks it - exact is compared against a list
            // inside check_answer, free comes back pending_review for a person -
            // and that difference arrives in the verdict, not in advance.
            //
            // "open" is the name MARKER-CONTENT.md gave this and nothing has ever
            // sent it. Kept because a document somewhere still says it.
            if (string.Equals(kind, "exact", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "free", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "open", StringComparison.OrdinalIgnoreCase))
            {
                return MarkerTaskKind.Open;
            }

            return MarkerTaskKind.None;
        }
    }

    /// <summary>
    /// A choice task with no options is not a question, it is a bug in the portal.
    /// Treated as absent so the sheet shows the marker's text rather than an empty
    /// list with a Check button under it.
    /// </summary>
    public bool IsUsable
    {
        get
        {
            if (Kind == MarkerTaskKind.None)
                return false;

            if (prompt == null || !prompt.HasText)
                return false;

            return Kind != MarkerTaskKind.Choice || (options != null && options.Length > 0);
        }
    }

    public string Prompt
    {
        get { return prompt != null ? prompt.Text : string.Empty; }
    }

    public WrongAnswerRule Rule
    {
        get
        {
            if (string.Equals(on_wrong, "lock", StringComparison.OrdinalIgnoreCase))
                return WrongAnswerRule.Lock;

            if (string.Equals(on_wrong, "deduct", StringComparison.OrdinalIgnoreCase))
                return WrongAnswerRule.Deduct;

            if (string.Equals(on_wrong, "skip", StringComparison.OrdinalIgnoreCase))
                return WrongAnswerRule.Skip;

            // Retry is the default on purpose. A marker whose rule is missing or
            // misspelled should be gentle rather than lock somebody out.
            return WrongAnswerRule.Retry;
        }
    }

    public string OptionAt(int index)
    {
        if (options == null || index < 0 || index >= options.Length)
            return string.Empty;

        return options[index] != null ? options[index].Text : string.Empty;
    }

    public int OptionCount
    {
        get { return options != null ? options.Length : 0; }
    }
}

/// <summary>Where uploaded media actually lives.</summary>
public static class MarkerMediaUrl
{
    /// <summary>
    /// The bucket the portal uploads into. Public-read: the paths are unguessable
    /// enough for a school trail and a signed URL per file would mean a round trip
    /// before every play, on a phone that may be on one bar.
    /// </summary>
    public const string Bucket = "marker-media";

    public static string For(MarkerMedia media)
    {
        if (media == null || string.IsNullOrWhiteSpace(media.path))
            return null;

        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            return null;

        // Already a full URL - a creator who pasted a link rather than uploading.
        if (media.path.Contains("://"))
            return media.path;

        return config.Endpoint("/storage/v1/object/public/" + Bucket + "/" +
                               media.path.TrimStart('/'));
    }
}
