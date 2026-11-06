using System;
using System.Linq;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The SKY tab: force the time of day, the season, the weather (kind + intensity) and the wind, to look at day/night and
// weather on the surface, the orbit globe and the outpost feed without waiting for them. Render-only (PlanetView.SkyOverride):
// never sent to the sim, so Climate's own weather (and its gameplay effects) carry on underneath. Marks TEST MODE.
public partial class TestPanel
{
    private SkyOverride Sky => _host.View.SkyOverride;

    /// <param name="rebake">The per-tile textures need baking again (not for a new hour: the shader shifts the day texture).</param>
    private void SkyEdit(string text, bool rebake = true)
    {
        if (rebake) Sky.Version++;
        LocalEdits = true;
        _lastEdit = text;
    }

    private static string Clock(float hours)
    {
        var minutes = Mathf.PosMod(Mathf.RoundToInt(hours * 60f), 24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private void BuildSky()
    {
        var sky = Sky;
        var note = Text("Render only: what the surface, the globe and the feed show. The sim's own climate (and its effects on rovers and drop ships) carries on.", 12, Dim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(Width - 60, 0);
        _content.AddChild(note);
        var said = Text("", 12, Soft);
        said.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        said.CustomMinimumSize = new Vector2(Width - 60, 0);
        _content.AddChild(said);
        _refreshers.Add(() =>
        {
            var s = Climate.Sun(Data, Surface.Map, Sim.Tick);
            said.Text = $"Climate now: sun over column {s.SubsolarColumn.ToFloat():0.0} at {s.DeclinationDeg.ToFloat():0.0}° · {s.SeasonName} · " +
                        $"{Mathf.RoundToInt(s.DayFraction.ToFloat() * 100)}% of the day at column 0 · light ×{s.Insolation.ToFloat():0.00}";
        });

        // ---- time of day
        _content.AddChild(Section("TIME OF DAY (AT THE VIEW'S CENTRE)"));
        var timeCheck = MakeCheck("Force the time", on =>
        {
            sky.TimeOn = on;
            SkyEdit(on ? $"sky: time forced to {Clock(sky.Hour)}" : "sky: time back to the climate's");
        });
        _content.AddChild(timeCheck);
        var timeRow = Row();
        var hourLabel = Text(Clock(sky.Hour), 14, Ink);
        hourLabel.CustomMinimumSize = new Vector2(52, 0);
        var hourSlider = MakeSlider(0, 96, _ => { });
        hourSlider.Value = Mathf.RoundToInt(sky.Hour * 4f);
        hourSlider.ValueChanged += v =>
        {
            sky.Hour = (float)v / 4f;
            hourLabel.Text = Clock(sky.Hour);
            var rebake = !sky.TimeOn;
            sky.TimeOn = true;
            SkyEdit($"sky: time {Clock(sky.Hour)}", rebake);
        };
        timeRow.AddChild(hourSlider);
        timeRow.AddChild(hourLabel);
        _content.AddChild(timeRow);
        var presets = Grid(4);
        foreach (var (label, hour) in new[] { ("Dawn 6:00", 6f), ("Noon", 12f), ("Dusk 18:30", 18.5f), ("Midnight", 0f) })
            presets.AddChild(MakeButton(label, Plain, () => hourSlider.Value = Mathf.RoundToInt(hour * 4f), 0, 30));
        _content.AddChild(presets);
        var runRow = Row();
        runRow.AddChild(Text("Clock", 13, Soft));
        float[] rates = { 0f, 1f, 10f, 60f };
        runRow.AddChild(Segmented(new[] { "Stopped", "×1", "×10", "×60" }, () => Array.IndexOf(rates, sky.RunRate) is var i && i >= 0 ? i : 0, i =>
        {
            sky.RunRate = rates[i];
            var rebake = !sky.TimeOn;
            sky.TimeOn = true;
            SkyEdit(rates[i] == 0f ? "sky: clock stopped" : $"sky: clock runs ×{rates[i]:0}", rebake);
        }));
        _content.AddChild(runRow);

        // ---- season
        _content.AddChild(Section("SEASON"));
        var seasonCheck = MakeCheck("Force the season (northern hemisphere; tilt 23.5°)", on =>
        {
            sky.SeasonOn = on;
            SkyEdit(on ? $"sky: season forced to {SkyModel.TestSeasons[sky.Season]}" : "sky: season back to the climate's");
        });
        _content.AddChild(seasonCheck);
        _content.AddChild(Segmented(SkyModel.TestSeasons, () => sky.Season, i =>
        {
            sky.Season = i;
            sky.SeasonOn = true;
            SkyEdit($"sky: season {SkyModel.TestSeasons[i]}");
        }));

        // ---- weather
        _content.AddChild(Section("WEATHER (EVERYWHERE)"));
        var weatherCheck = MakeCheck("Force the weather", on =>
        {
            sky.WeatherOn = on;
            SkyEdit(on ? $"sky: weather forced to {sky.Kind}" : "sky: weather back to the climate's");
        });
        _content.AddChild(weatherCheck);
        var kinds = Enum.GetValues<WeatherKind>();
        var kindOption = MakeOption(kinds.Select(k => SkyModel.Name(k, 0.5f)).ToArray(), Array.IndexOf(kinds, sky.Kind), i =>
        {
            sky.Kind = kinds[i];
            sky.WeatherOn = true;
            SkyEdit($"sky: weather {kinds[i]}");
        });
        kindOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.AddChild(kindOption);
        var quick = Grid(4);
        foreach (var k in new[] { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Storm, WeatherKind.Fog, WeatherKind.Snow, WeatherKind.Blizzard, WeatherKind.DustStorm, WeatherKind.Overcast })
        {
            var kind = k;
            quick.AddChild(MakeButton(SkyModel.Name(kind, 0.5f), kind is WeatherKind.Storm or WeatherKind.Blizzard or WeatherKind.DustStorm ? Yellow : Plain, () =>
            {
                sky.Kind = kind;
                sky.WeatherOn = true;
                kindOption.Selected = Array.IndexOf(kinds, kind);
                SkyEdit($"sky: weather {kind}");
            }, 0, 30, 12));
        }
        _content.AddChild(quick);
        var intensityRow = Row();
        intensityRow.AddChild(Text("Intensity", 13, Soft));
        var intensityLabel = Text($"{Mathf.RoundToInt(sky.Intensity * 100)}%", 14, Ink);
        intensityLabel.CustomMinimumSize = new Vector2(44, 0);
        var intensity = MakeSlider(0, 100, _ => SkyEdit($"sky: intensity {Mathf.RoundToInt(sky.Intensity * 100)}%"));
        intensity.Value = Mathf.RoundToInt(sky.Intensity * 100f);
        intensity.ValueChanged += v =>
        {
            sky.Intensity = (float)v / 100f;
            intensityLabel.Text = $"{Mathf.RoundToInt(v)}%";
        };
        intensityRow.AddChild(intensity);
        intensityRow.AddChild(intensityLabel);
        _content.AddChild(intensityRow);

        // ---- wind
        _content.AddChild(Section("WIND"));
        var windCheck = MakeCheck("Force the wind", on =>
        {
            sky.WindOn = on;
            SkyEdit(on ? $"sky: wind forced to {sky.WindMs:0} m/s toward {SkyModel.Compass(sky.WindDeg)}" : "sky: wind back to the climate's");
        });
        _content.AddChild(windCheck);
        var dirRow = Row();
        dirRow.AddChild(Text("Toward", 13, Soft));
        var dirLabel = Text($"{sky.WindDeg:0}° {SkyModel.Compass(sky.WindDeg)}", 14, Ink);
        dirLabel.CustomMinimumSize = new Vector2(64, 0);
        var dir = MakeSlider(0, 355, _ => { });
        dir.Step = 5;
        dir.Value = sky.WindDeg;
        dir.ValueChanged += v =>
        {
            sky.WindDeg = (float)v;
            dirLabel.Text = $"{v:0}° {SkyModel.Compass((float)v)}";
            if (!sky.WindOn) sky.WindOn = true;
            SkyEdit($"sky: wind toward {SkyModel.Compass(sky.WindDeg)}");
        };
        dirRow.AddChild(dir);
        dirRow.AddChild(dirLabel);
        _content.AddChild(dirRow);
        var speedRow = Row();
        speedRow.AddChild(Text("Speed", 13, Soft));
        var speedLabel = Text($"{sky.WindMs:0} m/s", 14, Ink);
        speedLabel.CustomMinimumSize = new Vector2(64, 0);
        var speed = MakeSlider(0, 40, _ => { });
        speed.Value = sky.WindMs;
        speed.ValueChanged += v =>
        {
            sky.WindMs = (float)v;
            speedLabel.Text = $"{v:0} m/s";
            if (!sky.WindOn) sky.WindOn = true;
            SkyEdit($"sky: wind {v:0} m/s");
        };
        speedRow.AddChild(speed);
        speedRow.AddChild(speedLabel);
        _content.AddChild(speedRow);

        _content.AddChild(RuleLine());
        _content.AddChild(MakeButton("Back to the climate (clear every override)", Green, () =>
        {
            sky.TimeOn = sky.SeasonOn = sky.WeatherOn = sky.WindOn = false;
            sky.RunRate = 0f;
            SkyEdit("sky: back to the climate");
        }, 0, 34));

        _refreshers.Add(() =>
        {
            timeCheck.SetPressedNoSignal(sky.TimeOn);
            seasonCheck.SetPressedNoSignal(sky.SeasonOn);
            weatherCheck.SetPressedNoSignal(sky.WeatherOn);
            windCheck.SetPressedNoSignal(sky.WindOn);
            // A running clock moves the slider (without counting as an edit).
            if (sky.TimeOn && !hourSlider.HasMeta("dragging"))
            {
                var at = Mathf.RoundToInt(sky.Hour * 4f) % 96;
                if (Mathf.RoundToInt(hourSlider.Value) != at) hourSlider.SetValueNoSignal(at);
                hourLabel.Text = Clock(sky.Hour);
            }
        });
    }
}
