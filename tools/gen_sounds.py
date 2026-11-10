#!/usr/bin/env python3
"""Synthesise the game's sounds (docs/sound-contract.md "Files") and the listening page.

    python3 tools/gen_sounds.py            # writes game/sounds/<group>/<name>.wav + docs/sounds/index.html
    python3 tools/gen_sounds.py --only ui  # one group (or a name: --only touchdown)
    python3 tools/gen_sounds.py --no-page  # skip the page

Everything is made from numbers: oscillators, coloured noise, hand-written filters (one-pole, RBJ biquads, FFT
shaping), modal "metal" resonators, Schroeder reverb and soft clipping. numpy + the stdlib `wave` module only, fixed
seeds (crc32 of the sound name), so a rerun writes identical files. 16-bit PCM mono 44.1 kHz. Loops are built
circularly (FFT-shaped noise, whole-cycle tones, wrapped impulses), so the last sample runs into the first.

Loudness: each group has a target for K-weighted loudness (UI < selection hums < landing; the vehicle, ground and
weather beds sit between, since the game scales them again by speed, distance and weather; LOUDNESS_OFFSET nudges single
sounds such as hover, the wipers and thunder); every file is capped at -3 dBFS peak (a look-ahead limiter takes up to 5 dB off transients). The script prints a check table: duration, peak,
RMS, weighted loudness, power-weighted spectral centroid, energy per band, DC, edge samples and loop seams, and exits 1
if a file clips, has DC, starts/ends off zero or a loop seam jumps.
"""
from __future__ import annotations

import argparse
import base64
import html
import io
import sys
import wave
import zlib
from pathlib import Path

import numpy as np

SR = 44100
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "game" / "sounds"
PAGE = ROOT / "docs" / "sounds" / "index.html"
PEAK_CAP = 10 ** (-3 / 20)          # -3 dBFS
TARGET_LOUDNESS = {"ui": -25.0, "select": -21.0, "landing": -17.0, "vehicle": -22.0, "ground": -23.0, "ambience": -24.0}


# ---------------------------------------------------------------------------------------------------------------------
# Basics

def ns(sec: float) -> int:
    return int(round(sec * SR))


def tline(n: int) -> np.ndarray:
    return np.arange(n) / SR


def rng_for(name: str) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32(name.encode()))


def db(x: float) -> float:
    return 20 * np.log10(max(x, 1e-12))


