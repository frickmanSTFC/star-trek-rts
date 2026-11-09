using Godot;

namespace FinalFrontier;

/// <summary>
/// Game sounds (docs/sound-contract.md; agent S2 fills this in). Stub: silent.
/// </summary>
public static class Sfx
{
    /// <summary>A UI sound by name ("click", "hover", …) on the UI bus.</summary>
    public static void Ui(string name, float volumeDb = 0f, float pitch = 1f) { }

    /// <summary>A one-shot positional sound ("landing/touchdown") on the Effects bus.</summary>
    public static void At(string path, Vector3 position, float volumeDb = 0f, float pitch = 1f) { }

    /// <summary>A looping positional player under <paramref name="parent"/> that the caller drives; null while silent.</summary>
    public static AudioStreamPlayer3D? Loop(Node3D parent, string path) => null;

    /// <summary>The selection hum of a building or vehicle id.</summary>
    public static void Select(string id, bool vehicle) { }

    /// <summary>Hover/click sounds for every Button under <paramref name="root"/> (now and added later).</summary>
    public static void Wire(Control root) { }
}
