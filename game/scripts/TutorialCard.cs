using Godot;

namespace FinalFrontier;

/// <summary>One tutorial step as the card shows it (docs/menu-contract.md). <see cref="Anchor"/> = the screen rect to
/// point at (empty = none); <see cref="NextText"/> null when the step waits for the player to do something.</summary>
public sealed record TutorialStep(string Id, string Title, string Body, string StepLabel, Rect2 Anchor, string? NextText);

/// <summary>
/// The tutorial popup card in the HUD's own look (agent C). Stub: shows nothing yet.
/// </summary>
public partial class TutorialCard : Control
{
    public event System.Action? Next;
    public event System.Action? SkipAll;

    /// <summary>Open (or re-use) the card under <paramref name="parent"/> showing <paramref name="step"/>.</summary>
    public static TutorialCard Open(Node parent, TutorialStep step)
    {
        var card = new TutorialCard { Name = "TutorialCard" };
        parent.AddChild(card);
        card.Show(step);
        return card;
    }

    /// <summary>Show another step on this card.</summary>
    public void Show(TutorialStep step) { _ = Next; _ = SkipAll; }

    /// <summary>Move the arrow to a new anchor (e.g. after a resize).</summary>
    public void SetAnchor(Rect2 anchor) { }

    public void Close() => QueueFree();
}