def phase(freq, n: int) -> np.ndarray:
    """Running phase (radians) of a frequency that may change per sample."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    return 2 * np.pi * (np.cumsum(f) - f[0]) / SR


def curve(n: int, pts, smooth: bool = True) -> np.ndarray:
    """Control curve through (seconds, value) points; cosine-smoothed between points."""
    t = tline(n)
    xs = np.array([p[0] for p in pts], float)
    ys = np.array([p[1] for p in pts], float)
    if not smooth:
        return np.interp(t, xs, ys)
    idx = np.clip(np.searchsorted(xs, t, side="right") - 1, 0, len(xs) - 2)
    x0, x1 = xs[idx], xs[idx + 1]
    u = np.clip((t - x0) / np.maximum(x1 - x0, 1e-9), 0, 1)
    u = 0.5 - 0.5 * np.cos(np.pi * u)
    return ys[idx] + (ys[idx + 1] - ys[idx]) * u


def expdec(n: int, tau: float, attack: float = 0.002) -> np.ndarray:
    t = tline(n)
    a = np.clip(t / max(attack, 1e-6), 0, 1) if attack > 0 else 1.0
    return np.exp(-t / tau) * a


def fade(x: np.ndarray, fin: float = 0.005, fout: float = 0.01) -> np.ndarray:
    y = x.copy()
    a, b = ns(fin), ns(fout)
    if a:
        y[:a] *= 0.5 - 0.5 * np.cos(np.pi * np.arange(a) / a)
    if b:
        y[-b:] *= 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)
    return y


def tail_off(src: np.ndarray, sec: float = 0.004) -> np.ndarray:
    """Short fade at the end so a layer cut off mid-decay never clicks."""
    k = min(len(src), ns(sec))
    if k == 0:
        return src
    out = src.copy()
    out[-k:] *= 0.5 + 0.5 * np.cos(np.pi * (np.arange(k) + 1) / k)
    return out


def place(dst: np.ndarray, src: np.ndarray, at: float, gain: float = 1.0) -> np.ndarray:
    i = ns(at)
    if i >= len(dst):
        return dst
    m = min(len(src), len(dst) - i)
    dst[i:i + m] += gain * tail_off(src[:m])
    return dst


def place_circ(dst: np.ndarray, src: np.ndarray, i: int, gain: float = 1.0) -> np.ndarray:
    """Add src at sample i, wrapping past the end (for loops)."""
    n = len(dst)
    idx = (i + np.arange(len(src))) % n
    np.add.at(dst, idx, gain * tail_off(src))
    return dst


def norm(x: np.ndarray) -> np.ndarray:
    p = np.max(np.abs(x))
    return x / p if p > 0 else x


def rmsn(x: np.ndarray) -> np.ndarray:
    r = np.sqrt(np.mean(x ** 2))
    return x / r if r > 0 else x


def softclip(x: np.ndarray, drive: float = 1.5) -> np.ndarray:
    return np.tanh(drive * x) / np.tanh(drive)


# ---------------------------------------------------------------------------------------------------------------------
# Filters (no scipy)

def onepole_lp(x: np.ndarray, fc) -> np.ndarray:
    n = len(x)
    a = 1 - np.exp(-2 * np.pi * np.broadcast_to(np.asarray(fc, float), (n,)) / SR)
    xs, al = x.tolist(), a.tolist()
    y, out = 0.0, [0.0] * n
    for i in range(n):
        y += al[i] * (xs[i] - y)
        out[i] = y
    return np.array(out)


def onepole_hp(x: np.ndarray, fc) -> np.ndarray:
    return x - onepole_lp(x, fc)


def _rbj(kind: str, f, q, gain_db=0.0):
    w = 2 * np.pi * np.clip(np.asarray(f, float), 5, SR * 0.49) / SR
    cw, sw = np.cos(w), np.sin(w)
    al = sw / (2 * np.asarray(q, float))
    if kind == "lp":
        b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
        a0, a1, a2 = 1 + al, -2 * cw, 1 - al
    elif kind == "hp":
        b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
        a0, a1, a2 = 1 + al, -2 * cw, 1 - al
    elif kind == "bp":                       # 0 dB peak
        b0, b1, b2 = al, 0 * cw, -al
        a0, a1, a2 = 1 + al, -2 * cw, 1 - al
    elif kind == "hs":                       # high shelf (shelf slope 1)
        A = 10 ** (gain_db / 40)
        sa = 2 * np.sqrt(A) * al
        b0 = A * ((A + 1) + (A - 1) * cw + sa)
        b1 = -2 * A * ((A - 1) + (A + 1) * cw)
        b2 = A * ((A + 1) + (A - 1) * cw - sa)
        a0 = (A + 1) - (A - 1) * cw + sa
        a1 = 2 * ((A - 1) - (A + 1) * cw)
        a2 = (A + 1) - (A - 1) * cw - sa
    elif kind == "peak":
        A = 10 ** (gain_db / 40)
        b0, b1, b2 = 1 + al * A, -2 * cw, 1 - al * A
        a0, a1, a2 = 1 + al / A, -2 * cw, 1 - al / A
    else:
        raise ValueError(kind)
    return b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0


def biquad(x: np.ndarray, kind: str, f, q=0.707, gain_db=0.0) -> np.ndarray:
    """RBJ cookbook biquad; f (and q) may be per-sample arrays for sweeps."""
    n = len(x)
    co = _rbj(kind, f, q, gain_db)
    if np.ndim(co[0]) == 0:
        b0, b1, b2, a1, a2 = (float(c) for c in co)
        xs = x.tolist()
        out = [0.0] * n
        x1 = x2 = y1 = y2 = 0.0
        for i in range(n):
            x0 = xs[i]
            y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            out[i] = y0
            x2, x1, y2, y1 = x1, x0, y1, y0
        return np.array(out)
    B0, B1, B2, A1, A2 = (np.broadcast_to(c, (n,)).tolist() for c in co)
    xs = x.tolist()
    out = [0.0] * n
    x1 = x2 = y1 = y2 = 0.0
    for i in range(n):
        x0 = xs[i]
        y0 = B0[i] * x0 + B1[i] * x1 + B2[i] * x2 - A1[i] * y1 - A2[i] * y2
        out[i] = y0
        x2, x1, y2, y1 = x1, x0, y1, y0
    return np.array(out)


def bandpass(x, lo, hi, order=2):
    """Cascade of hp + lp biquads."""
    y = x
    for _ in range(order // 2 or 1):
        y = biquad(biquad(y, "hp", lo), "lp", hi)
    return y


def circular(fn, x: np.ndarray, reps: int = 3) -> np.ndarray:
    """Run a filter with memory on a loop: process the loop tiled and keep the last (steady) period."""
    return fn(np.tile(x, reps))[-len(x):]


# Frequency-domain shaping (circular by construction: ideal for loops and stationary noise beds)

def lp_mag(f, fc, order=2):
    return 1 / np.sqrt(1 + (f / fc) ** (2 * order))


def hp_mag(f, fc, order=2):
    with np.errstate(divide="ignore"):
        return 1 / np.sqrt(1 + (fc / np.maximum(f, 1e-6)) ** (2 * order))


def bell_mag(f, fc, q, gain):
    """Gaussian-ish resonance on a log axis."""
    return 1 + (gain - 1) * np.exp(-0.5 * (np.log2(np.maximum(f, 1) / fc) * q) ** 2)


def tilt(f, slope):
    """slope -0.5 = pink, -1 = brown (amplitude per frequency), floored at 20 Hz."""
    return np.maximum(f, 20.0) ** slope


def shaped_noise(n: int, rng, mag) -> np.ndarray:
    X = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    X *= mag(f)
    X[0] = 0
    return rmsn(np.fft.irfft(X, n))


def fft_filter(x: np.ndarray, mag, circular_: bool = False, pad: float = 0.3) -> np.ndarray:
    """Zero-phase magnitude filter. One-shots are zero-padded so nothing wraps."""
    n = len(x)
    m = n if circular_ else n + ns(pad)
    X = np.fft.rfft(x, m)
    f = np.fft.rfftfreq(m, 1 / SR)
    X *= mag(f)
    return np.fft.irfft(X, m)[:n]


def slow_noise(n: int, rng, fc: float, circ: bool = False) -> np.ndarray:
    """Smooth random modulation, unit RMS, below fc Hz."""
    return shaped_noise(n, rng, lambda f: lp_mag(f, fc, 3))


# Reverb: Schroeder combs + allpasses, vectorised in blocks of the delay length

def _comb(x: np.ndarray, d: int, g: float) -> np.ndarray:
    y = x.copy()
    for s in range(d, len(y), d):
        e = min(s + d, len(y))
        y[s:e] += g * y[s - d:e - d]
    return y


def _allpass(x: np.ndarray, d: int, g: float) -> np.ndarray:
    v = -g * x
    v[d:] += x[:-d]
    return _comb(v, d, g)


def reverb(x: np.ndarray, mix=0.2, rt60=0.8, size=1.0, tone=5000.0, tail=None, predelay=0.012) -> np.ndarray:
    tail = rt60 if tail is None else tail
    xp = np.concatenate([x, np.zeros(ns(tail))])
    pre = np.concatenate([np.zeros(ns(predelay)), xp])[:len(xp)]
    wet = np.zeros_like(xp)
    for i, d in enumerate((1116, 1188, 1277, 1356, 1422, 1491)):
        d = int(d * size)
        g = 10 ** (-3 * d / (SR * rt60))
        c = _comb(pre, d, g)
        wet += c if i % 2 == 0 else -c        # alternate signs keeps the low end from summing up
    wet /= 6
    for d in (556, 441, 341):
        wet = _allpass(wet, int(d * size), 0.5)
    wet = fft_filter(wet, lambda f: lp_mag(f, tone, 1) * hp_mag(f, 120, 1), pad=0.05)
    return xp * (1 - mix) + wet * mix * 2.2


# ---------------------------------------------------------------------------------------------------------------------
# Sources

def tone(freq, n, partials=((1, 1.0),), ph0=0.0) -> np.ndarray:
    """Additive tone; partials above Nyquist are muted per sample."""
    f = np.broadcast_to(np.asarray(freq, float), (n,))
    p = phase(f, n) + ph0
    y = np.zeros(n)
    for k, a in partials:
        y += a * np.sin(k * p) * (k * f < SR * 0.45)
    return y


def saw_partials(k_max=16, roll=1.0):
    return tuple((k, 1 / k ** roll) for k in range(1, k_max + 1))


def square_partials(k_max=9, roll=1.0):
    return tuple((k, 1 / k ** roll) for k in range(1, k_max + 1, 2))


def blip(f, dur, sweep=1.0, sweep_t=0.03, tau=0.03, attack=0.002, bright=0.15, fm=0.0, fm_ratio=2.0) -> np.ndarray:
    """A console chirp: sine with a little 2nd harmonic, quick pitch glide, exp decay."""
    n = ns(dur)
    t = tline(n)
    fr = f * (sweep + (1 - sweep) * np.exp(-t / sweep_t))
    p = phase(fr, n)
    mod = fm * np.exp(-t / (tau * 0.7)) * np.sin(fm_ratio * p)
    y = np.sin(p + mod) + bright * np.sin(2 * p + 0.3)
    return y * expdec(n, tau, attack)


def bell(f, dur, ratio=1.4, index=2.0, tau=0.25, attack=0.002) -> np.ndarray:
    """Two-operator FM bell."""
    n = ns(dur)
    t = tline(n)
    p = 2 * np.pi * f * t
    idx = index * np.exp(-t / (tau * 0.5))
    return np.sin(p + idx * np.sin(ratio * p)) * expdec(n, tau, attack)


def modal(dur, f0, ratios, amps, taus, rng=None, detune=0.0) -> np.ndarray:
    """Struck-metal ring: inharmonic partials with their own decays."""
    n = ns(dur)
    t = tline(n)
    y = np.zeros(n)
    for i, (r, a, tau) in enumerate(zip(ratios, amps, taus)):
        ph = rng.uniform(0, 2 * np.pi) if rng is not None else 0.0
        fr = f0 * r * (1 + (rng.uniform(-detune, detune) if rng is not None else 0))
        if fr < SR * 0.45:
            y += a * np.sin(2 * np.pi * fr * t + ph) * np.exp(-t / tau)
    return y * np.clip(t / 0.0008, 0, 1)


BAR = (1.0, 2.756, 5.404, 8.933)           # free-free bar modes
PLATE = (1.0, 1.594, 2.136, 2.653, 2.918, 3.598)
TUBE = (1.0, 2.01, 3.03, 4.08, 5.15)       # slightly stretched, hollow


def burst(dur, rng, lo=200, hi=8000, tau=0.01, attack=0.0005) -> np.ndarray:
    n = ns(dur)
    x = rng.standard_normal(n) * expdec(n, tau, attack)
    return fft_filter(x, lambda f: hp_mag(f, lo, 2) * lp_mag(f, hi, 2), pad=0.05)


def thump(dur, f_hi, f_lo, tau, rng, drop=0.03, noise=0.4, noise_fc=300) -> np.ndarray:
    """Low hit: sine with a fast pitch drop plus a lowpassed noise puff."""
    n = ns(dur)
    t = tline(n)
    f = f_lo + (f_hi - f_lo) * np.exp(-t / drop)
    y = np.sin(phase(f, n)) * expdec(n, tau, 0.001)
    if noise:
        y += noise * burst(dur, rng, 20, noise_fc, tau * 0.4, 0.0005) * 3
    return y


def tick(rng, f0=3200, dur=0.03, tau=0.004, ring=0.5) -> np.ndarray:
    """Small metal latch tick: click + tiny bright ring."""
    c = burst(dur, rng, 1500, 9000, 0.0012) * 2.5
    r = modal(dur, f0, BAR[:3], (1, 0.5, 0.25), (tau * 2, tau, tau * 0.6), rng)
    return c + ring * r


def clunk(rng, f0=300, dur=0.7, low=90, weight=1.0, ring=0.5, ring_tau=0.3) -> np.ndarray:
    """Heavy metal clunk with a short ring."""
    y = weight * thump(dur, low * 1.6, low, 0.07, rng, drop=0.015, noise=0.5, noise_fc=900)
    y += 0.7 * burst(dur, rng, 300, 4000, 0.012)
    y += ring * modal(dur, f0, BAR, (1, 0.55, 0.3, 0.15), (ring_tau, ring_tau * 0.55, ring_tau * 0.3, ring_tau * 0.18),
                      rng, 0.004)
    return y


def impulses(n: int, rate, rng, circ: bool = False, heavy: float = 3.0) -> np.ndarray:
    """Random impulse train (rate may vary per sample: Poisson thinning); heavy-tailed amplitudes."""
    r = np.broadcast_to(np.asarray(rate, float), (n,))
    hits = rng.random(n) < r / SR
    amp = rng.random(n) ** heavy * rng.choice((-1.0, 1.0), n)
    return np.where(hits, amp, 0.0)


def convolve(x: np.ndarray, k: np.ndarray, circ: bool = False) -> np.ndarray:
    if circ:
        return np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(k, len(x)), len(x))
    return np.convolve(x, k)[:len(x)]


def wobble(n, rng, fc=6.0, amt=0.004):
    return 1 + amt * slow_noise(n, rng, fc)


def motor(freq, n, rng, partials=((1, 1), (2, 0.45), (3, 0.25), (4, 0.15), (5, 0.08)), mesh_k=0, mesh=0.0,
          wob=0.003, wob_fc=7.0) -> np.ndarray:
    """Motor / servo whine: harmonic stack, a gear-mesh partial and a little speed wander."""
    f = np.broadcast_to(np.asarray(freq, float), (n,)) * wobble(n, rng, wob_fc, wob)
    y = tone(f, n, partials)
    if mesh_k:
        p = phase(f, n)
        y += mesh * np.sin(mesh_k * p) * (0.6 + 0.4 * np.sin(p)) * (mesh_k * f < SR * 0.45)
    return y


# ---------------------------------------------------------------------------------------------------------------------
# UI (soft console chirps)

def ui_hover(rng):
    n = ns(0.045)
    y = blip(2650, 0.045, sweep=1.03, tau=0.009, attack=0.0015, bright=0.08)
    y += 0.25 * burst(0.045, rng, 4000, 10000, 0.0015)[:n]
    return fade(y, 0.002, 0.008)


def ui_click(rng):
    y = np.zeros(ns(0.085))
    place(y, blip(1720, 0.04, sweep=1.02, tau=0.012, bright=0.12), 0.0, 0.8)
    place(y, blip(2300, 0.065, sweep=1.015, tau=0.016, bright=0.1, fm=0.4, fm_ratio=3), 0.022)
    place(y, burst(0.01, rng, 3000, 9000, 0.0012), 0.0, 0.35)
    return fade(y, 0.001, 0.012)


def ui_back(rng):
    y = np.zeros(ns(0.12))
    place(y, blip(2150, 0.05, sweep=0.97, tau=0.014, bright=0.1), 0.0, 0.9)
    place(y, blip(1520, 0.075, sweep=0.96, tau=0.02, bright=0.12), 0.034)
    place(y, burst(0.01, rng, 2500, 8000, 0.0012), 0.0, 0.3)
    return fade(y, 0.001, 0.015)


def ui_locked(rng):
    y = np.zeros(ns(0.17))
    for at, g in ((0.0, 1.0), (0.072, 0.8)):
        k = 0.7 * burst(0.06, rng, 150, 1100, 0.006) + 0.9 * thump(0.06, 420, 330, 0.014, rng, 0.01, 0.0)
        place(y, k, at, g)
    y = biquad(y, "lp", 1400, 0.6)
    return fade(y, 0.001, 0.02)


def _arp(freqs, gap, tail, rng, tau=0.05, sweep=1.02, rev=0.18, whoosh=None):
    dur = gap * (len(freqs) - 1) + tail
    y = np.zeros(ns(dur))
    for i, f in enumerate(freqs):
        place(y, blip(f, tail, sweep=sweep, tau=tau, bright=0.1, fm=0.25, fm_ratio=2), i * gap,
              0.75 + 0.25 * i / max(1, len(freqs) - 1))
    if whoosh:
        n = len(y)
        w = shaped_noise(n, rng, lambda f: tilt(f, -0.3)) * curve(n, [(0, 0), (dur * 0.35, 1), (dur, 0)])
        w = biquad(w, "bp", curve(n, [(0, whoosh[0]), (dur, whoosh[1])]), 1.4)
        y += 0.12 * w
    y = reverb(y, rev, 0.35, 0.6, 7000, tail=0.18)
    return fade(y, 0.001, 0.04)


def ui_open(rng):
    return _arp((1175, 1568, 2093), 0.045, 0.16, rng, tau=0.045, whoosh=(900, 4500))


def ui_close(rng):
    return _arp((1760, 1319, 988), 0.045, 0.14, rng, tau=0.04, sweep=0.985, whoosh=(4000, 800))


def ui_confirm(rng):
    y = np.zeros(ns(0.34))
    place(y, blip(1319, 0.09, sweep=1.01, tau=0.035, bright=0.1), 0.0, 0.8)
    place(y, blip(1976, 0.26, sweep=1.005, tau=0.08, bright=0.08, fm=0.6, fm_ratio=2), 0.07)
    place(y, 0.25 * bell(3952, 0.2, 1.41, 0.8, 0.05), 0.075)
    y = reverb(y, 0.15, 0.4, 0.6, 7000, tail=0.15)
    return fade(y, 0.001, 0.05)


def ui_error(rng):
    y = np.zeros(ns(0.36))
    for at, f in ((0.0, 392.0), (0.15, 294.0)):
        n = ns(0.17)
        t = tline(n)
        s = tone(f * (1 + 0.004 * np.sin(2 * np.pi * 31 * t)), n, square_partials(9, 1.2))
        s *= curve(n, [(0, 0), (0.006, 1), (0.11, 0.8), (0.17, 0)])
        place(y, s, at)
    y = biquad(y, "lp", 1800, 0.8)
    y = reverb(y, 0.12, 0.3, 0.5, 4000, tail=0.1)
    return fade(y, 0.002, 0.04)


def ui_tutorial(rng):
    y = np.zeros(ns(0.62))
    n = ns(0.09)
    t = tline(n)
    bloop = np.sin(phase(480 + 520 * (1 - np.exp(-t / 0.025)), n)) * expdec(n, 0.03, 0.003)
    place(y, bloop, 0.0, 0.7)
    place(y, bell(1568, 0.5, 2.0, 1.2, 0.16), 0.05, 0.55)
    place(y, bell(2349, 0.45, 2.0, 0.9, 0.12), 0.09, 0.4)
    place(y, bell(3136, 0.3, 3.0, 0.6, 0.06), 0.12, 0.15)
    y = reverb(y, 0.25, 0.7, 0.8, 6500, tail=0.25)
    return fade(y, 0.002, 0.08)


def ui_tutorial_done(rng):
    y = np.zeros(ns(0.95))
    for i, f in enumerate((1047, 1319, 1568)):
        place(y, bell(f, 0.6, 2.0, 1.0, 0.2 + 0.05 * i), i * 0.08, 0.55)
        place(y, blip(f, 0.12, tau=0.04, bright=0.05), i * 0.08, 0.35)
    place(y, bell(2093, 0.55, 3.0, 0.7, 0.22), 0.24, 0.35)
    sp = impulses(ns(0.4), 70, rng, heavy=2)
    sp = convolve(sp, modal(0.01, 6200, (1, 1.5), (1, 0.5), (0.002, 0.0012)))
    place(y, sp, 0.26, 0.2)
    y = reverb(y, 0.3, 0.9, 0.9, 7000, tail=0.3)
    return fade(y, 0.002, 0.1)


def ui_place(rng):
    y = np.zeros(ns(0.46))
    place(y, thump(0.25, 150, 72, 0.06, rng, 0.012, 0.6, 600), 0.0, 0.9)
    place(y, 0.35 * modal(0.25, 520, BAR, (1, 0.4, 0.2, 0.1), (0.08, 0.05, 0.03, 0.02), rng), 0.0)
    place(y, blip(1760, 0.18, sweep=1.01, tau=0.05, bright=0.08, fm=0.4), 0.06, 0.45)
    place(y, burst(0.2, rng, 2500, 9000, 0.05, 0.01), 0.1, 0.1)
    y = reverb(y, 0.15, 0.4, 0.7, 5000, tail=0.12)
    return fade(y, 0.001, 0.05)


def ui_cancel(rng):
    n = ns(0.26)
    t = tline(n)
    f = 1250 * np.exp(-t / 0.12) + 380
    s = np.sin(phase(f, n)) * curve(n, [(0, 0), (0.008, 1), (0.12, 0.5), (0.26, 0)])
    w = biquad(shaped_noise(n, rng, lambda f_: tilt(f_, -0.3)), "bp", f * 2.2, 1.2) * curve(
        n, [(0, 0), (0.02, 1), (0.26, 0)])
    y = 0.8 * s + 0.15 * w
    y = reverb(y, 0.12, 0.3, 0.6, 5000, tail=0.08)
    return fade(y, 0.002, 0.03)


# ---------------------------------------------------------------------------------------------------------------------
# Landing

def rocket_roar(n: int, rng, circ: bool, bright: float = 1.0, crackle_rate=55.0) -> np.ndarray:
    """Broadband roar: brown rumble + pink mid roar with flutter + hiss + crackle. bright scales the top end."""
    rumble = shaped_noise(n, rng, lambda f: tilt(f, -1.0) * lp_mag(f, 170, 2) * hp_mag(f, 22, 2))
    roar = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * hp_mag(f, 160, 2) * lp_mag(f, 2600 * bright, 2)
                        * bell_mag(f, 650, 1.2, 1.8))
    hiss = shaped_noise(n, rng, lambda f: hp_mag(f, 2800, 2) * lp_mag(f, 9500 * bright, 2))
    sway = slow_noise(n, rng, 2.5)
    flutter = shaped_noise(n, rng, lambda f: hp_mag(f, 8, 2) * lp_mag(f, 25, 2))
    roar *= 1 + 0.18 * sway + 0.12 * flutter
    rumble *= 1 + 0.12 * slow_noise(n, rng, 1.5)
    imp = impulses(n, crackle_rate * (1 + 0.5 * np.clip(flutter, -1, 2)), rng, heavy=2.5)
    kern = rng.standard_normal(ns(0.004)) * np.exp(-np.arange(ns(0.004)) / ns(0.0008))
    cr = convolve(imp, kern, circ)
    cr = fft_filter(cr, lambda f: hp_mag(f, 1300, 2) * lp_mag(f, 11000, 1), circular_=circ)
    cr = cr / (np.sqrt(np.mean(cr ** 2)) + 1e-9)
    y = 1.0 * rumble + 0.62 * roar + 0.11 * hiss * bright + 0.16 * cr
    return softclip(0.28 * y, 1.2)


def landing_burn_loop(rng):
    n = ns(3.0)
    return rocket_roar(n, rng, circ=True)


def landing_burn_ignite(rng):
    dur = 1.7
    n = ns(dur)
    y = np.zeros(n)
    hiss = shaped_noise(ns(0.12), rng, lambda f: hp_mag(f, 2000, 2) * lp_mag(f, 9000)) * curve(
        ns(0.12), [(0, 0), (0.06, 1), (0.12, 0.6)])
    place(y, hiss, 0.0, 0.25)
    place(y, burst(0.2, rng, 400, 12000, 0.006), 0.09, 0.8)                       # igniter crack
    place(y, thump(0.4, 140, 48, 0.12, rng, 0.02, 0.6, 400), 0.09, 0.7)           # chamber pop
    m = n - ns(0.09)
    roar = rocket_roar(m, rng, circ=False, bright=1.0, crackle_rate=90)
    env = curve(m, [(0, 0.15), (0.06, 0.9), (0.16, 0.75), (0.45, 1.0), (1.1, 0.8), (dur - 0.09, 0.0)])
    bright = curve(m, [(0, 900), (0.25, 7000), (dur, 9000)])
    roar = biquad(roar, "lp", bright, 0.7)
    place(y, roar * env, 0.09)
    return fade(y, 0.003, 0.25)


def landing_engine_cut(rng):
    dur = 1.9
    n = ns(dur)
    y = np.zeros(n)
    m = ns(0.9)
    roar = rocket_roar(m, rng, circ=False) * curve(m, [(0, 1), (0.05, 0.9), (0.45, 0.12), (0.9, 0)])
    roar = biquad(roar, "lp", curve(m, [(0, 7000), (0.4, 300), (0.9, 150)]), 0.8)
    place(y, roar, 0.0)
    place(y, thump(0.4, 90, 40, 0.1, rng, 0.03, 0.8, 250), 0.04, 0.8)            # chuff of the valve closing
    hiss = shaped_noise(n, rng, lambda f: hp_mag(f, 2500) * lp_mag(f, 8000)) * curve(
        n, [(0, 0), (0.08, 0.5), (0.35, 0.15), (0.9, 0.04), (dur, 0)])
    y += 0.07 * hiss
    for at in (0.55, 0.83, 1.07, 1.38, 1.62):                                    # hot metal ticking as it cools
        place(y, tick(rng, rng.uniform(2800, 5200), 0.05, 0.006, 0.8), at, rng.uniform(0.12, 0.25))
    return fade(y, 0.003, 0.2)


def creak(n, rng, rate_pts, res=(520, 870, 1340), q=28, jitter=0.35) -> np.ndarray:
    """Stick-slip creak: an irregular pulse train through narrow metal resonances."""
    rate = curve(n, rate_pts)
    ph = np.cumsum(rate * (1 + jitter * slow_noise(n, rng, 30)) / SR)
    pulses = np.diff(np.floor(ph), prepend=0.0) * (0.5 + rng.random(n))
    pulses = convolve(pulses, np.hanning(ns(0.0012)))                 # rounded slips: no broadband spikes
    y = np.zeros(n)
    for i, f in enumerate(res):
        y += biquad(biquad(pulses, "bp", f * (1 + 0.01 * i), q), "bp", f * (1 + 0.01 * i), q * 0.5) / (1 + i * 0.4)
    return y


def landing_touchdown(rng):
    dur = 1.6
    n = ns(dur)
    y = np.zeros(n)
    place(y, thump(1.0, 95, 42, 0.2, rng, 0.04, 0.9, 260), 0.0, 1.0)
    place(y, burst(0.5, rng, 500, 2500, 0.05, 0.002), 0.0, 0.5)                    # gear crunch / grit
    place(y, thump(0.6, 70, 40, 0.12, rng, 0.03, 0.5, 200), 0.32, 0.45)            # suspension rebound
    m = ns(0.9)
    cr = creak(m, rng, [(0, 48), (0.4, 30), (0.9, 18)], (480, 815, 1290, 2100), 30)
    cr *= curve(m, [(0, 0), (0.08, 1), (0.5, 0.6), (0.9, 0)])
    place(y, rmsn(cr) * 0.13, 0.1)
    place(y, 0.45 * modal(1.0, 210, PLATE, (1, .7, .5, .35, .25, .2), (.3, .25, .2, .15, .12, .1), rng, 0.003), 0.004)
    dust = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * hp_mag(f, 300) * lp_mag(f, 3500)) * curve(
        n, [(0, 0), (0.05, 0.6), (0.5, 0.2), (dur, 0)])
    y += 0.08 * dust
    y = reverb(y, 0.12, 0.9, 1.2, 3000, tail=0.3)
    return fade(y, 0.002, 0.25)


def hydraulic(dur, rng, f_pts, mesh_k=7, start_clack=True, ripple=24.0) -> np.ndarray:
    """Hydraulic servo run: motor + pump ripple + fluid hiss."""
    n = ns(dur)
    t = tline(n)
    f = curve(n, f_pts)
    y = motor(f, n, rng, mesh_k=mesh_k, mesh=0.35, wob=0.004)
    run = curve(n, [(0, 0), (0.12, 1), (dur - 0.15, 1), (dur, 0)])
    y *= run * (1 + 0.25 * np.sin(2 * np.pi * ripple * t))
    hiss = shaped_noise(n, rng, lambda fr: hp_mag(fr, 1800) * lp_mag(fr, 6500)) * run
    y = 0.5 * y + 0.12 * hiss
    if start_clack:
        place(y, tick(rng, 2200, 0.06, 0.01, 0.6), 0.0, 0.5)
    return y


def landing_ramp_open(rng):
    dur = 2.3
    y = np.zeros(ns(dur))
    place(y, clunk(rng, 520, 0.4, 120, 0.4, 0.3, 0.12), 0.0, 0.6)                  # unlatch
    place(y, hydraulic(1.75, rng, [(0, 120), (0.3, 205), (1.5, 212), (1.75, 150)]), 0.06)
    place(y, clunk(rng, 190, 0.7, 70, 1.0, 0.6, 0.35), 1.72, 1.0)                  # ramp meets the ground
    place(y, burst(0.3, rng, 300, 3000, 0.06, 0.004), 1.73, 0.2)
    y = reverb(y, 0.12, 0.6, 1.0, 3500, tail=0.2)
    return fade(y, 0.002, 0.2)


def landing_ramp_close(rng):
    dur = 2.1
    y = np.zeros(ns(dur))
    place(y, hydraulic(1.6, rng, [(0, 140), (0.3, 232), (1.4, 240), (1.6, 175)], mesh_k=9), 0.0)
    place(y, clunk(rng, 360, 0.6, 85, 1.0, 0.45, 0.25), 1.55, 0.9)                 # ramp shuts
    place(y, tick(rng, 3400, 0.05, 0.008, 0.8), 1.7, 0.5)                         # latch
    n = ns(0.35)
    seal = shaped_noise(n, rng, lambda f: hp_mag(f, 2500) * lp_mag(f, 9000)) * expdec(n, 0.08, 0.01)
    place(y, seal, 1.72, 0.25)
    y = reverb(y, 0.12, 0.6, 1.0, 3500, tail=0.15)
    return fade(y, 0.002, 0.15)


def landing_liftoff(rng):
    dur = 3.3
    n = ns(dur)
    y = np.zeros(n)
    place(y, burst(0.25, rng, 400, 12000, 0.008), 0.05, 1.5)
    place(y, thump(0.6, 130, 40, 0.18, rng, 0.03, 0.8, 300), 0.05, 1.2)
    m = n - ns(0.05)
    roar = rocket_roar(m, rng, circ=False, bright=1.1, crackle_rate=120)
    env = curve(m, [(0, 0.2), (0.1, 0.8), (0.3, 0.7), (1.8, 1.0), (2.6, 0.85), (dur - 0.05, 0.0)])
    roar = biquad(roar, "lp", curve(m, [(0, 1500), (0.4, 6000), (2.0, 10000), (dur, 3000)]), 0.7)
    rum = shaped_noise(m, rng, lambda f: tilt(f, -1) * lp_mag(f, 90, 3) * hp_mag(f, 20)) * curve(
        m, [(0, 0), (0.5, 0.5), (1.6, 1), (dur, 0)])
    place(y, roar * env + 0.25 * rum, 0.05)
    y = softclip(0.8 * y, 1.2)
    return fade(y, 0.003, 0.6)


def landing_winch_loop(rng):
    L = 2.0
    n = ns(L)
    t = tline(n)
    f0 = 110.0                                                                    # whole cycles in 2 s
    p = 2 * np.pi * f0 * t + 0.6 * np.sin(2 * np.pi * 3 * t / L)
    hum = sum(a * np.sin(k * p) for k, a in ((1, 1), (2, 0.6), (3, 0.3), (4, 0.2), (8, 0.25), (16, 0.08)))
    hum *= 1 + 0.15 * np.sin(2 * np.pi * 24 * t)
    y = 0.35 * hum
    clicks = np.zeros(n)
    k = tick(rng, 2900, 0.04, 0.005, 0.7)
    for i in range(24):                                                           # pawl over the ratchet
        place_circ(clicks, k, int(i * n / 24 + rng.integers(-60, 60)), 0.7 if i % 2 else 1.0)
    y += 0.35 * clicks
    rope = shaped_noise(n, rng, lambda f: hp_mag(f, 900) * lp_mag(f, 4500)) * (1 + 0.4 * np.sin(
        2 * np.pi * 6 * t / L * 2))
    y += 0.08 * rope
    y += 0.1 * shaped_noise(n, rng, lambda f: tilt(f, -1) * lp_mag(f, 250) * hp_mag(f, 30))
    return y - y.mean()


def landing_module_clamp(rng):
    dur = 1.0
    y = np.zeros(ns(dur))
    place(y, tick(rng, 3000, 0.04, 0.006, 0.6), 0.0, 0.4)
    place(y, clunk(rng, 410, 0.9, 95, 1.0, 0.6, 0.32), 0.02, 1.0)
    place(y, clunk(rng, 455, 0.7, 105, 0.6, 0.4, 0.22), 0.17, 0.5)                 # second clamp jaw
    n = ns(0.3)
    place(y, shaped_noise(n, rng, lambda f: hp_mag(f, 2500) * lp_mag(f, 8000)) * expdec(n, 0.06, 0.01), 0.3, 0.12)
    y = reverb(y, 0.12, 0.7, 1.0, 4000, tail=0.2)
    return fade(y, 0.001, 0.2)


def landing_unfurl(rng):
    dur = 5.0
    y = np.zeros(ns(dur))
    place(y, tick(rng, 3300, 0.06, 0.01, 0.9), 0.02, 0.8)
    place(y, servo_run(rng, 1.3, [(0, 180), (0.25, 360), (1.0, 380), (1.3, 250)], mesh_k=6), 0.1, 0.9)
    place(y, tick(rng, 2600, 0.06, 0.01, 0.9), 1.45, 0.8)
    a = servo_run(rng, 1.25, [(0, 150), (0.3, 285), (1.0, 300), (1.25, 210)], mesh_k=11)
    m = len(a)
    a += 0.6 * rmsn(creak(m, rng, [(0, 25), (1.25, 14)], (610, 990, 1530), 26)) * 0.12 * curve(
        m, [(0, 0), (0.3, 1), (1.25, 0)])
    place(y, a, 1.55, 0.9)
    place(y, tick(rng, 3100, 0.06, 0.01, 0.9), 2.85, 0.8)
    b = servo_run(rng, 1.15, [(0, 260), (0.2, 500), (1.0, 530), (1.15, 380)], mesh_k=5)
    for i in range(8):                                                            # telescoping mast detents
        place(b, tick(rng, 4200, 0.03, 0.004, 0.5), 0.18 + i * 0.115, 0.35)
    place(y, b, 2.95, 0.85)
    place(y, clunk(rng, 380, 0.7, 90, 0.9, 0.5, 0.28), 4.18, 0.9)                  # lock
    n = ns(0.6)
    place(y, shaped_noise(n, rng, lambda f: hp_mag(f, 2200) * lp_mag(f, 8000)) * expdec(n, 0.15, 0.02), 4.33, 0.22)
    y = reverb(y, 0.12, 0.6, 1.0, 4000, tail=0.0)
    return fade(y[:ns(dur)], 0.002, 0.15)


def servo_run(rng, dur, f_pts, mesh_k=7) -> np.ndarray:
    """Electric servo whirr with a pitch glide."""
    n = ns(dur)
    f = curve(n, f_pts)
    y = motor(f, n, rng, ((1, 1), (2, 0.5), (3, 0.35), (4, 0.2), (6, 0.12)), mesh_k, 0.5, 0.006, 12)
    y *= curve(n, [(0, 0), (0.06, 1), (dur - 0.1, 1), (dur, 0)])
    y += 0.08 * shaped_noise(n, rng, lambda fr: hp_mag(fr, 1500) * lp_mag(fr, 5000)) * curve(
        n, [(0, 0), (0.1, 1), (dur, 0)])
    return 0.5 * y


def landing_uplink(rng):
    dur = 1.65
    n = ns(dur)
    y = np.zeros(n)
    for i, f in enumerate((900, 1100, 1350, 1650, 2000)):
        m = ns(0.06)
        tt = tline(m)
        c = np.sin(phase(f * (1 + 0.25 * tt / 0.06), m)) * curve(m, [(0, 0), (0.004, 1), (0.04, 0.7), (0.06, 0)])
        place(y, c, i * 0.085, 0.55 + 0.08 * i)
    m = ns(0.38)
    tt = tline(m)
    sym = (np.floor(tt * 45) + rng.integers(0, 2, 999)[np.floor(tt * 45).astype(int)]) % 2
    fsk = np.sin(phase(np.where(sym > 0, 2400.0, 1800.0), m)) * curve(m, [(0, 0), (0.02, 1), (0.34, 1), (0.38, 0)])
    place(y, fsk, 0.47, 0.18)
    place(y, bell(1568, 0.7, 2.0, 1.0, 0.18), 0.88, 0.5)
    place(y, bell(2349, 0.6, 2.0, 0.8, 0.15), 0.94, 0.4)
    static = shaped_noise(n, rng, lambda f: hp_mag(f, 400) * lp_mag(f, 3200)) * curve(
        n, [(0, 0), (0.05, 1), (0.85, 1), (1.1, 0), (dur, 0)])
    y += 0.03 * static
    y = biquad(y, "hp", 300, 0.7)
    y = reverb(y, 0.2, 0.6, 0.8, 6000, tail=0.0)
    return fade(y[:n], 0.002, 0.2)


def landing_vehicle_roll(rng):
    dur = 1.9
    n = ns(dur)
    y = np.zeros(n)
    f = curve(n, [(0, 320), (0.5, 470), (1.4, 520), (dur, 440)])
    wh = motor(f, n, rng, ((1, 1), (2, 0.3), (3, 0.1)), 0, 0, 0.003)
    y += 0.3 * wh * curve(n, [(0, 0), (0.15, 1), (1.5, 1), (dur, 0.3)])
    roll = shaped_noise(n, rng, lambda fr: tilt(fr, -0.8) * lp_mag(fr, 500) * hp_mag(fr, 40)) * curve(
        n, [(0, 0.2), (0.2, 1), (1.25, 1), (1.32, 0.5), (dur, 0.3)])
    y += 0.5 * roll
    for i, at in enumerate((0.18, 0.4, 0.59, 0.75, 0.89, 1.01, 1.11)):            # wheels over the ramp ribs
        place(y, clunk(rng, 260 + 15 * (i % 3), 0.3, 110, 0.6, 0.35, 0.09), at, 0.35)
    place(y, thump(0.6, 80, 40, 0.15, rng, 0.03, 0.9, 300), 1.27, 0.9)            # off the ramp onto the ground
    m = ns(0.6)
    place(y, shaped_noise(m, rng, lambda fr: tilt(fr, -0.5) * hp_mag(fr, 200) * lp_mag(fr, 3000)) * expdec(
        m, 0.15, 0.01), 1.28, 0.25)
    y = reverb(y, 0.1, 0.6, 1.0, 3500, tail=0.0)
    return fade(y[:n], 0.003, 0.2)


# ---------------------------------------------------------------------------------------------------------------------
# Selection hums (buildings + vehicles): ~1-1.6 s, fade in / out

def hum_env(n, fin=0.12, fout=0.35):
    d = n / SR
    return curve(n, [(0, 0), (fin, 1), (d - fout, 1), (d, 0)])


def sel_fusion_reactor(rng):
    dur = 1.6
    n = ns(dur)
    t = tline(n)
    p = phase(55 * wobble(n, rng, 2, 0.002), n)
    hum = (np.sin(p) + 0.55 * np.sin(2 * p) + 0.35 * np.sin(3 * p) + 0.18 * np.sin(4 * p) + 0.12 * np.sin(6 * p)
           + 0.08 * np.sin(8 * p) + 0.05 * np.sin(10 * p))
    buzz = tone(110, n, saw_partials(24, 1.3))
    buzz = biquad(buzz, "bp", 650, 0.8) * (0.6 + 0.4 * np.sin(2 * np.pi * 0.9 * t))
    sh = np.zeros(n)
    for f, rate in ((3520, 3.1), (4186, 4.3), (5274, 2.2), (4699, 5.7)):          # shimmer above the hum
        sh += np.sin(2 * np.pi * f * t + rng.uniform(0, 6)) * (0.5 + 0.5 * np.sin(2 * np.pi * rate * t + rng.uniform(0, 6)))
    plasma = shaped_noise(n, rng, lambda f: bell_mag(f, 4500, 2.5, 6) * hp_mag(f, 2000) * lp_mag(f, 9000)) * 0.4
    swell = curve(n, [(0, 0.7), (0.6, 1.0), (dur, 0.9)])
    y = (hum + 0.55 * buzz) * swell + 0.035 * sh + 0.03 * plasma
    return y * hum_env(n, 0.15, 0.4)


def sel_refinery(rng):
    dur = 1.5
    n = ns(dur)
    t = tline(n)
    mot = tone(72 * wobble(n, rng, 3, 0.01), n, saw_partials(20, 1.0))
    mot = biquad(mot, "lp", 900, 0.9)
    grit = shaped_noise(n, rng, lambda f: hp_mag(f, 700) * lp_mag(f, 3200) * bell_mag(f, 1500, 2, 2))
    grind_am = np.clip(0.55 + 0.45 * np.sin(2 * np.pi * 11.5 * t) + 0.35 * slow_noise(n, rng, 20), 0, None)
    grind = grit * grind_am
    y = 0.45 * mot + 0.5 * grind
    for at in (0.12, 0.52, 0.92, 1.32):                                          # crusher jaw clank
        place(y, clunk(rng, 330, 0.35, 85, 0.6, 0.4, 0.1), at, 0.45)
    y = softclip(y, 1.6)
    return y * hum_env(n)


def sel_mine(rng):
    dur = 1.45
    n = ns(dur)
    t = tline(n)
    drill = tone(96 * wobble(n, rng, 5, 0.008), n, ((1, 1), (2, 0.6), (3, 0.5), (5, 0.35), (7, 0.25), (9, 0.15)))
    drill *= 1 + 0.3 * np.sin(2 * np.pi * 15 * t)
    y = 0.25 * drill
    for i, at in enumerate((0.06, 0.45, 0.84, 1.23)):                            # piledriver thud
        place(y, thump(0.4, 120, 48, 0.11, rng, 0.02, 0.7, 380), at, 1.0 if i % 2 == 0 else 0.8)
        place(y, burst(0.2, rng, 500, 3000, 0.02), at + 0.005, 0.5)
    y += 0.06 * shaped_noise(n, rng, lambda f: tilt(f, -1) * lp_mag(f, 400) * hp_mag(f, 40))
    return y * hum_env(n, 0.03, 0.2)


def sel_storage_container(rng):
    dur = 1.35
    n = ns(dur)
    t = tline(n)
    air = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * hp_mag(f, 60) * lp_mag(f, 3000))
    y = np.zeros(n)
    f0 = 96.0
    for i, r in enumerate(TUBE):                                                  # hollow box modes, air driven
        y += biquad(air, "bp", f0 * r, 45) * (1.0 / (1 + 0.5 * i))
    y = rmsn(y) * (1 + 0.25 * np.sin(2 * np.pi * 0.8 * t))
    knock = modal(1.3, 182, PLATE, (1, .8, .5, .4, .3, .2), (.5, .4, .3, .25, .2, .15), rng, 0.003)
    knock += 0.6 * thump(1.3, 110, 80, 0.08, rng, 0.02, 0.3, 500)
    out = 0.6 * y + np.concatenate([np.zeros(ns(0.05)), knock])[:n] * 0.8
    out = reverb(out, 0.25, 1.0, 0.7, 2500, tail=0.0)[:n]
    return out * hum_env(n, 0.04, 0.35)


def sel_science_lab(rng):
    dur = 1.5
    n = ns(dur)
    t = tline(n)
    pad = np.zeros(n)
    for f in (220.0, 277.18, 329.63, 440.0):
        for d in (-0.003, 0.003):
            pad += np.sin(2 * np.pi * f * (1 + d) * t + rng.uniform(0, 6))
    pad = biquad(pad, "lp", 1200, 0.7) * 0.18
    bl = np.zeros(n)
    notes = (1760, 2637, 2093, 3136, 2349, 2637, 3520)
    for i, f in enumerate(notes):
        place(bl, blip(f, 0.08, tau=0.025, bright=0.05, fm=0.2), 0.1 + i * 0.15, 0.4 + 0.2 * (i % 2))
    y = pad + bl
    y = reverb(y, 0.25, 0.6, 0.7, 7000, tail=0.0)[:n]
    return y * hum_env(n, 0.15, 0.35)


def sel_vehicle_bay(rng):
    dur = 1.55
    n = ns(dur)
    t = tline(n)
    f = curve(n, [(0, 70), (0.35, 118), (0.75, 122), (dur, 110)])
    mot = motor(f, n, rng, ((1, 1), (2, 0.7), (3, 0.5), (4, 0.3), (6, 0.2), (8, 0.1)), 12, 0.25, 0.006)
    mot *= 1 + 0.2 * np.sin(2 * np.pi * 6 * t)                                   # door chain / roller pulse
    y = 0.4 * mot * curve(n, [(0, 0), (0.1, 1), (0.8, 1), (1.0, 0.5), (dur, 0.4)])
    m = ns(0.55)
    hiss = shaped_noise(m, rng, lambda fr: hp_mag(fr, 3000) * lp_mag(fr, 10000) * bell_mag(fr, 5000, 2, 2)) * curve(
        m, [(0, 0), (0.015, 1), (0.1, 0.7), (0.55, 0)])
    place(y, hiss, 0.82, 0.3)                                                    # pneumatic tool
    place(y, tick(rng, 2600, 0.05, 0.006, 0.6), 0.8, 0.3)
    place(y, clunk(rng, 300, 0.4, 100, 0.4, 0.4, 0.14), 0.05, 0.3)
    y = reverb(y, 0.18, 0.8, 1.1, 3500, tail=0.0)[:n]
    return y * hum_env(n, 0.05, 0.3)


def sel_machine_shop(rng):
    dur = 1.5
    n = ns(dur)
    t = tline(n)
    y = np.zeros(n)
    place(y, servo_run(rng, 0.6, [(0, 280), (0.2, 480), (0.45, 500), (0.6, 360)], mesh_k=8), 0.0, 0.9)
    m = ns(0.85)
    arc = tone(100, m, saw_partials(30, 0.8))                                    # arc buzz
    arc = biquad(arc, "bp", 2500, 0.7)
    sp = impulses(m, 900 * (1 + 0.8 * slow_noise(m, rng, 12)).clip(0.1), rng, heavy=2.0)
    sp = convolve(sp, burst(0.004, rng, 1800, 7500, 0.0008))
    sizzle = shaped_noise(m, rng, lambda fr: hp_mag(fr, 2500) * lp_mag(fr, 6500))
    weld = (0.3 * arc + 1.6 * sp + 0.15 * sizzle) * (0.7 + 0.3 * np.abs(slow_noise(m, rng, 8)))
    weld *= curve(m, [(0, 0), (0.03, 1), (0.75, 1), (0.85, 0)])
    place(y, weld, 0.62, 0.55)
    y += 0.05 * shaped_noise(n, rng, lambda fr: tilt(fr, -1) * lp_mag(fr, 300) * hp_mag(fr, 40))
    return y * hum_env(n, 0.03, 0.15)


def sel_prefab_habitat(rng):
    dur = 1.4
    n = ns(dur)
    t = tline(n)
    air = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * hp_mag(f, 80) * lp_mag(f, 900, 2))
    blade = tone(118 * wobble(n, rng, 1, 0.002), n, ((1, 1), (2, 0.35), (3, 0.12)))
    warm = np.zeros(n)
    for f in (174.6, 220.0, 261.6):
        warm += np.sin(2 * np.pi * f * t + rng.uniform(0, 6))
    y = 0.5 * air * (1 + 0.1 * np.sin(2 * np.pi * 3.9 * t)) + 0.3 * blade + 0.08 * warm
    return y * hum_env(n, 0.2, 0.4)


def sel_foundry(rng):
    dur = 1.5
    n = ns(dur)
    flame = shaped_noise(n, rng, lambda f: tilt(f, -0.7) * hp_mag(f, 40) * lp_mag(f, 1600, 2) * bell_mag(f, 300, 1, 1.6))
    flick = 1 + 0.35 * shaped_noise(n, rng, lambda f: hp_mag(f, 4, 2) * lp_mag(f, 11, 2))
    imp = impulses(n, 35, rng, heavy=2.5)
    cr = convolve(imp, burst(0.006, rng, 1000, 9000, 0.0012))
    low = tone(45, n, ((1, 1), (2, 0.4)))
    y = 0.75 * flame * flick + 0.6 * cr + 0.25 * low
    y = softclip(0.8 * y, 1.4)
    return y * hum_env(n, 0.18, 0.35)


def sel_deep_core_mine(rng):
    dur = 1.6
    n = ns(dur)
    t = tline(n)
    rum = shaped_noise(n, rng, lambda f: tilt(f, -1.2) * lp_mag(f, 120, 3) * hp_mag(f, 22, 2))
    rum *= 1 + 0.4 * slow_noise(n, rng, 3)
    sub = np.sin(2 * np.pi * 37 * t) + 0.8 * np.sin(2 * np.pi * 39.5 * t) + 0.3 * np.sin(2 * np.pi * 74 * t)
    grind = shaped_noise(n, rng, lambda f: hp_mag(f, 300) * lp_mag(f, 1500)) * np.clip(
        0.5 + 0.5 * slow_noise(n, rng, 9), 0, None)
    rattle = convolve(impulses(n, 25, rng, heavy=2), burst(0.01, rng, 800, 4000, 0.002))
    y = 0.8 * rum + 0.4 * sub + 0.35 * grind + 0.6 * rattle
    return y * hum_env(n, 0.25, 0.4)


def sel_excavation_site(rng):
    dur = 1.4
    n = ns(dur)
    t = tline(n)
    y = np.zeros(n)
    m = ns(0.3)
    scrape = shaped_noise(m, rng, lambda f: tilt(f, -0.3)) * curve(m, [(0, 0), (0.05, 1), (0.25, 0.6), (0.3, 0)])
    scrape = biquad(scrape, "bp", curve(m, [(0, 900), (0.3, 1800)]), 2.0)
    place(y, scrape, 0.02, 0.6)                                                  # shovel into soil
    place(y, thump(0.3, 110, 70, 0.05, rng, 0.01, 0.8, 900), 0.33, 0.5)          # dump onto the sieve
    sieve_t = np.clip(t - 0.4, 0, None)
    shake = np.clip(np.sin(2 * np.pi * 7.5 * sieve_t), 0, None) ** 2 * (t > 0.4)
    grains = convolve(impulses(n, 1400, rng, heavy=1.5), burst(0.003, rng, 2500, 9000, 0.0005))
    frame = modal(0.05, 1350, BAR[:3], (1, .4, .2), (.02, .012, .008))
    rattle = convolve(np.diff(np.floor(sieve_t * 15), prepend=0.0) * (t > 0.4), frame)
    y += 0.6 * grains * (0.15 + shake) + 0.5 * rattle
    return y * hum_env(n, 0.02, 0.3)


def sel_pressure_dome(rng):
    dur = 1.6
    n = ns(dur)
    t = tline(n)
    air = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * hp_mag(f, 150) * lp_mag(f, 7000))
    y = np.zeros(n)
    for i, f in enumerate((330, 495, 660, 990, 1320)):                            # glassy dome modes, breath driven
        drift = f * (1 + 0.01 * np.sin(2 * np.pi * (0.4 + 0.13 * i) * t + i))
        y += biquad(air, "bp", drift, 60) * (1 / (1 + 0.35 * i))
    y = rmsn(y) + 0.25 * air * curve(n, [(0, 0.5), (0.8, 1), (dur, 0.6)])
    y = reverb(y, 0.4, 1.8, 1.4, 6000, tail=0.0)[:n]
    return y * hum_env(n, 0.3, 0.45)


def sel_life_support_plant(rng):
    dur = 1.4
    n = ns(dur)
    t = tline(n)
    fan = tone(184 * wobble(n, rng, 2, 0.003), n, ((1, 1), (2, 0.5), (3, 0.3), (4, 0.15)))
    fan *= 1 + 0.15 * np.sin(2 * np.pi * 46 * t)
    fan2 = tone(231, n, ((1, 0.6), (2, 0.2)))
    whoosh = shaped_noise(n, rng, lambda f: tilt(f, -0.4) * hp_mag(f, 120) * lp_mag(f, 2500))
    hiss = shaped_noise(n, rng, lambda f: hp_mag(f, 3500) * lp_mag(f, 11000))
    breathe = curve(n, [(0, 0.1), (0.35, 1), (0.8, 0.25), (1.15, 0.9), (dur, 0.4)])  # scrubber valve cycling
    y = 0.22 * fan + 0.12 * fan2 + 0.4 * whoosh + 0.22 * hiss * breathe
    return y * hum_env(n, 0.15, 0.35)


def sel_colony_hub(rng):
    dur = 1.55
    n = ns(dur)
    t = tline(n)
    pad = np.zeros(n)
    for f, a in ((110, 1.0), (165, 0.5), (220, 0.35), (330, 0.15)):
        for d in (-0.002, 0.002):
            pad += a * np.sin(2 * np.pi * f * (1 + d) * t + rng.uniform(0, 6))
    for f, a in ((440.0, 0.12), (554.4, 0.08), (659.3, 0.06)):                     # soft upper voices
        pad += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6)) * (0.7 + 0.3 * np.sin(2 * np.pi * 0.7 * t + f))
    pad = biquad(pad, "lp", 2200, 0.7)
    y = 0.4 * pad
    for at, f in ((0.32, 1318.5), (0.42, 1760.0), (0.95, 1568.0)):                # far-off comms traffic
        b = blip(f, 0.09, tau=0.03, bright=0.0, fm=0.15)
        place(y, b, at, 0.3)
    st = shaped_noise(n, rng, lambda f: hp_mag(f, 500) * lp_mag(f, 3500))
    y += 0.03 * st * (0.6 + 0.4 * slow_noise(n, rng, 5))
    y = reverb(y, 0.2, 0.9, 1.0, 5000, tail=0.0)[:n]
    return y * hum_env(n, 0.2, 0.4)


def _rover(rng, dur, f_pts, layers, sub=0.0, chorus=0.0, servo_hiss=0.08):
    n = ns(dur)
    y = np.zeros(n)
    base = curve(n, f_pts)
    for ratio, gain, parts in layers:
        y += gain * motor(base * ratio, n, rng, parts, 0, 0, 0.0025, 5)
        if chorus:
            y += gain * chorus * motor(base * ratio * 1.006, n, rng, parts, 0, 0, 0.0025, 5)
    if sub:
        y += sub * tone(base * 0.25, n, ((1, 1), (2, 0.3)))
    inv = tone(base * 4.2, n, ((1, 1),)) * 0.04                                   # inverter whistle
    y += inv
    y += servo_hiss * shaped_noise(n, rng, lambda f: tilt(f, -0.5) * hp_mag(f, 200) * lp_mag(f, 2500))
    return y * hum_env(n, 0.06, 0.35)


def sel_survey_rover(rng):
    return _rover(rng, 1.3, [(0, 380), (0.45, 920), (0.6, 760), (1.3, 720)],
                  [(1.0, 0.5, ((1, 1), (2, 0.25), (3, 0.08)))])


def sel_survey_rover_mk2(rng):
    return _rover(rng, 1.35, [(0, 340), (0.45, 850), (0.62, 690), (1.35, 660)],
                  [(1.0, 0.45, ((1, 1), (2, 0.35), (3, 0.15), (4, 0.06))),
                   (1.5, 0.18, ((1, 1), (2, 0.2)))], sub=0.12)


def sel_survey_rover_mk3(rng):
    return _rover(rng, 1.45, [(0, 300), (0.5, 820), (0.7, 640), (1.45, 610)],
                  [(1.0, 0.4, ((1, 1), (2, 0.4), (3, 0.2), (4, 0.1), (5, 0.05))),
                   (1.5, 0.2, ((1, 1), (2, 0.25))),
                   (2.0, 0.1, ((1, 1),))], sub=0.2, chorus=0.6, servo_hiss=0.05)


def sel_mining_crawler(rng):
    dur = 1.6
    n = ns(dur)
    t = tline(n)
    rpm = curve(n, [(0, 20), (0.4, 29), (dur, 27)])                               # diesel firing rate
    ph = np.cumsum(rpm / SR)
    fire = np.diff(np.floor(ph), prepend=0.0) * (0.8 + 0.4 * rng.random(n))
    pulse = convolve(fire, thump(0.08, 140, 60, 0.02, rng, 0.006, 0.4, 500))
    gen = tone(curve(n, [(0, 160), (0.4, 232), (dur, 216)]), n, ((1, 1), (2, 0.4), (3, 0.2)))
    eng = 0.9 * pulse + 0.25 * gen + 0.25 * shaped_noise(n, rng, lambda f: tilt(f, -1) * lp_mag(f, 300) * hp_mag(f, 30))
    y = biquad(eng, "lp", 2400, 0.7)
    for i in range(8):                                                            # track links over the sprocket
        at = 0.22 + i * 0.17 + rng.uniform(-0.01, 0.01)
        place(y, clunk(rng, 520 + 40 * (i % 3), 0.25, 140, 0.3, 0.5, 0.05), at, 0.5)
    y = softclip(y * 1.2, 1.3)
    return y * hum_env(n, 0.12, 0.35)


def sel_construction_rover(rng):
    dur = 1.45
    n = ns(dur)
    t = tline(n)
    f = curve(n, [(0, 300), (0.35, 540), (dur, 560)])
    pump = motor(f, n, rng, ((1, 1), (2, 0.6), (3, 0.4), (4, 0.25), (6, 0.15), (9, 0.1)), 0, 0, 0.004)
    pump *= 1 + 0.35 * np.sin(2 * np.pi * 31 * t)                                 # piston ripple
    oil = shaped_noise(n, rng, lambda fr: hp_mag(fr, 1200) * lp_mag(fr, 5000) * bell_mag(fr, 2500, 2, 2))
    y = 0.4 * pump + 0.12 * oil * (1 + 0.3 * np.sin(2 * np.pi * 31 * t))
    place(y, tick(rng, 2400, 0.05, 0.008, 0.6), 0.02, 0.3)
    return y * hum_env(n, 0.08, 0.35)


def sel_survey_team(rng):
    dur = 1.15
    n = ns(dur)
    y = np.zeros(n)
    c1 = bell(2490, 0.22, 1.5, 1.2, 0.06)                                       # comm-badge style two-tone chirp
    c2 = bell(3320, 0.3, 1.5, 1.0, 0.09)
    place(y, c1, 0.0, 0.5)
    place(y, c2, 0.055, 0.45)
    place(y, blip(4980, 0.06, tau=0.02, bright=0.0), 0.06, 0.12)
    m = n - ns(0.2)
    stat = shaped_noise(m, rng, lambda f: hp_mag(f, 350) * lp_mag(f, 3000) * bell_mag(f, 1200, 1.5, 1.6))
    crack = convolve(impulses(m, 60, rng, heavy=2), burst(0.004, rng, 800, 5000, 0.0008))
    radio = (0.35 * stat * (0.7 + 0.3 * slow_noise(m, rng, 14)) + 0.8 * crack) * curve(
        m, [(0, 0), (0.01, 1), (0.25, 0.7), (m / SR - 0.2, 0.6), (m / SR, 0)])
    place(y, radio, 0.2, 0.5)
    place(y, burst(0.05, rng, 600, 4000, 0.01), 0.2, 0.5)                         # squelch
    return fade(y, 0.002, 0.25)


def sel_default_building(rng):
    dur = 1.2
    n = ns(dur)
    t = tline(n)
    h = tone(80 * wobble(n, rng, 2, 0.002), n, ((1, 1), (2, 0.6), (3, 0.4), (4, 0.3), (5, 0.2), (6, 0.12), (8, 0.08)))
    h *= 1 + 0.1 * np.sin(2 * np.pi * 2.3 * t)
    y = 0.5 * h + 0.1 * shaped_noise(n, rng, lambda f: tilt(f, -0.6) * hp_mag(f, 100) * lp_mag(f, 2500))
    return y * hum_env(n, 0.12, 0.35)


def sel_default_vehicle(rng):
    return _rover(rng, 1.1, [(0, 260), (0.4, 520), (1.1, 480)], [(1.0, 0.5, ((1, 1), (2, 0.45), (3, 0.2)))],
                  servo_hiss=0.1)


# ---------------------------------------------------------------------------------------------------------------------
# Loop helpers (everything below that loops repeats seamlessly: whole-cycle tones, FFT-shaped noise, wrapped events)

def whole(f: float, n: int) -> float:
    """f rounded to a whole number of cycles over n samples (at least one)."""
    return max(1, round(f * n / SR)) * SR / n


def loop_phase(f0: float, n: int, rng=None, wander: float = 0.0, fc: float = 3.0) -> np.ndarray:
    """Running phase of a tone that loops: f0 is rounded to whole cycles over the loop and the speed wander (a share of
    f0) is zero-mean circular noise, so the phase comes back to where it started."""
    f0 = whole(f0, n)
    p = 2 * np.pi * f0 * np.arange(n) / SR
    if wander and rng is not None:
        w = slow_noise(n, rng, fc)
        p = p + 2 * np.pi * np.cumsum(f0 * wander * (w - w.mean())) / SR
    return p


def ptone(p: np.ndarray, partials) -> np.ndarray:
    """Harmonic stack on a running phase (whole orders keep a looping phase looping)."""
    return sum(a * np.sin(k * p) for k, a in partials)


def cfilt(x: np.ndarray, mag) -> np.ndarray:
    """Zero-phase magnitude filter applied circularly (for loops)."""
    return fft_filter(x, mag, circular_=True)


def lsin(f: float, n: int, ph: float = 0.0) -> np.ndarray:
    """A sine with whole cycles over the loop (loop-safe modulation)."""
    return np.sin(2 * np.pi * whole(f, n) * np.arange(n) / SR + ph)


def band(f, lo, hi, order=2):
    return hp_mag(f, lo, order) * lp_mag(f, hi, order)


def grains(n: int, rate, rng, kernels, heavy: float = 3.0) -> np.ndarray:
    """Random impulses (rate per second, may vary per sample), each rung through one of several kernels and wrapped round
    the loop: crunch, patter, crackle."""
    imp = impulses(n, rate, rng, heavy=heavy)
    which = rng.integers(0, len(kernels), n)
    y = np.zeros(n)
    for i, k in enumerate(kernels):
        y += convolve(np.where(which == i, imp, 0.0), k, circ=True)
    return y


def grain_kernels(rng, count, lo, hi, tau):
    dur = max(0.004, tau * 8)
    return [burst(dur, rng, lo * rng.uniform(0.8, 1.25), hi * rng.uniform(0.8, 1.2), tau * rng.uniform(0.6, 1.5), 0.0002)
            for _ in range(count)]


def bubble(rng, f0, tau=0.012, rise=1.6) -> np.ndarray:
    """A small water bubble / drip: a sine that glides up as it decays."""
    n = ns(tau * 6)
    t = tline(n)
    return np.sin(phase(f0 * (1 + rise * t / (tau * 6)), n)) * expdec(n, tau, 0.0008)


def events_circ(n: int, rng, rate: float, make, gain=(0.5, 1.0), jitter: float = 0.35) -> np.ndarray:
    """Roughly evenly spaced events round a loop (rate per second, each slot jittered), each a fresh make(rng)."""
    y = np.zeros(n)
    count = max(1, int(round(rate * n / SR)))
    for i in range(count):
        at = int((i + rng.uniform(-jitter, jitter)) * n / count) % n
        place_circ(y, make(rng), at, rng.uniform(*gain))
    return y


def scatter_circ(n: int, rng, rate: float, make, gain=(0.2, 1.0)) -> np.ndarray:
    """Events at random places round a loop (rate per second)."""
    y = np.zeros(n)
    for _ in range(max(1, int(rate * n / SR))):
        place_circ(y, make(rng), int(rng.integers(0, n)), rng.uniform(*gain))
    return y


def roll_bed(n, rng, lp=600.0, hp=40.0, slope=-0.8, rev_hz=5.3, rev=0.12, wander=0.18):
    """Rolling tyre noise: dark coloured noise with a little once-a-turn swell and a slow wander."""
    x = shaped_noise(n, rng, lambda f: tilt(f, slope) * lp_mag(f, lp, 2) * hp_mag(f, hp, 2))
    return x * (1 + rev * lsin(rev_hz, n, rng.uniform(0, 6.28)) + wander * slow_noise(n, rng, 1.5))


# ---------------------------------------------------------------------------------------------------------------------
# Vehicles (driving loops; the game sets pitch and volume by the speed)

def veh_ev_loop(rng):
    """Soft electric drive: motor orders, a faint gear-mesh whine, the inverter's 100 Hz hum and PWM carrier, air."""
    n = ns(3.0)
    p = loop_phase(440, n, rng, 0.004, 4)
    whine = ptone(p, ((1, 1), (2, 0.3), (3, 0.45), (4, 0.1), (6, 0.07)))
    whine *= 1 + 0.08 * slow_noise(n, rng, 3)
    mesh = np.sin(9 * p) * (0.7 + 0.3 * np.sin(p))                                  # reduction gear mesh
    hum = ptone(loop_phase(100, n), ((1, 1), (2, 0.55), (3, 0.35), (4, 0.2), (6, 0.12)))
    pwm = np.sin(loop_phase(7800, n)) * (0.55 + 0.45 * np.sin(2 * p))               # inverter carrier + sidebands
    air = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * band(f, 250, 2500)) * (1 + 0.2 * slow_noise(n, rng, 2))
    y = 0.42 * whine + 0.05 * mesh + 0.2 * hum + 0.012 * pwm + 0.16 * air
    return cfilt(y, lambda f: lp_mag(f, 6000, 1))


