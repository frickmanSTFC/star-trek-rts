"""Build the GitHub Pages site in docs/: a landing page plus standalone copies of every design page.

Usage: python3 tools/build_site.py
Run it after regenerating the pages (tools/gen_*_page.py). The page files in the repo root are written
for claude.ai, which wraps them in a document head when it shows them; this script adds that head
(doctype, charset, viewport, base reset) so each page also works on its own, and copies the artwork
and video the landing page shows. GitHub Pages serves docs/ from the main branch.
"""
import json
import os
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SITE = os.path.join(ROOT, "docs")
REPO = "https://github.com/frickmanSTFC/star-trek-rts"

# (source file, published name, title, one-line description, accent colour)
PAGES = [
    ("factions.html", "factions.html", "Factions of the Galaxy", "The seven great powers side by side, full dossiers, and who appears in which era.", "#c99ad6"),
    ("faction-buildings.html", "faction-buildings.html", "Faction Buildings", "How the seven factions differ in what they build: opening sets, upgrade lines, wonders.", "#ff9c5b"),
    ("ship-designs.html", "ship-designs.html", "Starship Designs", "Fleet doctrine, the hull reference, every class by hull and era, specials and modules.", "#9aa3ff"),
    ("tech-tree.html", "tech-tree.html", "Research Tech Tree", "All 7 branches and 6 tiers with prerequisite tracing, faction access and starting eras.", "#8fd3b0"),
    ("hazards.html", "hazards.html", "Galactic Hazards", "Anomalies, world conditions, raiders, crises and wild cards, with counters by faction.", "#e2787a"),
    ("planet-classes.html", "planet-classes.html", "Planet Classes", "Every world and body: habitability, resources, terraforming, and who wants what.", "#7fc4e8"),
    ("vehicles.html", "vehicles.html", "Craft and Ground Forces", "Shuttles, probes, the civilian fleet, ground troops and surface vehicles.", "#9aa3ff"),
    ("resources.html", "resources.html", "Resources and Materials", "Resource tiers, production chains and what each faction's economy consumes.", "#f3cb5b"),
    ("federation-build-tree.html", "federation-build-tree.html", "Federation Build Tree", "The Federation's buildings as a tech-gated tree with prerequisite links.", "#ffc999"),
    ("animation/colony-pod-drop.html", "colony-pod-drop.html", "Colony Pod Drop", "The 20-second animated drop of a Federation colony pod, with a scrubbable timeline.", "#b8c46a"),
]

HEAD = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<style>
:root {{ padding-top: env(safe-area-inset-top, 0px); padding-bottom: env(safe-area-inset-bottom, 0px); }}
body {{ margin: 0; font: 14px/1.5 system-ui, sans-serif; }}
img, video {{ max-width: 100%; }}
[hidden] {{ display: none !important; }}
.site-back {{ display: block; padding: 10px max(16px, env(safe-area-inset-left, 0px)) 0; font: 600 13px/1 "Barlow Condensed", "Arial Narrow", sans-serif;
  letter-spacing: .1em; text-transform: uppercase; color: #a29fb6; text-decoration: none; background: #05060b; }}
.site-back:hover, .site-back:focus-visible {{ color: #ffc999; }}
</style>
{head}
</head>
<body>
<a class="site-back" href="./">&larr; All design pages</a>
{body}
</body>
</html>
"""


def standalone(src):
    """Split a claude.ai page into its head block (title, metas, fonts, styles) and its body."""
    s = open(src).read()
    end = s.index("</style>") + len("</style>")
    return HEAD.format(head=s[:end], body=s[end:].lstrip("\n"))


def main():
    os.makedirs(SITE, exist_ok=True)
    for src, out, *_ in PAGES:
        open(os.path.join(SITE, out), "w").write(standalone(os.path.join(ROOT, src)))
    media = os.path.join(SITE, "media")
    os.makedirs(media, exist_ok=True)
    art = sorted(f for f in os.listdir(os.path.join(ROOT, "art", "federation-colony-hub")) if f.endswith(".svg"))
    for f in art:
        shutil.copy(os.path.join(ROOT, "art", "federation-colony-hub", f), os.path.join(media, f))
    shutil.copy(os.path.join(ROOT, "animation", "colony-pod-drop-blender.mp4"), os.path.join(media, "colony-pod-drop.mp4"))
    shutil.copy(os.path.join(ROOT, "blender", "previews", "t17.6-pod-closeup.png"), os.path.join(media, "pod-closeup.png"))
    # GitHub Pages: serve the files as they are, without Jekyll
    open(os.path.join(SITE, ".nojekyll"), "w").write("")
    data = {"pages": [{"href": out, "title": t, "desc": d, "color": c} for _, out, t, d, c in PAGES],
            "art": [f"media/{f}" for f in art], "repo": REPO}
    tpl = open(os.path.join(HERE, "site_index.html")).read()
    open(os.path.join(SITE, "index.html"), "w").write(tpl.replace("/*DATA*/", json.dumps(data)))
    print("built", SITE, "with", len(PAGES), "pages")


main()
