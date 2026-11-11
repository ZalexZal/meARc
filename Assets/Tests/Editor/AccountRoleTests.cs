using System;
using NUnit.Framework;

/// <summary>
/// The role of an account (creator or viewer): how the profile read is
/// understood, what the profile PATCH sends, and what sign-up sends as user
/// metadata. Checked here rather than against the live service.
/// </summary>
public class AccountRoleTests
{
    [Test]
    public void OnlyCreatorAndViewerAreValid()
    {
        Assert.IsTrue(AccountRole.IsValid("creator"));
        Assert.IsTrue(AccountRole.IsValid("viewer"));

        Assert.IsFalse(AccountRole.IsValid(null));
        Assert.IsFalse(AccountRole.IsValid(""));
        Assert.IsFalse(AccountRole.IsValid("Creator"));
        Assert.IsFalse(AccountRole.IsValid("admin"));
        Assert.IsFalse(AccountRole.IsValid(" viewer"));
    }

    [Test]
    public void TryRead_ViewerWithoutWork()
    {
        string role, work;

        Assert.IsTrue(AccountRole.TryRead("[{\"role\":\"viewer\",\"work\":null}]", out role, out work));
        Assert.AreEqual("viewer", role);
        Assert.IsNull(work);
    }

    [Test]
    public void TryRead_CreatorWithWork()
    {
        string role, work;

        Assert.IsTrue(AccountRole.TryRead("[{\"role\":\"creator\",\"work\":\"teacher\"}]", out role, out work));
        Assert.AreEqual("creator", role);
        Assert.AreEqual("teacher", work);
    }

    [Test]
    public void TryRead_OldAccountHasWorkButNoRole()
    {
        string role, work;

        // The one case that leads to the question; the work is kept to preselect.
        Assert.IsTrue(AccountRole.TryRead("[{\"role\":null,\"work\":\"guide\"}]", out role, out work));
        Assert.IsNull(role);
        Assert.AreEqual("guide", work);

        Assert.IsTrue(AccountRole.TryRead("[{\"role\":null,\"work\":null}]", out role, out work));
        Assert.IsNull(role);
        Assert.IsNull(work);
    }

    [Test]
    public void TryRead_UnknownValuesCountAsUnanswered()
    {
        string role, work;

        Assert.IsTrue(AccountRole.TryRead("[{\"role\":\"admin\",\"work\":\"astronaut\"}]", out role, out work));
        Assert.IsNull(role);
        Assert.IsNull(work);
    }

    [Test]
    public void TryRead_NoRowOrGarbageIsUnreadable()
    {
        string role, work;

        Assert.IsFalse(AccountRole.TryRead("[]", out role, out work));
        Assert.IsNull(role);
        Assert.IsNull(work);

        Assert.IsFalse(AccountRole.TryRead("{\"message\":\"JWT expired\"}", out role, out work));
        Assert.IsFalse(AccountRole.TryRead("", out role, out work));
        Assert.IsFalse(AccountRole.TryRead(null, out role, out work));
        Assert.IsFalse(AccountRole.TryRead("not json", out role, out work));
        Assert.IsFalse(AccountRole.TryRead("[not json", out role, out work));
    }

    [Test]
    public void RolePatchBody_ViewerSendsNoWork()
    {
        Assert.AreEqual("{\"role\":\"viewer\"}", SupabaseAuth.RolePatchBody("viewer", null));
        Assert.AreEqual("{\"role\":\"viewer\"}", SupabaseAuth.RolePatchBody("viewer", "guide"));
        Assert.AreEqual("{\"role\":\"viewer\"}", SupabaseAuth.RolePatchBody("viewer", "nonsense"));
    }

    [Test]
    public void RolePatchBody_CreatorCarriesWork()
    {
        Assert.AreEqual("{\"role\":\"creator\",\"work\":\"broker\"}",
            SupabaseAuth.RolePatchBody("creator", "broker"));
    }

    [Test]
    public void RolePatchBody_RefusesInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody(null, "guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody("admin", "guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody("creator", null));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody("creator", "Guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody("creator", "guide\",\"role\":\"admin"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.RolePatchBody("viewer\"}", null));
    }

    [Test]
    public void SignUpBody_ViewerHasNoWorkKey()
    {
        Assert.AreEqual(
            "{\"email\":\"a@b.bg\",\"password\":\"secret123\"," +
            "\"data\":{\"display_name\":\"Иво\",\"role\":\"viewer\"}}",
            SupabaseAuth.SignUpBody("a@b.bg", "secret123", " Иво ", "viewer", null));

        // A work handed in with a viewer is ignored, not sent.
        string body = SupabaseAuth.SignUpBody("a@b.bg", "secret123", null, "viewer", "guide");
        Assert.AreEqual(
            "{\"email\":\"a@b.bg\",\"password\":\"secret123\",\"data\":{\"role\":\"viewer\"}}", body);
        StringAssert.DoesNotContain("work", body);
    }

    [Test]
    public void SignUpBody_CreatorCarriesRoleAndWork()
    {
        Assert.AreEqual(
            "{\"email\":\"a@b.bg\",\"password\":\"secret123\"," +
            "\"data\":{\"display_name\":\"Мария\",\"role\":\"creator\",\"work\":\"guide\"}}",
            SupabaseAuth.SignUpBody("a@b.bg", "secret123", "Мария", "creator", "guide"));
    }

    [Test]
    public void SignUpBody_RefusesInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", null, "guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "admin", "guide"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "creator", null));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "creator", "nope"));
        Assert.Throws<ArgumentException>(() => SupabaseAuth.SignUpBody("a@b.bg", "secret123", "A", "viewer\",\"x\":\"", null));
    }
}