def veh_crawler_loop(rng):
    """Heavy electric tracks: a low traction motor with a growl, reduction gears, ground rumble and a slow clank of
    track links over the sprocket (10 in 4 s) with a light rattle."""
    n = ns(4.0)
    p = loop_phase(62, n, rng, 0.006, 3)
    mot = ptone(p, ((1, 1), (2, 0.7), (3, 0.45), (4, 0.3), (5, 0.2), (6, 0.12), (8, 0.08)))
    mot *= 1 + 0.15 * lsin(17, n) + 0.1 * slow_noise(n, rng, 4)
    gear = ptone(loop_phase(310, n, rng, 0.004, 4), ((1, 1), (2, 0.3), (3, 0.15)))
    hum = ptone(loop_phase(100, n), ((1, 1), (3, 0.4)))
    rumble = shaped_noise(n, rng, lambda f: tilt(f, -1.0) * lp_mag(f, 180, 2) * hp_mag(f, 25, 2))
    rumble *= 1 + 0.2 * slow_noise(n, rng, 2)
    clanks = np.zeros(n)
    for i in range(10):
        k = clunk(rng, rng.uniform(380, 520), 0.25, 110, 0.5, 0.35, 0.08)
        place_circ(clanks, k, int((i + rng.uniform(-0.06, 0.06)) * n / 10), 1.0 if i % 2 == 0 else 0.7)
    rattle = grains(n, 50 * (1 + 0.6 * lsin(2.5, n)), rng, grain_kernels(rng, 3, 1200, 4500, 0.0015), 2.5)
    y = 0.38 * mot + 0.08 * gear + 0.06 * hum + 0.5 * rumble + 0.3 * clanks + 0.05 * rattle
    return cfilt(y, lambda f: lp_mag(f, 7000, 1))


