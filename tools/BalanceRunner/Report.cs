using System.Text.Json;

namespace FinalFrontier.Balance;

/// <summary>Writes a report folder: report.html (self-contained, inline SVG charts drawn by a small script),
/// summary.json (measures per variant) and runs/ (full JSON + CSV series per run).</summary>
public static class Report
{
    /// <summary>The series the HTML page embeds (the full set stays in runs/*.json and *.csv).</summary>
    private static readonly string[] ChartSeries =
    {
        "stock:duranium", "stock:tritanium", "stock:duranium_ore", "stock:tritanium_ore", "stock:parasteel", "stock:deuterium", "stock:crew",
        "income:duranium", "income:tritanium", "storage_pct", "storage_cap", "storage_used", "power_supply:1", "power_use:1",
        "research_known", "crawlers_mining", "rover_integrity_avg", "buildings", "buildings_idle", "surveyed_tiles",
    };

    public static void Write(string outDir, IReadOnlyList<Variant> variants, string title, double minutes, int sample, bool writeRuns = true)
    {
        Directory.CreateDirectory(outDir);
        var keys = Aggregate.MilestoneKeys(variants);
        foreach (var v in variants) Aggregate.Measure(v, keys);
        if (writeRuns)
        {
            var runs = Path.Combine(outDir, "runs");
            Directory.CreateDirectory(runs);
            foreach (var v in variants)
                foreach (var r in v.Runs)
                {
                    var name = Safe($"{v.Label}-seed{r.Seed}");
                    File.WriteAllText(Path.Combine(runs, name + ".json"), JsonSerializer.Serialize(r, Aggregate.Json));
                    File.WriteAllText(Path.Combine(runs, name + ".csv"), Aggregate.Csv(r));
                }
        }
        var summary = new
        {
            title,
            minutes,
            sample_seconds = sample,
            seeds = variants[0].Runs.Select(r => r.Seed).ToList(),
            variants = variants.Select(v => new
            {
                v.Label,
                v.Data,
                v.Order,
                v.Faction,
                v.Edits,
                runs = v.Runs.Select(r => new { r.Seed, r.StateHash, r.LandingTile, r.TicksPerSecond, r.Speedup, r.StepsDone, r.StepsTotal }),
                measures = v.Measures,
            }),
        };
        var pretty = new JsonSerializerOptions(Aggregate.Json) { WriteIndented = true };
        File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonSerializer.Serialize(summary, pretty));
        File.WriteAllText(Path.Combine(outDir, "report.html"), Html(variants, title, minutes, sample));
    }

    private static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    private static string Html(IReadOnlyList<Variant> variants, string title, double minutes, int sample)
    {
        var data = new
        {
            title,
            minutes,
            sample,
            variants = variants.Select(v => new
            {
                v.Label,
                v.Data,
                v.Order,
                v.Faction,
                v.Edits,
                measures = v.Measures,
                runs = v.Runs.Select(r => new
                {
                    r.Seed,
                    r.StateHash,
                    r.LandingTile,
                    r.TicksPerSecond,
                    r.Speedup,
                    r.WallMs,
                    r.StepsDone,
                    r.StepsTotal,
                    times = r.Times,
                    series = ChartSeries.Where(r.Series.ContainsKey).ToDictionary(k => k, k => r.Series[k]),
                    r.Milestones,
                    r.Stalls,
                    r.StatusSeconds,
                    r.CrawlerSeconds,
                    r.EndBuildings,
                    r.EndUnits,
                    r.EndTechs,
                    log = r.Log.Select(e => new object[] { System.Math.Round(e.Tick / 15.0, 1), e.Text }),
                }),
            }),
        };
        var json = JsonSerializer.Serialize(data, Aggregate.Json).Replace("</", "<\\/");
        return Template.Replace("__TITLE__", System.Net.WebUtility.HtmlEncode(title)).Replace("__DATA__", json);
    }

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
:root {
  color-scheme: light;
  --bg: #f4f4f1; --surface: #fcfcfb; --line: #dcdbd5; --grid: #e9e8e3;
  --text: #0b0b0b; --text2: #52514e; --muted: #77766f;
  --s1: #2a78d6; --s2: #eb6834; --s3: #1baf7a; --s4: #eda100;
  --good: #0b7d3b; --bad: #c63d3d; --chg: #fff2cc;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    color-scheme: dark;
    --bg: #111110; --surface: #1a1a19; --line: #33322f; --grid: #262624;
    --text: #f0efec; --text2: #c3c2b7; --muted: #8f8e86;
    --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s4: #c98500;
    --good: #4cc27a; --bad: #ef7070; --chg: #3a3216;
  }
}
:root[data-theme="dark"] {
  color-scheme: dark;
  --bg: #111110; --surface: #1a1a19; --line: #33322f; --grid: #262624;
  --text: #f0efec; --text2: #c3c2b7; --muted: #8f8e86;
  --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s4: #c98500;
  --good: #4cc27a; --bad: #ef7070; --chg: #3a3216;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1180px; margin: 0 auto; padding: 20px 16px 48px; }
h1 { font-size: 22px; margin: 0 0 4px; }
h2 { font-size: 16px; margin: 28px 0 8px; }
.sub { color: var(--text2); margin: 0 0 14px; }
.card { background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 14px; }
.tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 10px; }
.tile .k { color: var(--text2); font-size: 12px; }
.tile .v { font-size: 20px; font-weight: 600; font-variant-numeric: tabular-nums; }
.tile .d { color: var(--muted); font-size: 12px; }
.legend { display: flex; flex-wrap: wrap; gap: 14px; font-size: 13px; color: var(--text2); margin: 6px 0; }
.legend i { display: inline-block; width: 18px; height: 0; border-top: 3px solid; vertical-align: middle; margin-right: 6px; }
.seg { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 8px; }
.seg button { font: inherit; font-size: 12.5px; padding: 4px 9px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface); color: var(--text2); cursor: pointer; }
.seg button.on { background: var(--text); color: var(--surface); border-color: var(--text); }
.chart { position: relative; width: 100%; }
.chart svg { display: block; width: 100%; height: auto; }
.tip { position: absolute; pointer-events: none; background: var(--surface); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; font-size: 12px; box-shadow: 0 2px 10px rgba(0,0,0,.15); white-space: nowrap; display: none; }
.scroll { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 13px; font-variant-numeric: tabular-nums; }
th, td { padding: 5px 8px; border-bottom: 1px solid var(--grid); text-align: right; white-space: nowrap; }
th:first-child, td:first-child { text-align: left; white-space: normal; }
th { color: var(--text2); font-weight: 600; font-size: 12px; position: sticky; top: 0; background: var(--surface); }
tr.group td { color: var(--text2); font-weight: 600; padding-top: 12px; border-bottom: 1px solid var(--line); }
td .rng { color: var(--muted); font-size: 11.5px; margin-left: 4px; }
td.chg { background: var(--chg); }
.up { color: var(--good); font-weight: 600; } .down { color: var(--bad); font-weight: 600; } .same { color: var(--muted); }
details { margin: 6px 0; } summary { cursor: pointer; color: var(--text2); }
.log { font: 12px/1.5 ui-monospace, Consolas, monospace; max-height: 320px; overflow: auto; background: var(--bg); padding: 8px; border-radius: 6px; }
.note { color: var(--text2); font-size: 13px; }
code { font: 12px ui-monospace, Consolas, monospace; }
@media (max-width: 600px) { .tile .v { font-size: 17px; } th, td { padding: 4px 5px; } }
</style>
</head>
<body>
<main>
<h1 id="title"></h1>
<p class="sub" id="sub"></p>
<div class="tiles" id="tiles"></div>
<h2>Over time</h2>
<div class="card">
  <div class="seg" id="metric"></div>
  <div class="legend" id="legend"></div>
  <div class="chart" id="chart"><div class="tip" id="tip"></div></div>
  <p class="note">Thin lines: one per seed. Thick line: the median of the seeds at each sample.</p>
