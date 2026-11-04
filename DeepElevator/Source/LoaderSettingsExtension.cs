using Verse;

namespace DeepElevator;

/// <summary>Tunables for the transport loader, set in the ThingDef.</summary>
public class LoaderSettingsExtension : DefModExtension
{
    // The look while an order is in progress: a full sprite on the same canvas as the base sprite, drawn over it.
    // Null = the loader looks the same all the time.
    public string loadingTexPath;
}
