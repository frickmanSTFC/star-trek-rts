using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The SELECTED tab's tyre controls for a rover (vehicles with tyres in surfaces.json): one toggle per wheel to make it
// flat or fix it, "all flat" / "clear", and the spares on board. Each is a DebugUnitCommand with its undo.
public partial class TestPanel
{
    private void BuildTyres(int id)
    {
        if (Surface.FindUnit(id) is not { } u || Data.SurfaceUnit(u.DefId).Tyres is not { } tyres)
        {
            var none = Row();
            none.AddChild(Text("No tyres on this vehicle", 13, Off));
            _content.AddChild(none);
            return;
        }
        SurfaceUnit? Live() => Surface.FindUnit(id);
        var wheels = tyres.Wheels;
        var all = (1 << wheels) - 1;

        var head = Row();
        head.AddChild(Caption("TYRES"));
        var state = Text("", 13, Soft);
        state.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        state.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(state);
        _content.AddChild(head);

        // Wheels laid out as seen from above, front at the top: left column, right column.
        var grid = Grid(2);
        var checks = new CheckBox[wheels];
        for (var i = 0; i < wheels; i++)
        {
            var wheel = i;
            checks[i] = MakeCheck($"Flat · {Wheels.Name(wheels, i)}", on =>
            {
                if (Live() is not { } x) return;
                var mask = on ? x.FlatTyres | (1 << wheel) : x.FlatTyres & ~(1 << wheel);
                SetFlats(x, mask & all, $"{Wheels.Name(wheels, wheel)} {(on ? "flat" : "fixed")}");
            });
            grid.AddChild(checks[i]);
        }
        _content.AddChild(grid);

        var buttons = Row(6);
        buttons.AddChild(Grow(MakeButton("Random flat", Yellow, () =>
        {
            if (Live() is not { } x) return;
            var whole = Enumerable.Range(0, wheels).Where(i => (x.FlatTyres & (1 << i)) == 0).ToList();
            if (whole.Count == 0) return;
            var pick = whole[(int)(Time.GetTicksMsec() % (ulong)whole.Count)]; // render-side pick; the sim gets the exact mask
            SetFlats(x, x.FlatTyres | (1 << pick), $"{Wheels.Name(wheels, pick)} flat");
        }, 0, 32)));
        buttons.AddChild(Grow(MakeButton("All flat", Red, () =>
        {
            if (Live() is { } x) SetFlats(x, all, "every tyre flat");
        }, 0, 32)));
        buttons.AddChild(Grow(MakeButton("Clear", Green, () =>
        {
            if (Live() is { } x) SetFlats(x, 0, "tyres fixed");
        }, 0, 32)));
        _content.AddChild(buttons);

        var spares = Row(6);
        spares.AddChild(Text("Spares", 14, Ink));
        var count = Text("", 14, Soft);
        count.CustomMinimumSize = new Vector2(28, 0);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        spares.AddChild(count);
        foreach (var d in new[] { -1, 1 })
            spares.AddChild(MakeButton(d < 0 ? "−1" : "+1", Plain, () =>
            {
                if (Live() is not { } x) return;
                var to = Mathf.Clamp(x.Spares + d, 0, 20);
                if (to == x.Spares) return;
                Apply($"{CallSign(x)} spares {x.Spares} → {to}", new DebugUnitCommand(Me, id, DebugUnitField.Spares, Fix64.FromInt(to)),
                    new DebugUnitCommand(Me, id, DebugUnitField.Spares, Fix64.FromInt(x.Spares)));
            }, 44, 30));
        spares.AddChild(Text($"(carries {tyres.Spares} new)", 12, Off));
        _content.AddChild(spares);

        _refreshers.Add(() =>
        {
            if (Live() is not { } x) return;
            var flats = Surface.FlatCount(x);
            state.Text = (flats == 0 ? "all sound" : $"{flats} flat") +
                         (Surface.ChangingTyre(x) ? $" · changing {Mathf.RoundToInt(Surface.TyreChangeFraction(x).ToFloat() * 100)}%" : "") +
                         $" · speed {Mathf.RoundToInt(Surface.DriveShare(x).ToFloat() * 100)}%";
            for (var i = 0; i < wheels; i++) checks[i].SetPressedNoSignal((x.FlatTyres & (1 << i)) != 0);
            count.Text = x.Spares.ToString();
        });
    }

    private void SetFlats(SurfaceUnit u, int mask, string what)
    {
        if (mask == u.FlatTyres) return;
        Apply($"{CallSign(u)} {what}", new DebugUnitCommand(Me, u.Id, DebugUnitField.FlatTyres, Fix64.FromInt(mask)),
            new DebugUnitCommand(Me, u.Id, DebugUnitField.FlatTyres, Fix64.FromInt(u.FlatTyres)));
    }
}