WIPE_T = 1.3   # RoverLook.Lamps.WipeSeconds: one stroke, up across the glass and back


def _wiper(rng, dry: bool):
    """One wiper stroke as the view animates it (angle = 0.5 - 0.5 cos over WIPE_T): a rubber swish loudest at the
    fastest point of each half, a tiny motor whirr following the blade speed, a tick at the top, a soft thunk as it
    parks. dry = the rubber squeaks on dry glass."""
    dur = WIPE_T + 0.2
    n = ns(dur)
    t = tline(n)
    s = np.clip(t / WIPE_T, 0, 1)
    speed = np.abs(np.sin(2 * np.pi * s)) * (t < WIPE_T)                          # blade speed 0..1
    swish = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * band(f, 1400, 6500) * bell_mag(f, 2600, 1.5, 1.8))
    squee = shaped_noise(n, rng, lambda f: band(f, 500, 1300) * bell_mag(f, 820, 3, 2.5))
    y = (0.5 * swish + 0.3 * squee) * speed ** 1.4
    mot = motor(150 + 110 * speed, n, rng, ((1, 1), (2, 0.5), (3, 0.3), (5, 0.15)), 7, 0.25, 0.004, 9)
    y += 0.12 * mot * np.clip(speed * 3, 0, 1)
    if dry:
        cr = creak(n, rng, [(0, 70), (WIPE_T * 0.25, 140), (WIPE_T * 0.5, 30), (WIPE_T * 0.75, 140), (WIPE_T, 60),
                            (dur, 0)], (1250, 1890, 2620), 34, 0.5)
        y += 0.06 * rmsn(cr) * speed ** 0.8
    place(y, tick(rng, 2900, 0.03, 0.004, 0.4), WIPE_T * 0.5, 0.1)                  # turns at the top
    place(y, thump(0.15, 170, 110, 0.025, rng, 0.006, 0.3, 900), WIPE_T - 0.03, 0.35)  # parks on its rest
    place(y, tick(rng, 2100, 0.03, 0.004, 0.3), WIPE_T - 0.02, 0.15)
    return fade(y, 0.004, 0.05)


