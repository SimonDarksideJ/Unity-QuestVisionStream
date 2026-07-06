#!/usr/bin/env python3
"""Generate printable AprilTag (36h11) markers for the QuestVisionStream test set.

Reads the SAME tag registry the WebXR client uses
(`quest-client/src/apriltags/tag-registry.json`), so a printed tag's id/name
always matches the colour + label the headset shows for it — one source of truth.

Each tag is rendered with a white quiet-zone border (required for reliable
detection) and a caption (`#id  Name  (36h11)`), saved as an individual PNG plus
a combined contact sheet for easy printing.

Family: AprilTag 36h11 — this MUST match the dictionary the client decodes with
(see src/apriltags/detector.ts / the vendored js-aruco2 dictionary).

Run via `generate-apriltags.sh` (uses the server venv's OpenCV), or directly with
a Python that has `opencv-contrib-python`.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import cv2
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REGISTRY = os.path.normpath(
    os.path.join(HERE, "..", "quest-client", "src", "apriltags", "tag-registry.json")
)
DEFAULT_OUT = os.path.join(HERE, "apriltags")
APRILTAG_DICT = cv2.aruco.DICT_APRILTAG_36h11


def render_tag(dictionary, tag_id: int, name: str, tag_px: int, quiet_px: int, label_px: int):
    """A single tag: white quiet zone around the marker + a caption below."""
    marker = cv2.aruco.generateImageMarker(dictionary, tag_id, tag_px)
    side = tag_px + 2 * quiet_px
    canvas = np.full((side + label_px, side), 255, np.uint8)
    canvas[quiet_px:quiet_px + tag_px, quiet_px:quiet_px + tag_px] = marker

    text = f"#{tag_id}  {name}   (36h11)"
    font = cv2.FONT_HERSHEY_SIMPLEX
    scale = tag_px / 500.0
    thickness = max(1, round(scale * 2))
    (tw, th), _ = cv2.getTextSize(text, font, scale, thickness)
    tx = max(4, (side - tw) // 2)
    ty = side + (label_px + th) // 2
    cv2.putText(canvas, text, (tx, ty), font, scale, 0, thickness, cv2.LINE_AA)
    return canvas


def contact_sheet(tiles, cols: int, gap: int = 40):
    """Lay the tag tiles out on a white grid (cols per row) for one-page printing."""
    h = max(t.shape[0] for t in tiles)
    w = max(t.shape[1] for t in tiles)
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.full((rows * h + (rows + 1) * gap, cols * w + (cols + 1) * gap), 255, np.uint8)
    for i, tile in enumerate(tiles):
        r, c = divmod(i, cols)
        y = gap + r * (h + gap)
        x = gap + c * (w + gap)
        sheet[y:y + tile.shape[0], x:x + tile.shape[1]] = tile
    return sheet


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate printable AprilTag 36h11 markers.")
    ap.add_argument("--registry", default=DEFAULT_REGISTRY, help="tag-registry.json path")
    ap.add_argument("--out", default=DEFAULT_OUT, help="output directory")
    ap.add_argument("--tag-px", type=int, default=600, help="marker size in px (multiple of 8)")
    ap.add_argument("--quiet", type=float, default=0.25, help="quiet-zone as a fraction of tag size")
    ap.add_argument("--cols", type=int, default=2, help="columns in the contact sheet")
    args = ap.parse_args()

    with open(args.registry, encoding="utf-8") as fh:
        registry = json.load(fh)
    tags = registry.get("tags", [])
    if not tags:
        print(f"No tags in {args.registry}", file=sys.stderr)
        return 1

    dictionary = cv2.aruco.getPredefinedDictionary(APRILTAG_DICT)
    tag_px = args.tag_px - (args.tag_px % 8)
    quiet_px = max(8, round(tag_px * args.quiet))
    label_px = max(40, round(tag_px * 0.18))

    os.makedirs(args.out, exist_ok=True)
    tiles = []
    print(f"Generating {len(tags)} AprilTag 36h11 markers → {args.out}")
    for tag in tags:
        tid, name = int(tag["id"]), str(tag.get("name", f"Tag {tag['id']}"))
        tile = render_tag(dictionary, tid, name, tag_px, quiet_px, label_px)
        tiles.append(tile)
        path = os.path.join(args.out, f"tag_{tid:02d}_{name.lower()}.png")
        cv2.imwrite(path, tile)
        print(f"  #{tid:<2} {name:<10} → {os.path.relpath(path, HERE)}")

    sheet_path = os.path.join(args.out, "contact-sheet.png")
    cv2.imwrite(sheet_path, contact_sheet(tiles, args.cols))
    print(f"Contact sheet → {os.path.relpath(sheet_path, HERE)}")
    print(
        "\nPrint tip: at ~100 mm tag width and ~2 m viewing distance detection is "
        "reliable. Keep the white border — don't crop it off."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
