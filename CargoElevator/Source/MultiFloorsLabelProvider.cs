using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace CargoElevator;

/// <summary>
/// Labels MultiFloors levels ("B1", "G") by reading MultiFloors' classes through reflection.
/// Nothing here references MultiFloors at compile time, so the mod works without it.
/// </summary>
public class MultiFloorsLabelProvider : ILevelLabelProvider
{
    private const string PackageId = "telardo.MultiFloors";

    /// <summary>The level number of the map a stack starts from.</summary>
    private const int GroundLevel = 0;

    private readonly System.Type levelControllerType;       // MultiFloors.MF_LevelMapComp (map component holding a level stack)
    private readonly PropertyInfo levelMapsProperty;        // MF_LevelMapComp.MapByLevel (Dictionary<int, Map>)
    private readonly MethodInfo currentLevelStringMethod;   // LevelUtility.CurrentLevelString(int level, bool addParenthesis) -> string

    public string ProviderName => "MultiFloors";

    public string Tag => "CargoElevator_TagMultiFloors".Translate();

    private MultiFloorsLabelProvider(System.Type levelControllerType, PropertyInfo levelMapsProperty, MethodInfo currentLevelStringMethod)
    {
        this.levelControllerType = levelControllerType;
        this.levelMapsProperty = levelMapsProperty;
        this.currentLevelStringMethod = currentLevelStringMethod;
    }

    /// <summary>Returns a provider, or null (with a log line) if MultiFloors is missing or its members can't be resolved.</summary>
    public static MultiFloorsLabelProvider TryCreate()
    {
        if (!ModsConfig.IsActive(PackageId))
        {
            ElevatorUtil.Log("MultiFloors labels: disabled (mod not active)");
            return null;
        }

        System.Type levelControllerType = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp");
        System.Type levelUtilityType = GenTypes.GetTypeInAnyAssembly("MultiFloors.LevelUtility");
        PropertyInfo levelMapsProperty = levelControllerType?.GetProperty("MapByLevel", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo currentLevelStringMethod = levelUtilityType?.GetMethod("CurrentLevelString",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(bool) }, null);

        bool membersResolved = levelMapsProperty != null && typeof(Dictionary<int, Map>).IsAssignableFrom(levelMapsProperty.PropertyType)
            && currentLevelStringMethod != null && currentLevelStringMethod.ReturnType == typeof(string);
        if (!membersResolved)
        {
            ElevatorUtil.LogWarn("MultiFloors detected but its level data could not be read; using generic labels.");
            return null;
        }

        ElevatorUtil.Log("MultiFloors labels: enabled");
        return new MultiFloorsLabelProvider(levelControllerType, levelMapsProperty, currentLevelStringMethod);
    }

    public bool TryGetLabel(Map map, out string label, out int sortValue)
    {
        label = null;
        sortValue = 0;

        // The level stack is kept by the controller of the stack's ground map, so search the whole group
        // (including this map) for a stack that lists it. A map without a stack of 2+ levels gets no label.
        foreach (Map groupMap in Find.Maps)
        {
            if (groupMap != map && !ElevatorUtil.SameGroup(map, groupMap))
            {
                continue;
            }

            Dictionary<int, Map> mapsByLevel = GetMapsByLevel(groupMap);
            if (mapsByLevel == null)
            {
                continue;
            }

            foreach (KeyValuePair<int, Map> levelEntry in mapsByLevel)
            {
                if (levelEntry.Value == map)
                {
                    // Level 0 is the map a stack starts from (the surface, a DeepRim layer, ...). MultiFloors calls
                    // all of them "G", which says nothing about the map, so the map keeps its own label instead.
                    if (levelEntry.Key == GroundLevel)
                    {
                        return false;
                    }

                    sortValue = levelEntry.Key;
                    label = (string)currentLevelStringMethod.Invoke(null, new object[] { levelEntry.Key, false });
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The level stack stored on a map, or null if the map has no MultiFloors controller.</summary>
    private Dictionary<int, Map> GetMapsByLevel(Map map)
    {
        for (int componentIndex = 0; componentIndex < map.components.Count; componentIndex++)
        {
            MapComponent component = map.components[componentIndex];
            if (levelControllerType.IsInstanceOfType(component))
            {
                return (Dictionary<int, Map>)levelMapsProperty.GetValue(component);
            }
        }

        return null;
    }
}
