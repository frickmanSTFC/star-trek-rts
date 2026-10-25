"""Generate isometric SVG concept art for the Federation Colony Hub, levels 1-5 (v2: Starfleet style, grows from a landed colony pod).

One 120x120 world-unit plot, viewed isometrically. Every level reuses the same
anchors (hub at the centre-back, landing pad front-right, comm mast back-left)
so the progression reads as one site growing over time.

Usage: python3 gen_colony_hub.py OUT_DIR
"""
import math
import os
import random
import sys


W, H = 800, 600
CX, CY, S = 400, 158, 3.1
C30, S30 = 0.8660254, 0.5

# palette
SKY_TOP, SKY_BOT = "#0b1322", "#1b2a40"
GRASS, GRASS_DK, GRASS_LT = "#5f8f56", "#4d7a47", "#76a865"
EARTH_L, EARTH_R = "#6b5440", "#54412f"
PAVE, PAVE_DK, PAVE_LINE = "#c9c3b6", "#a9a294", "#e6e1d6"
HULL_T, HULL_L, HULL_R = "#eef1f6", "#c9d0dc", "#a3adbd"
HULL2_T, HULL2_L, HULL2_R = "#dfe4ec", "#b8c1cf", "#939eb0"
BLUE, BLUE_DK = "#4a7fd6", "#365fa6"
WIN = "#ffd98a"
GLASS = "#9fd0f0"
RED = "#e0574a"
GLOW = "#7fd4ff"
TREE_DK, TREE_LT, TRUNK = "#2f5a34", "#45784a", "#5b4632"
SHADOW = "#1d2a1c"


def iso(x, y, z=0.0):
    return (CX + (x - y) * C30 * S, CY + (x + y) * S30 * S - z * S)


def pts(points):
    return " ".join(f"{px:.1f},{py:.1f}" for px, py in points)


def hull(points):
    p = sorted(set((round(a, 2), round(b, 2)) for a, b in points))
    if len(p) <= 2:
        return p

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lo, up = [], []
    for q in p:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    for q in reversed(p):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0:
            up.pop()
        up.append(q)
    return lo[:-1] + up[:-1]


