using System;
using NUnit.Framework;

/// <summary>
/// The request bodies for a person's line of work (Task 14): what sign-up sends
/// as user metadata, what the profile PATCH sends, and how the profile read is
/// understood. Checked here rather than against the live service, which would
/// mean making real accounts.
/// </summary>
public class LineOfWorkTests
{
    [Test]
    public void FourValidKeys_AndNothingElse()
    {
        CollectionAssert.AreEqual(new[] { "broker", "teacher", "guide", "other" }, LineOfWork.Keys);

        foreach (string key in LineOfWork.Keys)
            Assert.IsTrue(LineOfWork.IsValid(key), key);

        Assert.IsFalse(LineOfWork.IsValid(null));
        Assert.IsFalse(LineOfWork.IsValid(""));
        Assert.IsFalse(LineOfWork.IsValid("Teacher"));
        Assert.IsFalse(LineOfWork.IsValid("admin"));
        Assert.IsFalse(LineOfWork.IsValid(" guide"));
    }

    [Test]
    public void SignUpBody_CarriesWorkInData()
    {
        Assert.AreEqual(
            "{\"email\":\"a@b.bg\",\"password\":\"secret123\"," +
            "\"data\":{\"display_name\":\"Мария\",\"role\":\"creator\",\"work\":\"guide\"}}",
            SupabaseAuth.SignUpBody("a@b.bg", "secret123", "  Мария ", "creator", "guide"));
    }

    [Test]
    public void SignUpBody_WorkWithoutAName()
    {
        Assert.AreEqual(
            "{\"email\":\"a@b.bg\",\"password\":\"secret123\",\"data\":{\"role\":\"creator\",\"work\":\"broker\"}}",
            SupabaseAuth.SignUpBody("a@b.bg", "secret123", "   ", "creator", "broker"));
    }

    [Test]
    public void SignUpBody_EscapesEveryField()
    {
        string body = SupabaseAuth.SignUpBody("x\"y@b.bg", "p\\a\"s\ns", "Ана \"Учителката\"\t", "creator", "teacher");

        Assert.AreEqual(
            "{\"email\":\"x\\\"y@b.bg\",\"password\":\"p\\\\a\\\"s\\ns\"," +
            "\"data\":{\"display_name\":\"Ана \\\"Учителката\\\"\",\"role\":\"creator\",\"work\":\"teacher\"}}",
            body);
    }

    [Test]
    public void SignUpBody_ControlCharactersCannotBreakTheJson()
    {
        string body = SupabaseAuth.SignUpBody("a@b.bg", "line1\r\nline2\u0001", null, "creator", "other");
        StringAssert.Contains("line1\\r\\nline2\\u0001", body);
        StringAssert.DoesNotContain("\n", body);
    }

    [Test]
    public void SignUpBody_RefusesAnInvalidWork()
    {
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "creator", null));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "creator", ""));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "creator", "\",\"role\":\"admin"));
    }

    [Test]
    public void WorkPatchBody_IsJustTheColumn()
    {
        Assert.AreEqual("{\"work\":\"guide\"}", SupabaseAuth.WorkPatchBody("guide"));
        Assert.AreEqual("{\"work\":\"other\"}", SupabaseAuth.WorkPatchBody("other"));
    }

    [Test]
    public void WorkPatchBody_RefusesAnInvalidWork()
    {
        Assert.Throws<ArgumentException>(() => SupabaseAuth.WorkPatchBody(null));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.WorkPatchBody("Guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.WorkPatchBody("guide\""));
    }

    [Test]
    public void PatchLanded_OnlyWhenARowCameBack()
    {
        Assert.IsTrue(LineOfWork.PatchLanded("[{\"id\":\"u1\",\"work\":\"guide\"}]"));
        Assert.IsTrue(LineOfWork.PatchLanded(" [ {\"work\":\"guide\"} ]\n"));

        // RLS filtered it to nothing: the server says 200 and changed nothing.
        Assert.IsFalse(LineOfWork.PatchLanded("[]"));
        Assert.IsFalse(LineOfWork.PatchLanded(" [ ] "));
        Assert.IsFalse(LineOfWork.PatchLanded(""));
        Assert.IsFalse(LineOfWork.PatchLanded(null));
        Assert.IsFalse(LineOfWork.PatchLanded("{\"message\":\"denied\"}"));
    }

    [Test]
    public void ReadReply_SetWorkNullWorkAndFailures()
    {
        string work;

        Assert.IsTrue(LineOfWork.TryRead("[{\"work\":\"teacher\"}]", out work));
        Assert.AreEqual("teacher", work);

        // Never asked: the one case that leads to the question.
        Assert.IsTrue(LineOfWork.TryRead("[{\"work\":null}]", out work));
        Assert.IsNull(work);

        // A value the app does not know is treated as unanswered.
        Assert.IsTrue(LineOfWork.TryRead("[{\"work\":\"astronaut\"}]", out work));
        Assert.IsNull(work);

        // No row, an error body, or nothing at all: unreadable, so nobody is asked.
        Assert.IsFalse(LineOfWork.TryRead("[]", out work));
        Assert.IsFalse(LineOfWork.TryRead("{\"message\":\"JWT expired\"}", out work));
        Assert.IsFalse(LineOfWork.TryRead("", out work));
        Assert.IsFalse(LineOfWork.TryRead(null, out work));
        Assert.IsFalse(LineOfWork.TryRead("not json", out work));
    }
}
