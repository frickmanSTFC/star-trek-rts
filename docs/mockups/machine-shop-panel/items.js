// Machine Shop item graphics + descriptions (owner 2026-11-12: "graphics for each build ... on the cards, description of
// what it does"). Vector illustrations in one style: 3/4 isometric objects, light from the top left, dark outline, faction-
// neutral colony-service colours; kits are crates in their kit colour with an emblem. window.ITEM_GFX[id](size) -> SVG.
(() => {
  const O = "#0b1020"; // outline
  const sh = (hex, k) => { // shade a #rrggbb by k (-1..1)
    const n = parseInt(hex.slice(1), 16); let r = n >> 16, g = (n >> 8) & 255, b = n & 255;
    const f = (c) => Math.round(k >= 0 ? c + (255 - c) * k : c * (1 + k));
    return `rgb(${f(r)},${f(g)},${f(b)})`;
  };
  const svg = (body, size) => `<svg viewBox="0 0 96 96" width="${size}" height="${size}" role="img" aria-hidden="true">
    <ellipse cx="48" cy="86" rx="30" ry="6" fill="#000" opacity=".28"/>${body}</svg>`;
  // an isometric box: top centre (x, y), half-width w, half-depth d (screen), height h
  function box(x, y, w, d, h, col, extra = "") {
    const T = `${x},${y - d} ${x + w},${y} ${x},${y + d} ${x - w},${y}`;
    const L = `${x - w},${y} ${x},${y + d} ${x},${y + d + h} ${x - w},${y + h}`;
    const R = `${x},${y + d} ${x + w},${y} ${x + w},${y + h} ${x},${y + d + h}`;
    return `<g stroke="${O}" stroke-width="2" stroke-linejoin="round"><polygon points="${L}" fill="${sh(col, -.12)}"/><polygon points="${R}" fill="${sh(col, -.38)}"/><polygon points="${T}" fill="${sh(col, .22)}"/></g>${extra}`;
  }
  // map a 24x24 emblem onto the left face of box(48, 34, 30, 15, 34)
  const onLeft = (g) => `<g transform="matrix(${30 / 24},${15 / 24},0,${34 / 24},18,34)">${g}</g>`;
  const onRight = (g) => `<g transform="matrix(${30 / 24},${-15 / 24},0,${34 / 24},48,49)">${g}</g>`;
  function crate(col, emblem) {
    const straps = `<g stroke="${O}" stroke-width="1.6" fill="none" opacity=".75">
      <polyline points="18,41 48,56 78,41"/><polyline points="18,61 48,76 78,61"/></g>
      <g fill="${sh(col, -.6)}" stroke="${O}" stroke-width="1.2"><rect x="44" y="49" width="8" height="5" rx="1"/></g>`;
    const decal = onLeft(`<circle cx="12" cy="12" r="9.5" fill="#0c1220" opacity=".82"/>${emblem}`);
    const stencil = onRight(`<rect x="3" y="9" width="18" height="5" rx="1" fill="#0c1220" opacity=".55"/><rect x="5" y="11" width="9" height="1.4" fill="${sh(col, .5)}"/>`);
    return box(48, 34, 30, 15, 34, col) + straps + decal + stencil +
      `<polyline points="18,34 48,19 78,34" fill="none" stroke="#fff" stroke-width="1.4" opacity=".35"/>`;
  }
  const E = {
    gear: (c) => `<g fill="${c}"><circle cx="12" cy="12" r="5.2"/>${[0, 45, 90, 135, 180, 225, 270, 315].map((a) =>
      `<rect x="10.6" y="3.2" width="2.8" height="4" transform="rotate(${a} 12 12)"/>`).join("")}</g><circle cx="12" cy="12" r="2" fill="#0c1220"/>`,
    bolt: (c) => `<polygon points="13,3.5 6.5,13 11,13 9.5,20.5 17.5,10 12.5,10" fill="${c}"/>`,
    tyre: (c) => `<circle cx="12" cy="12" r="7" fill="none" stroke="${c}" stroke-width="3.2" stroke-dasharray="2.2 1.2"/><circle cx="12" cy="12" r="2.6" fill="${c}"/>`,
    leaf: (c) => `<path d="M12 4 C17 8 18 14 12 20 C6 14 7 8 12 4 Z" fill="${c}"/><path d="M12 7 V18" stroke="#0c1220" stroke-width="1.2"/>`,
  };
  const P = {
    mechanical_spares: () => crate("#e0a33c", E.gear("#ffd38a")),
    electrical_spares: () => crate("#d9c23c", E.bolt("#fff3a0")),
    rover_spares: () => crate("#e07f3a", E.tyre("#ffc79a")),
    environmental_spares: () => crate("#4fb4c9", E.leaf("#b9f1ff")),
    structural_frame: () => {
      // an isometric lattice beam: two rails and diagonals
      const a = [14, 58], b = [74, 28], up = 16, dx = (b[0] - a[0]) / 5, dy = (b[1] - a[1]) / 5;
      let diag = "", posts = "";
      for (let i = 0; i < 5; i++) {
        const x0 = a[0] + dx * i, y0 = a[1] + dy * i;
        diag += `<line x1="${x0}" y1="${y0}" x2="${x0 + dx}" y2="${y0 + dy - up}"/>`;
        posts += `<line x1="${x0}" y1="${y0}" x2="${x0}" y2="${y0 - up}"/>`;
      }
      posts += `<line x1="${b[0]}" y1="${b[1]}" x2="${b[0]}" y2="${b[1] - up}"/>`;
      const rail = (o, w, col) => `<line x1="${a[0]}" y1="${a[1] + o}" x2="${b[0]}" y2="${b[1] + o}" stroke="${col}" stroke-width="${w}" stroke-linecap="round"/>`;
      const back = `<g transform="translate(8,6)" opacity=".85"><g stroke="${O}" stroke-width="5" stroke-linecap="round">${diag}${posts}</g><g stroke="#7d8aa3" stroke-width="2.6" stroke-linecap="round">${diag}${posts}</g>${rail(0, 6, O)}${rail(-up, 6, O)}${rail(0, 3.4, "#8c99b2")}${rail(-up, 3.4, "#8c99b2")}</g>`;
      return back + `<g stroke="${O}" stroke-width="5" stroke-linecap="round">${diag}${posts}</g><g stroke="#b4bfd4" stroke-width="2.6" stroke-linecap="round">${diag}${posts}</g>` +
        rail(0, 7, O) + rail(-up, 7, O) + rail(0, 4, "#cfd8e8") + rail(-up, 4, "#cfd8e8") +
        `<g fill="#e0a33c" stroke="${O}" stroke-width="1.5"><rect x="9" y="38" width="9" height="24" rx="2"/><rect x="70" y="10" width="9" height="24" rx="2"/></g>`;
    },
    hull_plating: () => {
      const plate = (y, col) => `<g stroke="${O}" stroke-width="2" stroke-linejoin="round">
        <path d="M14 ${y} L48 ${y - 17} L82 ${y} L48 ${y + 17} Z" fill="${sh(col, .18)}"/>
        <path d="M14 ${y} L48 ${y + 17} L48 ${y + 22} L14 ${y + 5} Z" fill="${sh(col, -.2)}"/>
        <path d="M48 ${y + 17} L82 ${y} L82 ${y + 5} L48 ${y + 22} Z" fill="${sh(col, -.42)}"/></g>
        <g fill="${O}">${[[30, y - 6], [40, y - 11], [56, y - 11], [66, y - 6], [40, y + 7], [56, y + 7]].map(([x, yy]) => `<circle cx="${x}" cy="${yy}" r="1.3"/>`).join("")}</g>`;
      return plate(64, "#7f8fa8") + plate(52, "#93a2bb") + plate(40, "#aab7cd") +
        `<path d="M28 33 L48 23 L68 33" stroke="#fff" stroke-width="1.5" fill="none" opacity=".45"/>`;
    },
    mechanism: () => {
      const gear = (cx, cy, r, teeth, col) => `<g stroke="${O}" stroke-width="2" stroke-linejoin="round" fill="${col}">
        <path d="${Array.from({ length: teeth * 2 }, (_, i) => { const a = Math.PI * i / teeth, rr = i % 2 ? r : r * 1.22;
          return `${i ? "L" : "M"}${(cx + Math.cos(a) * rr).toFixed(1)} ${(cy + Math.sin(a) * rr * .62).toFixed(1)}`; }).join(" ")} Z"/>
        <ellipse cx="${cx}" cy="${cy}" rx="${r * .35}" ry="${r * .22}" fill="${O}"/></g>`;
      return `<g transform="translate(-14,-4) scale(1.3)"><rect x="24" y="50" width="48" height="12" rx="3" fill="#4a5568" stroke="${O}" stroke-width="2"/>` +
        gear(36, 46, 17, 10, "#c9d2e2") + gear(62, 38, 12, 8, "#e0a33c") + gear(62, 34, 12, 8, "#f2bf63") +
        gear(36, 42, 17, 10, "#dfe6f1") + `<ellipse cx="36" cy="42" rx="6" ry="3.7" fill="${O}"/><ellipse cx="62" cy="34" rx="4.2" ry="2.6" fill="${O}"/></g>`;
    },
    power_coupling: () => `<g stroke="${O}" stroke-width="2" stroke-linejoin="round">
      <path d="M8 66 C18 70 22 58 30 56" fill="none" stroke="${O}" stroke-width="7" stroke-linecap="round"/><path d="M8 66 C18 70 22 58 30 56" fill="none" stroke="#3b4253" stroke-width="4" stroke-linecap="round"/>
      <path d="M88 34 C80 30 74 40 66 40" fill="none" stroke="${O}" stroke-width="7" stroke-linecap="round"/><path d="M88 34 C80 30 74 40 66 40" fill="none" stroke="#3b4253" stroke-width="4" stroke-linecap="round"/>
      <path d="M26 46 L70 26 L74 46 L30 66 Z" fill="#8c99b2"/><path d="M26 46 L30 66 L22 70 L18 50 Z" fill="#5d6a82"/>
      <ellipse cx="72" cy="36" rx="5" ry="10" fill="#c7d2e4" transform="rotate(-24 72 36)"/>
      <path d="M40 40 L52 34 L56 54 L44 60 Z" fill="#59c6ff"/><path d="M42 41 L50 37 L53 52 L45 56 Z" fill="#c8f0ff" opacity=".9"/></g>
      <g fill="#59c6ff" opacity=".35"><ellipse cx="48" cy="47" rx="16" ry="10" transform="rotate(-24 48 47)"/></g>`,
    control_module: () => box(48, 38, 32, 16, 14, "#2f3a52") +
      `<g transform="matrix(1,.5,-1,.5,48,22)" stroke="${O}" stroke-width="1.2">
        <rect x="2" y="2" width="28" height="28" rx="2" fill="#2f8f5a"/>
        <rect x="6" y="6" width="9" height="9" fill="#1b2433"/><rect x="18" y="6" width="8" height="5" fill="#1b2433"/><rect x="18" y="14" width="8" height="5" fill="#1b2433"/>
        <rect x="6" y="19" width="20" height="7" fill="#1b2433"/></g>
      <g fill="#7dff9a"><circle cx="30" cy="58" r="2"/><circle cx="36" cy="61" r="2"/></g><circle cx="42" cy="64" r="2" fill="#ffb84a"/>
      <g stroke="#e0c070" stroke-width="1.2"><line x1="56" y1="62" x2="56" y2="70"/><line x1="61" y1="59.5" x2="61" y2="67.5"/><line x1="66" y1="57" x2="66" y2="65"/><line x1="71" y1="54.5" x2="71" y2="62.5"/></g>`,
    seal_kit: () => {
      const ring = (cx, cy, rx, col, w) => `<ellipse cx="${cx}" cy="${cy}" rx="${rx}" ry="${rx * .5}" fill="none" stroke="${O}" stroke-width="${w + 3}"/><ellipse cx="${cx}" cy="${cy}" rx="${rx}" ry="${rx * .5}" fill="none" stroke="${col}" stroke-width="${w}"/>`;
      return ring(36, 64, 20, "#1f2a3c", 6) + ring(36, 58, 20, "#3f6f8f", 5) + ring(36, 52, 16, "#6cc4d8", 4) +
        `<g stroke="${O}" stroke-width="2"><rect x="58" y="22" width="16" height="44" rx="5" fill="#d8dfe9"/><rect x="61" y="14" width="10" height="10" rx="2" fill="#4fb4c9"/>
        <rect x="58" y="36" width="16" height="10" fill="#4fb4c9"/></g><rect x="61" y="25" width="3" height="36" rx="1.5" fill="#fff" opacity=".6"/>`;
    },
    prefab_module: () => box(48, 30, 32, 16, 34, "#c8cfdb") +
      `<g transform="matrix(${32 / 24},${16 / 24},0,${34 / 24},16,30)" stroke="${O}" stroke-width="1">
        <rect x="4" y="6" width="10" height="9" rx="1" fill="#6db7ff"/><rect x="5" y="7" width="4" height="3" fill="#d6ecff"/>
        <rect x="16" y="6" width="5" height="16" fill="#8d97a8"/><rect x="0" y="20" width="24" height="2" fill="#b48ce0"/></g>
      <g transform="matrix(${32 / 24},${-16 / 24},0,${34 / 24},48,46)" stroke="${O}" stroke-width="1">
        <rect x="3" y="5" width="18" height="4" fill="#9aa4b6"/><rect x="3" y="12" width="18" height="4" fill="#9aa4b6"/><rect x="0" y="20" width="24" height="2" fill="#b48ce0"/></g>
      <path d="M16 30 L48 14 L80 30" stroke="#fff" stroke-width="1.6" fill="none" opacity=".5"/>`,
    machinery_set: () => box(48, 50, 34, 17, 14, "#6b5a8a") +
      `<g stroke="${O}" stroke-width="2" stroke-linejoin="round">
        <rect x="24" y="22" width="22" height="26" rx="4" fill="#a7b2c6"/><g fill="#7c879c">${[27, 31, 35, 39].map((y) => `<rect x="24" y="${y}" width="22" height="2"/>`).join("")}</g>
        <ellipse cx="35" cy="22" rx="11" ry="5" fill="#cfd7e5"/>
        <rect x="54" y="16" width="10" height="34" rx="2" fill="#e0a33c"/><rect x="56.5" y="8" width="5" height="12" fill="#d8dfe9"/>
        <ellipse cx="59" cy="8" rx="6" ry="3" fill="#cfd7e5"/></g>
      <rect x="56" y="18" width="2.5" height="30" fill="#fff" opacity=".4"/>`,
    scout_drone: () => {
      const rotor = (cx, cy) => `<line x1="${cx}" y1="${cy}" x2="${cx}" y2="${cy - 8}" stroke="${O}" stroke-width="3"/>
        <ellipse cx="${cx}" cy="${cy - 9}" rx="13" ry="4" fill="#9ad8ff" opacity=".28" stroke="${O}" stroke-width="1.4"/>
        <ellipse cx="${cx}" cy="${cy - 9}" rx="2.2" ry="1.4" fill="${O}"/>`;
      return `<g stroke="${O}" stroke-width="5" stroke-linecap="round"><line x1="22" y1="44" x2="74" y2="62"/><line x1="74" y1="40" x2="22" y2="66"/></g>
        <g stroke="#7d8aa3" stroke-width="2.5" stroke-linecap="round"><line x1="22" y1="44" x2="74" y2="62"/><line x1="74" y1="40" x2="22" y2="66"/></g>
        ${box(48, 46, 15, 8, 9, "#d4dbe6")}
        <ellipse cx="48" cy="61" rx="5" ry="3" fill="#59c6ff" stroke="${O}" stroke-width="1.5"/>
        ${rotor(22, 44)}${rotor(74, 40)}${rotor(22, 66)}${rotor(74, 62)}
        <circle cx="40" cy="57" r="1.6" fill="#ff5a4a"/>`;
    },
  };
  window.ITEM_GFX = Object.fromEntries(Object.entries(P).map(([id, f]) => [id, (size = 96) => svg(f(), size)]));
  window.ITEM_DESC = {
    mechanical_spares: "A crate of bearings, seals, belts and fasteners. The crew use it to repair mining crawlers and most buildings (mine, refinery, storage, Machine Shop). One kit restores a set amount of condition.",
    electrical_spares: "Breakers, cabling and power cells for the high-voltage kit. Repairs the Fusion Reactor and the Research Lab.",
    rover_spares: "Suspension parts, armour patches and a spare tyre. Repairs survey rovers and the construction rover, and fits new tyres on the repair pad.",
    environmental_spares: "Filters, membranes and pressure seals. Keeps Pressure Domes and Life Support Plants airtight.",
    structural_frame: "A welded duranium truss with tritanium joints. The skeleton of mechanical spare kits and prefab modules.",
    hull_plating: "Pressed parasteel plates, riveted and ready to fit. Armour for rover spares and the walls of prefab modules.",
    mechanism: "A geared drive train with its housing. Goes into mechanical and rover spares, and into machinery sets.",
    power_coupling: "A sealed connector that joins high-power lines. Goes into electrical spares, machinery sets and scout drones.",
    control_module: "A ruggedised circuit board in a sealed case: the brain of a machine. Goes into electrical and environmental spares, machinery sets and scout drones.",
    seal_kit: "Gaskets, O-rings and sealant. Goes into environmental spares.",
    prefab_module: "A ready-made wall and floor section. A construction part for bigger buildings (with later missions).",
    machinery_set: "A motor, a pump and their controls, boxed. A construction part for industry upgrades (with later missions).",
    scout_drone: "A packed, folded scout drone. A Drone Pad takes one from stock into each empty bay.",
  };
})();
