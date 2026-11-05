using System.Collections;
using System.Reflection;
using Verse;

namespace CargoElevator;

/// <summary>
/// Labels DeepRim layers ("70m Mines") by reading DeepRim's classes through reflection.
/// Nothing here references DeepRim at compile time, so the mod works without it.
/// </summary>
public class DeepRimLabelProvider : ILevelLabelProvider
{
    private const string PackageId = "Mlie.DeepRim";

    /// <summary>DeepRim shows a layer's depth as depth * 10 metres.</summary>
    private const int MetresPerDepthLevel = 10;

    private readonly System.Type layerParentType;     // DeepRim.UndergroundMapParent (the world object of a layer)
    private readonly System.Type layerManagerType;    // DeepRim.UndergroundManager (map component that knows all layers)
    private readonly FieldInfo depthField;            // UndergroundMapParent.depth (int)
    private readonly FieldInfo layersStateField;      // UndergroundManager.layersState (Dictionary<int, UndergroundMapParent>)
    private readonly MethodInfo getLayerNameMethod;   // UndergroundManager.GetLayerName(int) -> string, "" when unnamed

    public string ProviderName => "DeepRim";

    public string Tag => "CargoElevator_TagDeepRim".Translate();

    private DeepRimLabelProvider(System.Type layerParentType, System.Type layerManagerType, FieldInfo depthField,
        FieldInfo layersStateField, MethodInfo getLayerNameMethod)
    {
        this.layerParentType = layerParentType;
        this.layerManagerType = layerManagerType;
        this.depthField = depthField;
        this.layersStateField = layersStateField;
        this.getLayerNameMethod = getLayerNameMethod;
    }

    /// <summary>Returns a provider, or null (with a log line) if DeepRim is missing or its members can't be resolved.</summary>
    public static DeepRimLabelProvider TryCreate()
    {
        if (!ModsConfig.IsActive(PackageId))
        {
            ElevatorUtil.Log("DeepRim labels: disabled (mod not active)");
            return null;
        }

        System.Type layerParentType = GenTypes.GetTypeInAnyAssembly("DeepRim.UndergroundMapParent");
        System.Type layerManagerType = GenTypes.GetTypeInAnyAssembly("DeepRim.UndergroundManager");
        FieldInfo depthField = layerParentType?.GetField("depth", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo layersStateField = layerManagerType?.GetField("layersState", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo getLayerNameMethod = layerManagerType?.GetMethod("GetLayerName", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);

        bool membersResolved = depthField != null && depthField.FieldType == typeof(int)
            && layersStateField != null && typeof(IDictionary).IsAssignableFrom(layersStateField.FieldType)
            && getLayerNameMethod != null && getLayerNameMethod.ReturnType == typeof(string);
        if (!membersResolved)
        {
            ElevatorUtil.LogWarn("DeepRim detected but its layer data could not be read; using generic labels.");
            return null;
        }

        ElevatorUtil.Log("DeepRim labels: enabled");
        return new DeepRimLabelProvider(layerParentType, layerManagerType, depthField, layersStateField, getLayerNameMethod);
    }

    public bool TryGetLabel(Map map, out string label, out int sortValue)
    {
        label = null;
        sortValue = 0;

        // Only layers are labelled; the surface map is not a DeepRim layer.
        if (map.Parent == null || !layerParentType.IsInstanceOfType(map.Parent))
        {
            return false;
        }

        int depth = (int)depthField.GetValue(map.Parent);
        string layerName = FindLayerName(map, depth);
        int depthInMetres = depth * MetresPerDepthLevel;

        label = layerName.NullOrEmpty()
            ? "CargoElevator_DeepRimDepth".Translate(depthInMetres)
            : "CargoElevator_DeepRimDepthNamed".Translate(depthInMetres, layerName);
        sortValue = depth;
        return true;
    }

    /// <summary>
    /// Layer names live in the UndergroundManager of the map that holds the layer's shaft. That is some map of the
    /// same level group that is not itself a layer, so look for the manager that lists this layer's world object.
    /// </summary>
    private string FindLayerName(Map layerMap, int depth)
    {
        foreach (Map groupMap in Find.Maps)
        {
            if (groupMap == layerMap || layerParentType.IsInstanceOfType(groupMap.Parent) || !ElevatorUtil.SameGroup(layerMap, groupMap))
            {
                continue;
            }

            for (int componentIndex = 0; componentIndex < groupMap.components.Count; componentIndex++)
            {
                MapComponent component = groupMap.components[componentIndex];
                if (!layerManagerType.IsInstanceOfType(component))
                {
                    continue;
                }

                var layersState = (IDictionary)layersStateField.GetValue(component);
                foreach (object layerParent in layersState.Values)
                {
                    if (layerParent == layerMap.Parent)
                    {
                        return (string)getLayerNameMethod.Invoke(component, new object[] { depth });
                    }
                }
            }
        }

        return null;
    }
}
