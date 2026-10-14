"""asset.title-rift: the ROOT 33 title art (see tools/art/make_title_rift.py)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from make_title_rift import H, W, render  # noqa: E402


def make(out_dir, fonts):
    render(fonts["black"], str(Path(out_dir) / "title-rift.png"))
    return [{"asset": "asset.title-rift", "path": "art/title-rift.png", "size": [W, H], "sprites": []}]
