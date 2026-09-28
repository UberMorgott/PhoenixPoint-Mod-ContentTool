"""annotate-shots.py - draw the numbered callouts on the wiki's bench screenshots.

    python tools/annotate-shots.py            re-annotate every image listed in the JSON
    python tools/annotate-shots.py sounds     only images whose name contains 'sounds'

Reads   docs/images/bench/src/<name>.png   (clean 1280x720 shots, never edited by hand)
        tools/annotate-shots.json          (per image: callouts)
Writes  docs/images/bench/<name>.png       (annotated, palette-quantized)

A callout = a box around a UI element plus a numbered circle beside it. The page text refers to
the numbers, so keep them in the order the text reads. When the UI changes: re-shoot into src/,
fix the rectangles, run this again.
Needs Pillow.
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "images" / "bench" / "src"
OUT = ROOT / "docs" / "images" / "bench"
SPEC = Path(__file__).resolve().with_suffix(".json")

ACCENT = (255, 106, 0)
SHADOW = (0, 0, 0)
TEXT = (255, 255, 255)
RADIUS = 12
BOX = 3


def font(size):
    for name in ("arialbd.ttf", "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def circle_centre(rect, at, size):
    x0, y0, x1, y1 = rect
    w, h = size
    r = RADIUS + 3
    if isinstance(at, list):
        return tuple(at)
    cy = (y0 + y1) // 2
    cx = (x0 + x1) // 2
    spots = {
        "l": (x0 - r, cy), "r": (x1 + r, cy), "t": (cx, y0 - r), "b": (cx, y1 + r),
        "tl": (x0 + r - 2, y0 + r - 2), "tr": (x1 - r + 2, y0 + r - 2),
        "bl": (x0 + r - 2, y1 - r + 2), "br": (x1 - r + 2, y1 - r + 2),
    }
    x, y = spots[at]
    return (min(max(x, r), w - r), min(max(y, r), h - r))


def edge_point(rect, p):
    """The point on the box outline nearest to p - where a leader line from a far circle ends."""
    x0, y0, x1, y1 = rect
    return (min(max(p[0], x0), x1), min(max(p[1], y0), y1))


def annotate(name, spec, num_font):
    img = Image.open(SRC / name).convert("RGB")
    d = ImageDraw.Draw(img)
    callouts = spec.get("callouts", [])
    for c in callouts:
        x0, y0, x1, y1 = c["rect"]
        d.rounded_rectangle((x0 - 1, y0 - 1, x1 + 1, y1 + 1), radius=5, outline=SHADOW, width=BOX + 2)
        d.rounded_rectangle((x0, y0, x1, y1), radius=4, outline=ACCENT, width=BOX)
    for c in callouts:
        centre = circle_centre(c["rect"], c.get("at", "l"), img.size)
        end = edge_point(c["rect"], centre)
        if abs(end[0] - centre[0]) + abs(end[1] - centre[1]) > RADIUS + 6:
            d.line((centre, end), fill=SHADOW, width=5)
            d.line((centre, end), fill=ACCENT, width=3)
        x, y = centre
        d.ellipse((x - RADIUS - 2, y - RADIUS - 2, x + RADIUS + 2, y + RADIUS + 2), fill=SHADOW)
        d.ellipse((x - RADIUS, y - RADIUS, x + RADIUS, y + RADIUS), fill=ACCENT)
        d.text((x, y), str(c["n"]), font=num_font, fill=TEXT, anchor="mm")
    out = img.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    out.save(OUT / name, optimize=True)
    return OUT / name


def main(argv):
    spec = json.loads(SPEC.read_text(encoding="utf-8"))
    num_font = font(15)
    wanted = argv[1] if len(argv) > 1 else ""
    for name, item in spec["images"].items():
        if wanted not in name:
            continue
        path = annotate(name, item, num_font)
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size} B  {len(item.get('callouts', []))} callout(s)")


if __name__ == "__main__":
    main(sys.argv)
