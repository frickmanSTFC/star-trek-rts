using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// The landing fields on the map (owner 2026-11-17; sim/Surface/LandingField.cs, LandingFieldView.cs): pad markings,
// pad lights and fuel bowsers near every colony core, where the drop ships set down to refuel before they fly back up.
public partial class PlanetView
{
    private LandingFieldView _landingField = null!;
    private readonly List<Node3D> _fieldFurniture = new();

    /// <summary>Scatter (trees, rocks, grass) is cleared this far round a pad's centre.</summary>
    private const float PadClearing = 11.8f;

    /// <summary>Makes the field view and hands it to the drop ships (call once they exist).</summary>
    private void SetupLandingField()
    {
        _landingField = new LandingFieldView(this, _sim) { Name = "LandingFields" };
        AddChild(_landingField);
        _dropShips.Field = _landingField;
    }

    /// <summary>Pads appear and go with their cores; lights follow the ships (before the drop ships are drawn).</summary>
    private void UpdateLandingField(float delta) => _landingField.Update(delta);

    /// <summary>The bowsers people walk round (PlanetView.People.cs).</summary>
    private List<Node3D> LandingFieldFurniture()
    {
        _fieldFurniture.Clear();
        _landingField.Furniture(_fieldFurniture);
        return _fieldFurniture;
    }
}