</div>
<h2>Key numbers <span class="note" id="keynote"></span></h2>
<div class="card scroll"><table id="measures"></table></div>
<h2>Idle reasons (building-minutes, median of the seeds)</h2>
<div class="card scroll" id="idle"></div>
<h2>Runs</h2>
<div class="card" id="runs"></div>
</main>
<script>
"use strict";
const R = __DATA__;
const $ = (s) => document.querySelector(s);
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const COLORS = ["var(--s1)", "var(--s2)", "var(--s3)", "var(--s4)"];
const DASH = ["", "6 4", "2 3", "8 3 2 3"];
const V = R.variants;
const mmss = (s) => s == null ? "–" : `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, "0")}`;
const num = (v, d = 1) => v == null ? "–" : (Math.abs(v) >= 100 ? Math.round(v).toLocaleString("en-GB") : (+v.toFixed(d)).toLocaleString("en-GB"));
const fmt = (v, unit) => unit === "time" ? mmss(v) : unit === "%" ? `${num(v, 0)}%` : unit === "min" ? `${num(v, 1)} min` : unit === "×" ? `×${num(v, 0)}` : num(v, 1);

$("#title").textContent = R.title;
const seeds = V[0].runs.map((r) => r.seed);
$("#sub").innerHTML = `${V.length > 1 ? V.length + " variants · " : ""}${seeds.length} seed${seeds.length > 1 ? "s" : ""} (${seeds.join(", ")}) · ${R.minutes} min of game time each · sampled every ${R.sample} s · `
  + V.map((v, i) => `<b style="color:${COLORS[i]}">${esc(v.label)}</b>: order <code>${esc(v.order)}</code>, ${esc(v.faction)}, data <code>${esc(v.data)}</code>${v.edits.length ? ", edits <code>" + v.edits.map(esc).join("</code>, <code>") + "</code>" : ""}`).join(" · ");

