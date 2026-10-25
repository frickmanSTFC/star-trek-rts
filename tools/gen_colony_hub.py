"""Generate isometric SVG concept art for the Federation Colony Hub, levels 1-5.

One 120x120 world-unit plot, viewed isometrically. Every level reuses the same
anchors (hub at the centre-back, landing pad front-right, comm mast back-left)
so the progression reads as one site growing over time.

Usage: python3 gen_colony_hub.py OUT_DIR
"""
import math
import os
import random
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

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
               '<stop offset="1" stop-color="#000000" stop-opacity="0.45"/></radialGradient></defs>',
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


def level1():
    sc = Scene(1)
    avoid = [(28, 78, 30, 72), (78, 112, 74, 106), (12, 32, 14, 34)]
    sc.quad(30, 34, 46, 34, "#8c7a5e", opacity=0.55)           # trampled clearing
    sc.line(76, 62, PAD[0] - 8, PAD[1] - 6, "#8c7a5e", sw=6)    # dirt track to pad
    sc.pad(*PAD, 12)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    # three prefab modules joined by tubes
    sc.box(34, 40, 12, 10, 7, windows=(4.2,), door=(4, 3, 4.2))
    sc.tube(46, 43, 5, 4)
    sc.box(51, 38, 14, 12, 8.5, windows=(5,), band=(6.6, 7.4), door=(5, 3.2, 4.6))
    sc.tube(55, 50, 4, 5)
    sc.box(53, 55, 12, 10, 7, windows=(4.2,), door=(4, 3, 4.2))
    sc.mast(*MAST, 24)
    sc.crates(72, 66, 4)
    sc.solar(36, 60, 3)
    for bx, by in [(26, 28), (82, 28), (26, 82), (70, 80)]:
        sc.beacon(bx, by)
    sc.people([(50, 54), (68, 70), (88, 84), (90, 82), (44, 52)])
    trees = [(10, 12, 1.1), (16, 52, 1.0), (8, 74, 1.2), (14, 98, 1.0), (30, 106, 1.1), (52, 104, 0.9), (60, 14, 1.0),
             (78, 10, 1.2), (98, 16, 1.0), (108, 40, 1.1), (112, 62, 0.9), (104, 112, 1.0), (40, 18, 0.9), (6, 34, 0.8),
             (24, 90, 0.9), (70, 104, 1.1), (90, 50, 0.9)]
    wild(sc, trees, avoid)
    return sc


def level2():
    sc = Scene(2)
    avoid = [(26, 82, 28, 76), (78, 112, 74, 106), (12, 34, 14, 36)]
    sc.quad(28, 32, 50, 40, "#a79a82", opacity=0.75)            # gravel yard
    sc.line(78, 64, PAD[0] - 8, PAD[1] - 6, "#a79a82", sw=7)
    sc.line(48, 72, 48, 112, "#a79a82", sw=5)
    sc.pad(*PAD, 12)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    # rebuilt two-storey hub with observation dome
    sc.box(43, 37, 20, 16, 13, windows=(4.5, 9.5), band=(11.6, 12.6), door=(8, 4, 5))
    sc.cyl(53, 45, 4, 2, z=13, side_l=HULL2_L, side_r=HULL2_R, top=HULL2_T, cap="dome", shadow=False, depth=110)
    # modules, two old + two new
    sc.box(30, 40, 11, 9, 7, windows=(4.2,), door=(4, 3, 4.2))
    sc.tube(41, 43, 2.5, 4)
    sc.box(46, 58, 12, 10, 7, windows=(4.2,), door=(4, 3, 4.2))
    sc.tube(52, 53, 4, 5)
    sc.box(64, 40, 11, 9, 7, windows=(4.2,), door=(3.5, 3, 4.2))
    sc.tube(63, 43, 1.5, 4)
    sc.box(30, 56, 11, 9, 7, windows=(4.2,), door=(3.5, 3, 4.2))
    # greenhouse
    sc.box(64, 56, 13, 9, 6, top=GLASS, left=GLASS, right="#7fb0d0", glass=True)
    for gx in (67, 71, 75):
        sc.tree(gx, 60, 0.45)
    sc.mast(*MAST, 28)
    sc.mast(36, 26, 16, dish=True)
    sc.crates(78, 64, 5)
    sc.solar(32, 70, 4)
    sc.hover_truck(80, 76)
    for bx, by in [(24, 26), (84, 26), (24, 84), (72, 84)]:
        sc.beacon(bx, by)
    sc.people([(52, 56), (60, 70), (86, 84), (90, 82), (40, 54), (72, 52), (48, 76)])
    trees = [(10, 12, 1.1), (14, 50, 1.0), (8, 74, 1.2), (14, 98, 1.0), (30, 106, 1.1), (62, 104, 0.9), (60, 14, 1.0),
             (80, 10, 1.2), (100, 16, 1.0), (108, 40, 1.1), (112, 62, 0.9), (104, 112, 1.0), (6, 34, 0.8), (72, 104, 1.1)]
    wild(sc, trees, avoid, rocks=4, tuft=70)
    return sc