def veh_wiper(rng):
    return _wiper(rng, False)


def veh_wiper_squeak(rng):
    return _wiper(rng, True)


def veh_flat_loop(rng):
    """A flat tyre: a soft rubber flap and thump once a turn (4 in 2 s at pitch 1; the game speeds it up with the
    vehicle)."""
    n = ns(2.0)
    y = np.zeros(n)
    for i in range(4):
        k = thump(0.18, 120, 62, 0.04, rng, 0.012, 0.6, 600) + 0.3 * burst(0.18, rng, 300, 1800, 0.02, 0.003)
        place_circ(y, k, int((i + rng.uniform(-0.03, 0.03)) * n / 4), rng.uniform(0.8, 1.0))
    return y + 0.05 * shaped_noise(n, rng, lambda f: tilt(f, -0.8) * band(f, 60, 700))


# ---------------------------------------------------------------------------------------------------------------------
# Ground under the tyres (one loop per SprayKind in scripts/SprayRig.cs)

def gnd_dirt_loop(rng):
    """Packed dirt (plains; Veyra's lowlands): rolling rumble, a soft crumbly crunch, now and then a pebble."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 650)
    crunch = grains(n, 180 * (1 + 0.4 * slow_noise(n, rng, 6)), rng, grain_kernels(rng, 4, 250, 2200, 0.003), 3)
    pops = grains(n, 6, rng, grain_kernels(rng, 3, 500, 3500, 0.006), 1.5)
    soil = shaped_noise(n, rng, lambda f: band(f, 700, 2800)) * (0.5 + 0.5 * np.abs(slow_noise(n, rng, 8)))
    return roll + 0.45 * rmsn(crunch) + 0.25 * rmsn(pops) + 0.08 * soil


def _stone(rng):
    k = modal(0.03, rng.uniform(1800, 4200), BAR[:3], (1, 0.5, 0.25), (0.006, 0.004, 0.002), rng)
    return k + 0.5 * burst(0.03, rng, 1500, 8000, 0.0008)


def gnd_gravel_loop(rng):
    """Grit and gravel (hills): a denser, brighter crunch with stones clacking."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 900, slope=-0.7)
    crunch = grains(n, 900 * (1 + 0.35 * slow_noise(n, rng, 7)), rng, grain_kernels(rng, 5, 900, 6000, 0.0015), 2.5)
    stones = scatter_circ(n, rng, 25, _stone)
    return 0.9 * roll + 0.55 * rmsn(crunch) + 0.22 * rmsn(stones)


