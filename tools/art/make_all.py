#!/usr/bin/env python3
"""Rebuild every made-for-mod art piece and art/manifest.json.

Each module in tools/art/pieces exposes  make(out_dir: Path, fonts: dict) -> list[dict]  where every dict is
{"asset": "<assets.json id>", "path": "art/...png", "size": [w, h], "sprites": [{"name": str, "box": [x0, y0, x1, y1]}]}.
fonts maps "regular", "bold" and "black" to Cinzel font files (from the @fontsource/cinzel npm package).

Usage: python3 -I tools/art/make_all.py --font-dir <fontsource-cinzel/package/files> [--only <module>]
"""
import argparse
import importlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "art"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font-dir", required=True)
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    fd = Path(args.font_dir)
    fonts = {"regular": str(fd / "cinzel-latin-400-normal.woff2"), "bold": str(fd / "cinzel-latin-700-normal.woff2"),
             "black": str(fd / "cinzel-latin-900-normal.woff2")}
    manifest_path = ROOT / "art" / "manifest.json"
    manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {"pieces": []}
    done = {}
    for mod_path in sorted((ROOT / "tools" / "art" / "pieces").glob("*.py")):
        name = mod_path.stem
        if name.startswith("_") or (args.only and name != args.only):
            continue
        mod = importlib.import_module(f"pieces.{name}")
        for entry in mod.make(ROOT / "art", fonts):
            done[entry["asset"]] = entry
            print(f"{name}: {entry['path']} {entry['size']}")
    kept = [e for e in manifest["pieces"] if e["asset"] not in done]
    manifest["pieces"] = sorted(kept + list(done.values()), key=lambda e: e["path"])
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"manifest: {len(manifest['pieces'])} pieces")


if __name__ == "__main__":
    main()
