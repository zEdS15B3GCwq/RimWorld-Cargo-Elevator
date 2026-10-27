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

    /// <summary>Placeholder vanilla icon (magnifier) for the "Go to partner" gizmo.</summary>
    public static readonly Texture2D GoToPartner =
        ContentFinder<Texture2D>.Get("UI/Widgets/Search", reportFailure: false) ?? BaseContent.BadTex;
}