def gnd_sand_loop(rng):
    """Loose sand and dust (desert): a soft swishing hiss over a dull rumble, fine grains."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 450, slope=-0.9, rev=0.08)
    hiss = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * band(f, 1200, 6500)) * (1 + 0.35 * slow_noise(n, rng, 2.5))
    fine = grains(n, 1500, rng, grain_kernels(rng, 3, 2000, 8000, 0.0004), 2)
    return 0.8 * roll + 0.3 * hiss + 0.12 * rmsn(fine)


def gnd_rust_loop(rng):
    """Rust dust (class L tundra, flats and hills): coarser and lower than sand, with sparse grit."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 520, slope=-0.85)
    hiss = shaped_noise(n, rng, lambda f: tilt(f, -0.5) * band(f, 700, 3800) * bell_mag(f, 1500, 1.5, 1.5))
    hiss *= 1 + 0.3 * slow_noise(n, rng, 3)
    grit = grains(n, 400, rng, grain_kernels(rng, 4, 900, 4000, 0.0008), 2.5)
    pops = grains(n, 9, rng, grain_kernels(rng, 2, 600, 3000, 0.004), 1.5)
    return 0.85 * roll + 0.28 * hiss + 0.25 * rmsn(grit) + 0.15 * rmsn(pops)


def _squelch(rng):
    """One squelch: noise through a resonance that gulps up and back down, and a wet pop."""
    dur = rng.uniform(0.14, 0.26)
    n = ns(dur)
    f0 = rng.uniform(300, 450)
    fc = curve(n, [(0, f0), (dur * 0.4, f0 * rng.uniform(1.9, 2.6)), (dur, f0 * 1.2)])
    x = rng.standard_normal(n) * curve(n, [(0, 0), (0.015, 1), (dur * 0.5, 0.7), (dur, 0)])
    y = biquad(x, "bp", fc, 4.0) * 1.6
    pop = bubble(rng, rng.uniform(160, 300), 0.018, 0.8)
    m = min(len(pop), n)
    y[:m] += 0.5 * pop[:m]
    return y


def _mud_bubble(rng):
    return bubble(rng, rng.uniform(300, 900), rng.uniform(0.006, 0.014), 1.2)


def gnd_mud_loop(rng):
    """Mud (forest floor; Veyra's lowlands by water): a low wet roll, squelches as the treads pull out, small bubbles and sucks."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 380, slope=-1.0, rev=0.2)
    sq = events_circ(n, rng, 3.5, _squelch, (0.5, 1.0), 0.3)
    bubbles = scatter_circ(n, rng, 14, _mud_bubble)
    wet = shaped_noise(n, rng, lambda f: band(f, 400, 2500)) * (0.4 + 0.6 * np.abs(slow_noise(n, rng, 5)))
    return 0.9 * roll + 0.4 * rmsn(sq) + 0.18 * rmsn(bubbles) + 0.07 * wet


def _snow_crunch(rng):
    """One crunch of packed snow under a tread: a cluster of tiny crackles, a soft compaction thud, a little squeak."""
    dur = rng.uniform(0.08, 0.13)
    n = ns(dur + 0.05)
    env = curve(n, [(0, 0), (0.01, 1), (dur * 0.6, 0.7), (dur, 0), (dur + 0.05, 0)])
    imp = impulses(n, 3500, rng, heavy=2) * env
    y = convolve(imp, burst(0.003, rng, 1500, 7000, 0.0006))
    y += 0.25 * thump(dur + 0.05, 130, 75, 0.02, rng, 0.006, 0.3, 500)
    sq = shaped_noise(n, rng, lambda f: band(f, 1600, 2500) * bell_mag(f, 2000, 4, 3)) * env
    return y + 0.12 * sq


def gnd_snow_loop(rng):
    """Snow (polar ice): a soft rumble under a steady train of crisp crunches."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 500, slope=-0.9, rev=0.08)
    cr = events_circ(n, rng, 4.2, _snow_crunch, (0.6, 1.0), 0.25)
    cr += 0.5 * events_circ(n, rng, 3.1, _snow_crunch, (0.3, 0.7), 0.4)              # the other side's track
    hush = shaped_noise(n, rng, lambda f: band(f, 1500, 6000))
    return 0.6 * roll + 0.45 * rmsn(cr) + 0.01 * hush


def _drip(rng):
    return bubble(rng, rng.uniform(500, 2400), rng.uniform(0.004, 0.012), 1.8)


def gnd_water_loop(rng):
    """Water (plains and forest beside rivers and lakes): a washing slosh, spray hiss in surges, bubbles and drips."""
    n = ns(3.0)
    slosh = shaped_noise(n, rng, lambda f: tilt(f, -0.6) * band(f, 150, 1400)) * (1 + 0.5 * slow_noise(n, rng, 1.5))
    wash = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * band(f, 400, 3000))
    wash *= np.clip(1 + 0.8 * slow_noise(n, rng, 6), 0.1, None)
    spray = shaped_noise(n, rng, lambda f: band(f, 2200, 9000)) * np.clip(0.6 + 0.6 * slow_noise(n, rng, 4), 0.05, None)
    drops = scatter_circ(n, rng, 40, _drip, (0.1, 1.0))
    roll = roll_bed(n, rng, 300, slope=-1.0, rev=0.1)
    return 0.4 * roll + 0.5 * slosh + 0.35 * wash + 0.14 * spray + 0.15 * rmsn(drops)


def _tinkle(rng):
    return modal(0.05, rng.uniform(3800, 7500), PLATE[:4], (1, 0.6, 0.4, 0.3), (0.01, 0.007, 0.005, 0.004), rng, 0.01)


def gnd_frost_loop(rng):
    """Frost grit (frozen ground, Veyra's night frost): thin grit with an icy crackle and glassy tinkles."""
    n = ns(3.0)
    roll = roll_bed(n, rng, 600, slope=-0.85)
    grit = grains(n, 500, rng, grain_kernels(rng, 4, 1000, 5000, 0.0008), 2.5)
    crackle = grains(n, 90 * (1 + 0.5 * slow_noise(n, rng, 5)), rng, grain_kernels(rng, 4, 3000, 10000, 0.0003), 2)
    tinkle = scatter_circ(n, rng, 18, _tinkle, (0.1, 1.0))
    return 0.65 * roll + 0.25 * rmsn(grit) + 0.25 * rmsn(crackle) + 0.12 * rmsn(tinkle)


# ---------------------------------------------------------------------------------------------------------------------
# Weather at the site (flat, bus Ambience; the game crossfades the loops by the weather)

def _wind_bed(n, rng, lp=800.0, bands=((170, 1.6), (300, 2.2), (520, 2.8), (850, 3.5)), am_fc=0.2, depth=0.6):
    """Wind: dark noise plus a few resonant bands that swell and fade on their own, under a slow overall swell."""
    y = 0.6 * shaped_noise(n, rng, lambda f: tilt(f, -0.9) * lp_mag(f, lp, 2) * hp_mag(f, 35, 2))
    for fc, q in bands:
        b = shaped_noise(n, rng, lambda f, fc=fc, q=q: np.exp(-0.5 * (np.log2(np.maximum(f, 1) / fc) * q) ** 2)
                         * hp_mag(f, 30, 2))
        y += 0.5 * b * np.clip(0.55 + depth * slow_noise(n, rng, am_fc * 1.6), 0.05, None)
    return y * np.clip(1 + 0.35 * slow_noise(n, rng, am_fc), 0.3, None)


def amb_wind_loop(rng):
    """Low steady wind (8 s): dark air with drifting resonances; the game raises it with the wind speed."""
    return _wind_bed(ns(8.0), rng)


def _gust(rng, dur, f_lo, f_hi, peak, whistle):
    """A gust: the air swells and its pitch rises with it, then falls away; maybe a thin whistle on top."""
    n = ns(dur)
    env = curve(n, [(0, 0), (peak * 0.6, 0.6), (peak, 1.0), (peak + (dur - peak) * 0.4, 0.55), (dur, 0)])
    x = shaped_noise(n, rng, lambda f: tilt(f, -0.7) * hp_mag(f, 40, 2) * lp_mag(f, 2500, 2))
    fc = curve(n, [(0, f_lo), (peak, f_hi), (dur, f_lo * 0.9)])
    y = biquad(x, "bp", fc, 1.1) * 1.4 + 0.35 * biquad(x, "lp", fc * 0.6, 0.7)
    if whistle:
        y += 0.75 * biquad(x, "bp", fc * whistle, 30) * env
    y *= env * (1 + 0.25 * slow_noise(n, rng, 3))
    return fade(y, 0.05, 0.3)


def amb_wind_gust_a(rng):
    return _gust(rng, 3.2, 220, 620, 1.2, 1.9)


def amb_wind_gust_b(rng):
    return _gust(rng, 2.6, 260, 760, 0.8, 0.0)


def amb_wind_gust_c(rng):
    return _gust(rng, 3.6, 180, 520, 1.6, 2.4)


def _splat(rng):
    y = burst(0.05, rng, 400, 5000, 0.008)
    b = bubble(rng, rng.uniform(900, 2200), 0.006, 1.2)[:len(y)]
    y[:len(b)] += 0.4 * b
    return y


def _rain(n, rng, rate, heavy):
    """Rain: drops ticking on the ground and leaves (several drop sounds), a few fat splats, and the hiss of rain
    further off."""
    drops = grains(n, rate * (1 + 0.25 * slow_noise(n, rng, 0.8)), rng,
                   grain_kernels(rng, 4, 1600, 9000, 0.0012) + grain_kernels(rng, 2, 700, 3000, 0.0025), 2.5)
    splats = scatter_circ(n, rng, 60 if heavy else 14, _splat)
    hiss = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * band(f, 1000, 8000)) * (1 + 0.15 * slow_noise(n, rng, 0.6))
    low = shaped_noise(n, rng, lambda f: tilt(f, -0.6) * band(f, 60, 600))
    if heavy:
        return 0.35 * rmsn(drops) + 0.25 * rmsn(splats) + 0.75 * hiss + 0.4 * low
    return 0.55 * rmsn(drops) + 0.2 * rmsn(splats) + 0.22 * hiss + 0.08 * low


def amb_rain_light_loop(rng):
    """Light rain (4 s): a soft patter of drops on the ground, a faint far-off hiss."""
    return _rain(ns(4.0), rng, 260, False)


def amb_rain_heavy_loop(rng):
    """Heavy rain (4 s): a dense roar of drops and splats with a full hiss and a low wash under it."""
    return _rain(ns(4.0), rng, 2600, True)


def _rumble(n, rng, swells, lp, mid=0.0):
    """Thunder's roll: brown noise under an envelope of overlapping swells (each a lump at time at, width w, height a)."""
    t = tline(n)
    env = np.zeros(n)
    for at, w, a in swells:
        env += a * np.exp(-0.5 * ((t - at) / w) ** 2)
    low = shaped_noise(n, rng, lambda f: tilt(f, -1.0) * lp_mag(f, lp, 3) * hp_mag(f, 22, 2))
    y = low * env * (1 + 0.3 * slow_noise(n, rng, 6))
    if mid:
        y += mid * shaped_noise(n, rng, lambda f: tilt(f, -0.6) * band(f, 150, 1200)) * env ** 1.5
    return y


def amb_thunder_a(rng):
    """Distant thunder (6.5 s): a soft-edged roll that rises twice and dies away, all low."""
    n = ns(6.5)
    y = _rumble(n, rng, [(0.6, 0.35, 0.6), (1.4, 0.5, 1.0), (2.4, 0.6, 0.8), (3.4, 0.7, 0.55), (4.6, 0.8, 0.3)], 170)
    y = reverb(y, 0.3, 2.4, 1.6, 900, tail=0.0)[:n]
    return fade(y, 0.2, 1.0)


def amb_thunder_b(rng):
    """Thunder a few km off (5.5 s): a muffled crack, then a rolling rumble with a little mid-range grit."""
    n = ns(5.5)
    y = _rumble(n, rng, [(0.25, 0.15, 0.7), (0.8, 0.35, 1.0), (1.6, 0.5, 0.75), (2.6, 0.6, 0.5), (3.7, 0.7, 0.25)], 240,
                mid=0.25)
    m = ns(0.12)
    crack = convolve(impulses(m, 300, rng, heavy=1.5), burst(0.006, rng, 200, 1600, 0.0015))
    place(y, 0.6 * rmsn(crack) * expdec(m, 0.05, 0.002), 0.18)
    y = reverb(y, 0.3, 2.2, 1.5, 1500, tail=0.0)[:n]
    return fade(y, 0.05, 1.0)


def amb_thunder_close(rng):
    """A close strike (5.5 s): a ripping crack (sharp snaps over ~40 ms and a torn hiss), the boom, a long roll."""
    n = ns(5.5)
    y = np.zeros(n)
    snaps = np.zeros(ns(0.25))
    for _ in range(26):
        place(snaps, burst(0.02, rng, 300, 12000, 0.0015, 0.0001), rng.uniform(0, 1) ** 1.6 * 0.045, rng.uniform(0.3, 1.0))
    place(y, snaps, 0.0, 1.0)
    m = ns(0.4)
    place(y, shaped_noise(m, rng, lambda f: tilt(f, -0.3) * band(f, 500, 9000)) * expdec(m, 0.07, 0.002), 0.01, 0.6)
    place(y, thump(1.2, 80, 34, 0.35, rng, 0.05, 0.9, 200), 0.03, 1.2)
    y += 0.9 * _rumble(n, rng, [(0.35, 0.2, 0.9), (0.9, 0.4, 1.0), (1.8, 0.55, 0.7), (2.9, 0.7, 0.45), (4.0, 0.7, 0.2)],
                       280, mid=0.3)
    y = reverb(y, 0.35, 2.8, 1.7, 3500, tail=0.0)[:n]
    return fade(y, 0.0005, 1.0)


