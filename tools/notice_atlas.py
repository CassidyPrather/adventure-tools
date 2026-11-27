#!/usr/bin/env python3
"""Draws the cards the screen notice shows and packs them into one atlas.

    tools/notice_atlas.py                 # every set
    tools/notice_atlas.py notices.json    # one set, by its list

Reads a list under Packages/net.wirenook.adventure-tools/Textures/ and writes
its atlas beside it: one card per notice, laid out in a grid. The Unity side
picks a card by index, so the order in the list is the order of the menu;
append new notices rather than reordering.

The sets (list -> atlas):

    notices.json           -> notice-atlas.png            the everyday notices
    notices-roleplay.json  -> notice-roleplay-atlas.png   for running a scene
    stage-cards.json       -> stage-card-atlas.png        what a pause says to the people inside it

Icons and the lettering face come from the sibling wirenook-assets checkout,
which sits next to this repo both as a submodule and as a separate clone.
"""

import json
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFont

REPO = pathlib.Path(__file__).resolve().parents[1]
ASSETS = REPO.parent / "wirenook-assets"
TEXTURES = REPO / "Packages" / "net.wirenook.adventure-tools" / "Textures"

# Each card is 2:1 and the atlas stays a power of two on both sides.
CARD = (512, 256)

INK = (39, 20, 82, 255)
ICE = (215, 251, 255, 255)
VIOLET = (109, 44, 149, 255)
PANEL = (231, 252, 255, 255)

# The stickers' own proportions: an ink edge and a violet drop to the lower right.
EDGE = 10
DROP = 12
MARGIN = 14
RADIUS = 34


def require(path: pathlib.Path) -> pathlib.Path:
    if not path.exists():
        raise SystemExit(f"notice-atlas: missing {path}; check out wirenook-assets beside this repository")
    return path


def fit(draw: ImageDraw.ImageDraw, text: str, width: int, face: pathlib.Path, largest: int) -> ImageFont.FreeTypeFont:
    """The largest size at which the text fits the width."""
    for size in range(largest, 20, -2):
        font = ImageFont.truetype(str(face), size)
        if draw.textlength(text, font=font) <= width:
            return font
    raise SystemExit(f"notice-atlas: {text!r} does not fit a card even at the smallest size; shorten it")


BOX = (MARGIN, MARGIN, CARD[0] - MARGIN - DROP, CARD[1] - MARGIN - DROP)
INNER = (BOX[0] + EDGE, BOX[1] + EDGE, BOX[2] - EDGE, BOX[3] - EDGE)
ICON_SIZE = INNER[3] - INNER[1] - 24
TEXT_LEFT = INNER[0] + 14 + ICON_SIZE + 12
TEXT_WIDTH = INNER[2] - 22 - TEXT_LEFT


def lettering(labels: list[str]) -> ImageFont.FreeTypeFont:
    """One size for the whole set: the largest at which every line of every card fits."""
    face = require(ASSETS / "tools" / "fonts" / "AtkinsonHyperlegible-Bold.ttf")
    draw = ImageDraw.Draw(Image.new("RGBA", CARD))
    lines = [line for label in labels for line in label.split("\n")]
    return min((fit(draw, line, TEXT_WIDTH, face, 76) for line in lines), key=lambda candidate: candidate.size)


def card(label: str, icon: str, font: ImageFont.FreeTypeFont) -> Image.Image:
    image = Image.new("RGBA", CARD, (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    box, inner, icon_size = BOX, INNER, ICON_SIZE
    dropped = tuple(value + DROP for value in box)
    draw.rounded_rectangle(dropped, RADIUS, fill=VIOLET)
    draw.rounded_rectangle(box, RADIUS, fill=INK)
    draw.rounded_rectangle(inner, RADIUS - EDGE, fill=PANEL)

    art = Image.open(require(ASSETS / "Packages" / "net.wirenook.assets" / "Icons" / f"{icon}.png")).convert("RGBA")
    art = art.resize((icon_size, icon_size), Image.LANCZOS)
    image.alpha_composite(art, (inner[0] + 14, inner[1] + 12))

    lines = label.split("\n")
    line_height = int(font.size * 1.08)
    top = (inner[1] + inner[3]) // 2 - line_height * len(lines) // 2
    for index, line in enumerate(lines):
        draw.text((TEXT_LEFT, top + index * line_height), line, font=font, fill=INK)
    return image


SETS = {
    "notices.json": "notice-atlas.png",
    "notices-roleplay.json": "notice-roleplay-atlas.png",
    "stage-cards.json": "stage-card-atlas.png",
}


def build(listing: str) -> None:
    spec = json.loads((TEXTURES / listing).read_text(encoding="utf-8"))
    columns, rows = spec["columns"], spec["rows"]
    notices = spec["notices"]
    if len(notices) > columns * rows:
        raise SystemExit(f"notice-atlas: {len(notices)} notices do not fit a {columns}x{rows} atlas")

    font = lettering([notice["label"] for notice in notices])
    atlas = Image.new("RGBA", (CARD[0] * columns, CARD[1] * rows), (0, 0, 0, 0))
    for index, notice in enumerate(notices):
        position = ((index % columns) * CARD[0], (index // columns) * CARD[1])
        atlas.alpha_composite(card(notice["label"], notice["icon"], font), position)
    out = TEXTURES / SETS[listing]
    atlas.save(out, optimize=True)
    print(f"notice-atlas: {len(notices)} cards, {atlas.size[0]}x{atlas.size[1]} -> {out}")


def main() -> int:
    wanted = sys.argv[1:] or list(SETS)
    for listing in wanted:
        if listing not in SETS:
            raise SystemExit(f"notice-atlas: no set is listed in {listing!r}; there are {', '.join(SETS)}")
        build(listing)
    return 0


if __name__ == "__main__":
    sys.exit(main())