def level3():
    sc = Scene(3)
    sc.quad(24, 28, 60, 52, PAVE)                               # paved plaza
    for k in range(1, 6):
        sc.line(24, 28 + k * 52 / 6, 84, 28 + k * 52 / 6, PAVE_LINE, sw=0.8)
    sc.line(84, 66, 112, 66, PAVE_DK, sw=8)                     # road to pads
    sc.line(54, 80, 54, 116, PAVE_DK, sw=8)
    sc.pad(*PAD, 12)
    sc.pad(98, 64, 9)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    # L-shaped administration block
    sc.box(40, 34, 26, 16, 18, windows=(4.5, 9, 13.5), band=(16.4, 17.4), door=(10, 5, 5.5))
    sc.box(40, 50, 12, 14, 14, windows=(4.5, 9), band=(12.4, 13.4), door=(3, 4, 5))
    sc.cyl(56, 41, 3.5, 1.8, z=18, side_l=HULL2_L, side_r=HULL2_R, top=HULL2_T, cap="dome", shadow=False, depth=120)
    # comm tower replaces the mast
    sc.box(MAST[0] - 2, MAST[1] - 2, 5, 5, 34, windows=(10, 20, 28), band=(30, 31.4))
    sc.mast(MAST[0] + 0.5, MAST[1] + 0.5, 46)
    # habitat ring
    sc.box(70, 32, 11, 9, 8, windows=(4.6,), door=(3.5, 3, 4.4))
    sc.box(70, 46, 11, 9, 8, windows=(4.6,), door=(3.5, 3, 4.4))
    sc.box(28, 66, 11, 9, 8, windows=(4.6,), door=(3.5, 3, 4.4))
    sc.box(58, 66, 13, 9, 6, top=GLASS, left=GLASS, right="#7fb0d0", glass=True)
    for gx in (61, 65, 69):
        sc.tree(gx, 70, 0.45)
    # small garden in the plaza
    sc.quad(60, 54, 8, 8, GRASS_LT)
    sc.tree(64, 58, 0.8)
    for lx, ly in [(26, 30), (82, 30), (26, 78), (82, 78), (84, 70), (96, 74)]:
        sc.lamp(lx, ly)
    sc.hover_truck(86, 60)
    sc.crates(100, 78, 3)
    sc.people([(54, 56), (58, 62), (66, 60), (86, 86), (90, 84), (46, 68), (34, 62), (76, 58), (52, 76)])
    trees = [(10, 12, 1.1), (8, 50, 1.0), (8, 74, 1.2), (14, 98, 1.0), (30, 106, 1.1), (70, 106, 0.9), (60, 12, 1.0),
             (84, 10, 1.2), (104, 18, 1.0), (110, 42, 1.0), (112, 106, 1.0), (40, 96, 0.9)]
    wild(sc, trees, [(22, 86, 26, 82), (78, 114, 56, 104), (48, 60, 80, 116), (84, 114, 62, 70)], rocks=2, tuft=45)
    return sc


