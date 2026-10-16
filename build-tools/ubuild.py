#!/usr/bin/env python3
"""Build a Unity package / mod assembly with the pwsh Roslyn driver.

usage: ubuild.py --out X.dll --src DIR [--src DIR2] [--exclude-dir NAME ...]
                 [--ref path|glob ...] [--define SYM ...] [--resource file,name ...]
                 [--no-unity-defines] [--unsafe] [--langversion V] [--extra-src file ...]
"""
import argparse, glob, os, shutil, subprocess, sys
from pathlib import Path

UNITY_DEFINES = """
UNITY_5_3_OR_NEWER UNITY_5_4_OR_NEWER UNITY_5_5_OR_NEWER UNITY_5_6_OR_NEWER
UNITY_2017_1_OR_NEWER UNITY_2017_2_OR_NEWER UNITY_2017_3_OR_NEWER UNITY_2017_4_OR_NEWER
UNITY_2018_1_OR_NEWER UNITY_2018_2_OR_NEWER UNITY_2018_3_OR_NEWER UNITY_2018_4_OR_NEWER
UNITY_2019_1_OR_NEWER UNITY_2019_2_OR_NEWER UNITY_2019_3_OR_NEWER UNITY_2019_4_OR_NEWER
UNITY_2020_1_OR_NEWER UNITY_2020_2_OR_NEWER UNITY_2020_3_OR_NEWER
UNITY_2021_1_OR_NEWER UNITY_2021_2_OR_NEWER UNITY_2021_3_OR_NEWER
UNITY_2021_3_33 UNITY_2021_3 UNITY_2021
UNITY_STANDALONE_WIN UNITY_STANDALONE UNITY_64 PLATFORM_ARCH_64 PLATFORM_SUPPORTS_MONO
ENABLE_MONO ENABLE_VR ENABLE_AUDIO ENABLE_PHYSICS ENABLE_CLOTH ENABLE_UNET ENABLE_UNITYEVENTS
ENABLE_UNITYWEBREQUEST ENABLE_WWW ENABLE_MANAGED_JOBS ENABLE_MANAGED_ANIMATION_JOBS ENABLE_MANAGED_AUDIO_JOBS
ENABLE_VIDEO ENABLE_MICROPHONE ENABLE_MULTIPLE_DISPLAYS ENABLE_TEXTURE_STREAMING ENABLE_LEGACY_INPUT_MANAGER
ENABLE_RUNTIME_GI INCLUDE_DYNAMIC_GI ENABLE_WEBCAM ENABLE_NETWORK ENABLE_CACHING ENABLE_LZMA
NET_STANDARD NET_STANDARD_2_0 NET_STANDARD_2_1 CSHARP_7_OR_LATER CSHARP_7_3_OR_NEWER
""".split()

