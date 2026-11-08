using Godot;

namespace FinalFrontier;

// The top bar's SIGNAL DELAY tag (owner 2026-11-08: "if the ship is not in orbit, communications are delayed by the
// distance"): shown while the flagship is away from the colony's world, with the delay and the orders still on their
// way. Built on first use just left of the TEST MODE tag.
public partial class PlanetHud
{
    private PanelContainer? _signalTag;
    private Label _signalText = null!;

    /// <summary>Shows "SIGNAL DELAY 6 s · 2 orders in transit"; 0 seconds hides it.</summary>
    public void ShowSignal(int seconds, int ordersInTransit)
    {
        if (_signalTag == null)
        {
            if (seconds <= 0) return;
            var row = _testTag.GetParent();
            _signalTag = Panel(new Color(0.16f, 0.1f, 0.03f, 0.95f), new Color(1f, 0.7f, 0.42f), 10, 9, 2);
            _signalTag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _signalTag.MouseFilter = Control.MouseFilterEnum.Pass;
            _signalTag.TooltipText = "Your ship is away from the colony's world: orders reach the colony late, and its reports reach you late.\nReturn to orbit for instant contact.";
            _signalText = MakeLabel("", 12, new Color(1f, 0.78f, 0.52f));
            _signalTag.AddChild(_signalText);
            row.AddChild(_signalTag);
            row.MoveChild(_signalTag, _testTag.GetIndex());
        }
        var show = seconds > 0;
        if (_signalTag.Visible != show) _signalTag.Visible = show;
        if (!show) return;
        var text = $"SIGNAL DELAY {seconds} s" + (ordersInTransit > 0 ? $" · {ordersInTransit} order{(ordersInTransit == 1 ? "" : "s")} in transit" : "");
        if (_signalText.Text != text) _signalText.Text = text;
    }
}