// ---- tiles: the headline numbers of the first variant (and the change in the others)
const M = (v, key) => v.measures.find((m) => m.key === key);
const TILES = ["ms:first_metal", "ms:built:refinery", "ms:first_parasteel", "ms:second_base", "end:duranium", "end:tritanium", "peak_storage", "stall:storage_full", "crawler_uptime", "idle_share", "speed"];
$("#tiles").innerHTML = TILES.map((k) => {
  const m = M(V[0], k); if (!m) return "";
  const others = V.slice(1).map((v, i) => { const o = M(v, k); return `<span style="color:${COLORS[i + 1]}">${esc(v.label)} ${fmt(o.spread.median, o.unit)}</span>`; }).join(" · ");
  const reach = m.spread.reached < m.spread.runs ? ` · reached in ${m.spread.reached}/${m.spread.runs}` : "";
  return `<div class="card tile"><div class="k">${esc(m.label)}</div><div class="v">${fmt(m.spread.median, m.unit)}</div><div class="d">${V.length > 1 ? esc(V[0].label) + " · " : ""}seeds ${fmt(m.spread.min, m.unit)} – ${fmt(m.spread.max, m.unit)}${reach}${others ? "<br>" + others : ""}</div></div>`;
}).join("");

// ---- chart
const METRICS = [
  ["stock:duranium", "Duranium"], ["stock:tritanium", "Tritanium"], ["stock:duranium_ore", "Duranium ore"], ["stock:tritanium_ore", "Tritanium ore"],
  ["stock:parasteel", "Parasteel"], ["stock:deuterium", "Deuterium"], ["income:duranium", "Duranium income/min"], ["income:tritanium", "Tritanium income/min"],
  ["storage_pct", "Storage fill %"], ["storage_cap", "Storage capacity"], ["power", "Power, main base"], ["research_known", "Techs known"],
  ["crawlers_mining", "Crawlers digging"], ["rover_integrity_avg", "Rover integrity (mean)"], ["buildings", "Finished buildings"], ["buildings_idle", "Idle buildings"], ["surveyed_tiles", "Surveyed tiles"],
];
let metric = "stock:duranium";
$("#metric").innerHTML = METRICS.map(([k, n]) => `<button type="button" data-k="${k}">${esc(n)}</button>`).join("");
$("#metric").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) { metric = b.dataset.k; draw(); } });

