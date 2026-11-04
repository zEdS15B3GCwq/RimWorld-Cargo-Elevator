using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>Textures used by gizmos. Loaded once at startup, never inside UI loops.</summary>
[StaticConstructorOnStartup]
public static class ElevatorTextures
{
    // The mod's own icons live in the folder of the building sprites. Each one falls back to a vanilla
    // icon when its file is missing, so a missing texture never shows the "bad texture" checkerboard.
    private const string IconFolder = "Things/Buildings/DeepElevator/";

    /// <summary>Loads a mod icon by file name (without extension), or returns <paramref name="fallback"/> if it is missing.</summary>
    private static Texture2D LoadIcon(string fileName, Texture2D fallback)
        => ContentFinder<Texture2D>.Get(IconFolder + fileName, reportFailure: false) ?? fallback;

    private static readonly Texture2D VanillaLoadTransporter =
        ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", reportFailure: false) ?? BaseContent.BadTex;

    /// <summary>The elevator's "Connect to..." gizmo.</summary>
    public static readonly Texture2D ElevatorConnect = LoadIcon("DeepElevator_ConnectToIcon", VanillaLoadTransporter);

    /// <summary>The loader's "Connect to..." gizmo.</summary>
    public static readonly Texture2D LoaderConnect = LoadIcon("DeepElevatorLoader_ConnectToIcon", VanillaLoadTransporter);

    /// <summary>
    /// "Unload" on the elevator and "Unload all" on the loader: the vanilla transport pod and shuttle unload icon,
    /// so both buildings look like the vanilla command.
    /// </summary>
    public static readonly Texture2D Unload = CompTransporter.CancelLoadCommandTex;

    /// <summary>The elevator's buffer toggles.</summary>
    public static readonly Texture2D ToggleFifo = LoadIcon("DeepElevator_FIFOIcon", TexCommand.HoldOpen);
    public static readonly Texture2D ToggleMerge = LoadIcon("DeepElevator_MergeIcon", TexCommand.GatherSpotActive);
    public static readonly Texture2D ToggleDrop = LoadIcon("DeepElevator_DropNearbyIcon", TexCommand.ForbidOff);
    public static readonly Texture2D ToggleInsert = LoadIcon("DeepElevator_InsertIcon", TexCommand.Install);

    /// <summary>
    /// Icon of the loader's Enter command (the vanilla transport pod "load" icon). One cached reference, which the
    /// loader also uses to recognise that command among the portal's gizmos.
    /// </summary>
    public static readonly Texture2D LoaderEnter = VanillaLoadTransporter;

    /// <summary>The loader's "Transfer items to elevator" toggle.</summary>
    public static readonly Texture2D LoaderTransfer = LoadIcon("DeepElevatorLoader_TransferToElevatorIcon", TexCommand.SelectCarriedThing);

    /// <summary>Placeholder vanilla icon for the loader's priority button, used for any level that has no icon of its own.</summary>
    public static readonly Texture2D LoaderPriority = TexButton.ReorderUp;

    // One icon per priority level, in the folder of the loader's texture: DeepElevatorLoader_Priority<Level>.png
    // (Low, Normal, Preferred, Important, Critical). A missing file falls back to the placeholder above.
    private const string LoaderPriorityIconPrefix = "DeepElevatorLoader_Priority";

    private static readonly Dictionary<StoragePriority, Texture2D> LoaderPriorityIcons = BuildLoaderPriorityIcons();

    private static Dictionary<StoragePriority, Texture2D> BuildLoaderPriorityIcons()
    {
        var icons = new Dictionary<StoragePriority, Texture2D>();
        foreach (StoragePriority level in System.Enum.GetValues(typeof(StoragePriority)))
        {
            icons[level] = LoadIcon(LoaderPriorityIconPrefix + level, LoaderPriority);
        }
        return icons;
    }

    /// <summary>The loader's priority button icon for <paramref name="level"/>.</summary>
    public static Texture2D LoaderPriorityIcon(StoragePriority level)
        => LoaderPriorityIcons.TryGetValue(level, out Texture2D icon) ? icon : LoaderPriority;

    /// <summary>The elevator's "Go to partner" gizmo (fallback: the vanilla magnifier).</summary>
    public static readonly Texture2D GoToPartner = LoadIcon("DeepElevator_GoToPartnerIcon",
        ContentFinder<Texture2D>.Get("UI/Widgets/Search", reportFailure: false) ?? BaseContent.BadTex);
}
