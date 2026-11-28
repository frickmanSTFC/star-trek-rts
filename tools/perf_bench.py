"""Time the same jobs on any machine, to compare the cloud container with the owner's laptop.

  python tools/perf_bench.py --name laptop [--blender "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe"]
  python tools/perf_bench.py --compare            (table of every result in tools/perf/results/)

Jobs: a full build of the game solution, the test suite, a 30-minute balance run (pure sim speed), the Prefab Habitat
built + baked + exported in Blender, and one 1280 px Cycles picture of it at 64 samples on the processor and, when
Cycles finds a graphics card, on that too. --blender takes blender(.exe) or a Python that has the bpy module;
without it the script looks in FF_BLENDER, the cloud venv, PATH and the usual Windows folders. --skip a,b leaves
jobs out (build, test, balance, model, render_cpu, render_gpu). Results: tools/perf/results/<name>.json.
"""
import glob
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RESULTS = os.path.join(REPO, "tools", "perf", "results")
JOBS = [("build", "Build the game (full)"), ("test", "Run the tests"), ("balance", "30-minute balance run"),
        ("model", "Habitat: build + bake + export"), ("render_cpu", "Habitat picture, processor"),
        ("render_gpu", "Habitat picture, graphics card")]


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def run(cmd, cwd=REPO, env=None):
    t = time.perf_counter()
    p = subprocess.run(cmd, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    return time.perf_counter() - t, p.returncode, p.stdout


def find_blender():
    spots = [arg("--blender"), os.environ.get("FF_BLENDER"), "/home/user/.bpyvenv52/bin/python", shutil.which("blender")]
    spots += sorted(glob.glob(r"C:\Program Files\Blender Foundation\Blender*\blender.exe"), reverse=True)
    spots += ["/Applications/Blender.app/Contents/MacOS/Blender"]
    return next((s for s in spots if s and os.path.exists(s)), None)


def blender_cmd(blender, script, args):
    if os.path.basename(blender).lower().startswith("python"):
        return [blender, script, *args]
    return [blender, "--background", "--python", script, "--", *args]


def cpu_name():
    try:
        if platform.system() == "Windows":
            out = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"], capture_output=True, text=True).stdout
            return out.strip().splitlines()[0]
        with open("/proc/cpuinfo") as f:
            return next(line.split(":", 1)[1].strip() for line in f if line.startswith("model name"))
    except Exception:
        return platform.processor() or "unknown"


def measure(name):
    skip = set((arg("--skip") or "").split(","))
    tmp = tempfile.mkdtemp(prefix="ffbench")
    game = os.path.join(REPO, "game")
    out = {"name": name, "when": time.strftime("%Y-%m-%d %H:%M"), "system": platform.platform(), "cpu": cpu_name(),
           "cores": os.cpu_count(), "jobs": {}, "notes": {}}

    def job(key, cmd, cwd=REPO, env=None, check=None):
        if key in skip:
            return ""
        print(f"  {dict(JOBS)[key]} ...", end=" ", flush=True)
        secs, code, text = run(cmd, cwd, env)
        ok = code == 0 and (check is None or check(text))
        print(f"{secs:.1f} s" if ok else ("not available" if code == 0 else f"FAILED (exit {code})"), flush=True)
        if ok:
            out["jobs"][key] = round(secs, 1)
        else:
            out["notes"][key] = "failed: " + " | ".join(text.strip().splitlines()[-3:])
        return text

    if shutil.which("dotnet"):
        out["dotnet"] = run(["dotnet", "--version"])[2].strip()
        job("build", ["dotnet", "build", "FinalFrontier.sln", "--no-incremental"], game)
        text = job("test", ["dotnet", "test", "--no-build"], game)
        for line in text.splitlines():
            if "Passed!" in line or "Failed!" in line:
                out["notes"]["test"] = line.strip()
        if "balance" not in skip:
            run(["dotnet", "build", "-c", "Release", os.path.join("tools", "BalanceRunner")])
            job("balance", ["dotnet", "run", "-c", "Release", "--no-build", "--project", os.path.join("tools", "BalanceRunner"), "--",
                            "--orders", os.path.join("balance", "orders", "standard_opener.json"), "--seeds", "1701", "--minutes", "30",
                            "--lean", "--out", os.path.join(tmp, "balance")])
    else:
        out["notes"]["dotnet"] = "dotnet not found"

    blender = find_blender()
    if blender:
        out["blender"] = blender
        script = os.path.join("blender", "build_habitat.py")
        job("model", blender_cmd(blender, script, ["--mark", "0", "--out", os.path.join(tmp, "habitat.glb")]))
        shot = ["--mark", "0", "--no-bake", "--no-join", "--preview", tmp, "--shots", "hero", "--res", "1280", "--samples", "64"]
        for key, device in (("render_cpu", "CPU"), ("render_gpu", "GPU")):
            text = job(key, blender_cmd(blender, script, shot), env={**os.environ, "FF_CYCLES_DEVICE": device},
                       check=lambda t, d=device: d == "CPU" or "cycles device: GPU" in t)
            for line in text.splitlines():
                if line.startswith("cycles device:"):
                    out["notes"][key] = line[len("cycles device:"):].strip()
            if key == "render_gpu" and key not in out["jobs"] and key not in skip:
                out["notes"][key] = "no graphics card Cycles can use"
    else:
        out["notes"]["blender"] = "Blender not found (pass --blender <path>)"
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(RESULTS, exist_ok=True)
    path = os.path.join(RESULTS, f"{name}.json")
    if skip - {""} and os.path.exists(path):   # a partial run keeps the skipped jobs' earlier times
        old = json.load(open(path))
        for table in ("jobs", "notes"):
            out[table] = {**{k: v for k, v in old.get(table, {}).items() if k in skip}, **out[table]}
    with open(path, "w") as f:
        json.dump(out, f, indent=2)
    return out


def compare():
    runs = [json.load(open(p)) for p in sorted(glob.glob(os.path.join(RESULTS, "*.json")))]
    if not runs:
        sys.exit("no results yet")
    base = next((r for r in runs if r["name"] == "cloud"), runs[0])
    print()
    for r in runs:
        print(f"{r['name']}: {r['cpu']}, {r['cores']} cores, {r['system']} ({r['when']})")
    print()
    print(f"{'Job':34}" + "".join(f"{r['name']:>16}" for r in runs))
    for key, label in JOBS:
        row = f"{label:34}"
        for r in runs:
            v = r["jobs"].get(key)
            b = base["jobs"].get(key)
            cell = "-" if v is None else f"{v:.1f} s" + (f" ({b / v:.1f}x)" if b and r is not base and v else "")
            row += f"{cell:>16}"
        print(row)
    print(f"\n(Nx) = how many times faster than '{base['name']}'; below 1 is slower.")
    for r in runs:
        for k, v in r["notes"].items():
            print(f"  {r['name']} {k}: {v}")


if "--compare" not in sys.argv:
    print(f"Timing jobs as '{arg('--name', platform.node())}' (a few minutes):")
    measure(arg("--name", platform.node()))
compare()
