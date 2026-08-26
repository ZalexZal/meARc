using UnityEngine;

public class BuildingDatabaseTest : MonoBehaviour
{
    void Start()
    {
        BuildingData[] allBuildings = Resources.LoadAll<BuildingData>("BuildingData");

        Debug.Log($"Found {allBuildings.Length} building(s) in Resources.");

        foreach (BuildingData building in allBuildings)
        {
            string coords = (building.latitude == 0d && building.longitude == 0d)
                ? "NO COORDINATES SET"
                : $"Lat: {building.latitude}, Long: {building.longitude}";

            Debug.Log($"{building.buildingName} — {coords}");
        }
    }
}