def level4():
    sc = Scene(4)
    sc.quad(18, 22, 70, 62, PAVE)
    for k in range(1, 8):
        sc.line(18, 22 + k * 62 / 8, 88, 22 + k * 62 / 8, PAVE_LINE, sw=0.8)
    sc.line(88, 66, 116, 66, PAVE_DK, sw=9)
    sc.line(54, 84, 54, 118, PAVE_DK, sw=9)
    sc.pad(*PAD, 12)
    sc.pad(98, 62, 9)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    sc.shuttle(98, 62, s=1.0)
    # reflecting pool in front of the complex
    sc.quad(42, 68, 20, 10, "#3f78b0")
    sc.quad(43, 69, 18, 8, "#5a9ad0", opacity=0.6)
    # administration block (kept) + new office tower with rounded crown
    sc.box(36, 44, 24, 14, 18, windows=(4.5, 9, 13.5), band=(16.4, 17.4), door=(9, 5, 5.5))
    sc.cyl(52, 34, 9, 42, windows=(6, 12, 18, 24, 30, 36), band=(39.5,), depth=95)
    sc.cyl(52, 34, 7.5, 5, z=42, side_l=HULL2_L, side_r=HULL2_R, top=HULL2_T, cap="dome", shadow=False, depth=96)
    # skybridge to the east wing
    sc.box(61, 32, 10, 3, 2.4, z=22, top=GLASS, left=GLASS, right="#7fb0d0", glass=True, shadow=False, depth=104)
    sc.box(70, 28, 12, 12, 26, windows=(5, 10, 15, 20), band=(24, 25))
    # glass atrium
    sc.dome(70, 54, 7)
    sc.box(MAST[0] - 2, MAST[1] - 2, 5, 5, 36, windows=(10, 20, 28), band=(32, 33.4))
    sc.mast(MAST[0] + 0.5, MAST[1] + 0.5, 48)
    # residential blocks
    sc.box(22, 56, 10, 10, 12, windows=(4.5, 9), door=(3, 3, 4.4))
    sc.box(22, 70, 10, 10, 10, windows=(4.5, 8), door=(3, 3, 4.4))
    sc.box(70, 68, 12, 10, 12, windows=(4.5, 9), door=(4, 3, 4.4))
    for tx, ty in [(36, 62), (64, 64), (40, 80), (66, 80), (30, 84)]:
        sc.quad(tx - 2, ty - 2, 4, 4, "#8a7a62")
        sc.tree(tx, ty, 0.8)
    for lx, ly in [(20, 24), (86, 24), (20, 82), (86, 82), (88, 70), (100, 74), (58, 86)]:
        sc.lamp(lx, ly)
    sc.hover_truck(88, 56)
    sc.hover_truck(56, 96)
    sc.shuttle(30, 18, z=60, s=0.9, landed=False)
    sc.people([(48, 64), (52, 66), (62, 64), (44, 82), (86, 88), (90, 86), (34, 54), (66, 50), (58, 80), (76, 60)])
    trees = [(8, 10, 1.1), (6, 48, 1.0), (8, 96, 1.0), (28, 108, 1.1), (76, 108, 0.9), (100, 14, 1.0), (110, 40, 1.0), (112, 108, 1.0)]
    wild(sc, trees, [(16, 90, 20, 86), (84, 116, 56, 104), (48, 60, 84, 118)], rocks=1, tuft=25)
    return sc


