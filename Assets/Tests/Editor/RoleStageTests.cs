using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The A5 stage's pure parts: which stage an answer shows, the scene index,
/// the skies, the preview walk, the glyph table and the keyframe curves.
/// Checked against the website's role.ts, stage.ts and rolepick.ts.
/// </summary>
public class RoleStageTests
{
    [Test]
    public void StageKey_FollowsTheAnswer()
    {
        Assert.AreEqual("none", RoleStage.StageKey(null, null));
        Assert.AreEqual("none", RoleStage.StageKey(null, "teacher"));
        Assert.AreEqual("none", RoleStage.StageKey("admin", "teacher"));
        Assert.AreEqual("none", RoleStage.StageKey("creator", null));
        Assert.AreEqual("none", RoleStage.StageKey("creator", "pilot"));

        Assert.AreEqual("viewer", RoleStage.StageKey("viewer", null));
        Assert.AreEqual("viewer", RoleStage.StageKey("viewer", "teacher"));

        Assert.AreEqual("teacher", RoleStage.StageKey("creator", "teacher"));
        Assert.AreEqual("guide", RoleStage.StageKey("creator", "guide"));
        Assert.AreEqual("broker", RoleStage.StageKey("creator", "broker"));
        Assert.AreEqual("other", RoleStage.StageKey("creator", "other"));
    }

    [Test]
    public void KeyIndex_IsTheSceneOrder()
    {
        Assert.AreEqual(0, RoleStage.KeyIndex("guide"));
        Assert.AreEqual(1, RoleStage.KeyIndex("teacher"));
        Assert.AreEqual(2, RoleStage.KeyIndex("broker"));
        Assert.AreEqual(3, RoleStage.KeyIndex("other"));
        Assert.AreEqual(4, RoleStage.KeyIndex("viewer"));
        Assert.AreEqual(-1, RoleStage.KeyIndex("none"));
        Assert.AreEqual(-1, RoleStage.KeyIndex(null));
    }

    [TestCase("teacher", false, "D3ECD6")]
    [TestCase("teacher", true, "0F2A22")]
    [TestCase("guide", false, "F9E2BD")]
    [TestCase("guide", true, "2B2114")]
    [TestCase("broker", false, "CFE5F8")]
    [TestCase("broker", true, "0D2238")]
    [TestCase("other", false, "E3E8F0")]
    [TestCase("other", true, "1B2130")]
    [TestCase("viewer", false, "D0ECE2")]
    [TestCase("viewer", true, "0F2A24")]
    [TestCase("none", false, "ECEDF0")]
    [TestCase("none", true, "17191C")]
    public void Sky_IsTheWebsitesHex(string key, bool night, string hex)
    {
        Assert.AreEqual(hex, ColorUtility.ToHtmlStringRGB(RoleStage.Sky(key, night)));
    }

    [Test]
    public void Preview_WalksTheRolesInTheWebsitesOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "guide", "teacher", "broker", "viewer", "other" }, RoleStage.PreviewOrder);
        Assert.AreEqual(7f, RoleStage.LoopSeconds);

        foreach (string key in RoleStage.PreviewOrder)
            Assert.GreaterOrEqual(RoleStage.KeyIndex(key), 0, key);
    }

    [Test]
    public void Glyphs_AreRoleTsAndAllHaveAnIcon()
    {
        Assert.AreEqual("book", RoleStage.SignGlyph("teacher", 0));
        Assert.AreEqual("camera", RoleStage.SignGlyph("viewer", 2));
        Assert.IsNull(RoleStage.SignGlyph("none", 0));
        Assert.IsNull(RoleStage.SignGlyph("guide", 3));

        Assert.AreEqual("plus", RoleStage.MomentGlyph("teacher"));
        Assert.AreEqual("pin", RoleStage.MomentGlyph("guide"));
        Assert.AreEqual("check", RoleStage.MomentGlyph("broker"));
        Assert.AreEqual("pin", RoleStage.MomentGlyph("other"));
        Assert.AreEqual("arrow", RoleStage.MomentGlyph("viewer"));
        Assert.IsNull(RoleStage.MomentGlyph("none"));

        Assert.AreEqual("ask", RoleStage.SealGlyph(null, null));
        Assert.AreEqual("pencil", RoleStage.SealGlyph("creator", null));
        Assert.AreEqual("broker", RoleStage.SealGlyph("creator", "broker"));
        Assert.AreEqual("viewer", RoleStage.SealGlyph("viewer", "teacher"));

        foreach (string key in RoleStage.Keys)
        {
            for (int i = 0; i < 3; i++)
                CollectionAssert.Contains(RoleStage.IconNames, RoleStage.SignGlyph(key, i));
            CollectionAssert.Contains(RoleStage.IconNames, RoleStage.MomentGlyph(key));
            CollectionAssert.Contains(RoleStage.IconNames, RoleStage.SealGlyph("creator", key == "viewer" ? null : key));
        }
    }

    [Test]
    public void EveryStageHasItsSignsAndMoment_InBothLanguages()
    {
        foreach (AppLanguage language in new[] { AppLanguage.English, AppLanguage.Bulgarian })
        {
            foreach (string key in RoleStage.Keys)
            {
                foreach (string part in new[] { "sign1", "sign2", "sign3", "moment" })
                {
                    string id = "stage." + key + "." + part;
                    Assert.AreNotEqual(id, AppStrings.GetIn(language, id), id);
                }
            }
        }
    }

    [Test]
    public void Keyframes_MatchStageCss()
    {
        float y, alpha;

        RoleStage.Drop(0f, out y, out alpha);
        Assert.AreEqual(-30f, y, 1e-4f);
        Assert.AreEqual(0f, alpha, 1e-4f);
        RoleStage.Drop(0.5f, out y, out alpha);
        Assert.AreEqual(0f, y, 1e-4f);
        Assert.AreEqual(1f, alpha, 1e-4f);

        Assert.AreEqual(0f, RoleStage.Draw(0.2f), 1e-4f);
        Assert.AreEqual(1f, RoleStage.Draw(0.8f), 1e-4f);

        RoleStage.Pop(0.3f, out y, out alpha);
        Assert.AreEqual(0f, alpha, 1e-4f);
        RoleStage.Pop(0.7f, out y, out alpha);
        Assert.AreEqual(1f, alpha, 1e-4f);

        Assert.AreEqual(0.35f, RoleStage.Twinkle(0f), 1e-4f);
        Assert.AreEqual(1f, RoleStage.Twinkle(0.5f), 1e-4f);
    }
}
