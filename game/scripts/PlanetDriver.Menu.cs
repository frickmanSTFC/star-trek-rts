using Godot;

namespace FinalFrontier;

// The game's ties to the main menu (docs/menu-contract.md A): the faction a game starts with (the campaign's, else the
// sandbox's Federation), the Esc pause card (PauseMenu: resume, settings, main menu, quit) and edge scrolling.
public partial class PlanetDriver
{
    private PauseMenu _pause = null!;

    /// <summary>The faction the local player starts as: the campaign's pick, or the Federation in a debug game.</summary>
    private static string StartFaction => GameSetup.Mode == GameMode.Campaign ? GameSetup.Faction : "federation";

    /// <summary>The pause card on its own layer above every other overlay (HUD 1, test panel 2, chooser 3, screens 4,
    /// toasts 5); it keeps running while the tree is paused.</summary>
    private void SetupPause()
    {
        var layer = new CanvasLayer { Name = "PauseLayer", Layer = 8, ProcessMode = ProcessModeEnum.Always };
        AddChild(layer);
        _pause = new PauseMenu { Name = "PauseMenu" };
        layer.AddChild(_pause);
    }

    /// <summary>Esc with nothing else to close (OnKey on the surface, OrbitKey in orbit): pause.</summary>
    private void OpenPause()
    {
        var mode = GameSetup.Mode == GameMode.Campaign ? "CAMPAIGN · DAWN OF WARP" : "DEBUG GAME";
        if (GameSetup.Mode == GameMode.Campaign && GameSetup.Era is { } era && EraNames.TryGetValue(era, out var eraName))
            mode = $"CAMPAIGN · {eraName.ToUpperInvariant()}";
        _pause.Subtitle = $"{PlanetName.ToUpperInvariant()}  ·  {mode}";
        _pause.Open();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The pan direction from the mouse touching the window's edge (GameSettings.EdgeScroll): a few pixels at
    /// each side, only while the window has focus and the mouse is inside it.</summary>
    private Vector3 EdgeScroll(Vector3 forward, Vector3 right)
    {
        if (!GetWindow().HasFocus()) return Vector3.Zero;
        var size = GetViewport().GetVisibleRect().Size;
        var m = GetViewport().GetMousePosition();
        if (m.X < 0 || m.Y < 0 || m.X > size.X || m.Y > size.Y) return Vector3.Zero;
        const float edge = 6f;
        var move = Vector3.Zero;
        if (m.X <= edge) move -= right;
        else if (m.X >= size.X - edge) move += right;
        if (m.Y <= edge) move += forward;
        else if (m.Y >= size.Y - edge) move -= forward;
        return move;
    }
}
