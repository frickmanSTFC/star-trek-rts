"""Sealing collar (hostile worlds, 2026-11-08): the Pressure Dome's airlock vestibule on its own, an add-on that the
game's variety pass can stand against a crewed building's door on a Domes world (BuildingModels.SealCollar; not wired
in yet). Built with build_pressure_dome.py's airlock() so it matches the dome.

Frame: Blender +Z up, door toward -Y (= Godot +Z); the back end (Blender y +1.5, Godot z -1.5) meets the wall.
Parts: "Collar" (the vestibule pivot), "CollarDoor" (door leaf, separate; slides along local X), material "DoorLight".

Run: /home/user/.bpyvenv/bin/python blender/build_seal_collar.py --game [--preview blender/previews/hostile --shots hero]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
import bpy  # noqa: E402

D = K.load("build_pressure_dome.py")
CFG = K.setup("seal_collar")


def collar(g):
    D["airlock"](g, 0, -90.0)
    piv = bpy.data.objects["Airlock0"]
    piv.location = (0.0, 0.0, 0.0)
    piv.name = "Collar"
    bpy.data.objects["Airlock0Door"].name = "CollarDoor"
    for name in ("SignFront", "RoofSpot0"):
        bpy.data.objects.remove(bpy.data.objects[name])


def main():
    D["materials"]()
    root = K.assemble("seal_collar", ["Collar"], [collar])
    keep = {"Build0", "Collar", "CollarDoor"}
    shots = {"hero": ((5.0, -6.5, 3.6), (0.0, 0.0, 1.3), 35)}
    K.finish(root, keep, {"CollarDoor"}, "CollarGrime", 0.0, shots=shots, night_lights=[])


main()