def level5():
    sc = Scene(5)
    sc.quad(10, 12, 92, 86, PAVE)
    for k in range(1, 10):
        sc.line(10, 12 + k * 86 / 10, 102, 12 + k * 86 / 10, PAVE_LINE, sw=0.7)
    sc.line(102, 62, 118, 62, PAVE_DK, sw=10)
    sc.line(56, 98, 56, 119, PAVE_DK, sw=10)
    # shuttleport: original pad plus two more
    sc.quad(84, 74, 30, 32, "#8f949c")
    sc.pad(*PAD, 12)
    sc.pad(106, 78, 7)
    sc.pad(108, 100, 7)
    sc.shuttle(PAD[0], PAD[1], s=1.25)
    sc.shuttle(106, 78, s=0.9)
    # reflecting pool and civic lawn
    sc.quad(40, 70, 30, 12, "#3f78b0")
    sc.quad(41, 71, 28, 10, "#5a9ad0", opacity=0.6)
    sc.quad(36, 84, 38, 10, GRASS_LT)
    # ring of towers
    sc.cyl(26, 50, 7, 48, windows=(6, 12, 18, 24, 30, 36, 42), band=(46,), depth=70)
    sc.cyl(26, 50, 5.5, 4, z=48, side_l=HULL2_L, side_r=HULL2_R, top=HULL2_T, cap="dome", shadow=False, depth=71)
    sc.box(68, 22, 13, 13, 54, windows=(6, 12, 18, 24, 30, 36, 42, 48), band=(51, 52.4), depth=92)
    sc.cyl(84, 44, 6.5, 40, windows=(6, 12, 18, 24, 30, 36), band=(38,), depth=118)
    sc.box(30, 64, 12, 12, 30, windows=(6, 12, 18, 24), band=(28, 29))
    sc.box(70, 60, 12, 12, 26, windows=(6, 12, 18, 23), band=(24, 25))
    # central spire on the hub's original footprint
    sc.cyl(HUB[0] + 4, HUB[1] - 2, 11, 30, windows=(6, 12, 18, 24), band=(28,), depth=100)
    sc.cyl(HUB[0] + 4, HUB[1] - 2, 8, 30, z=30, windows=(6, 12, 18, 24), band=(28,), shadow=False, depth=101)
    sc.cyl(HUB[0] + 4, HUB[1] - 2, 5, 24, z=60, windows=(6, 12, 18), band=(22,), shadow=False, depth=102)
    sc.cyl(HUB[0] + 4, HUB[1] - 2, 3.5, 3, z=84, side_l=HULL2_L, side_r=HULL2_R, top=HULL2_T, cap="dome", shadow=False, depth=103)
    sc.shadow(HUB[0] - 7, HUB[1] - 13, 22, 22, 60, 0.22)
    # skybridges
    sc.box(34, 44, 9, 2.6, 2.4, z=28, top=GLASS, left=GLASS, right="#7fb0d0", glass=True, shadow=False, depth=90)
    sc.box(63, 30, 6, 2.6, 2.4, z=36, top=GLASS, left=GLASS, right="#7fb0d0", glass=True, shadow=False, depth=96)
    # the old comm tower, kept as a heritage landmark
    sc.box(MAST[0] - 2, MAST[1] - 2, 5, 5, 36, windows=(10, 20, 28), band=(32, 33.4))
    sc.mast(MAST[0] + 0.5, MAST[1] + 0.5, 48)
    for tx, ty in [(38, 88), (46, 90), (54, 88), (62, 90), (70, 88), (16, 30), (16, 74), (96, 20)]:
        sc.tree(tx, ty, 0.85)
    for lx, ly in [(12, 14), (100, 14), (12, 96), (100, 60), (38, 68), (72, 68), (58, 100), (84, 72)]:
        sc.lamp(lx, ly, 7)
    sc.hover_truck(98, 58)
    sc.hover_truck(58, 104)
    sc.shuttle(20, 10, z=70, s=0.9, landed=False)
    sc.shuttle(96, 30, z=80, s=0.8, landed=False)
    sc.shuttle(70, 0, z=92, s=0.7, landed=False)
    sc.people([(46, 84), (50, 86), (58, 84), (64, 86), (42, 66), (74, 80), (88, 86), (92, 84), (60, 68), (34, 82), (22, 66), (80, 70)])
    for x, y, s in [(4, 6, 1.0), (4, 108, 1.0), (114, 112, 1.0), (116, 6, 0.9)]:
        sc.tree(x, y, s)
    return sc


for i, fn in enumerate([level1, level2, level3, level4, level5], start=1):
    with open(os.path.join(OUT, f"colony-hub-L{i}.svg"), "w") as f:
        f.write(fn().render())
print("ok")
