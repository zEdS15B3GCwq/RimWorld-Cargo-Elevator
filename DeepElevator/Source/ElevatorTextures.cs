using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>Textures used by gizmos. Loaded once at startup, never inside UI loops.</summary>
[StaticConstructorOnStartup]
public static class ElevatorTextures
{
    /// <summary>Placeholder vanilla icon for the "Connect to..." gizmo (a proper icon comes later).</summary>
    public static readonly Texture2D Connect =
        ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", reportFailure: false) ?? BaseContent.BadTex;

    /// <summary>
    /// Icon of the loader's Enter command (the vanilla transport pod "load" icon). One cached reference, which the
    /// loader also uses to recognise that command among the portal's gizmos.
    /// </summary>
    public static readonly Texture2D LoaderEnter =
        ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", reportFailure: false) ?? BaseContent.BadTex;

    /// <summary>Placeholder vanilla icon for the loader's "Transfer items to elevator" toggle.</summary>
    public static readonly Texture2D LoaderTransfer = TexCommand.SelectCarriedThing;

    /// <summary>Placeholder vanilla icon for the loader's priority button, used for any level that has no icon of its own.</summary>
    public static readonly Texture2D LoaderPriority = TexButton.ReorderUp;

    // One icon per priority level, in the folder of the loader's texture: DeepElevatorLoader_Priority<Level>.png
    // (Low, Normal, Preferred, Important, Critical). A missing file falls back to the placeholder above.
    private const string LoaderPriorityIconFolder = "Things/Buildings/DeepElevator/DeepElevatorLoader_Priority";

    private static readonly Dictionary<StoragePriority, Texture2D> LoaderPriorityIcons = BuildLoaderPriorityIcons();

    private static Dictionary<StoragePriority, Texture2D> BuildLoaderPriorityIcons()
    {
        var icons = new Dictionary<StoragePriority, Texture2D>();
        foreach (StoragePriority level in System.Enum.GetValues(typeof(StoragePriority)))
        {
            icons[level] = ContentFinder<Texture2D>.Get(LoaderPriorityIconFolder + level, reportFailure: false) ?? LoaderPriority;
        }
        return icons;
    }

    /// <summary>The loader's priority button icon for <paramref name="level"/>.</summary>
    public static Texture2D LoaderPriorityIcon(StoragePriority level)
        => LoaderPriorityIcons.TryGetValue(level, out Texture2D icon) ? icon : LoaderPriority;

    /// <summary>Placeholder vanilla icon (magnifier) for the "Go to partner" gizmo.</summary>
    public static readonly Texture2D GoToPartner =
        ContentFinder<Texture2D>.Get("UI/Widgets/Search", reportFailure: false) ?? BaseContent.BadTex;
}
