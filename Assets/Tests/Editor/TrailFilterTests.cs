using NUnit.Framework;

/// <summary>
/// The same cases as the website's fold/matches tests (tests/lab/unit, work.ts),
/// so the app's search and the site's search agree letter for letter.
/// </summary>
public class TrailFilterTests
{
    [Test]
    public void Fold_CaseAccentsCyrillic()
    {
        Assert.AreEqual("yambol", TrailFilter.Fold("Ямбол"));
        Assert.AreEqual("shtastie zhalto tsarkva", TrailFilter.Fold("  Щастие   ЖЪЛТО\tЦЪРКВА "));
        Assert.AreEqual("cafe munchen", TrailFilter.Fold("Café Münchën"));
        Assert.AreEqual("yordan yuli yacho chudo hlyab", TrailFilter.Fold("Йордан Юли Ячо Чудо Хляб"));
        Assert.AreEqual("", TrailFilter.Fold(""));
    }

    [Test]
    public void Fold_ShortIIsY_NotI()
    {
        // Transliterated before accents are stripped: stripping first turns й into и.
        Assert.AreEqual("y", TrailFilter.Fold("й"));
        Assert.AreEqual("y", TrailFilter.Fold("й"));
    }

    [Test]
    public void Fold_SoftSignDropped_HardSignIsA()
    {
        Assert.AreEqual("lo", TrailFilter.Fold("льо"));
        Assert.AreEqual("sabota", TrailFilter.Fold("събота"));
    }

    [Test]
    public void Fold_Null_IsEmpty()
    {
        Assert.AreEqual("", TrailFilter.Fold(null));
    }

    [Test]
    public void Matches_CityInEitherLanguage()
    {
        Assert.IsTrue(TrailFilter.Matches("yambol", "Безистен", "Ямбол", "Yambol"));
        Assert.IsTrue(TrailFilter.Matches("ямбол", "Безистен", "Ямбол", "Yambol"));
        Assert.IsTrue(TrailFilter.Matches("YAMBOL безистен", "Безистен", "Ямбол", "Yambol"));
        Assert.IsTrue(TrailFilter.Matches("yambol", "Безистен", "Ямбол", null));
    }

    [Test]
    public void Matches_EveryWordCounts_PartialWordsFine()
    {
        Assert.IsTrue(TrailFilter.Matches("ески джам", "Ески джамия", null, null));
        Assert.IsFalse(TrailFilter.Matches("ески мост", "Ески джамия", null, null));
    }

    [Test]
    public void Matches_NoMatch_EmptyQuery_MissingCity()
    {
        Assert.IsFalse(TrailFilter.Matches("sofia", "Безистен", "Ямбол", null));
        Assert.IsTrue(TrailFilter.Matches("", "Безистен", null, null));
        Assert.IsTrue(TrailFilter.Matches(null, "Безистен", null, null));
        Assert.IsTrue(TrailFilter.Matches("   ", "Безистен", null, null));
        Assert.IsTrue(TrailFilter.Matches("безистен", "Безистен", null, null));
    }
}