def amb_dust_storm_loop(rng):
    """Dust storm (5 s): sand hissing and scouring in gusts over a strong, brighter wind."""
    n = ns(5.0)
    gust = np.clip(1 + 0.55 * slow_noise(n, rng, 1.2), 0.15, None)
    grit = grains(n, 4000 * gust, rng, grain_kernels(rng, 4, 2000, 9000, 0.0003), 2) * gust
    hiss = shaped_noise(n, rng, lambda f: tilt(f, -0.3) * band(f, 1500, 8000)) * gust
    wind = _wind_bed(n, rng, 1200, ((220, 1.4), (420, 2.0), (700, 2.6), (1100, 3.2)), 0.5, 0.7)
    roar = shaped_noise(n, rng, lambda f: tilt(f, -1.0) * lp_mag(f, 300, 2) * hp_mag(f, 30, 2)) * gust
    return 0.3 * rmsn(grit) + 0.35 * hiss + 0.8 * wind + 0.4 * roar


def amb_snowfall_loop(rng):
    """Snowfall (6 s): a very soft, muffled hush of air; snow damps everything."""
    n = ns(6.0)
    low = shaped_noise(n, rng, lambda f: tilt(f, -1.0) * lp_mag(f, 350, 2) * hp_mag(f, 30, 2))
    mid = shaped_noise(n, rng, lambda f: np.exp(-0.5 * (np.log2(np.maximum(f, 1) / 380) * 1.4) ** 2))
    hush = shaped_noise(n, rng, lambda f: band(f, 600, 2500))
    y = 0.8 * low * np.clip(1 + 0.3 * slow_noise(n, rng, 0.2), 0.3, None)
    y += 0.35 * mid * np.clip(0.6 + 0.5 * slow_noise(n, rng, 0.35), 0.05, None)
    return y + 0.08 * hush * (1 + 0.3 * slow_noise(n, rng, 0.5))


# ---------------------------------------------------------------------------------------------------------------------
# Registry: (group, name, description, maker, loop)

SOUNDS = [
    ("ui", "hover", "Pointer over a button: a faint 45 ms tick, high and soft.", ui_hover, False),
    ("ui", "click", "Button press: two quick rising console chirps.", ui_click, False),
    ("ui", "back", "Back / close button: the same chirp, falling.", ui_back, False),
    ("ui", "locked", "Locked button: a dull, muted double tick.", ui_locked, False),
    ("ui", "open", "A panel opens: three rising notes with a short airy swish.", ui_open, False),
    ("ui", "close", "A panel closes: three falling notes.", ui_close, False),
    ("ui", "confirm", "Order accepted: two-tone up with a bell overtone.", ui_confirm, False),
    ("ui", "error", "Refused: low two-tone, soft square wave, falling.", ui_error, False),
    ("ui", "tutorial", "Tutorial card pops up: a soft bloop and a chime.", ui_tutorial, False),
    ("ui", "tutorial_done", "Tutorial step complete: rising three-note chime with a sparkle.", ui_tutorial_done, False),
    ("ui", "place", "Building placed: a solid thunk, a chirp and a short hiss.", ui_place, False),
    ("ui", "cancel", "Placement cancelled: soft downward sweep.", ui_cancel, False),

    ("landing", "burn_loop", "Rocket roar (loop): rumble, mid roar, crackle; driven by throttle in game.",
     landing_burn_loop, True),
    ("landing", "burn_ignite", "Engine lights: valve hiss, igniter crack, roar swell.", landing_burn_ignite, False),
    ("landing", "engine_cut", "Engine shuts off: roar collapses, valve chuff, hot metal ticking.",
     landing_engine_cut, False),
    ("landing", "touchdown", "Touchdown: heavy thump, gear creak, strut ring, rebound.", landing_touchdown, False),
    ("landing", "ramp_open", "Ramp lowers: unlatch, hydraulic servo run, ramp meets the ground.",
     landing_ramp_open, False),
    ("landing", "ramp_close", "Ramp raises: servo run, shut, latch, seal hiss.", landing_ramp_close, False),
    ("landing", "liftoff", "Lift-off: ignition crack and a 3 s roar swell that trails away.", landing_liftoff, False),
    ("landing", "winch_loop", "Cable pay-out (loop): winch motor, ratchet pawl, rope hiss.", landing_winch_loop, True),
    ("landing", "module_clamp", "Module locks on: metal clunk, second jaw, short ring.", landing_module_clamp, False),
    ("landing", "unfurl", "Outpost unfurls (5 s): three servo runs with latch clicks, mast detents, lock and hiss.",
     landing_unfurl, False),
    ("landing", "uplink", "Uplink to the ship: rising comms chirps, data warble, two-tone ping.", landing_uplink, False),
    ("landing", "vehicle_roll", "Vehicle drives down the ramp: drive whine, rib clunks, thud onto the ground.",
     landing_vehicle_roll, False),

    ("select", "colony_hub", "Colony Hub: calm 110 Hz pad with far-off comms blips.", sel_colony_hub, False),
    ("select", "storage_container", "Storage Container: a knock and hollow metal box resonance.",
     sel_storage_container, False),
    ("select", "refinery", "Ore Refinery: industrial grind over a gritty motor, crusher clanks.", sel_refinery, False),
    ("select", "mine", "Mine: rhythmic piledriver thuds over a drill hum.", sel_mine, False),
    ("select", "fusion_reactor", "Fusion Reactor: deep 55 Hz power hum with a high shimmer.", sel_fusion_reactor,
     False),
    ("select", "prefab_habitat", "Prefab Habitat: warm air handler, soft fan tone.", sel_prefab_habitat, False),
    ("select", "foundry", "Alloy Foundry: furnace roar with flicker and crackle.", sel_foundry, False),
    ("select", "deep_core_mine", "Deep-Core Mine: heavy sub rumble, distant grinding, rattle.", sel_deep_core_mine,
     False),
    ("select", "vehicle_bay", "Vehicle Bay: garage motor run-up and a pneumatic tool hiss.", sel_vehicle_bay, False),
    ("select", "science_lab", "Research Lab: bright bleeps over a soft chord pad.", sel_science_lab, False),
    ("select", "machine_shop", "Machine Shop: servo move, then weld arc buzz and crackle.", sel_machine_shop, False),
    ("select", "excavation_site", "Excavation Site: shovel scrape, then a rattling sieve.", sel_excavation_site,
     False),
    ("select", "pressure_dome", "Pressure Dome: airy glass-like resonance, wide tail.", sel_pressure_dome, False),
    ("select", "life_support_plant", "Life Support Plant: fans and a cycling scrubber hiss.", sel_life_support_plant,
     False),
    ("select", "survey_team", "Survey Team: comm-badge style chirp, then radio static.", sel_survey_team, False),
    ("select", "survey_rover", "Survey Rover: electric drive whine rising, then idling.", sel_survey_rover, False),
    ("select", "survey_rover_mk2", "Survey Rover Mk II: same whine, second motor and a sub.", sel_survey_rover_mk2,
     False),
    ("select", "survey_rover_mk3", "Survey Rover Mk III: three motor layers, chorus, deeper sub.",
     sel_survey_rover_mk3, False),
    ("select", "mining_crawler", "Mining Crawler: diesel-electric chug with track clank.", sel_mining_crawler, False),
    ("select", "construction_rover", "Construction Rover: hydraulic pump whine with piston ripple.",
     sel_construction_rover, False),
    ("select", "default_building", "Fallback for other buildings: plain machine hum.", sel_default_building, False),
    ("select", "default_vehicle", "Fallback for other vehicles: plain drive whine.", sel_default_vehicle, False),

    ("vehicle", "ev_loop", "Rover drive (loop): soft electric whine, gear mesh, inverter hum; pitch follows the speed "
                           "and mark in game.", veh_ev_loop, True),
    ("vehicle", "crawler_loop", "Crawler and construction rover drive (loop): low traction motor, ground rumble, slow "
                                "track clank.", veh_crawler_loop, True),
    ("vehicle", "wiper", "One wiper stroke on wet glass (1.3 s up and back): rubber swish, tiny motor whirr, soft thunk "
                         "as it parks.", veh_wiper, False),
    ("vehicle", "wiper_squeak", "The same stroke on dry glass: the rubber squeaks.", veh_wiper_squeak, False),
    ("vehicle", "flat_loop", "Driving on a flat tyre (loop): a soft flap and thump once a turn.", veh_flat_loop, True),

    ("ground", "dirt_loop", "Packed dirt (plains; Veyra's lowlands): rolling rumble, crumbly crunch, the odd pebble.",
     gnd_dirt_loop, True),
    ("ground", "gravel_loop", "Grit and gravel (hills): denser, brighter crunch, stones clacking.", gnd_gravel_loop, True),
    ("ground", "sand_loop", "Sand and dust (desert): soft swishing hiss over a dull rumble.", gnd_sand_loop, True),
    ("ground", "rust_loop", "Rust dust (Veyra): coarser and lower than sand, sparse grit.", gnd_rust_loop, True),
    ("ground", "mud_loop", "Mud (forest floor): wet roll, squelches, bubbles.", gnd_mud_loop, True),
    ("ground", "snow_loop", "Snow (polar ice): crisp crunches over a soft rumble.", gnd_snow_loop, True),
    ("ground", "water_loop", "Water (plains and forest beside water): washing slosh, spray in surges, drips.", gnd_water_loop, True),
    ("ground", "frost_loop", "Frost grit (frozen ground): thin grit, icy crackle, glassy tinkles.", gnd_frost_loop, True),

    ("ambience", "wind_loop", "Low steady wind (loop, 8 s): dark air with drifting resonances; louder with the wind "
                              "speed.", amb_wind_loop, True),
    ("ambience", "wind_gust_a", "A gust swells and falls, with a thin whistle.", amb_wind_gust_a, False),
    ("ambience", "wind_gust_b", "A shorter, brighter gust.", amb_wind_gust_b, False),
    ("ambience", "wind_gust_c", "A long low gust with a high whistle.", amb_wind_gust_c, False),
    ("ambience", "rain_light_loop", "Light rain (loop): soft patter on the ground, faint hiss.", amb_rain_light_loop,
     True),
    ("ambience", "rain_heavy_loop", "Heavy rain (loop): dense drops and splats, full hiss, low wash.",
     amb_rain_heavy_loop, True),
    ("ambience", "thunder_a", "Distant thunder: a low roll that rises twice and dies away.", amb_thunder_a, False),
    ("ambience", "thunder_b", "Thunder a few km off: muffled crack, rolling rumble.", amb_thunder_b, False),
    ("ambience", "thunder_close", "A close strike: ripping crack, boom, long roll.", amb_thunder_close, False),
    ("ambience", "dust_storm_loop", "Dust storm (loop): sand scouring in gusts over a strong wind.",
     amb_dust_storm_loop, True),
    ("ambience", "snowfall_loop", "Snowfall (loop): a very soft, muffled hush.", amb_snowfall_loop, True),
]

GROUPS = {
    "ui": ("Interface", "Buttons, panels, tutorial cards and placing buildings. Quietest group."),
    "landing": ("Landing", "Drop ships coming down, unloading and the outpost unfurling. Loudest group; "
                           "the two loops repeat seamlessly."),
    "select": ("Selection hums", "Played once when a building or a kind of vehicle is selected."),
    "vehicle": ("Vehicles", "Moving vehicles near the camera: the electric drive loops (pitch and volume follow the "
                            "speed in game), wiper strokes in time with the wipers, a flat tyre."),
    "ground": ("Ground", "Tyre-on-ground loops under a moving vehicle, one per kind of wheel spray; the game "
                         "crossfades them as the ground changes. Previews at 16 kHz."),
    "ambience": ("Weather", "The weather at the site on the Ambience bus: wind by its speed, gusts, rain light to heavy, "
                            "snowfall, dust storms, thunder after each lightning flash. Previews at 8–16 kHz (the dark ones lower)."),
}

# Page previews: 22 kHz; the long ground and weather beds at 16 kHz, and the dark ones (wind, snowfall, thunder: next to
# nothing above 3 kHz) lower still, which keeps the page under 8 MB. By name first, then by group.
PREVIEW_SR = {"ground": 16000, "ambience": 16000, "wind_loop": 11025, "wind_gust_a": 11025, "wind_gust_b": 11025,
              "wind_gust_c": 11025, "snowfall_loop": 8000, "thunder_a": 8000, "thunder_b": 11025, "thunder_close": 16000}

# Per-sound loudness offsets from the group's target (dB).
LOUDNESS_OFFSET = {
    "hover": -8.0,
    "wiper": -4.0, "wiper_squeak": -4.0, "flat_loop": -4.0,
    "wind_gust_a": 1.0, "wind_gust_b": 1.0, "wind_gust_c": 1.0,
    "rain_light_loop": -3.0, "rain_heavy_loop": 2.0, "dust_storm_loop": 2.0, "snowfall_loop": -6.0,
    "thunder_a": 4.0, "thunder_b": 5.0, "thunder_close": 8.0,
}


# ---------------------------------------------------------------------------------------------------------------------
# Finishing, checks, output

def weighted(x: np.ndarray) -> np.ndarray:
    """Loudness weighting: BS.1770 K-weighting (+4 dB shelf over 1.5 kHz, 38 Hz high-pass) plus a gentle
    first-order 60 Hz roll-off, so deep hums get enough body to be heard on small speakers too."""
    y = biquad(biquad(x, "hs", 1500, 0.707, 4.0), "hp", 38, 0.5)
    return onepole_hp(y, 60)


def loudness(x: np.ndarray) -> float:
    return active_rms(weighted(x))


def active_rms(x: np.ndarray) -> float:
    w = ns(0.02)
    k = len(x) // w
    if k == 0:
        return float(np.sqrt(np.mean(x ** 2)))
    e = np.mean(x[:k * w].reshape(k, w) ** 2, axis=1)
    keep = e >= e.max() * 10 ** (-30 / 10)
    return float(np.sqrt(np.mean(e[keep])))


def limit(x: np.ndarray, ceiling: float, look: float = 0.0015, release: float = 0.06) -> np.ndarray:
    """Look-ahead peak limiter: the gain dips just before a peak and recovers over `release` s."""
    need = np.minimum(1.0, ceiling / np.maximum(np.abs(x), 1e-12))
    w = max(1, ns(look))
    pad = np.concatenate([need, np.ones(w)])
    win = np.lib.stride_tricks.sliding_window_view(pad, w + 1).min(axis=1)[:len(x)]   # min over [i, i+w]
    k = 1 - np.exp(-1 / (release * SR))
    g, out, wl = 1.0, [0.0] * len(x), win.tolist()
    for i, t in enumerate(wl):
        g = t if t < g else g + (t - g) * k
        out[i] = g
    gd = np.concatenate([np.ones(w), np.array(out)])[:len(x)]                 # delay the gain to sit on the peak
    gd = np.minimum(gd, need)                                                    # never over
    return x * gd


