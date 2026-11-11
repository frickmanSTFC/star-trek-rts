#!/usr/bin/env python3
"""Synthesise the Jack raid's sounds (docs/jack-contract.md; owner 2026-11-11, a joke for Craig).

    python3 tools/gen_jack_sounds.py      # writes game/sounds/ui/jack_alarm.wav + game/sounds/jack/*.wav

Same style as tools/gen_sounds.py (numbers only, numpy + stdlib wave, fixed seeds, 16-bit mono 44.1 kHz, peaks
capped at -3 dBFS, edges faded to zero), kept apart so the big script and its listening page stay as they are.
  ui/jack_alarm     red-alert klaxon: two rising whoops
  jack/rise         a low ghostly swell with an eerie tremolo chord (him rising out of the mist)
  jack/throw        a whoosh (a pumpkin thrown)
  jack/burst        a soft boom with a crackle tail (the pumpkin bursting into green fire)
  jack/bats         a flutter of wings with squeaks
  jack/shriek       a spooky wavering "whooo" (a line from Jack)
  jack/step         a heavy, slightly hollow footfall
"""
from __future__ import annotations

import wave
import zlib
from pathlib import Path

import numpy as np

SR = 44100
OUT = Path(__file__).resolve().parent.parent / "game" / "sounds"


def rng(name: str) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32(name.encode()))


def t_of(seconds: float) -> np.ndarray:
    return np.arange(int(seconds * SR)) / SR


def lowpass(x: np.ndarray, hz: float) -> np.ndarray:
    a = np.exp(-2 * np.pi * hz / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def band(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    f = np.fft.rfft(x)
    fr = np.fft.rfftfreq(len(x), 1 / SR)
    f[(fr < lo) | (fr > hi)] = 0
    return np.fft.irfft(f, len(x))


def env(n: int, attack: float, release: float) -> np.ndarray:
    e = np.ones(n)
    a, r = max(1, int(attack * SR)), max(1, int(release * SR))
    e[:a] = np.linspace(0, 1, a) ** 2
    e[-r:] *= np.linspace(1, 0, r) ** 2
    return e


def reverb(x: np.ndarray, room: float = 0.5, wet: float = 0.3) -> np.ndarray:
    y = np.concatenate([x, np.zeros(int(SR * 0.8))])
    out = np.zeros_like(y)
    for d, g in ((1557, 0.84), (1617, 0.83), (1491, 0.82), (1422, 0.81)):
        d = int(d * (0.6 + room))
        buf = y.copy()
        for i in range(d, len(buf)):
            buf[i] += g * buf[i - d] * 0.5
        out += buf
    return (1 - wet) * y + wet * out / 4


def write(path: str, x: np.ndarray) -> None:
    x = x - np.mean(x)
    fade = int(0.004 * SR)
    x[:fade] *= np.linspace(0, 1, fade)
    x[-fade:] *= np.linspace(1, 0, fade)
    peak = np.max(np.abs(x)) or 1.0
    x = x / peak * 10 ** (-3 / 20)
    file = OUT / f"{path}.wav"
    file.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(file), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{file.relative_to(OUT.parent.parent)}  {len(x) / SR:.2f} s")


def alarm() -> np.ndarray:
    parts = []
    for _ in range(2):
        t = t_of(0.7)
        f = 420 + 520 * (t / 0.7) ** 0.8
        ph = 2 * np.pi * np.cumsum(f) / SR
        tone = np.sign(np.sin(ph)) * 0.35 + np.sin(2 * ph) * 0.3 + np.sin(ph) * 0.5
        parts.append(lowpass(tone, 3200) * env(len(t), 0.02, 0.08))
        parts.append(np.zeros(int(0.12 * SR)))
    return reverb(np.concatenate(parts), 0.3, 0.2)


def rise() -> np.ndarray:
    t = t_of(3.2)
    g = rng("rise")
    swell = band(g.standard_normal(len(t)), 60, 420) * 0.6
    chord = sum(np.sin(2 * np.pi * f * t + p) for f, p in ((110, 0), (130.8, 1), (155.6, 2), (207.7, 3)))
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * 6 * t)
    x = (swell + chord * 0.25 * trem) * np.linspace(0, 1, len(t)) ** 1.5
    return reverb(x * env(len(t), 0.1, 0.4), 0.8, 0.45)


def throw() -> np.ndarray:
    t = t_of(0.6)
    g = rng("throw")
    n = g.standard_normal(len(t))
    x = band(n, 300, 2500) * np.exp(-((t - 0.25) / 0.12) ** 2)
    return x


def burst() -> np.ndarray:
    t = t_of(1.6)
    g = rng("burst")
    boom = np.sin(2 * np.pi * (70 * t - 30 * t ** 2)) * np.exp(-t * 6)
    hit = band(g.standard_normal(len(t)), 80, 1800) * np.exp(-t * 9)
    crackle = (g.random(len(t)) > 0.997) * g.standard_normal(len(t)) * np.exp(-t * 2.2) * 2.5
    whoosh = band(g.standard_normal(len(t)), 500, 3000) * np.exp(-t * 3) * 0.25
    return reverb(boom * 1.2 + hit * 0.8 + band(crackle, 1500, 9000) + whoosh, 0.5, 0.25)


def bats() -> np.ndarray:
    t = t_of(2.2)
    g = rng("bats")
    x = np.zeros(len(t))
    for _ in range(9):
        rate = g.uniform(11, 17)
        start = g.uniform(0, 0.6)
        flap = np.clip(np.sin(2 * np.pi * rate * (t - start)), 0, None) ** 3
        flap *= (t > start) * np.exp(-np.clip(t - start - 1.0, 0, None) * 4)
        x += band(g.standard_normal(len(t)), 250, 2200) * flap * g.uniform(0.4, 1)
    for _ in range(6):
        s = g.uniform(0.1, 1.8)
        n = int(0.06 * SR)
        i = int(s * SR)
        if i + n >= len(t):
            continue
        tt = np.arange(n) / SR
        f = g.uniform(3800, 5200) - 9000 * tt
        x[i:i + n] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tt / tt[-1]) * 0.5
    return x * env(len(t), 0.05, 0.4)


def shriek() -> np.ndarray:
    t = t_of(2.0)
    f = 330 + 140 * np.sin(2 * np.pi * 0.9 * t) + 25 * np.sin(2 * np.pi * 5.5 * t)
    ph = 2 * np.pi * np.cumsum(f) / SR
    voice = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)
    breath = band(rng("shriek").standard_normal(len(t)), 600, 3000) * 0.08
    return reverb((voice + breath) * env(len(t), 0.25, 0.7), 0.9, 0.5)


def step() -> np.ndarray:
    t = t_of(0.7)
    g = rng("step")
    thud = np.sin(2 * np.pi * (55 * t - 15 * t ** 2)) * np.exp(-t * 11)
    knock = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 28) * 0.4
    grit = band(g.standard_normal(len(t)), 100, 900) * np.exp(-t * 16) * 0.4
    return reverb(thud + knock + grit, 0.4, 0.15)


def main() -> None:
    write("ui/jack_alarm", alarm())
    write("jack/rise", rise())
    write("jack/throw", throw())
    write("jack/burst", burst())
    write("jack/bats", bats())
    write("jack/shriek", shriek())
    write("jack/step", step())


if __name__ == "__main__":
    main()
