"""Shared loader for the split data files (data/manifest.json lists each document's files).

    from data_loader import load
    B = load("buildings")          # or load("buildings.json"): the whole document, as one dict

A document's files are merged in manifest order: a table that appears in several files is joined (lists one after
the other, objects key by key, recursively); the same key with two plain values is an error. The page generators
read the base data only (no layers, no draft). The game's loader (game/sim/Data/GameData.cs) does the same merge.
"""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "data")
_cache = {}


def manifest(data_dir=DATA):
    return json.load(open(os.path.join(data_dir, "manifest.json"), encoding="utf-8"))


def merge(into, part, where):
    for key, value in part.items():
        if key not in into:
            into[key] = value
        elif isinstance(into[key], list) and isinstance(value, list):
            into[key] = into[key] + value
        elif isinstance(into[key], dict) and isinstance(value, dict):
            into[key] = dict(into[key])
            merge(into[key], value, f"{where}/{key}")
        else:
            raise ValueError(f"data: '{where}/{key}' is given twice")
    return into


def load(name, data_dir=DATA):
    """The assembled document 'name' (e.g. "surfaces"; a trailing .json is ignored)."""
    doc = name[:-5] if name.endswith(".json") else name
    key = (data_dir, doc)
    if key not in _cache:
        parts = manifest(data_dir)["documents"].get(doc)
        if parts is None:
            raise KeyError(f"data/manifest.json has no document '{doc}'")
        out = {}
        for rel in parts:
            merge(out, json.load(open(os.path.join(data_dir, rel), encoding="utf-8")), doc)
        _cache[key] = out
    return json.loads(json.dumps(_cache[key]))  # a fresh copy: callers may edit what they get


def documents(data_dir=DATA):
    """Names of every data document, in manifest order."""
    return list(manifest(data_dir)["documents"])
