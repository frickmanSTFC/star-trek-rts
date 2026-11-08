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

## Sim side, filled in (2026-11-08, sim branch)

Names and signatures above are unchanged. Added for the view:

- `SystemGenerator.Generate(data, seed)` → `GeneratedSystem` (`Star`, `Bodies` in orbit order with each planet's moons
  after it, `Entries` (`GeneratedBody`: `Body`, `Zone` inner/habitable/outer, `OrbitRadius`, `Climate`), `Climate`
  (`SystemClimate`), `Landable(data)`, `Body(id)`, `ToMapDef(era)`, `FallbackUsed`); `SystemGenerator.SurfaceSeed(seed,
  bodyId)` = the seed of that body's surface (use it for look-only maps of other landable bodies:
  `SurfaceMap.Generate(data, data.Surface(cls), SurfaceSeed(seed, id), id, system.Climate)`). Body ids are `b1`…`bN`,
  moons `b3a`, `b3b`…
- `StarSystemState`: `Seed`, `Generated`, `Satellites` (`SystemSatellite(Owner, BodyId, PlacedTick)`), `Body(id)`,
  `FindShip(id)`, `MoonsOf(id)`, `NameFor(player, id)` ("unknown body" until Charted), `ZoneOf(id)`, `RoughSize(size)`,
  `Report(player, id)` → `BodyReport` (only what the player knows: name/class/moons/gravity/pressure/gases/mean
  temperature/landable/resources once Charted; position, size, rough size, zone always), `EtaSeconds(ship, tick)`,
  `Heading(ship)`, `OrbitProblem(player)` ("the ship is not in orbit of <name>"), `ScanProblem(player, shipId)`,
  `ScanTargets(ship)`, `SatelliteProblem(player, bodyId)`.
- `SystemShip` gained `Faction`, `Role`, `IsNeutral`, `InFlight`, `ScanReadyTick`, `LastScanTick`, `Trips`, `LeaveTick`.
- Commands (`CommandHash` tags 22–24): `SetCourseCommand(Player, ShipId, BodyId)`, `LongRangeScanCommand(Player,
  ShipId)`, `SystemSatelliteCommand(Player, BodyId)`; refused with "no star system" in planet-only games.
- `MatchSetup.HomeBody(data, system, homeBody?)`; `CreateSystemGame` takes a body id or name (any case) and throws for one
  that isn't a landable body of the system. `Simulation.LoadSystem(state)`; `Simulation.LoadSurface(cls, seed, body,
  systemClimate)`; `SurfaceMap.SystemClimate`; `Climate.Summary(data, systemClimate, bodyId)`;
  `GameData.SystemGenerator`, `GameData.SystemRules`, `GameData.IsLandableClass`, `GameData.TrafficClasses()`.
- Orbit gate: `PlanetSurface.LandProblem/OutpostProblem/ShuttleProblem/BeamProblem/SatelliteProblem` return the
  `OrbitProblem` text while the flagship is away from `SurfaceBody` (so `PlanetDriver.OrbitProblem` shows it as is).
