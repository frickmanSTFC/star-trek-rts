# Project memory

Fan-made Star Trek RTS design package. `README.md` lists every page, data file and artifact link; `GDD.md` is the design document. Keep both in step with the data.

## Design rules decided so far

- Seven great powers (see `tools/faction_meta.py` FACTIONS); 28 factions in `data/factions.json`.
- **Borg never research.** `tech_tree.json` → `faction_rules.borg`: `research: false`; they assimilate techs and combine them, costing 20 nanoprobes × tier. `tools/tech_access.py` `reachable()` honours this.
- Terraforming runs K → L → M.
- Dominion War era is 2373–2379; `eras.json` and `factions.json` must agree.
- Galaxy map (`data/galaxy_map.json`): Sol-centred light-years, +x toward Beta, +y toward the core (core at 0, 26000). Routes use the stable Bajoran wormhole but skip the unstable Barzan wormhole (opt-in checkbox in the planner); transwarp is Borg only. Match drive speeds are placeholders: Warp 5 = 30, Warp 8 = 60, Warp 9.9 = 110, slipstream = 300 ly per minute of play.
- All numbers are placeholders for balancing.

## How the pages are built

- Each page: `tools/gen_<x>_page.py` reads `data/*.json` + `tools/faction_meta.py`, replaces `/*DATA*/` in `tools/<x>_page.html`, writes `<x>.html` at the repo root.
- Shared dark "console frame" style; every page ends with a data-checks list, which should come back empty. When a page flags gaps, fix the data rather than the check.
- After any data change: rerun the affected `gen_*_page.py`, then `python3 tools/build_site.py` (builds `docs/` for GitHub Pages; new pages go in its PAGES list and in README).
- Data JSON is hand-formatted: edit with targeted unique string replacements, not full rewrites. One-object-per-line files (tech_tree, ships, vehicles, hazards) keep that layout.
- Test pages with Playwright at 1400 px and 400 px: no horizontal overflow, no console errors.
- Each root page is also published as a claude.ai Artifact (links in README); republish after regenerating. Artifacts are private until shared from the page's Share menu.

## Repo

- github.com/frickmanSTFC/star-trek-rts, public, branch `main`. Site: https://frickmanstfc.github.io/star-trek-rts/ once Pages is enabled (Settings → Pages → `main` / `docs`; still to do by the owner).
- Commit as `frickMan` with the owner's email; the session proxy blocks GitHub collaborator and Pages API calls, so those are done by the owner in the web UI.
- Blender scene: `blender/build_colony_pod.py` builds `colony_pod.blend` (see README for flags).
