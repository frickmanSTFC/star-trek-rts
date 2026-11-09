# Main menu, campaign start, rocket landing, tutorial: contract

Owner 2026-11-09: "let's add a menu to our game. Campaign / Multiplayer / Settings / DEBUG. When you start campaign,
show each faction, but only Federation is selectable. When you choose federation, only pre-warp is available, all the
others show but they should have a lock. When you do a drop, the ships need to be less impulse and such, more rocket.
The dust particles need to be much smaller, they are too big when landing. You still need to see a star system to
pick one or two of the objects as a starting point, more like a tutorial. Show popups for a tutorial style getting
started."

All render-side (`game/scripts/`, `game/scenes/`); the sim is unchanged except possibly a Dawn of Warp era record in
data (see A). Placeholder numbers throughout.

## Shared API (stubs committed with this contract)

- `GameSetup` (in `scripts/WorldChooser.cs`, owned by agent A): adds
  - `GameMode Mode` (`Debug` | `Campaign`; default `Debug` so an old F6 run of Planet.tscn behaves as before),
  - `string Faction` ("federation"), `string? Era` ("dawn_of_warp" for the campaign, null for debug),
  - `bool Tutorial` (campaign + `GameSettings.TutorialHints`),
  - `StartCampaign(faction, era, seed, homeBodyId)` sets them (not saved to disk: a campaign start is chosen in the menu
    each time; the DEBUG world/seed stays saved as now).
- `GameSettings` (`scripts/GameSettings.cs`, owned by A): static, `user://settings.cfg`; `Load()`, `Save()`, `Apply()`;
  at least `TutorialHints` (default true), `MasterVolume` (0..1), `WindowMode`, `VSync`, `UiScale` (feeds
  `HudKit.ScaleFor` only if cheap; otherwise leave it out), `CameraSpeed`, `EdgeScroll`. Only add settings that
  actually do something.
- `TutorialCard` (`scripts/TutorialCard.cs`, owned by agent C): one popup card in the HUD's own look (HudKit),
  `TutorialCard.Open(Node parent, TutorialStep step)` → shows it; `Close()`; events `Next`, `SkipAll`. A `TutorialStep`
  record: `Id, Title, Body, StepLabel ("2 / 9"), Anchor (screen rect to point at, or empty), NextText ("GOT IT" | null
  when the step waits for an action)`. A uses it on the menu's system picker; C uses it in the game.

## A: main menu + settings + campaign flow (agent A)

Files: `scenes/MainMenu.tscn` (new main scene in `project.godot`), `scripts/MainMenu.cs` (+ partials
`MainMenu.Campaign.cs`, `MainMenu.Settings.cs`, `MainMenu.SystemPick.cs` as needed), `scripts/GameSettings.cs`,
`GameSetup` in `scripts/WorldChooser.cs`, small hooks in `PlanetDriver.cs` / `PlanetDriver.System.cs` (read the mode,
faction, era, seed and home body; in-game way back to the menu).

- Title screen: game name, a starfield/planet backdrop (cheap: reuse SkyField / a slowly turning OrbitPainter globe
  if it is easy, otherwise a 2D starfield), four buttons: **CAMPAIGN**, **MULTIPLAYER** (shown, locked: "coming
  later"), **SETTINGS**, **DEBUG** (starts the current sandbox exactly as today: saved world/seed, world chooser, test
  panel). QUIT too.
- Campaign → **faction screen**: the seven great powers (`tools/faction_meta.py` FACTIONS order: Federation, Klingon,
  Cardassian, Romulan, Dominion, Ferengi, Borg; names/blurbs from `data/factions/`), each a card; only Federation is
  selectable, the others show a lock and "not playable yet".
