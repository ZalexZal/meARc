using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A city the database could not place has lat/lng null (or the whole city is
/// null). Rows are parsed the way TrailService parses them - wrapped in
/// {"items":...} and read by JsonUtility - and must survive that. JsonUtility reads null
/// as 0 for a double and as an empty object for a null object, so no cleaning is needed.
/// </summary>
public class CityJsonTests
{
    [System.Serializable] class ListedList { public TrailService.Listed[] items; }
    [System.Serializable] class WorldRow { public string id; public string name; public TrailService.CityJson city; }
    [System.Serializable] class MembershipRow { public string role; public WorldRow worlds; }
    [System.Serializable] class MembershipList { public MembershipRow[] items; }

    [Test]
    public void Listed_CityWithNullLatLng_StillParses()
    {
        string json = "[{\"id\":\"a\",\"name\":\"Trail\",\"stop_count\":3," +
                      "\"city\":{\"key\":\"x\",\"bg\":\"X\",\"en\":\"X\",\"lat\":null,\"lng\":null}}]";

        TrailService.Listed[] rows = JsonUtility.FromJson<ListedList>(
            "{\"items\":" + json + "}").items;

        Assert.AreEqual(1, rows.Length);
        Assert.AreEqual("a", rows[0].id);
        Assert.AreEqual("x", rows[0].city.key);
        Assert.AreEqual("X", rows[0].city.bg);
        Assert.AreEqual(0d, rows[0].city.lat);
        Assert.AreEqual(0d, rows[0].city.lng);
    }

    [Test]
    public void Membership_CityWithNullLatLng_StillParses()
    {
        string json = "[{\"role\":\"owner\",\"worlds\":{\"id\":\"w\",\"name\":\"N\"," +
                      "\"city\":{\"key\":\"x\",\"bg\":\"X\",\"en\":\"X\",\"lat\":null,\"lng\":null}}}]";

        MembershipRow[] rows = JsonUtility.FromJson<MembershipList>(
            "{\"items\":" + json + "}").items;

        Assert.AreEqual(1, rows.Length);
        Assert.AreEqual("w", rows[0].worlds.id);
        Assert.AreEqual("x", rows[0].worlds.city.key);
    }

    [Test]
    public void WholeCityNull_StillParses_AndHasNoName()
    {
        string json = "[{\"id\":\"a\",\"name\":\"Trail\",\"city\":null}," +
                      "{\"id\":\"b\",\"name\":\"Other\",\"city\":{\"key\":\"y\",\"bg\":\"Й\",\"en\":\"Y\",\"lat\":1.5,\"lng\":2.5}}]";

        TrailService.Listed[] rows = JsonUtility.FromJson<ListedList>(
            "{\"items\":" + json + "}").items;

        Assert.AreEqual(2, rows.Length);
        Assert.AreEqual("", rows[0].CityName());
        Assert.AreEqual("y", rows[1].city.key);
        Assert.AreEqual(1.5d, rows[1].city.lat);
    }
}
