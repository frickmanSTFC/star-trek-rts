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
- For the view's wiring (added after the view branch): `LiveReadings(player, bodyId)` → `LiveReadings` record (null
  without live data; the colony's body = outpost telemetry at the landing site with `Surface` set and `FromUplink`;
  elsewhere a satellite's reading: class air, gravity, sunlight, season, temperature now at the equator's reference
  meridian, the season's prevailing weather; `Climate.Now(data, systemClimate?, bodyId, tick)` underneath);
  `GravityOf(bodyId)` (own gravity_g, else the class's; null for giants/belts; generated bodies carry their class's
  today); `Version` (goes up on any knowledge, course, arrival, cargo, satellite or scan change; not hashed);
  `ScanCooldownTicks`, `ScanRange`; a ship's `ScanReadyTick`/`LastScanTick`.
- Confirmed: `SystemShip.ClassId` is the ships data `Key` ("federation.Galaxy", neutral "ferengi.Latinum Hauler"); only if
  orbit.ships names a class that isn't in the ships data is it the bare class name. `ShipPosition(ship, tick)` is pure
  (any tick; clamped to the destination past `ArriveTick`) and returns the body's own position for a ship in orbit (no
  orbit offset: the view spreads parked ships). `Ships` is not fogged: it lists every ship (ours, rivals', neutral).
  `HasLiveData` is true for the colony's body once `UplinkOnline`.
- `SystemShip` gained `Faction`, `Role`, `IsNeutral`, `InFlight`, `ScanReadyTick`, `LastScanTick`, `Trips`, `LeaveTick`.
- Commands (`CommandHash` tags 22–24): `SetCourseCommand(Player, ShipId, BodyId)`, `LongRangeScanCommand(Player,
  ShipId)`, `SystemSatelliteCommand(Player, BodyId)`; refused with "no star system" in planet-only games.
- `MatchSetup.HomeBody(data, system, homeBody?)`; `CreateSystemGame` takes a body id or name (any case) and throws for one
  that isn't a landable body of the system. `Simulation.LoadSystem(state)`; `Simulation.LoadSurface(cls, seed, body,
  systemClimate)`; `SurfaceMap.SystemClimate`; `Climate.Summary(data, systemClimate, bodyId)`;
  `GameData.SystemGenerator`, `GameData.SystemRules`, `GameData.IsLandableClass`, `GameData.TrafficClasses()`.
- Orbit gate: `PlanetSurface.LandProblem/OutpostProblem/ShuttleProblem/BeamProblem/SatelliteProblem` return the
  `OrbitProblem` text while the flagship is away from `SurfaceBody` (so `PlanetDriver.OrbitProblem` shows it as is).

## Signal delay and the orbital sensor sweep (2026-11-08)

Owner: "if the ship is not in orbit, communications to them is delayed by the distance, so any SOS or support is
delayed"; "if you go to a non-explored body, how do you scan it? Ship sensors … test what is in the atmosphere, some
information on structures on the planet, same as a satellite in orbit". Answers: **orders + alerts** delayed (the surface
view stays live, with a SIGNAL DELAY tag), **1–10 s** by distance, **auto sweep on arrival** (~15 s), **long-range scan
only identifies** (name, class, size, moons).

- Data `maps/system.json`: `sweep.seconds` 15; `comms.delay_seconds` [1, 10] over `comms.distance` [0, 1000] map units
  (`StarSystemRules.SweepSeconds/CommsMinSeconds/CommsMaxSeconds/CommsNear/CommsFar`, validated).
- Knowledge: `BodyKnowledge.Scanned` between Detected and Charted (`NameFor` shows the name from Scanned). Long-range scan
  → Scanned. Entering orbit → Scanned at once and, if not Charted, a sweep (`SystemShip.SweepBody/SweepStartTick/
  SweepDoneTick`, hashed; `Sweeping`); done → Charted; leaving orbit calls it off. `SweepingShip(player, body)`,
  `SweepShare(ship)`, `SweepSecondsLeft(ship)`, `SweepTicks`.
- Live: `HasLiveData` is also true while a ship of the player's orbits a Charted body (`LiveFromShip`).
  `Structures(player, body)` → `StructureSighting(Owner, Bases, Buildings, Vehicles)` per owner, only with live data
  (only the colony's body has a surface today); also `BodyReport.Structures`. `Report` at Scanned: name, class, moons,
  landable; no air/gravity/temperature/resources.
- Comms: `CommsDelayTicks(player)` / `CommsDelaySeconds` = 0 in orbit of `SurfaceBody` (or no ship / no colony body),
  else min + (max − min) × distance share. `Simulation.IsColonyOrder` (move, place, cancel, upgrade, mine, pack up,
  salvage, repair, tyre, bay orders, research, maintain, pause, found outpost) is relayed: `Simulation.OrdersInTransit`
  (`OrderInTransit(SentTick, ArriveTick, Command)`, hashed only in system games) and applied at ArriveTick (checked
  then). Ship, orbit and debug commands are never delayed.
- View: `PlanetDriver.Comms.cs` (`ColonyReport` = event lines from the surface (DetectEvents, RoverEvents, FounderEvents,
  hazards, uplink) wait the delay in game time and show "· relayed, N s old"; "Order relayed … arrives in N s"; sweep
  start/done/called-off lines), `PlanetHud.Signal.cs` (SIGNAL DELAY tag + orders in transit), helm subtitles
  "SENSOR SWEEP · N S" / "SIGNAL DELAY N S", body cards by level (Scanned: class/size/moons + sweep progress; Charted:
  + STRUCTURES / CONTACT rows; LIVE says satellite or ship's sensors), plot tags UNCHARTED / SWEEPING.
