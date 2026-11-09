namespace FinalFrontier;

/// <summary>
/// Player settings (docs/menu-contract.md, agent A): static, saved in user://settings.cfg. Stub: defaults only.
/// </summary>
public static class GameSettings
{
    /// <summary>Tutorial cards in a campaign start.</summary>
    public static bool TutorialHints { get; set; } = true;

    public static void Load() { }
    public static void Save() { }
    public static void Apply() { }
}
