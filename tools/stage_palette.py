#!/usr/bin/env python3
"""Writes the mask that says which swatches of the wirenook palette follow a wearer's signature colour.

    tools/stage_palette.py

The game master's kit is drawn in one material: the shared wirenook palette on
the toon shader. Two of the palette's swatches, `cyan` and `glow-cyan`, are the
signature colour as it is painted, and this writes
Textures/stage-palette-emission.png, white on exactly those two. The material
uses it twice: as the mask of what turns with the wearer's choice, and as the
mask of what glows, so that on Android and iOS, where a glow is one colour for
the whole material, the only thing that glows is the thing that colour is.

The palette's layout comes from the sibling wirenook-assets checkout, which
sits next to this repo both as a submodule and as a separate clone.
"""

import json
import pathlib
import sys

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parents[1]
ASSETS = REPO.parent / "wirenook-assets" / "Packages" / "net.wirenook.assets" / "Textures"
OUT = REPO / "Packages" / "net.wirenook.adventure-tools" / "Textures" / "stage-palette-emission.png"

# The swatches painted in the signature colour.
SIGNATURE = ("cyan", "glow-cyan")


def main() -> int:
    layout = ASSETS / "wirenook-palette.json"
    if not layout.exists():
        raise SystemExit(f"stage-palette: missing {layout}; check out wirenook-assets beside this repository")
    palette = json.loads(layout.read_text(encoding="utf-8"))
    width, height = palette["size"]
    cell = palette["cell"]
    mask = Image.new("L", (width, height), 0)
    for name in SIGNATURE:
        u, v = palette["swatches"][name]["uv"]
        # UVs run bottom-up; images top-down.
        left = int(u * width) // cell * cell
        top = int((1 - v) * height) // cell * cell
        mask.paste(255, (left, top, left + cell, top + cell))
    mask.save(OUT, optimize=True)
    print(f"stage-palette: {', '.join(SIGNATURE)} -> {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
