# Star Trek RTS: design package

Early planning material for a Star Trek–themed real-time strategy game (1–4 players, single-player and multiplayer).

## Start here

- `GDD.md`: the game design document (pillars, modes, map, economy, colonization, eras, factions, research, ships, threats, multiplayer, open decisions).
- `federation-build-tree.html`: interactive Federation build tree. Open it in any browser.
- `concept-canvas/`: source for the structure concept boards (7 factions × 4 upgrade lines). These files render inside the online canvas, not in a plain browser.

## Online versions

- Faction structure concepts (canvas): https://claude.ai/artifact/Q8fDXqjgVAQuyQWAyuEame
- Federation build tree: https://claude.ai/artifact/CtWfg7e9HB3neyC4Y1hmAa

These links only open for people the owner has shared them with (Share menu on each page).

## Data files (`data/`)

| File | Contents |
|---|---|
| `resources.json` | Raw materials, refined components, faction resources, trade goods |
| `factions.json` | Playable, minor, pirate and crisis factions with signature mechanics |
| `eras.json` | Eight starting eras with available factions, events and stardate formats |
| `planet_classes.json` | Planet classes and other celestial bodies with yields and hazards |
| `tech_tree.json` | Research tree across seven branches |
| `ships.json` | Hull sizes, module slots and ship classes per faction |
| `vehicles.json` | Small craft, probes, civilian/support ships, ground forces and vehicles |
| `buildings.json` | All structures, faction-only buildings, relics, `faction_lines` (level-1-to-max upgrade chains for all seven factions) and `starting_sets` (each faction's Class M opening buildings) |
| `hazards.json` | Anomalies, habitat dangers, raiders, crisis entities and wildcards |

Each entry is tagged `canon` (on screen), `beta` (licensed books/games) or `original` (invented for this game). All costs and stats are placeholders.

Star Trek is owned by Paramount. This is a fan design project; a commercial release would need a license or an original setting.
