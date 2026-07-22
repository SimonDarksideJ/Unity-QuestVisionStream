#!/usr/bin/env python3
"""Generate printable AprilTag markers for the QuestVisionStream test set.

Reads the SAME tag registry the clients use
(`quest-client/src/apriltags/tag-registry.json` — the Unity client's
TagRegistryAsset mirrors it), so a printed tag's id/name always matches the
colour + label the headset shows for it — one source of truth.

Each tag is rendered with a white quiet-zone border (required for reliable
detection) and a caption (`#id  Name  (family)`), saved as an individual PNG
plus a combined contact sheet for easy printing.

Families:
  41h12 (default) — tagStandard41h12, decoded by the Unity client's Keijiro
         AprilTag module. OpenCV cannot generate this family; the official
         pre-rendered bitmaps are fetched from the AprilRobotics/apriltag-imgs
         repository (or use --apriltag-imgs to point at a local checkout) and
         upscaled losslessly.
  36h11 — the legacy family the retired WebXR client decoded (js-aruco2);
         generated locally via OpenCV. Only needed for old printed sheets.

THE PRINTED FAMILY MUST MATCH THE CLIENT'S DECODER. For the V2 Unity client
that is tagStandard41h12.

Run via `generate-apriltags.sh` (uses the server venv's OpenCV), or directly
with a Python that has `opencv-contrib-python`.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.request

import cv2
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REGISTRY = os.path.normpath(
    os.path.join(HERE, "..", "quest-client", "src", "apriltags", "tag-registry.json")
)
DEFAULT_OUT = os.path.join(HERE, "apriltags")

APRILTAG_IMGS_RAW = (
    "https://raw.githubusercontent.com/AprilRobotics/apriltag-imgs/master/"
    "tagStandard41h12/tag41_12_{tag_id:05d}.png"
)

FAMILY_LABELS = {"41h12": "tagStandard41h12", "36h11": "36h11"}


def marker_bitmap_41h12(tag_id: int, tag_px: int, imgs_dir: str | None) -> np.ndarray:
    """The official tagStandard41h12 bitmap, upscaled to ~tag_px with hard pixels."""
    if imgs_dir:
        path = os.path.join(imgs_dir, "tagStandard41h12", f"tag41_12_{tag_id:05d}.png")
        small = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
        if small is None:
            raise FileNotFoundError(f"Missing {path} — check --apriltag-imgs")
    else:
        url = APRILTAG_IMGS_RAW.format(tag_id=tag_id)
        with urllib.request.urlopen(url) as response:
            data = np.frombuffer(response.read(), np.uint8)
        small = cv2.imdecode(data, cv2.IMREAD_GRAYSCALE)
        if small is None:
            raise RuntimeError(f"Could not decode {url}")

    scale = max(1, tag_px // small.shape[0])
    return cv2.resize(small, None, fx=scale, fy=scale, interpolation=cv2.INTER_NEAREST)


def marker_bitmap_36h11(dictionary, tag_id: int, tag_px: int) -> np.ndarray:
    return cv2.aruco.generateImageMarker(dictionary, tag_id, tag_px)


def render_tag(marker: np.ndarray, tag_id: int, name: str, family: str, quiet_frac: float,
               class_name: str = ""):
    """A single tag: white quiet zone around the marker + a caption below."""
    tag_px = marker.shape[0]
    quiet_px = max(8, round(tag_px * quiet_frac))
    label_px = max(40, round(tag_px * 0.18))
    side = tag_px + 2 * quiet_px
    canvas = np.full((side + label_px, side), 255, np.uint8)
    canvas[quiet_px:quiet_px + tag_px, quiet_px:quiet_px + tag_px] = marker

    # When the registry maps this tag to a detection class, print it — the
    # headset registry (TagRegistryAsset.ClassName) and the printed sheet must
    # tell the same story about what the tag stands for.
    class_suffix = f"  [{class_name}]" if class_name and class_name != name else ""
    text = f"#{tag_id}  {name}{class_suffix}   ({FAMILY_LABELS[family]})"
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
    ap = argparse.ArgumentParser(description="Generate printable AprilTag markers.")
    ap.add_argument("--registry", default=DEFAULT_REGISTRY, help="tag-registry.json path")
    ap.add_argument("--out", default=DEFAULT_OUT, help="output directory")
    ap.add_argument("--family", choices=("41h12", "36h11"), default="41h12",
                    help="tag family — 41h12 for the Unity client (default), 36h11 legacy")
    ap.add_argument("--apriltag-imgs", default=None,
                    help="local AprilRobotics/apriltag-imgs checkout (otherwise fetched from GitHub)")
    ap.add_argument("--tag-px", type=int, default=600, help="approx marker size in px")
    ap.add_argument("--quiet", type=float, default=0.25, help="quiet-zone as a fraction of tag size")
    ap.add_argument("--cols", type=int, default=2, help="columns in the contact sheet")
    args = ap.parse_args()

    with open(args.registry, encoding="utf-8") as fh:
        registry = json.load(fh)
    tags = registry.get("tags", [])
    if not tags:
        print(f"No tags in {args.registry}", file=sys.stderr)
        return 1

    dictionary = None
    if args.family == "36h11":
        dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_APRILTAG_36h11)

    out_dir = os.path.join(args.out, args.family)
    os.makedirs(out_dir, exist_ok=True)
    tiles = []
    print(f"Generating {len(tags)} AprilTag {FAMILY_LABELS[args.family]} markers → {out_dir}")
    for tag in tags:
        tid, name = int(tag["id"]), str(tag.get("name", f"Tag {tag['id']}"))
        class_name = str(tag.get("className", ""))
        if args.family == "41h12":
            marker = marker_bitmap_41h12(tid, args.tag_px, args.apriltag_imgs)
        else:
            marker = marker_bitmap_36h11(dictionary, tid, args.tag_px - (args.tag_px % 8))
        tile = render_tag(marker, tid, name, args.family, args.quiet, class_name)
        tiles.append(tile)
        path = os.path.join(out_dir, f"tag_{tid:02d}_{name.lower()}.png")
        cv2.imwrite(path, tile)
        print(f"  #{tid:<2} {name:<10} → {os.path.relpath(path, HERE)}")

    sheet_path = os.path.join(out_dir, "contact-sheet.png")
    cv2.imwrite(sheet_path, contact_sheet(tiles, args.cols))
    print(f"Contact sheet → {os.path.relpath(sheet_path, HERE)}")
    print(
        "\nPrint tip: at ~100 mm tag width and ~2 m viewing distance detection is "
        "reliable. Keep the white border — don't crop it off. The Unity client's "
        "tag-size setting (TagPlacement/KeijiroDetector profiles) must match the "
        "printed width for correct distances."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