def gather(src_dir, exclude_dirs):
    if not os.path.isdir(src_dir):
        raise ValueError(f"source directory not found: {src_dir}")
    out = []
    for dirpath, dirnames, filenames in os.walk(src_dir):
        # prune excluded / editor / test / sample / nested-asmdef dirs
        keep = []
        for d in dirnames:
            full = os.path.join(dirpath, d)
            name_l = d.lower()
            if d in exclude_dirs or name_l in ("editor", "tests", "test", "samples~", "documentation~", "samples", "bin", "obj") or d.endswith("~"):
                continue
            if full != src_dir and any(f.endswith(".asmdef") for f in os.listdir(full)):
                continue
            keep.append(d)
        dirnames[:] = keep
        for f in filenames:
            if f.endswith(".cs"):
                out.append(os.path.join(dirpath, f))
    return sorted(out)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--src", action="append", default=[])
    ap.add_argument("--extra-src", action="append", default=[])
    ap.add_argument("--exclude-dir", action="append", default=[])
    ap.add_argument("--exclude-file", action="append", default=[])
    ap.add_argument("--ref", action="append", default=[])
    ap.add_argument("--ref-file", action="append", default=[])
    ap.add_argument("--define", action="append", default=[])
    ap.add_argument("--resource", action="append", default=[])
    ap.add_argument("--no-unity-defines", action="store_true")
    ap.add_argument("--unsafe", action="store_true")
    ap.add_argument("--langversion", default="latest")
    ap.add_argument("--nowarn", default="")
    ap.add_argument("--warnings", action="store_true")
    ap.add_argument("--pwsh", default=os.environ.get("PWSH", "pwsh"))
    ap.add_argument("--driver", default=str(Path(__file__).with_name("csc.ps1")))
    ap.add_argument("--var", action="append", default=[], metavar="NAME=PATH")
    ap.add_argument("--plan", action="store_true", help="Write response file without compiling")
    a = ap.parse_args()
    variables = dict(v.split("=", 1) for v in a.var)
    def expand(value):
        for key, val in variables.items():
            value = value.replace("${" + key + "}", val)
        if "${" in value:
            raise ValueError(f"unresolved path variable: {value}; supply --var NAME=PATH")
        return value

    files = []
    for s in a.src:
        files += gather(os.path.abspath(s), set(a.exclude_dir))
    files += [os.path.abspath(f) for f in a.extra_src]
    excl = set(os.path.abspath(f) for f in a.exclude_file)
    files = [f for f in files if f not in excl and os.path.basename(f) not in a.exclude_file]
    files = sorted(set(files))
    if not files:
        sys.exit("no source files")
    for source in files:
        if not Path(source).is_file():
            sys.exit(f"source file not found: {source}")

    reflist = list(a.ref)
    for rf in a.ref_file:
        with open(rf, encoding="utf-8") as handle:
            reflist += [line.strip() for line in handle if line.strip() and not line.lstrip().startswith("#")]
    refs = []
    for r in reflist:
        r = expand(r)
        hits = [str(Path(p).resolve()) for p in sorted(glob.glob(r)) if Path(p).is_file()]
        if not hits:
            sys.exit(f"reference not found: {r}")
        refs += hits
    # dedupe by file name (first wins)
    seen, uniq = set(), []
    for r in refs:
        n = os.path.basename(r).lower()
        if n in seen:
            continue
        seen.add(n); uniq.append(r)
    refs = uniq

    defines = ([] if a.no_unity_defines else UNITY_DEFINES) + a.define
    out = os.path.abspath(a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    rsp = out + ".rsp"
    with open(rsp, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"/target:library /out:\"{out}\" /noconfig /nostdlib+ /optimize+ /deterministic+ /debug- /langversion:{a.langversion}\n")
        f.write("/nologo /nowarn:1701,1702,CS8632,CS0618,CS0612,CS0414,CS0169,CS0649,CS0108,CS0109,CS0162,CS0168,CS0219,CS0252,CS0420,CS0436,CS0628,CS0659,CS0661,CS0693,CS1591,CS1573,CS1570,CS1572,CS1574,CS1584,CS1587,CS1658,CS3001,CS3003,CS3005,CS3009,CS3021,CS8321,CS0809,CS0114,CS0067,CS0105,CS0184,CS0472,CS0642,CS0659,CS8073,CS0665,CS1717,CS8981" + ("," + a.nowarn if a.nowarn else "") + "\n")
        if a.unsafe:
            f.write("/unsafe+\n")
        for d in defines:
            f.write(f"/define:{d}\n")
        for r in refs:
            f.write(f"/r:\"{r}\"\n")
        for res in a.resource:
            path, separator, name = res.partition(",")
            path = str(Path(expand(path)).resolve())
            if not Path(path).is_file():
                raise ValueError(f"resource not found: {path}")
            f.write(f'/resource:"{path}"{separator}{name}\n')
        for s in files:
            f.write(f"\"{s}\"\n")
    env = dict(os.environ)
    if a.warnings:
        env["CSC_SHOW_WARNINGS"] = "1"
    print(f"compiling {len(files)} files, {len(refs)} refs -> {out}")
    if a.plan:
        print(f"response file: {rsp} (not compiled)")
        return
    pwsh = shutil.which(a.pwsh)
    if not pwsh:
        sys.exit(f"PowerShell 7 not found: {a.pwsh}; install it or pass --pwsh PATH")
    # Never leave a previous DLL looking like the result of a failed invocation.
    Path(out).unlink(missing_ok=True)
    r = subprocess.run([pwsh, "-NoProfile", "-File", a.driver, "@" + rsp], env=env)
    sys.exit(r.returncode)

if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        sys.exit(str(error))