def quiet_seam(x: np.ndarray) -> np.ndarray:
    """Turn a loop so its seam falls on a quiet, small step (a circular shift: the loop itself is unchanged). The seam
    check compares the wrap step with a typical step, so a loop shouldn't wrap in the middle of a clank."""
    d = np.abs(np.diff(np.concatenate([x, x[:1]])))         # d[i] = step from x[i] to x[i + 1], circularly
    w = ns(0.01)
    local = np.convolve(np.concatenate([d[-w:], d, d[:w]]), np.ones(2 * w + 1) / (2 * w + 1), "valid")
    i = int(np.argmin(d + 2 * local))
    return np.roll(x, -(i + 1))


def finish(x: np.ndarray, group: str, name: str, loop: bool) -> np.ndarray:
    x = np.asarray(x, float)
    if loop:
        x = x - x.mean()
        if group != "landing":                               # (the landing loops predate this and pass as they are)
            x = quiet_seam(x)
    else:
        x = x - np.mean(x)
        x = fade(onepole_hp(x, 18), 0.005 if x[:ns(0.005)].any() else 0.0, 0.01)
        x = fade(x, 0.0, 0.005)
    target = TARGET_LOUDNESS[group] + LOUDNESS_OFFSET.get(name, 0.0)
    g = 10 ** (target / 20) / max(loudness(x), 1e-9)
    # up to 5 dB of transient limiting (cracks, thumps); beyond that the whole sound comes down instead
    g = min(g, PEAK_CAP * 10 ** (5 / 20) / max(np.max(np.abs(x)), 1e-9))
    y = x * g
    if np.max(np.abs(y)) > PEAK_CAP * 0.98 and not loop:
        y = limit(y, PEAK_CAP * 0.97)
    if np.max(np.abs(y)) > PEAK_CAP:
        y *= PEAK_CAP / np.max(np.abs(y))
    return y


def to_pcm(x: np.ndarray) -> bytes:
    return (np.clip(np.round(x * 32767), -32768, 32767).astype("<i2")).tobytes()


def write_wav(path: Path, x: np.ndarray, sr: int = SR) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(to_pcm(x))
    return path.stat().st_size


def wav_bytes(x: np.ndarray, sr: int) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(to_pcm(x))
    return buf.getvalue()


def downsample2(x: np.ndarray, loop: bool) -> np.ndarray:
    """Halve the rate by truncating the spectrum (band-limited; circular for loops)."""
    n = len(x) // 2 * 2
    pad = 0 if loop else 2048
    X = np.fft.rfft(np.concatenate([x[:n], np.zeros(pad)]))
    m = (n + pad) // 2
    Y = X[:m // 2 + 1].copy()
    Y[-1] = 0
    return np.fft.irfft(Y, m)[:n // 2] * 0.5


def resample(x: np.ndarray, sr_out: int, loop: bool) -> np.ndarray:
    """Band-limited rate change by truncating the spectrum (top 8% tapered; circular for loops)."""
    if sr_out * 2 == SR:
        return downsample2(x, loop)
    pad = 0 if loop else 2048
    m = len(x) + pad
    m_out = int(round(m * sr_out / SR))
    X = np.fft.rfft(np.concatenate([x, np.zeros(pad)]))
    k = m_out // 2 + 1
    Y = X[:k].copy()
    taper = max(1, int(k * 0.08))
    Y[-taper:] *= 0.5 + 0.5 * np.cos(np.pi * np.arange(1, taper + 1) / taper)
    return np.fft.irfft(Y, m_out)[:int(round(len(x) * sr_out / SR))] * (m_out / m)


def spectrum(x: np.ndarray):
    """Spectral centroid (power-weighted, Hz) and the share of energy in four bands."""
    P = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    f = np.fft.rfftfreq(len(x), 1 / SR)
    tot = max(np.sum(P), 1e-20)
    bands = [float(np.sum(P[(f >= lo) & (f < hi)]) / tot) for lo, hi in ((0, 200), (200, 2000), (2000, 6000),
                                                                         (6000, SR))]
    return float(np.sum(f * P) / tot), bands


def check(x: np.ndarray, loop: bool) -> dict:
    d = np.abs(np.diff(x))
    info = {
        "dur": len(x) / SR, "peak": db(np.max(np.abs(x))), "rms": db(active_rms(x)), "loud": db(loudness(x)), "cent": spectrum(x)[0], "bands": spectrum(x)[1],
        "dc": float(np.mean(x)), "edge": max(abs(x[0]), abs(x[-1])),
    }
    if loop:
        seam = abs(x[0] - x[-1])
        info["seam"] = seam / max(np.median(d), 1e-12)        # seam step in units of a typical sample step
        info["seam_max"] = seam / max(np.max(d), 1e-12)
        w = ns(0.01)                                           # and against the steps right round the seam
        near = np.concatenate([d[-w:], d[:w]])
        info["seam_local"] = seam / max(np.percentile(near, 90), 1e-12)
    return info


def build(only: str | None):
    results = []
    for group, name, desc, fn, loop in SOUNDS:
        if only and only not in (group, name):
            continue
        x = finish(fn(rng_for(name)), group, name, loop)
        size = write_wav(OUT / group / f"{name}.wav", x)
        results.append((group, name, desc, loop, x, size, check(x, loop)))
    return results


def report(results) -> bool:
    ok = True
    print(f"{'file':34} {'dur s':>6} {'peak':>6} {'rms':>6} {'loud':>6} {'cent Hz':>8} {'<200 / -2k / -6k / >6k %':>24} "
          f"{'dc':>8} {'edge':>7}  loop seam")
    for group, name, _, loop, x, size, c in results:
        bad = []
        if c["peak"] > -2.9:
            bad.append("PEAK")
        if abs(c["dc"]) > 1e-3:
            bad.append("DC")
        if not loop and c["edge"] > 1e-3:
            bad.append("EDGE")
        if loop and (c["seam"] > 4 or c["seam_local"] > 2):
            bad.append("SEAM")
        seam = f"{c['seam']:.2f}x median step, {c['seam_max']:.2f}x max, {c['seam_local']:.2f}x near" if loop else ""
        print(f"{group + '/' + name:34} {c['dur']:6.2f} {c['peak']:6.1f} {c['rms']:6.1f} {c['loud']:6.1f} {c['cent']:8.0f} "
              f"{' / '.join(f'{100 * b:3.0f}' for b in c['bands']):>24} {c['dc']:8.1e} {c['edge']:7.1e}  {seam} {' '.join(bad)}")
        ok &= not bad
    for g in GROUPS:
        r = [c["loud"] for gg, *_, c in results if gg == g]
        if r:
            print(f"  {g}: weighted loudness {min(r):.1f} .. {max(r):.1f} dBFS (median {np.median(r):.1f})")
    total = sum(r[5] for r in results)
    print(f"{len(results)} files, {total / 1e6:.2f} MB, {sum(r[6]['dur'] for r in results):.1f} s")
    return ok


PAGE_HEAD = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Final Frontier Sound Board</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@500;600;700&family=Barlow:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap">
<style>
/* Generated by tools/gen_sounds.py: do not edit by hand. A listening sheet: one section per sound group,
   each sound a row with its name, length and a one-line description over a native audio player. */
:root {
  --bg: #f3f4f7; --panel: #ffffff; --ink: #161b28; --dim: #586178; --rule: #d8dce6;
  --accent: #b5620d; --accent-soft: #f6e6d3; --loop: #1f5f9a; --loop-soft: #dde8f4;
  --display: "Barlow Condensed", "Arial Narrow", system-ui, sans-serif;
  --body: "Barlow", system-ui, -apple-system, "Segoe UI", sans-serif;
  --mono: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #0b0f18; --panel: #121827; --ink: #e5e9f2; --dim: #96a0b8; --rule: #252d42;
    --accent: #f2a54a; --accent-soft: #33240f; --loop: #8cc2f2; --loop-soft: #132638;
    color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --bg: #0b0f18; --panel: #121827; --ink: #e5e9f2; --dim: #96a0b8; --rule: #252d42;
  --accent: #f2a54a; --accent-soft: #33240f; --loop: #8cc2f2; --loop-soft: #132638;
  color-scheme: dark;
}
* { box-sizing: border-box; }
html, body { margin: 0; }
body { background: var(--bg); color: var(--ink); font: 400 15px/1.5 var(--body); }
.wrap { max-width: 980px; margin: 0 auto; padding-left: max(16px, env(safe-area-inset-left, 0px));
  padding-right: max(16px, env(safe-area-inset-right, 0px)); padding-block: 28px 64px; display: grid; gap: 28px; }
header { display: grid; gap: 10px; }
.eyebrow { font: 600 13px/1 var(--display); letter-spacing: .16em; text-transform: uppercase; color: var(--accent); }
h1 { margin: 0; font: 700 clamp(30px, 5vw, 44px)/1.05 var(--display); letter-spacing: .01em; text-wrap: balance; }
.lede { margin: 0; color: var(--dim); max-width: 66ch; }
.lede code, .row code { font: 13px/1.4 var(--mono); }
.tools { display: flex; flex-wrap: wrap; gap: 6px; }
button { font: 600 13px/1 var(--display); letter-spacing: .08em; text-transform: uppercase; border: 1px solid var(--rule);
  background: var(--panel); color: var(--ink); border-radius: 6px; padding: 8px 11px; cursor: pointer; }
button:hover { border-color: var(--accent); }
button[aria-pressed="true"] { border-color: var(--accent); background: var(--accent-soft); }
button:focus-visible, audio:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
section { display: grid; gap: 10px; }
h2 { margin: 0; font: 700 22px/1.1 var(--display); letter-spacing: .02em; display: flex; gap: 10px;
  align-items: baseline; flex-wrap: wrap; }
h2 small { font: 500 14px/1.3 var(--body); color: var(--dim); }
.rows { display: grid; gap: 8px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 300px), 1fr)); }
.row { background: var(--panel); border: 1px solid var(--rule); border-left: 4px solid var(--accent);
  border-radius: 8px; padding: 12px 14px; display: grid; gap: 6px; min-width: 0; }
.row.loop { border-left-color: var(--loop); }
.row.playing { box-shadow: 0 0 0 1px var(--accent); }
.top { display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; }
.row h3 { margin: 0; font: 600 18px/1.2 var(--display); letter-spacing: .04em; overflow-wrap: anywhere; }
.meta { font: 500 12px/1 var(--mono); color: var(--dim); }
.tag { font: 600 11px/1 var(--display); letter-spacing: .1em; text-transform: uppercase; color: var(--loop);
  background: var(--loop-soft); border-radius: 10px; padding: 4px 7px; }
.row p { margin: 0; font-size: 14px; color: var(--dim); }
audio { width: 100%; height: 36px; display: block; }
.foot { color: var(--dim); font-size: 13px; }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; } }
</style>
</head>
<body>
<div class="wrap">
<header>
  <div class="eyebrow">Final Frontier · game prototype audio</div>
  <h1>Final Frontier Sound Board</h1>
  <p class="lede">Every sound in the game, synthesised from numbers by <code>tools/gen_sounds.py</code> (no
  recordings). Files live in <code>game/sounds/&lt;group&gt;/&lt;name&gt;.wav</code>; a real recording with the same
  name replaces one. Previews here are 22 kHz copies of the 44.1 kHz game files (8–16 kHz for the ground and weather
  beds). Loops repeat on their own; in the game the vehicle, ground and weather loops are mixed by speed, ground and
  weather.</p>
  <div class="tools"><button type="button" id="stop">Stop all</button>
  <button type="button" id="auto" aria-pressed="false">Play group in turn</button></div>
</header>
"""

PAGE_TAIL = """<p class="foot">Regenerate with <code>python3 tools/gen_sounds.py</code>. %s files, %s s in all.</p>
</div>
<script>
const players = [...document.querySelectorAll("audio")];
let auto = false;
const autoBtn = document.getElementById("auto");
autoBtn.addEventListener("click", () => {
  auto = !auto;
  autoBtn.setAttribute("aria-pressed", String(auto));
});
players.forEach((a) => {
  a.addEventListener("play", () => {
    players.forEach((b) => { if (b !== a) b.pause(); });
    document.querySelectorAll(".row.playing").forEach((r) => r.classList.remove("playing"));
    a.closest(".row").classList.add("playing");
  });
  a.addEventListener("pause", () => a.closest(".row").classList.remove("playing"));
  a.addEventListener("ended", () => {
    a.closest(".row").classList.remove("playing");
    if (!auto) return;
    const next = a.closest(".row").nextElementSibling;
    const n = next && next.querySelector("audio");
    if (n && !n.loop) { n.currentTime = 0; n.play().catch(() => {}); }
  });
});
document.getElementById("stop").addEventListener("click", () => players.forEach((a) => { a.pause(); a.currentTime = 0; }));
</script>
</body>
</html>
"""


def write_page(results) -> int:
    out = [PAGE_HEAD]
    for g, (title, blurb) in GROUPS.items():
        rows = [r for r in results if r[0] == g]
        if not rows:
            continue
        out.append(f'<section id="{g}">\n<h2>{html.escape(title)} <small>{html.escape(blurb)} '
                   f'<code>game/sounds/{g}/</code></small></h2>\n<div class="rows">\n')
        for _, name, desc, loop, x, _, c in rows:
            rate = PREVIEW_SR.get(name, PREVIEW_SR.get(g, SR // 2))
            data = base64.b64encode(wav_bytes(resample(x, rate, loop), rate)).decode()
            tag = '<span class="tag">loop</span>' if loop else ""
            out.append(
                f'<div class="row{" loop" if loop else ""}"><div class="top"><h3>{name}</h3>'
                f'<span class="meta">{c["dur"]:.2f} s</span>{tag}</div>'
                f'<p>{html.escape(desc)}</p>'
                f'<audio controls preload="metadata"{" loop" if loop else ""} '
                f'src="data:audio/wav;base64,{data}"></audio></div>\n')
        out.append("</div>\n</section>\n")
    out.append(PAGE_TAIL % (len(results), f"{sum(r[6]['dur'] for r in results):.0f}"))
    PAGE.parent.mkdir(parents=True, exist_ok=True)
    PAGE.write_text("".join(out), encoding="utf-8")
    return PAGE.stat().st_size


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", help="one group (ui, landing, select, vehicle, ground, ambience) or one sound name")
    ap.add_argument("--no-page", action="store_true", help="don't write docs/sounds/index.html")
    a = ap.parse_args()
    results = build(a.only)
    ok = report(results)
    if not a.no_page and not a.only:
        size = write_page(results)
        print(f"page {PAGE.relative_to(ROOT)}: {size / 1e6:.2f} MB")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