- Federation → **era screen**: Dawn of Warp (pre-warp, 2063–2150) is the only one open; every era from
  `data/eras/eras.json` shows after it, with a lock. Dawn of Warp is not in eras.json yet: add it as a real record
  (tech_cap_tier 0, playable federation; keep `GameData.Validate()` and `DataChecks.All` empty, factions.json must
  agree) or, if that ripples too far, show it from the menu only and note why. The game itself doesn't play Dawn of
  Warp's content yet (queued separately); the campaign starts the normal colony game with
  `DataSetup(Era: "dawn_of_warp", Factions: ["federation"])`; check nothing breaks with that era (e.g. game year,
  Prime Directive from 2168 → no Standing meter in that era is correct).
- Era → **star system picker**: a generated system (new random seed each visit, REROLL button) drawn top-down
  (reuse `SystemPlot` + `SystemSource` over a throwaway `MatchSetup.CreateSystemGame` sim, or `SystemGenerator`
  directly). Every body shows; the landable ones (usually 1–2, `MatchSetup.LandableBodies`) are highlighted and
  clickable with a card (class, gravity, habitability tier, map size, a "recommended" tag for the easiest); others
  say why they can't be landed on. **LAND HERE** → `GameSetup.StartCampaign(...)` → `ChangeSceneToFile(Planet.tscn)`.
  Tutorial cards (TutorialCard) explain the screen when tutorial hints are on: what a star system is, what to
  look for, pick a world.
- Settings screen (from the title and in-game): the GameSettings above, applied live, saved.
- In game: a way back to the main menu (Esc when nothing else is open opens a small pause card: RESUME, SETTINGS,
  MAIN MENU, QUIT; check every existing Esc use in PlanetDriver*.cs keeps working). In campaign mode the world chooser
  (WORLD button / F2) is hidden; the test panel stays in debug builds.

## B: rocket landing + smaller dust (agent B)

Files: `scripts/DropShipView.cs`, `scripts/LandingView.cs` (its convoy flight + puffs), new `scripts/RocketPlume.cs` if
useful. No model changes required (Blender `build_dropship.py` only if a nozzle is really missing).

- Less impulse, more rocket: descent = a ballistic-looking fall that ends in a hard deceleration burn (suicide-burn
  feel), mostly vertical in the last ~60 units; take-off = a straight-up climb that accelerates, plume growing, then
  pitches away. No gliding sideways on blue impulse glow.
- Exhaust: from the four lift thrusters (`Thruster0..3`) a bright flame cone (emissive, additive, flickering length
  and width by throttle) + hot core, a flickering orange OmniLight on the ground, rising smoke/steam (grey-white,
  soft) that spreads out on the ground near touchdown. Aft thrusters (4,5) stay small/dim. Throttle drives all of it.
- Dust much smaller: the downwash/burst puffs today are 0.5-radius spheres at scale 1.4–3.4; make them fine
  (roughly a fifth of that), more numerous, faster outward, staying low. Same for LandingView's Puff/Trail and
  anything else that uses `Downwash`.
- Keep the existing timeline hooks (TouchdownAt, ramp, convoy Approach) working; the convoy's outpost-module drop
  stays.

## C: tutorial (agent C)

Files: `scripts/TutorialCard.cs`, new `scripts/Tutorial.cs` (steps + conditions), `scripts/PlanetDriver.Tutorial.cs`
(partial driver: `BeginTutorial()` in _Ready, `UpdateTutorial()` per frame), plus at most a couple of one-line calls in
`PlanetDriver.cs`.

- Runs only when `GameSetup.Tutorial` is true (campaign + hints on). A short guided start, one card at a time, each
  advancing on its own when the player does the thing (or GOT IT for read-only cards): welcome in orbit over the
  chosen world → scan / read the findings → pick a landing site and land → watch the convoy set up the Colony Hub →
  select the hub / read the top bar → survey with the rover → build a Storage Container → Ore Refinery → the Mining
  Crawler mines and fills storage → Research Lab → done ("you're on your own, F1 for controls"). Use the real game
  state (sim queries, commands seen), not timers. Cards point at the relevant HUD area where possible. SKIP TUTORIAL
  ends it. Progress kept only for the current game.
- Steps written as data in code (one list), easy to change.
