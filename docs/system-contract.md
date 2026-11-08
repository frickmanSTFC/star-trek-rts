# Star system contract (2026-11-08)

Owner's request: in the SYSTEM view show where the ship(s) are, choose the next place to go, show other ships and
their info; a "fog" for the system (know an object is there but not everything about it; what you learn is kept;
local live information only where a satellite is); every object can be orbited; Class M must be rare; findings list
resources as present/possible, not every spot.

Owner's answers: **no guaranteed Class M** (systems are generated, M is a rare roll; at least one landable world);
**timed flight** between bodies (20–90 s by distance; orbit actions only on arrival); **3 fog levels**
(Detected → Charted, kept → Live with a satellite); **neutral traffic** (a few civilian ships, clickable).

## Sim (`game/sim/Space/StarSystem.cs`, stubs until the sim branch lands)

- `Simulation.System` → `StarSystemState?` (null for planet-only games such as the balance runner's).
- `StarSystemState`: `Star`, `Bodies` (BodyDef, static positions in map units), `Ships` (`SystemShip`),
  `SurfaceBody`, `KnowledgeOf(player, body)` → `BodyKnowledge` (Unknown/Detected/Charted),
  `HasLiveData(player, body)`, `FlagshipOf(player)`, `ShipPosition(ship, tick)`, `TravelTicks(from, to)`,
  `CourseProblem(player, shipId, body)`, `PossibleResources(player, body)`.
- Commands (sim agent adds them, with CommandHash tags and CommandChecks): `SetCourseCommand(player, shipId, bodyId)`,
  `LongRangeScanCommand(player, shipId)` (charts bodies in range, cooldown), `SystemSatelliteCommand(player, bodyId)`
  (a satellite at the body the flagship orbits; gives Live data there).
- `SystemGenerator.Generate(data, seed)` → a `MapDef`-like system; `MatchSetup.CreateSystemGame(data, seed, faction,
  homeBodyId?)` = generated system + the PlanetSurface of the home body (a landable class; the chooser picks it).

## View

- System display (`scripts/SystemPlot.cs` and the helm): ships as icons with course lines and ETAs, neutral traffic,
  fog states (Detected = grey "unknown body", Charted = class colour + name), body card by knowledge level with
  SET COURSE, ship cards (class, owner, heading, cargo, ETA).
- Orbit any body: the orbit globe for the body the flagship is at (the landed surface for the colony's body; a
  look-only generated map for other landable classes; procedural globes for gas giants, airless/ice moons, belts).
- World chooser lists the landable bodies of the system the seed generates.
