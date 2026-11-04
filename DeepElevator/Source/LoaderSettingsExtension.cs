using Verse;

namespace DeepElevator;

/// <summary>Tunables for the transport loader, set in the ThingDef.</summary>
public class LoaderSettingsExtension : DefModExtension
{
    // The orange look (order in progress, or items waiting): a lights-only overlay on the same canvas as the base
    // sprite (transparent except the lamp pixels), drawn over it with a shader that ignores darkness.
    // Null = the loader looks the same all the time. The glow radius and colour are on the CompProperties_Glower.
    public string lightTexPath;
}