function median(xs) { const s = xs.filter((x) => x != null).sort((a, b) => a - b); if (!s.length) return null; const h = s.length >> 1; return s.length % 2 ? s[h] : (s[h - 1] + s[h]) / 2; }
function lines(key) {
  // [{variant, seed|null (median), pts:[[t,v]], dash, width}]
  const out = [];
  const keys = key === "power" ? [["power_use:1", ""], ["power_supply:1", "supply"]] : [[key, ""]];
  V.forEach((v, vi) => keys.forEach(([k, tag], ki) => {
    const runs = v.runs.filter((r) => r.series[k]);
    if (!runs.length) return;
    runs.forEach((r) => out.push({ vi, seed: r.seed, tag, pts: r.times.map((t, i) => [t / 60, r.series[k][i]]), dash: ki ? "2 3" : DASH[vi], width: 1, op: runs.length > 1 ? 0.35 : 1 }));
    if (runs.length > 1) {
      const n = Math.min(...runs.map((r) => r.times.length));
      out.push({ vi, seed: null, tag, pts: runs[0].times.slice(0, n).map((t, i) => [t / 60, median(runs.map((r) => r.series[k][i]))]), dash: ki ? "2 3" : DASH[vi], width: 2.5, op: 1 });
    }
  }));
  return out;
}
function niceMax(v) { if (v <= 0) return 1; const p = Math.pow(10, Math.floor(Math.log10(v))); for (const m of [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (m * p >= v) return m * p; return 10 * p; }
function draw() {
  document.querySelectorAll("#metric button").forEach((b) => b.classList.toggle("on", b.dataset.k === metric));
  const L = lines(metric);
  const W = 1000, H = 340, ml = 56, mr = 16, mt = 12, mb = 34;
  const xMax = R.minutes;
  const yMax = metric === "storage_pct" ? 100 : niceMax(Math.max(1, ...L.flatMap((l) => l.pts.map((p) => p[1] ?? 0))));
  const X = (t) => ml + (t / xMax) * (W - ml - mr), Y = (v) => mt + (1 - v / yMax) * (H - mt - mb);
  let svg = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(METRICS.find((m) => m[0] === metric)[1])} over ${R.minutes} minutes">`;
  for (let i = 0; i <= 4; i++) { const v = yMax * i / 4; svg += `<line x1="${ml}" x2="${W - mr}" y1="${Y(v)}" y2="${Y(v)}" stroke="var(--grid)"/><text x="${ml - 6}" y="${Y(v) + 4}" text-anchor="end" font-size="11" fill="var(--muted)">${num(v, 0)}</text>`; }
  const step = xMax <= 20 ? 5 : xMax <= 60 ? 10 : 30;
  for (let t = 0; t <= xMax + 1e-9; t += step) svg += `<text x="${X(t)}" y="${H - 12}" text-anchor="middle" font-size="11" fill="var(--muted)">${t} min</text>`;
  svg += `<line x1="${ml}" x2="${W - mr}" y1="${Y(0)}" y2="${Y(0)}" stroke="var(--line)"/>`;
  for (const l of L) {
    const d = l.pts.filter((p) => p[1] != null).map((p, i) => `${i ? "L" : "M"}${X(p[0]).toFixed(1)},${Y(p[1]).toFixed(1)}`).join("");
    svg += `<path d="${d}" fill="none" stroke="${COLORS[l.vi]}" stroke-width="${l.width}" stroke-opacity="${l.op}" stroke-dasharray="${l.dash}" stroke-linejoin="round"/>`;
  }
  svg += `<line id="cross" x1="0" x2="0" y1="${mt}" y2="${H - mb}" stroke="var(--muted)" stroke-dasharray="3 3" visibility="hidden"/><rect id="hit" x="${ml}" y="${mt}" width="${W - ml - mr}" height="${H - mt - mb}" fill="transparent"/></svg>`;
  const box = $("#chart"); box.querySelectorAll("svg").forEach((s) => s.remove()); box.insertAdjacentHTML("afterbegin", svg);
  $("#legend").innerHTML = V.map((v, i) => `<span><i style="border-color:${COLORS[i]};${i ? "border-top-style:dashed" : ""}"></i>${esc(v.label)}</span>`).join("") + (metric === "power" ? `<span><i style="border-color:var(--muted);border-top-style:dotted"></i>supply (dotted) vs use</span>` : "");
  const hit = box.querySelector("#hit"), cross = box.querySelector("#cross"), tip = $("#tip"), svgEl = box.querySelector("svg");
  const med = L.filter((l) => l.seed === null || V.every((v) => v.runs.length === 1));
  hit.addEventListener("pointermove", (e) => {
    const r = svgEl.getBoundingClientRect(); const sx = (e.clientX - r.left) / r.width * W; const t = Math.max(0, Math.min(xMax, (sx - ml) / (W - ml - mr) * xMax));
    cross.setAttribute("x1", X(t)); cross.setAttribute("x2", X(t)); cross.setAttribute("visibility", "visible");
    const rows = med.map((l) => { let best = l.pts[0]; for (const p of l.pts) if (Math.abs(p[0] - t) < Math.abs(best[0] - t)) best = p; return `<div><span style="color:${COLORS[l.vi]}">■</span> ${esc(V[l.vi].label)}${l.tag ? " " + l.tag : ""}${l.seed !== null && V[l.vi].runs.length === 1 ? "" : " (median)"}: <b>${num(best[1], 1)}</b></div>`; });
    tip.innerHTML = `<div><b>${mmss(t * 60)}</b></div>${rows.join("")}`; tip.style.display = "block";
    const left = (e.clientX - r.left) + 14; tip.style.left = Math.min(left, r.width - tip.offsetWidth - 4) + "px"; tip.style.top = "8px";
  });
  hit.addEventListener("pointerleave", () => { cross.setAttribute("visibility", "hidden"); tip.style.display = "none"; });
}
draw();

// ---- measures table
function delta(m0, m, i) {
  const a = m0.spread.median, b = m.spread.median;
  if (a == null && b == null) return `<span class="same">–</span>`;
  if (a == null || b == null) return b == null ? `<span class="${m.good_up === false ? "up" : "down"}">not reached</span>` : `<span class="${m.good_up === false ? "down" : "up"}">reached</span>`;
  const d = b - a; const rel = Math.abs(a) > 1e-9 ? Math.abs(d / a) : Math.abs(d);
  if (Math.abs(d) < 1e-9 || rel < 0.02) return `<span class="same">same</span>`;
  const better = m.good_up == null ? null : (d > 0) === m.good_up;
  const cls = better == null ? "" : better ? "up" : "down";
  const txt = m.unit === "time" ? (d > 0 ? "+" : "−") + mmss(Math.abs(d)) : (d > 0 ? "+" : "−") + fmt(Math.abs(d), m.unit);
  return `<span class="${cls}">${txt}</span>`;
}
(function () {
  const cols = V.map((v, i) => `<th style="color:${COLORS[i]}">${esc(v.label)}<br><span class="rng">median (min–max)</span></th>` + (i ? `<th>change vs ${esc(V[0].label)}</th>` : "")).join("");
  let html = `<thead><tr><th>Measure</th>${cols}</tr></thead><tbody>`;
  let group = "";
  V[0].measures.forEach((m0, k) => {
    if (m0.group !== group) { group = m0.group; html += `<tr class="group"><td colspan="${1 + V.length * 2}">${esc(group)}</td></tr>`; }
    html += `<tr><td>${esc(m0.label)}</td>` + V.map((v, i) => {
      const m = v.measures[k]; const s = m.spread;
      const reach = m.unit === "time" && s.reached < s.runs ? ` <span class="rng">${s.reached}/${s.runs}</span>` : "";
      const changed = i && delta(m0, m, i).indexOf("same") < 0;
      const cell = `<td class="${changed ? "chg" : ""}">${fmt(s.median, m.unit)}${s.runs > 1 && s.min != null ? `<span class="rng">(${fmt(s.min, m.unit)}–${fmt(s.max, m.unit)})</span>` : ""}${reach}</td>`;
      return cell + (i ? `<td>${delta(m0, m, i)}</td>` : "");
    }).join("") + `</tr>`;
  });
  $("#measures").innerHTML = html + "</tbody>";
  $("#keynote").textContent = V.length > 1 ? "· green = better for the player, red = worse; changes under 2% count as the same" : "";
})();

// ---- idle reasons
(function () {
  const statuses = ["Working", "LowPower", "NoInput", "StorageFull", "NoUpkeep", "Depleted", "Broken"];
  $("#idle").innerHTML = V.map((v, i) => {
    const types = [...new Set(v.runs.flatMap((r) => Object.keys(r.status_seconds)))];
    const rows = types.map((t) => `<tr><td>${esc(t)}</td>` + statuses.map((s) => { const x = median(v.runs.map((r) => (r.status_seconds[t] || {})[s] || 0)) / 60; return `<td>${x ? num(x, 1) : ""}</td>`; }).join("") + "</tr>").join("");
    const crawler = ["mining", "storage_full", "no_destination", "unfolding", "driving", "broken", "repairing", "idle"];
    const crow = `<tr><td>crawlers (crawler-minutes)</td><td colspan="${statuses.length}" style="text-align:left">` + crawler.map((s) => { const x = median(v.runs.map((r) => r.crawler_seconds[s] || 0)) / 60; return x ? `${s} ${num(x, 1)}` : ""; }).filter(Boolean).join(" · ") + "</td></tr>";
    return `${V.length > 1 ? `<h3 style="color:${COLORS[i]};font-size:14px">${esc(v.label)}</h3>` : ""}<table><thead><tr><th>Building</th>${statuses.map((s) => `<th>${s}</th>`).join("")}</tr></thead><tbody>${rows}${crow}</tbody></table>`;
  }).join("");
})();

// ---- runs: speed, hash, stalls, bot log
$("#runs").innerHTML = V.map((v, i) => v.runs.map((r) => {
  const stalls = r.stalls.filter((s) => s.end - s.start >= 10).map((s) => `${mmss(s.start)}–${mmss(s.end)} ${esc(s.kind)}`).join(" · ") || "none of 10 s or more";
  return `<details><summary><b style="color:${COLORS[i]}">${esc(v.label)}</b> seed ${r.seed} · ${num(r.ticks_per_second, 0)} ticks/s (×${num(r.speedup, 0)} real time, ${num(r.wall_ms / 1000, 1)} s) · steps ${r.steps_done}/${r.steps_total} · hash <code>${esc(r.state_hash)}</code></summary>
  <p class="note">Landing tile ${r.landing_tile}. End: ${Object.entries(r.end_buildings).map(([k, n]) => `${n}× ${esc(k)}`).join(", ")}; units ${Object.entries(r.end_units).map(([k, n]) => `${n}× ${esc(k)}`).join(", ")}; techs ${r.end_techs.map(esc).join(", ") || "none"}.<br>Stalls: ${stalls}</p>
  <div class="log">${r.log.map(([t, s]) => `${mmss(t)}  ${esc(s)}`).join("<br>")}</div></details>`;
}).join("")).join("");
</script>
</body>
</html>
""";
}