class Scene:
    def __init__(self, seed):
        self.ground = []   # flat things drawn before objects
        self.shadows = []
        self.objs = []     # (depth, svg)
        self.sky = []      # drawn last (aircraft)
        self.rng = random.Random(seed)

    # ---------- flat ground features ----------
    def quad(self, x, y, w, d, fill, z=0.0, stroke=None, sw=1, layer="ground", opacity=None):
        p = [iso(x, y, z), iso(x + w, y, z), iso(x + w, y + d, z), iso(x, y + d, z)]
        extra = f' stroke="{stroke}" stroke-width="{sw}"' if stroke else ""
        op = f' fill-opacity="{opacity}"' if opacity is not None else ""
        s = f'<polygon points="{pts(p)}" fill="{fill}"{extra}{op}/>'
        (self.ground if layer == "ground" else self.objs.append((x + y + w + d, s)) or [])
        if layer == "ground":
            self.ground.append(s)

    def line(self, x1, y1, x2, y2, color, sw=1.2, z=0.0, dash=None, layer="ground", depth=None):
        a, b = iso(x1, y1, z), iso(x2, y2, z)
        ds = f' stroke-dasharray="{dash}"' if dash else ""
        s = f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{color}" stroke-width="{sw}" stroke-linecap="round"{ds}/>'
        if layer == "ground":
            self.ground.append(s)
        else:
            self.objs.append((depth if depth is not None else (x1 + y1 + x2 + y2) / 2, s))

    def disc(self, x, y, r, fill, z=0.0, stroke=None, sw=1, dash=None, layer="ground", opacity=None):
        c = iso(x, y, z)
        rx, ry = 1.2247 * r * S, 0.7071 * r * S
        extra = f' stroke="{stroke}" stroke-width="{sw}"' if stroke else ""
        extra += f' stroke-dasharray="{dash}"' if dash else ""
        extra += f' fill-opacity="{opacity}"' if opacity is not None else ""
        s = f'<ellipse cx="{c[0]:.1f}" cy="{c[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{fill}"{extra}/>'
        if layer == "ground":
            self.ground.append(s)
        else:
            self.objs.append((x + y, s))

    def shadow(self, x, y, w, d, h, opacity=0.28):
        k = 0.45 * h
        foot = [(x, y), (x + w, y), (x + w, y + d), (x, y + d)]
        sh = foot + [(a + k, b - k) for a, b in foot]
        p = hull([iso(a, b) for a, b in sh])
        self.shadows.append(f'<polygon points="{pts(p)}" fill="{SHADOW}" fill-opacity="{opacity}"/>')

    # ---------- solids ----------
    def box(self, x, y, w, d, h, z=0.0, top=HULL_T, left=HULL_L, right=HULL_R, windows=(), band=None,
            shadow=True, depth=None, glass=False, door=None):
        if shadow and z < 1:
            self.shadow(x, y, w, d, h)
        out = []
        tp = [iso(x, y, z + h), iso(x + w, y, z + h), iso(x + w, y + d, z + h), iso(x, y + d, z + h)]
        lf = [iso(x, y + d, z), iso(x + w, y + d, z), iso(x + w, y + d, z + h), iso(x, y + d, z + h)]
        rt = [iso(x + w, y, z), iso(x + w, y + d, z), iso(x + w, y + d, z + h), iso(x + w, y, z + h)]
        op = ' fill-opacity="0.55"' if glass else ""
        out.append(f'<polygon points="{pts(lf)}" fill="{left}"{op}/>')
        out.append(f'<polygon points="{pts(rt)}" fill="{right}"{op}/>')
        out.append(f'<polygon points="{pts(tp)}" fill="{top}"{op} stroke="#ffffff" stroke-opacity="0.55" stroke-width="0.7" stroke-linejoin="round"/>')
        e0, e1 = iso(x + w, y + d, z), iso(x + w, y + d, z + h)
        out.append(f'<line x1="{e0[0]:.1f}" y1="{e0[1]:.1f}" x2="{e1[0]:.1f}" y2="{e1[1]:.1f}" stroke="#ffffff" stroke-opacity="0.35" stroke-width="0.7"/>')
        if glass:
            for t in range(1, 4):
                a = iso(x + w * t / 4, y + d, z); b = iso(x + w * t / 4, y + d, z + h)
                out.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#ffffff" stroke-opacity="0.55" stroke-width="0.8"/>')
                a = iso(x + w, y + d * t / 4, z); b = iso(x + w, y + d * t / 4, z + h)
                out.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#ffffff" stroke-opacity="0.45" stroke-width="0.8"/>')
        if band:
            bz0, bz1 = band
            for face in ("l", "r"):
                if face == "l":
                    q = [iso(x, y + d, z + bz0), iso(x + w, y + d, z + bz0), iso(x + w, y + d, z + bz1), iso(x, y + d, z + bz1)]
                    col = BLUE
                else:
                    q = [iso(x + w, y, z + bz0), iso(x + w, y + d, z + bz0), iso(x + w, y + d, z + bz1), iso(x + w, y, z + bz1)]
                    col = BLUE_DK
                out.append(f'<polygon points="{pts(q)}" fill="{col}"/>')
        for zr in windows:
            ins = 1.6
            a, b = iso(x + ins, y + d, z + zr), iso(x + w - ins, y + d, z + zr)
            out.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{WIN}" stroke-width="2.2" stroke-dasharray="3.2 2.6"/>')
            a, b = iso(x + w, y + ins, z + zr), iso(x + w, y + d - ins, z + zr)
            out.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{WIN}" stroke-opacity="0.8" stroke-width="2.2" stroke-dasharray="3.2 2.6"/>')
        if door:
            # door on the +y (left-front) face: (offset along x, width, height)
            ox, dw, dh = door
            q = [iso(x + ox, y + d, z), iso(x + ox + dw, y + d, z), iso(x + ox + dw, y + d, z + dh), iso(x + ox, y + d, z + dh)]
            out.append(f'<polygon points="{pts(q)}" fill="{BLUE}"/>')
            q2 = [iso(x + ox + 0.4, y + d, z + dh + 0.3), iso(x + ox + dw - 0.4, y + d, z + dh + 0.3)]
            out.append(f'<line x1="{q2[0][0]:.1f}" y1="{q2[0][1]:.1f}" x2="{q2[1][0]:.1f}" y2="{q2[1][1]:.1f}" stroke="{WIN}" stroke-width="1.4"/>')
        self.objs.append((depth if depth is not None else x + y + (w + d) / 2, "".join(out)))

    def cyl(self, x, y, r, h, z=0.0, side_l=HULL_L, side_r=HULL_R, top=HULL_T, windows=(), band=None, shadow=True, depth=None, cap=None):
        if shadow and z < 1:
            self.shadow(x - r, y - r, 2 * r, 2 * r, h, 0.24)
        b, t = iso(x, y, z), iso(x, y, z + h)
        rx, ry = 1.2247 * r * S, 0.7071 * r * S
        out = []
        out.append(f'<path d="M{b[0]-rx:.1f},{t[1]:.1f} L{b[0]-rx:.1f},{b[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]:.1f},{b[1]+ry:.1f} L{b[0]:.1f},{t[1]+ry:.1f} A{rx:.1f},{ry:.1f} 0 0 1 {b[0]-rx:.1f},{t[1]:.1f} Z" fill="{side_l}"/>')
        out.append(f'<path d="M{b[0]:.1f},{t[1]+ry:.1f} L{b[0]:.1f},{b[1]+ry:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{b[1]:.1f} L{b[0]+rx:.1f},{t[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 1 {b[0]:.1f},{t[1]+ry:.1f} Z" fill="{side_r}"/>')
        if band:
            for bz in band:
                yy = b[1] - bz * S
                out.append(f'<path d="M{b[0]-rx:.1f},{yy:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{yy:.1f}" fill="none" stroke="{BLUE}" stroke-width="3"/>')
        for zr in windows:
            yy = b[1] - zr * S
            out.append(f'<path d="M{b[0]-rx*0.92:.1f},{yy+ry*0.25:.1f} A{rx*0.92:.1f},{ry:.1f} 0 0 0 {b[0]+rx*0.92:.1f},{yy+ry*0.25:.1f}" fill="none" stroke="{WIN}" stroke-width="2" stroke-dasharray="3 2.6"/>')
        out.append(f'<ellipse cx="{t[0]:.1f}" cy="{t[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{top}"/>')
        if cap == "dome":
            out.append(f'<path d="M{t[0]-rx:.1f},{t[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {t[0]+rx:.1f},{t[1]:.1f} A{rx:.1f},{r*S*0.9:.1f} 0 0 0 {t[0]-rx:.1f},{t[1]:.1f} Z" fill="{GLASS}" fill-opacity="0.8" stroke="#ffffff" stroke-opacity="0.6" stroke-width="1"/>')
            out.append(f'<path d="M{t[0]-rx*0.5:.1f},{t[1]-r*S*0.55:.1f} Q{t[0]-rx*0.1:.1f},{t[1]-r*S*0.8:.1f} {t[0]+rx*0.2:.1f},{t[1]-r*S*0.75:.1f}" fill="none" stroke="#ffffff" stroke-opacity="0.8" stroke-width="1.6"/>')
        self.objs.append((depth if depth is not None else x + y + r, "".join(out)))

    def dome(self, x, y, r, z=0.0, fill=GLASS, depth=None, inner=None):
        self.shadow(x - r, y - r, 2 * r, 2 * r, r * 0.8, 0.2)
        c = iso(x, y, z)
        rx, ry = 1.2247 * r * S, 0.7071 * r * S
        out = [f'<ellipse cx="{c[0]:.1f}" cy="{c[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{PAVE_DK}"/>']
        if inner:
            out.append(inner)
        out.append(f'<path d="M{c[0]-rx:.1f},{c[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {c[0]+rx:.1f},{c[1]:.1f} A{rx:.1f},{r*S*1.05:.1f} 0 0 0 {c[0]-rx:.1f},{c[1]:.1f} Z" fill="{fill}" fill-opacity="0.55" stroke="#ffffff" stroke-opacity="0.7" stroke-width="1.2"/>')
        out.append(f'<path d="M{c[0]-rx*0.55:.1f},{c[1]-r*S*0.6:.1f} Q{c[0]-rx*0.15:.1f},{c[1]-r*S*0.95:.1f} {c[0]+rx*0.25:.1f},{c[1]-r*S*0.9:.1f}" fill="none" stroke="#ffffff" stroke-opacity="0.85" stroke-width="1.8"/>')
        self.objs.append((depth if depth is not None else x + y + r, "".join(out)))

    def tree(self, x, y, s=1.0):
        c = iso(x, y)
        self.shadows.append(f'<ellipse cx="{c[0]+5*s:.1f}" cy="{c[1]:.1f}" rx="{7*s:.1f}" ry="{3*s:.1f}" fill="{SHADOW}" fill-opacity="0.3"/>')
        out = (f'<line x1="{c[0]:.1f}" y1="{c[1]:.1f}" x2="{c[0]:.1f}" y2="{c[1]-7*s:.1f}" stroke="{TRUNK}" stroke-width="{1.8*s:.1f}"/>'
               f'<circle cx="{c[0]:.1f}" cy="{c[1]-11*s:.1f}" r="{6.5*s:.1f}" fill="{TREE_DK}"/>'
               f'<circle cx="{c[0]-1.6*s:.1f}" cy="{c[1]-12.5*s:.1f}" r="{4.2*s:.1f}" fill="{TREE_LT}"/>')
        self.objs.append((x + y, out))

    def rock(self, x, y, s=1.0):
        c = iso(x, y)
        self.ground.append(f'<path d="M{c[0]-4*s:.1f},{c[1]:.1f} L{c[0]-2*s:.1f},{c[1]-3*s:.1f} L{c[0]+3*s:.1f},{c[1]-3.5*s:.1f} L{c[0]+5*s:.1f},{c[1]:.1f} Z" fill="#7d7a70"/>'
                           f'<path d="M{c[0]-2*s:.1f},{c[1]-3*s:.1f} L{c[0]+3*s:.1f},{c[1]-3.5*s:.1f} L{c[0]+1*s:.1f},{c[1]-1*s:.1f} Z" fill="#9a968a"/>')

    def tufts(self, n, area, avoid):
        for _ in range(n):
            x = self.rng.uniform(*area[0]); y = self.rng.uniform(*area[1])
            if any(a0 <= x <= a1 and b0 <= y <= b1 for a0, a1, b0, b1 in avoid):
                continue
            c = iso(x, y)
            col = self.rng.choice([GRASS_DK, GRASS_LT])
            self.ground.append(f'<path d="M{c[0]-2:.1f},{c[1]:.1f} L{c[0]-1:.1f},{c[1]-3:.1f} M{c[0]:.1f},{c[1]:.1f} L{c[0]:.1f},{c[1]-4:.1f} M{c[0]+2:.1f},{c[1]:.1f} L{c[0]+1:.1f},{c[1]-3:.1f}" stroke="{col}" stroke-width="1" fill="none"/>')

    def people(self, spots):
        for x, y in spots:
            c = iso(x, y)
            col = self.rng.choice(["#c8463c", "#d9a43a", BLUE, "#e8ecf2"])
            self.objs.append((x + y, f'<rect x="{c[0]-1:.1f}" y="{c[1]-4.5:.1f}" width="2.2" height="4.5" fill="{col}"/><circle cx="{c[0]+0.1:.1f}" cy="{c[1]-5.6:.1f}" r="1.1" fill="#f0d4b8"/>'))

    def lamp(self, x, y, h=6):
        b, t = iso(x, y), iso(x, y, h)
        self.objs.append((x + y, f'<line x1="{b[0]:.1f}" y1="{b[1]:.1f}" x2="{t[0]:.1f}" y2="{t[1]:.1f}" stroke="#5b6577" stroke-width="1.2"/><circle cx="{t[0]:.1f}" cy="{t[1]:.1f}" r="2.2" fill="{WIN}"/><circle cx="{t[0]:.1f}" cy="{t[1]:.1f}" r="5" fill="{WIN}" fill-opacity="0.18"/>'))

    def beacon(self, x, y):
        b, t = iso(x, y), iso(x, y, 4)
        self.objs.append((x + y, f'<line x1="{b[0]:.1f}" y1="{b[1]:.1f}" x2="{t[0]:.1f}" y2="{t[1]:.1f}" stroke="#8a94a6" stroke-width="1.4"/><circle cx="{t[0]:.1f}" cy="{t[1]:.1f}" r="1.8" fill="{RED}"/>'))

    def mast(self, x, y, h, dish=True):
        self.cyl(x, y, 0.9, h, side_l="#b8c1cf", side_r="#8a94a6", top="#dfe4ec", shadow=False, depth=x + y + 2)
        t = iso(x, y, h)
        out = f'<circle cx="{t[0]:.1f}" cy="{t[1]-2:.1f}" r="2.4" fill="{RED}"/><circle cx="{t[0]:.1f}" cy="{t[1]-2:.1f}" r="6" fill="{RED}" fill-opacity="0.2"/>'
        if dish:
            m = iso(x, y, h * 0.7)
            out += (f'<path d="M{m[0]-2:.1f},{m[1]-9:.1f} Q{m[0]+10:.1f},{m[1]-4:.1f} {m[0]-2:.1f},{m[1]+7:.1f} Z" fill="#dfe4ec" stroke="#8a94a6" stroke-width="1"/>'
                    f'<line x1="{m[0]+1:.1f}" y1="{m[1]-1:.1f}" x2="{m[0]+8:.1f}" y2="{m[1]-1:.1f}" stroke="#8a94a6" stroke-width="1"/>')
        self.objs.append((x + y + 2.1, out))

    def pad(self, x, y, r, lights=True):
        self.disc(x, y, r, "#8f949c")
        self.disc(x, y, r * 0.86, "#a6abb3")
        self.disc(x, y, r * 0.62, "none", stroke=WIN, sw=1.4, dash="5 4")
        a, b = iso(x - r * 0.3, y, 0), iso(x + r * 0.3, y, 0)
        self.ground.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#eef1f6" stroke-width="2"/>')
        a, b = iso(x, y - r * 0.3, 0), iso(x, y + r * 0.3, 0)
        self.ground.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#eef1f6" stroke-width="2"/>')
        if lights:
            for k in range(8):
                ang = k * math.pi / 4
                c = iso(x + r * 0.93 * math.cos(ang), y + r * 0.93 * math.sin(ang))
                self.ground.append(f'<circle cx="{c[0]:.1f}" cy="{c[1]:.1f}" r="1.5" fill="{GLOW}"/>')

    def shuttle(self, x, y, z=0.0, s=1.0, landed=True):
        c = iso(x, y, z)
        if landed:
            self.shadows.append(f'<ellipse cx="{c[0]+4:.1f}" cy="{c[1]:.1f}" rx="{13*s:.1f}" ry="{4.5*s:.1f}" fill="{SHADOW}" fill-opacity="0.35"/>')
        body = (f'<path d="M{c[0]-14*s:.1f},{c[1]-3*s:.1f} L{c[0]+6*s:.1f},{c[1]-10*s:.1f} L{c[0]+15*s:.1f},{c[1]-6*s:.1f} L{c[0]+14*s:.1f},{c[1]-1*s:.1f} L{c[0]-4*s:.1f},{c[1]+4*s:.1f} Z" fill="{HULL_L}"/>'
                f'<path d="M{c[0]-14*s:.1f},{c[1]-3*s:.1f} L{c[0]+6*s:.1f},{c[1]-10*s:.1f} L{c[0]+15*s:.1f},{c[1]-6*s:.1f} L{c[0]-6*s:.1f},{c[1]-1*s:.1f} Z" fill="{HULL_T}"/>'
                f'<path d="M{c[0]+8*s:.1f},{c[1]-8.5*s:.1f} L{c[0]+14*s:.1f},{c[1]-6*s:.1f} L{c[0]+13*s:.1f},{c[1]-4*s:.1f} L{c[0]+8*s:.1f},{c[1]-5.5*s:.1f} Z" fill="#2a3a55"/>'
                f'<line x1="{c[0]-10*s:.1f}" y1="{c[1]-0.5*s:.1f}" x2="{c[0]+2*s:.1f}" y2="{c[1]-5*s:.1f}" stroke="{BLUE}" stroke-width="{1.6*s:.1f}"/>'
                f'<path d="M{c[0]-12*s:.1f},{c[1]+1*s:.1f} L{c[0]+4*s:.1f},{c[1]-5*s:.1f}" stroke="{GLOW}" stroke-width="{1.4*s:.1f}" stroke-linecap="round"/>')
        if landed:
            self.objs.append((x + y + 1, body))
        else:
            self.sky.append(body + f'<path d="M{c[0]-14*s:.1f},{c[1]-3*s:.1f} l{-10*s:.1f},{3*s:.1f}" stroke="{GLOW}" stroke-opacity="0.5" stroke-width="{2*s:.1f}" stroke-linecap="round"/>')

    def hover_truck(self, x, y):
        self.box(x, y, 6, 3.2, 2.2, z=0.8, top="#d9a43a", left="#c08a2a", right="#a07020", shadow=False)
        self.shadow(x, y, 6, 3.2, 1.2, 0.3)

    def crates(self, x, y, n=3):
        cols = [("#c8a070", "#a88050", "#8a6a40"), ("#8aa0b8", "#6f86a0", "#5a6f88")]
        for i in range(n):
            t, l, r = cols[i % 2]
            dx, dy = (i % 2) * 3.4, (i // 2) * 3.4
            self.box(x + dx, y + dy, 3, 3, 2.6, top=t, left=l, right=r, shadow=(i == 0))

    def solar(self, x, y, n=3):
        for i in range(n):
            self.box(x + i * 5.2, y, 4.6, 7, 0.6, z=1.4, top="#24406e", left="#1a2f52", right="#152642", shadow=False)
            a = iso(x + i * 5.2 + 2.3, y + 3.5, 0); b = iso(x + i * 5.2 + 2.3, y + 3.5, 1.4)
            self.objs.append((x + y + i * 5.2 + 5, f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#8a94a6" stroke-width="1"/>'))
            for g in range(1, 4):
                p, q = iso(x + i * 5.2, y + g * 7 / 4, 2.0), iso(x + i * 5.2 + 4.6, y + g * 7 / 4, 2.0)
                self.objs.append((x + y + i * 5.2 + 5.5, f'<line x1="{p[0]:.1f}" y1="{p[1]:.1f}" x2="{q[0]:.1f}" y2="{q[1]:.1f}" stroke="#5b8def" stroke-width="0.6"/>'))
        self.shadow(x, y, n * 5.2, 7, 1.0, 0.22)

    def tube(self, x, y, w, d):
        self.box(x, y, w, d, 2.6, z=0.6, top="#dfe4ec", left="#b8c1cf", right="#939eb0", shadow=False)

    def render(self, title_note=""):
        out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">',
               '<defs><linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">'
               f'<stop offset="0" stop-color="{SKY_TOP}"/><stop offset="1" stop-color="{SKY_BOT}"/></linearGradient>'
               '<radialGradient id="vig" cx="0.5" cy="0.55" r="0.65"><stop offset="0.6" stop-color="#000000" stop-opacity="0"/>'
               '<stop offset="1" stop-color="#000000" stop-opacity="0.45"/></radialGradient>'
               '<linearGradient id="hg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#dde3ec"/><stop offset="0.45" stop-color="#c3cbd8"/><stop offset="1" stop-color="#8e99ab"/></linearGradient>'
               '<radialGradient id="tg" cx="0.4" cy="0.4" r="0.7"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#d3dae5"/></radialGradient>'
               '<linearGradient id="gg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#d8f0ff"/><stop offset="1" stop-color="#6fb4e0"/></linearGradient>'
               '<radialGradient id="glow" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#bfe9ff" stop-opacity="0.9"/><stop offset="1" stop-color="#7fd4ff" stop-opacity="0"/></radialGradient>'
               '</defs>',
               f'<rect width="{W}" height="{H}" fill="url(#sky)"/>']
        rng = random.Random(7)
        for _ in range(60):
            out.append(f'<circle cx="{rng.uniform(0, W):.0f}" cy="{rng.uniform(0, 260):.0f}" r="{rng.choice([0.6, 0.8, 1.1]):.1f}" fill="#e8ecf3" fill-opacity="{rng.uniform(0.3, 0.9):.2f}"/>')
        # plot tile with earth sides
        dep = 9
        a, b, c, d = iso(0, 0), iso(120, 0), iso(120, 120), iso(0, 120)
        out.append(f'<polygon points="{pts([d, c, (c[0], c[1] + dep * S), (d[0], d[1] + dep * S)])}" fill="{EARTH_L}"/>')
        out.append(f'<polygon points="{pts([c, b, (b[0], b[1] + dep * S), (c[0], c[1] + dep * S)])}" fill="{EARTH_R}"/>')
        for k in range(1, 4):
            yy = k * dep * S / 4
            out.append(f'<polyline points="{pts([(d[0], d[1] + yy), (c[0], c[1] + yy), (b[0], b[1] + yy)])}" fill="none" stroke="#000000" stroke-opacity="0.12" stroke-width="1"/>')
        out.append(f'<polygon points="{pts([a, b, c, d])}" fill="{GRASS}"/>')
        out.extend(self.ground)
        out.extend(self.shadows)
        for _, s in sorted(self.objs, key=lambda t: t[0]):
            out.append(s)
        out.extend(self.sky)
        out.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
        out.append("</svg>")
        return "\n".join(out)


# ------------------------------------------------------------------ levels
PAD = (94, 90)   # landing pad anchor, fixed at every level
HUB = (48, 46)   # hub anchor (centre-back)
MAST = (22, 24)  # comm mast anchor (back-left)




def wild(sc, trees, avoid, rocks=6, tuft=90):
    for x, y, s in trees:
        sc.tree(x, y, s)
    for _ in range(rocks):
        x, y = sc.rng.uniform(6, 114), sc.rng.uniform(6, 114)
        if not any(a0 <= x <= a1 and b0 <= y <= b1 for a0, a1, b0, b1 in avoid):
            sc.rock(x, y, sc.rng.uniform(0.7, 1.3))
    sc.tufts(tuft, ((4, 116), (4, 116)), avoid)


# ================================================================ v2: Starfleet vocabulary
PANEL = "#b7c0cd"
UNDER = "#7d889a"


def ell(r):
    return 1.2247 * r * S, 0.7071 * r * S


class FedScene(Scene):
    def cylg(self, x, y, r, h, z=0.0, top="url(#tg)", windows=(), band=(), glowband=(), shadow=True, depth=None, cap=None):
        """Vertical cylinder with a smooth gradient side. cap: None | 'dome' (glass) | 'hull' (low hull dome)."""
        if shadow and z < 1:
            self.shadow(x - r, y - r, 2 * r, 2 * r, h, 0.22)
        b, t = iso(x, y, z), iso(x, y, z + h)
        rx, ry = ell(r)
        o = [f'<path d="M{b[0]-rx:.1f},{t[1]:.1f} L{b[0]-rx:.1f},{b[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{b[1]:.1f} '
             f'L{b[0]+rx:.1f},{t[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 1 {b[0]-rx:.1f},{t[1]:.1f} Z" fill="url(#hg)"/>']
        for bz in band:
            yy = b[1] - bz * S
            o.append(f'<path d="M{b[0]-rx:.1f},{yy:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{yy:.1f}" fill="none" stroke="{BLUE}" stroke-width="2.6"/>')
        for bz in glowband:
            yy = b[1] - bz * S
            o.append(f'<path d="M{b[0]-rx:.1f},{yy:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{yy:.1f}" fill="none" stroke="{GLOW}" stroke-width="1.8"/>')
        for zr in windows:
            yy = b[1] - zr * S
            o.append(f'<path d="M{b[0]-rx*0.94:.1f},{yy+ry*0.3:.1f} A{rx*0.94:.1f},{ry:.1f} 0 0 0 {b[0]+rx*0.94:.1f},{yy+ry*0.3:.1f}" fill="none" stroke="{WIN}" stroke-width="1.9" stroke-dasharray="2.6 2.4"/>')
        o.append(f'<ellipse cx="{t[0]:.1f}" cy="{t[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{top}" stroke="#ffffff" stroke-opacity="0.5" stroke-width="0.6"/>')
        if cap == "dome":
            o.append(f'<path d="M{t[0]-rx*0.86:.1f},{t[1]:.1f} A{rx*0.86:.1f},{ry*0.86:.1f} 0 0 0 {t[0]+rx*0.86:.1f},{t[1]:.1f} A{rx*0.86:.1f},{r*S*0.8:.1f} 0 0 0 {t[0]-rx*0.86:.1f},{t[1]:.1f} Z" fill="url(#gg)" fill-opacity="0.9" stroke="#ffffff" stroke-opacity="0.7" stroke-width="0.8"/>')
            o.append(f'<path d="M{t[0]-rx*0.45:.1f},{t[1]-r*S*0.45:.1f} Q{t[0]-rx*0.1:.1f},{t[1]-r*S*0.7:.1f} {t[0]+rx*0.25:.1f},{t[1]-r*S*0.62:.1f}" fill="none" stroke="#ffffff" stroke-opacity="0.85" stroke-width="1.4"/>')
        elif cap == "hull":
            o.append(f'<path d="M{t[0]-rx:.1f},{t[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {t[0]+rx:.1f},{t[1]:.1f} A{rx:.1f},{r*S*0.5:.1f} 0 0 0 {t[0]-rx:.1f},{t[1]:.1f} Z" fill="url(#hg)"/>')
            o.append(f'<ellipse cx="{t[0]-rx*0.15:.1f}" cy="{t[1]-r*S*0.32:.1f}" rx="{rx*0.32:.1f}" ry="{ry*0.3:.1f}" fill="#ffffff" fill-opacity="0.55"/>')
        self.objs.append((depth if depth is not None else x + y + r * 0.5, "".join(o)))

    def saucer(self, x, y, r, t, z, depth=None, shadow=True, panels=True, windows=True, glow=True):
        """Starfleet saucer: underside bulge, rim band with windows, panelled top."""
        if shadow:
            c = iso(x, y)
            rx, ry = ell(r)
            off = min(z, 40) * 0.45 * S * 1.4
            self.shadows.append(f'<ellipse cx="{c[0]+off:.1f}" cy="{c[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{SHADOW}" fill-opacity="0.2"/>')
        b, tp = iso(x, y, z), iso(x, y, z + t)
        rx, ry = ell(r)
        u = iso(x, y, z - t * 0.35)
        urx, ury = ell(r * 0.78)
        o = [f'<path d="M{u[0]-urx:.1f},{b[1]:.1f} A{urx:.1f},{ury:.1f} 0 0 0 {u[0]+urx:.1f},{b[1]:.1f} Z" fill="{UNDER}"/>',
             f'<path d="M{u[0]-urx:.1f},{b[1]:.1f} Q{u[0]:.1f},{u[1]+ury*1.6:.1f} {u[0]+urx:.1f},{b[1]:.1f}" fill="{UNDER}"/>']
        if glow:
            grx, gry = ell(r * 0.5)
            o.append(f'<path d="M{u[0]-grx:.1f},{u[1]+gry*0.2:.1f} A{grx:.1f},{gry:.1f} 0 0 0 {u[0]+grx:.1f},{u[1]+gry*0.2:.1f}" fill="none" stroke="{GLOW}" stroke-width="2.2"/>')
        o.append(f'<path d="M{b[0]-rx:.1f},{tp[1]:.1f} L{b[0]-rx:.1f},{b[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 0 {b[0]+rx:.1f},{b[1]:.1f} '
                 f'L{b[0]+rx:.1f},{tp[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 1 {b[0]-rx:.1f},{tp[1]:.1f} Z" fill="url(#hg)"/>')
        if windows:
            ym = (b[1] + tp[1]) / 2
            o.append(f'<path d="M{b[0]-rx*0.97:.1f},{ym+ry*0.15:.1f} A{rx*0.97:.1f},{ry:.1f} 0 0 0 {b[0]+rx*0.97:.1f},{ym+ry*0.15:.1f}" fill="none" stroke="{WIN}" stroke-width="1.7" stroke-dasharray="2.2 2.2"/>')
        o.append(f'<ellipse cx="{tp[0]:.1f}" cy="{tp[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="url(#tg)" stroke="#ffffff" stroke-opacity="0.6" stroke-width="0.7"/>')
        if panels:
            irx, iry = ell(r * 0.55)
            o.append(f'<ellipse cx="{tp[0]:.1f}" cy="{tp[1]:.1f}" rx="{irx:.1f}" ry="{iry:.1f}" fill="none" stroke="{PANEL}" stroke-width="0.7"/>')
            for deg in (-30, 5, 45, 85, 120, 160, 200, 240, 280):
                a = math.radians(deg)
                p1 = iso(x + r * 0.55 * math.cos(a), y + r * 0.55 * math.sin(a), z + t)
                p2 = iso(x + r * 0.96 * math.cos(a), y + r * 0.96 * math.sin(a), z + t)
                o.append(f'<line x1="{p1[0]:.1f}" y1="{p1[1]:.1f}" x2="{p2[0]:.1f}" y2="{p2[1]:.1f}" stroke="{PANEL}" stroke-width="0.7"/>')
            a1, a2 = iso(x + r * 0.62, y - r * 0.15, z + t), iso(x + r * 0.62, y + r * 0.3, z + t)
            o.append(f'<line x1="{a1[0]:.1f}" y1="{a1[1]:.1f}" x2="{a2[0]:.1f}" y2="{a2[1]:.1f}" stroke="{BLUE}" stroke-width="1.4"/>')
        self.objs.append((depth if depth is not None else x + y, "".join(o)))

    def tubeline(self, a, b, z=2.2, w=6.5, depth=None):
        p, q = iso(a[0], a[1], z), iso(b[0], b[1], z)
        o = (f'<line x1="{p[0]:.1f}" y1="{p[1]+1:.1f}" x2="{q[0]:.1f}" y2="{q[1]+1:.1f}" stroke="#7d889a" stroke-width="{w+1.6}" stroke-linecap="round"/>'
             f'<line x1="{p[0]:.1f}" y1="{p[1]:.1f}" x2="{q[0]:.1f}" y2="{q[1]:.1f}" stroke="#c9d0dc" stroke-width="{w}" stroke-linecap="round"/>'
             f'<line x1="{p[0]:.1f}" y1="{p[1]-w*0.28:.1f}" x2="{q[0]:.1f}" y2="{q[1]-w*0.28:.1f}" stroke="#f4f6fa" stroke-width="{w*0.28:.1f}" stroke-linecap="round"/>'
             f'<line x1="{p[0]:.1f}" y1="{p[1]+w*0.2:.1f}" x2="{q[0]:.1f}" y2="{q[1]+w*0.2:.1f}" stroke="{GLOW}" stroke-opacity="0.7" stroke-width="0.9"/>')
        self.objs.append((depth if depth is not None else (a[0] + a[1] + b[0] + b[1]) / 2, o))

    def ring_corridor(self, x, y, r, z=2.4, w=6.5):
        c = iso(x, y, z)
        rx, ry = ell(r)
        for sweep, dep in ((1, x + y - r), (0, x + y + r)):
            o = (f'<path d="M{c[0]-rx:.1f},{c[1]+1:.1f} A{rx:.1f},{ry:.1f} 0 0 {sweep} {c[0]+rx:.1f},{c[1]+1:.1f}" fill="none" stroke="#7d889a" stroke-width="{w+1.6}"/>'
                 f'<path d="M{c[0]-rx:.1f},{c[1]:.1f} A{rx:.1f},{ry:.1f} 0 0 {sweep} {c[0]+rx:.1f},{c[1]:.1f}" fill="none" stroke="#c9d0dc" stroke-width="{w}"/>'
                 f'<path d="M{c[0]-rx:.1f},{c[1]-w*0.28:.1f} A{rx:.1f},{ry:.1f} 0 0 {sweep} {c[0]+rx:.1f},{c[1]-w*0.28:.1f}" fill="none" stroke="#f4f6fa" stroke-width="{w*0.28:.1f}"/>'
                 f'<path d="M{c[0]-rx:.1f},{c[1]+w*0.2:.1f} A{rx:.1f},{ry:.1f} 0 0 {sweep} {c[0]+rx:.1f},{c[1]+w*0.2:.1f}" fill="none" stroke="{GLOW}" stroke-opacity="0.7" stroke-width="0.9"/>')
            self.objs.append((dep, o))

    def module(self, x, y, r=4.4, h=5.5, storeys=1, depth=None):
        win = tuple(2.6 + 3.2 * k for k in range(storeys))
        self.cylg(x, y, r, h, windows=win, band=(h - 0.9,), cap="hull", depth=depth)
        # door facing the viewer
        d0, d1 = iso(x + r * 0.62, y + r * 0.62, 0), iso(x + r * 0.62, y + r * 0.62, 2.6)
        self.objs.append(((depth if depth is not None else x + y + r * 0.5) + 0.01,
                          f'<rect x="{d0[0]-2:.1f}" y="{d1[1]:.1f}" width="4" height="{d0[1]-d1[1]:.1f}" rx="1.6" fill="{BLUE}"/>'))

    def legs(self, x, y, r, z):
        for deg in (0, 90, 180, 270):
            a = math.radians(deg)
            top = iso(x + r * 0.6 * math.cos(a), y + r * 0.6 * math.sin(a), z)
            ft = iso(x + r * 1.05 * math.cos(a), y + r * 1.05 * math.sin(a), 0)
            dep = x + y + r * 1.05 * (math.cos(a) + math.sin(a)) * 0.5
            self.objs.append((dep, f'<line x1="{top[0]:.1f}" y1="{top[1]:.1f}" x2="{ft[0]:.1f}" y2="{ft[1]:.1f}" stroke="#8a94a6" stroke-width="2.6" stroke-linecap="round"/>'
                                   f'<line x1="{top[0]:.1f}" y1="{top[1]:.1f}" x2="{ft[0]:.1f}" y2="{ft[1]:.1f}" stroke="#c9d0dc" stroke-width="1" stroke-linecap="round"/>'
                                   f'<ellipse cx="{ft[0]:.1f}" cy="{ft[1]:.1f}" rx="4" ry="2" fill="#8a94a6"/>'))

    def petals(self, x, y, r1, r2, angles, spread=20):
        for deg in angles:
            a0, a1 = math.radians(deg - spread / 2), math.radians(deg + spread / 2)
            p = [iso(x + r1 * math.cos(a0), y + r1 * math.sin(a0), 0.5), iso(x + r2 * math.cos(a0), y + r2 * math.sin(a0), 0.5),
                 iso(x + r2 * math.cos(a1), y + r2 * math.sin(a1), 0.5), iso(x + r1 * math.cos(a1), y + r1 * math.sin(a1), 0.5)]
            o = f'<polygon points="{pts(p)}" fill="#1f3a66" stroke="#5b8def" stroke-width="0.8"/>'
            for k in (0.33, 0.66):
                am = a0 + (a1 - a0) * k
                q1, q2 = iso(x + r1 * math.cos(am), y + r1 * math.sin(am), 0.5), iso(x + r2 * math.cos(am), y + r2 * math.sin(am), 0.5)
                o += f'<line x1="{q1[0]:.1f}" y1="{q1[1]:.1f}" x2="{q2[0]:.1f}" y2="{q2[1]:.1f}" stroke="#5b8def" stroke-width="0.6"/>'
            self.ground.append(o)

    def ramp(self, x, y, r_from, r_to, z, deg=45, w=3.2):
        a = math.radians(deg)
        nx, ny = -math.sin(a), math.cos(a)
        f = (x + r_from * math.cos(a), y + r_from * math.sin(a))
        g = (x + r_to * math.cos(a), y + r_to * math.sin(a))
        p = [iso(f[0] + nx * w, f[1] + ny * w, z), iso(f[0] - nx * w, f[1] - ny * w, z), iso(g[0] - nx * w, g[1] - ny * w, 0), iso(g[0] + nx * w, g[1] + ny * w, 0)]
        self.objs.append((x + y + r_to, f'<polygon points="{pts(p)}" fill="#b8c1cf" stroke="#8a94a6" stroke-width="0.8"/>'
                          f'<line x1="{(p[0][0]+p[1][0])/2:.1f}" y1="{(p[0][1]+p[1][1])/2:.1f}" x2="{(p[2][0]+p[3][0])/2:.1f}" y2="{(p[2][1]+p[3][1])/2:.1f}" stroke="{GLOW}" stroke-width="1" stroke-dasharray="3 3"/>'))

    def arch(self, a, b, peak, w=6, depth=None):
        p, q = iso(a[0], a[1]), iso(b[0], b[1])
        m = iso((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, peak * 2)
        o = (f'<path d="M{p[0]:.1f},{p[1]:.1f} Q{m[0]:.1f},{m[1]:.1f} {q[0]:.1f},{q[1]:.1f}" fill="none" stroke="#8e99ab" stroke-width="{w+1.5}" stroke-linecap="round"/>'
             f'<path d="M{p[0]:.1f},{p[1]:.1f} Q{m[0]:.1f},{m[1]:.1f} {q[0]:.1f},{q[1]:.1f}" fill="none" stroke="#eef1f6" stroke-width="{w}" stroke-linecap="round"/>'
             f'<path d="M{p[0]:.1f},{p[1]:.1f} Q{m[0]:.1f},{m[1]:.1f} {q[0]:.1f},{q[1]:.1f}" fill="none" stroke="{BLUE}" stroke-width="1.6"/>')
        self.objs.append((depth if depth is not None else (a[0] + a[1] + b[0] + b[1]) / 2, o))

    def dropship(self, x, y, z, s=1.8):
        c = iso(x, y, z)
        self.sky.append(f'<ellipse cx="{c[0]-2:.1f}" cy="{c[1]+14*s:.1f}" rx="{9*s:.1f}" ry="{16*s:.1f}" fill="url(#glow)" fill-opacity="0.6"/>')
        self.shuttle(x, y, z, s=s, landed=False)
        self.sky.append(f'<circle cx="{c[0]-4*s:.1f}" cy="{c[1]+3*s:.1f}" r="{2.4*s:.1f}" fill="{GLOW}"/><circle cx="{c[0]+8*s:.1f}" cy="{c[1]-2*s:.1f}" r="{2*s:.1f}" fill="{GLOW}"/>')

    def drop_capsule(self, x, y):
        self.disc(x, y, 4.2, "#3b4a2e", opacity=0.55)
        self.cylg(x, y, 2.2, 4.4, glowband=(1.2,), cap="hull")

    def fed_mast(self, x, y, h):
        self.cylg(x, y, 0.8, h, shadow=False, depth=x + y + 1)
        t, m = iso(x, y, h), iso(x, y, h * 0.75)
        rx, ry = ell(2.6)
        self.objs.append((x + y + 1.1,
                          f'<ellipse cx="{m[0]:.1f}" cy="{m[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="none" stroke="#c9d0dc" stroke-width="1.8"/>'
                          f'<ellipse cx="{m[0]:.1f}" cy="{m[1]:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="none" stroke="{GLOW}" stroke-width="0.8"/>'
                          f'<path d="M{t[0]-1:.1f},{t[1]+16:.1f} Q{t[0]+12:.1f},{t[1]+20:.1f} {t[0]-1:.1f},{t[1]+32:.1f} Z" fill="#eef1f6" stroke="#8a94a6" stroke-width="0.8"/>'
                          f'<circle cx="{t[0]:.1f}" cy="{t[1]-2:.1f}" r="2.3" fill="{RED}"/><circle cx="{t[0]:.1f}" cy="{t[1]-2:.1f}" r="6" fill="{RED}" fill-opacity="0.2"/>'))


HUB2 = (52, 48)


def lander(sc, z, legs=True, depth=100):
    """The original colony pod: saucer lander with a domed command capsule on top."""
    x, y = HUB2
    if legs:
        sc.legs(x, y, 10, z)
    sc.saucer(x, y, 10, 3.2, z, depth=depth)
    sc.cylg(x, y, 5, 4.6, z=z + 3.2, band=(3.6,), windows=(1.8,), cap="dome", shadow=False, depth=depth + 0.1)
    t = iso(x, y, z + 3.2 + 4.6 + 4.2)
    sc.objs.append((depth + 0.2, f'<circle cx="{t[0]:.1f}" cy="{t[1]:.1f}" r="2" fill="{RED}"/>'))


def v_level1():
    sc = FedScene(11)
    x, y = HUB2
    sc.disc(x, y, 17, "#3b4a2e", opacity=0.45)
    sc.disc(x, y, 12, "#2e3a24", opacity=0.45)
    sc.petals(x, y, 11.5, 18, (100, 145, 190, 235, 280, 325, 10))
    lander(sc, 6.5)
    sc.ramp(x, y, 8.5, 19, 6.5, deg=0, w=2.4)
    sc.drop_capsule(76, 34)
    sc.drop_capsule(30, 66)
    sc.drop_capsule(80, 64)
    for bx, by in [(28, 26), (84, 26), (28, 84), (86, 84)]:
        sc.beacon(bx, by)
    sc.people([(66, 62), (69, 65), (72, 60), (78, 70), (34, 70)])
    sc.dropship(92, 30, 74, s=1.9)
    trees = [(10, 12, 1.1), (16, 52, 1.0), (8, 74, 1.2), (14, 98, 1.0), (30, 106, 1.1), (52, 104, 0.9), (60, 14, 1.0),
             (78, 10, 1.2), (98, 16, 1.0), (108, 40, 1.1), (112, 62, 0.9), (104, 112, 1.0), (40, 18, 0.9), (6, 34, 0.8),
             (24, 90, 0.9), (70, 104, 1.1), (96, 52, 0.9), (98, 92, 1.0), (86, 104, 0.8)]
    wild(sc, trees, [(28, 76, 26, 72)])
    return sc


def v_level2():
    sc = FedScene(12)
    x, y = HUB2
    sc.disc(x, y, 30, "#a79a82", opacity=0.7)
    sc.disc(x, y, 12.5, PAVE_DK)
    sc.line(x + 14, y + 14, PAD[0] - 9, PAD[1] - 9, "#a79a82", sw=7)
    sc.pad(*PAD, 12)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    sc.cylg(x, y, 7.5, 1.8, shadow=False, depth=99)
    lander(sc, 1.8, legs=False)
    for deg in (0, 90, 180, 270):
        a = math.radians(deg)
        mx, my = x + 19 * math.cos(a), y + 19 * math.sin(a)
        sc.tubeline((x + 8 * math.cos(a), y + 8 * math.sin(a)), (mx - 4 * math.cos(a), my - 4 * math.sin(a)))
        sc.module(mx, my)
    sc.dome(70, 68, 6, inner="".join(
        f'<circle cx="{iso(70 + dx, 68 + dy)[0]:.1f}" cy="{iso(70 + dx, 68 + dy)[1]-4:.1f}" r="3.2" fill="{TREE_LT}"/>' for dx, dy in ((-2, -1), (1.5, 1), (2, -2.5))))
    sc.fed_mast(*MAST, 30)
    sc.solar(18, 60, 3)
    sc.petals(x, y, 13, 17, (135, 225), spread=24)
    sc.hover_truck(80, 76)
    for bx, by in [(24, 26), (86, 26), (24, 86), (74, 86)]:
        sc.beacon(bx, by)
    sc.people([(64, 60), (68, 58), (60, 70), (86, 84), (90, 82), (40, 56), (72, 48)])
    trees = [(10, 12, 1.1), (12, 46, 1.0), (8, 76, 1.2), (14, 100, 1.0), (32, 108, 1.1), (62, 106, 0.9), (60, 10, 1.0),
             (82, 10, 1.2), (102, 16, 1.0), (110, 40, 1.1), (112, 62, 0.9), (106, 112, 1.0), (72, 106, 1.1)]
    wild(sc, trees, [(20, 84, 16, 80), (78, 112, 74, 106)], rocks=3, tuft=60)
    return sc


def v_level3():
    sc = FedScene(13)
    x, y = HUB2
    sc.disc(x, y, 34, PAVE)
    for rr in (14, 24, 31):
        sc.disc(x, y, rr, "none", stroke=PAVE_LINE, sw=0.9)
    sc.line(x + 22, y + 22, PAD[0] - 9, PAD[1] - 9, PAVE_DK, sw=8)
    sc.line(x + 30, y + 6, 112, 62, PAVE_DK, sw=8)
    sc.pad(*PAD, 12)
    sc.pad(100, 60, 9)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    # the pod lifted onto a column above the ring
    sc.cylg(x, y, 7.5, 1.8, shadow=False, depth=99)
    sc.cylg(x, y, 3.4, 12, z=1.8, glowband=(4, 8), shadow=False, depth=99.5)
    lander(sc, 13.8, legs=False)
    sc.shadow(x - 10, y - 10, 20, 20, 14, 0.18)
    sc.ring_corridor(x, y, 18)
    for deg in range(0, 360, 60):
        a = math.radians(deg)
        sc.module(x + 18 * math.cos(a), y + 18 * math.sin(a), r=4.6, h=7.5, storeys=2)
    # administration rotunda
    sc.cylg(82, 28, 8, 12, windows=(3, 6.5, 9.5), band=(11,), cap="dome")
    sc.tubeline((74, 33), (68, 40), z=4)
    # comm tower grows from the mast
    sc.cylg(*MAST, 2.2, 30, windows=(8, 16, 24), band=(28,), depth=MAST[0] + MAST[1] + 1)
    sc.saucer(MAST[0], MAST[1], 4.5, 1.6, 30, depth=MAST[0] + MAST[1] + 1.2, panels=False, shadow=False)
    sc.fed_mast(MAST[0], MAST[1], 44)
    sc.dome(30, 76, 6.5)
    for lx, ly in [(22, 40), (40, 84), (84, 50), (70, 82), (88, 70), (98, 74)]:
        sc.lamp(lx, ly)
    sc.hover_truck(88, 60)
    sc.people([(62, 70), (66, 72), (72, 58), (86, 86), (90, 84), (40, 72), (36, 44), (60, 32)])
    trees = [(8, 10, 1.1), (6, 50, 1.0), (8, 96, 1.0), (28, 108, 1.1), (76, 108, 0.9), (100, 12, 1.0), (110, 40, 1.0), (112, 106, 1.0), (12, 80, 0.9)]
    wild(sc, trees, [(16, 88, 12, 84), (80, 114, 54, 104)], rocks=1, tuft=30)
    return sc


def v_level4():
    sc = FedScene(14)
    x, y = HUB2
    sc.disc(x, y, 42, PAVE)
    for rr in (14, 26, 38):
        sc.disc(x, y, rr, "none", stroke=PAVE_LINE, sw=0.9)
    sc.line(x + 28, y + 28, PAD[0] - 9, PAD[1] - 9, PAVE_DK, sw=9)
    sc.pad(*PAD, 12)
    sc.pad(102, 58, 9)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    sc.shuttle(102, 58, s=1.0)
    sc.quad(56, 74, 16, 9, "#3f78b0")
    sc.quad(57, 75, 14, 7, "#5a9ad0", opacity=0.6)
    sc.ring_corridor(x, y, 18)
    for deg in range(0, 360, 60):
        a = math.radians(deg)
        sc.module(x + 18 * math.cos(a), y + 18 * math.sin(a), r=4.8, h=10.5, storeys=3)
    # central column carries a starbase-style saucer deck
    sc.cylg(x, y, 7.5, 1.8, shadow=False, depth=99)
    sc.cylg(x, y, 5, 26, z=1.8, windows=(5, 10, 15, 20), glowband=(23,), shadow=False, depth=99.5)
    sc.saucer(x, y, 20, 4, 27.8, depth=100.5)
    sc.saucer(x, y, 10, 2.6, 31.8, depth=100.6, glow=False)
    sc.cylg(x, y, 4.2, 3, z=34.4, cap="dome", shadow=False, depth=100.7)
    sc.shadow(x - 20, y - 20, 40, 40, 30, 0.16)
    sc.cylg(84, 26, 9, 18, windows=(3, 6.5, 10, 13.5), band=(17,), cap="dome")
    sc.tubeline((76, 31), (60, 42), z=14, w=5)
    sc.cylg(*MAST, 2.2, 32, windows=(8, 16, 24), band=(30,), depth=MAST[0] + MAST[1] + 1)
    sc.saucer(MAST[0], MAST[1], 4.5, 1.6, 32, depth=MAST[0] + MAST[1] + 1.2, panels=False, shadow=False)
    sc.fed_mast(MAST[0], MAST[1], 46)
    sc.cylg(24, 74, 3, 12, glowband=(6,), shadow=True)
    sc.saucer(24, 74, 8, 2.4, 12, depth=99)
    for tx, ty in [(40, 88), (48, 92), (78, 66), (88, 44)]:
        sc.tree(tx, ty, 0.85)
    for lx, ly in [(16, 44), (36, 92), (90, 50), (74, 88), (92, 70), (104, 70)]:
        sc.lamp(lx, ly)
    sc.hover_truck(90, 60)
    sc.shuttle(26, 14, z=62, s=1.0, landed=False)
    sc.people([(60, 72), (64, 72), (70, 72), (86, 88), (90, 86), (40, 80), (30, 60), (76, 56)])
    for x2, y2, s in [(6, 8, 1.1), (6, 104, 1.0), (112, 110, 1.0), (112, 8, 1.0)]:
        sc.tree(x2, y2, s)
    return sc


def v_level5():
    sc = FedScene(15)
    x, y = HUB2
    sc.quad(6, 6, 108, 108, PAVE)
    for rr in (14, 28, 42, 56):
        sc.disc(x, y, rr, "none", stroke=PAVE_LINE, sw=0.9)
    sc.quad(84, 74, 30, 34, "#8f949c")
    sc.pad(*PAD, 12)
    sc.pad(106, 78, 7)
    sc.pad(108, 102, 7)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    sc.shuttle(106, 78, s=0.9)
    sc.quad(40, 78, 28, 10, "#3f78b0")
    sc.quad(41, 79, 26, 8, "#5a9ad0", opacity=0.6)
    sc.quad(30, 92, 40, 10, GRASS_LT)
    # surrounding towers, each crowned with a small saucer
    for tx, ty, r, h in [(22, 40, 6, 44), (80, 20, 5.5, 52), (88, 52, 5.5, 36), (28, 76, 5, 28)]:
        sc.cylg(tx, ty, r, h, windows=tuple(range(5, h - 2, 5)), band=(h - 1.5,), depth=tx + ty + r)
        sc.saucer(tx, ty, r * 1.6, 2.2, h, depth=tx + ty + r + 0.1, panels=False)
        sc.cylg(tx, ty, r * 0.6, 2.4, z=h + 2.2, cap="dome", shadow=False, depth=tx + ty + r + 0.2)
    # the spire: the original column, now carrying three decks
    sc.cylg(x, y, 7.5, 1.8, shadow=False, depth=99)
    sc.cylg(x, y, 6, 28.2, z=1.8, windows=tuple(range(5, 27, 5)), glowband=(25,), shadow=False, depth=99.5)
    sc.saucer(x, y, 22, 4.2, 30, depth=100.5)
    sc.cylg(x, y, 5, 19.8, z=34.2, windows=(4, 9, 14), glowband=(17,), shadow=False, depth=100.55)
    sc.saucer(x, y, 14, 3, 54, depth=100.6)
    sc.cylg(x, y, 4, 13, z=57, windows=(4, 9), shadow=False, depth=100.65)
    sc.saucer(x, y, 8, 2.4, 70, depth=100.7, panels=False)
    sc.cylg(x, y, 3, 7.6, z=72.4, glowband=(4,), shadow=False, depth=100.75)
    sc.cylg(x, y, 3.6, 3, z=80, cap="dome", shadow=False, depth=100.8)
    sc.shadow(x - 22, y - 22, 44, 44, 60, 0.16)
    # Academy-style arches over the plaza
    sc.arch((34, 84), (74, 72), 22, depth=125)
    sc.arch((60, 92), (84, 66), 16, depth=130)
    # the original colony pod kept as a monument
    sc.cylg(50, 92, 3, 1.6, depth=141)
    sc.saucer(50, 92, 5, 1.6, 1.6, depth=141.1, panels=False, shadow=False)
    sc.cylg(50, 92, 2.4, 2, z=3.2, cap="dome", shadow=False, depth=141.2)
    sc.cylg(*MAST, 2.2, 32, windows=(8, 16, 24), band=(30,), depth=MAST[0] + MAST[1] + 1)
    sc.saucer(MAST[0], MAST[1], 4.5, 1.6, 32, depth=MAST[0] + MAST[1] + 1.2, panels=False, shadow=False)
    sc.fed_mast(MAST[0], MAST[1], 46)
    for tx, ty in [(32, 96), (40, 98), (48, 100), (58, 98), (66, 96), (12, 20), (100, 16)]:
        sc.tree(tx, ty, 0.85)
    for lx, ly in [(10, 10), (104, 10), (10, 100), (100, 62), (38, 76), (72, 76), (84, 72)]:
        sc.lamp(lx, ly, 7)
    sc.hover_truck(98, 58)
    sc.shuttle(16, 6, z=72, s=0.9, landed=False)
    sc.shuttle(100, 30, z=84, s=0.8, landed=False)
    sc.shuttle(64, -6, z=96, s=0.7, landed=False)
    sc.people([(44, 88), (48, 90), (54, 90), (60, 88), (46, 76), (74, 84), (88, 86), (92, 84), (36, 86), (66, 80), (56, 94)])
    return sc


if __name__ == "__main__":
    OUT = sys.argv[1]
    os.makedirs(OUT, exist_ok=True)
    for i, fn in enumerate([v_level1, v_level2, v_level3, v_level4, v_level5], start=1):
        with open(os.path.join(OUT, f"colony-hub-L{i}.svg"), "w") as f:
            f.write(fn().render())
    print("ok")
