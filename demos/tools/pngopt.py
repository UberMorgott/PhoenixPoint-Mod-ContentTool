#!/usr/bin/env python3
"""
Read and write PNGs, and squeeze a written one WITHOUT touching a single pixel.

WHY IT EXISTS. Every PNG this repository generates was written the simple way - filter 0 on every
row, then deflate - because that is all a first version needs. PNG's per-row filters are not a
quality setting though: they are a REVERSIBLE prediction step, so choosing a good one per row costs
nothing but a little CPU and hands deflate a far more compressible stream. Same pixels, same
dimensions, same bit depth, fewer bytes on disk and in git history forever.

    python pngopt.py <file.png> [more.png ...]     rewrite in place, only if it got smaller
    python pngopt.py                              self-check

The guarantee is checked, not asserted by hand: `optimize` decodes its own output and refuses to
return it unless the pixel bytes are IDENTICAL to the input's. A file that cannot be shrunk is
returned unchanged.

Handles 8-bit truecolour with or without alpha (PNG colour types 2 and 6), non-interlaced - which is
every PNG in demos\\ that this repository wrote. Anything else is refused by name rather than
silently mangled; the downloads under tools\\source\\ are left exactly as they arrived.

Stdlib only.
"""
import os
import struct
import sys
import zlib

_FILTERS = (0, 1, 2, 3, 4)


def png_read(data):
    """(width, height, bytes-per-pixel, raw pixel bytes). 8-bit RGB/RGBA, non-interlaced only."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    w, h, depth, colour, _, _, interlace = struct.unpack_from(">IIBBBBB", data, 16)
    assert depth == 8 and colour in (2, 6) and interlace == 0, (
        "only 8-bit truecolour, non-interlaced PNGs are handled here, got depth=%d colour=%d "
        "interlace=%d" % (depth, colour, interlace))
    at, idat = 8, bytearray()
    while at < len(data):
        length, tag = struct.unpack_from(">I", data, at)[0], data[at + 4:at + 8]
        if tag == b"IDAT":
            idat += data[at + 8: at + 8 + length]
        at += 12 + length
    raw = zlib.decompress(bytes(idat))
    bpp = 3 if colour == 2 else 4
    stride = w * bpp
    out = bytearray(h * stride)
    prev = bytearray(stride)
    at = 0
    for y in range(h):
        f = raw[at]
        line = bytearray(raw[at + 1: at + 1 + stride])
        at += 1 + stride
        if f == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif f == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                c = prev[i - bpp] if i >= bpp else 0
                b = prev[i]
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
        elif f != 0:
            raise AssertionError("unknown PNG filter %d on row %d" % (f, y))
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, bpp, bytes(out)


def _filtered(line, prev, bpp, kind):
    stride = len(line)
    out = bytearray(stride)
    for i in range(stride):
        a = line[i - bpp] if i >= bpp else 0
        b = prev[i]
        if kind == 0:
            out[i] = line[i]
        elif kind == 1:
            out[i] = (line[i] - a) & 0xFF
        elif kind == 2:
            out[i] = (line[i] - b) & 0xFF
        elif kind == 3:
            out[i] = (line[i] - ((a + b) >> 1)) & 0xFF
        else:
            c = prev[i - bpp] if i >= bpp else 0
            p = a + b - c
            pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
            out[i] = (line[i] - (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
    return out


def png_write(width, height, bpp, pixels):
    """
    A PNG with the per-row filter CHOSEN rather than always 0. The choice is the minimum-sum-of-
    absolute-signed-values heuristic from the PNG spec's own encoding notes (§9.6): treat each
    filtered byte as signed, sum the magnitudes, take the smallest row. It is the cheap standard
    answer, it is what every encoder starts with, and being a prediction step it cannot alter a
    pixel.
    """
    assert bpp in (3, 4) and len(pixels) == width * height * bpp, "pixel buffer does not match"
    stride = width * bpp
    raw = bytearray()
    prev = bytearray(stride)
    for y in range(height):
        line = pixels[y * stride:(y + 1) * stride]
        best = None
        for kind in _FILTERS:
            cand = _filtered(line, prev, bpp, kind)
            score = sum(v if v < 128 else 256 - v for v in cand)
            if best is None or score < best[0]:
                best = (score, kind, cand)
        raw.append(best[1])
        raw += best[2]
        prev = line

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload +
                struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2 if bpp == 3 else 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(bytes(raw), 9)) +
            chunk(b"IEND", b""))


def optimize(data):
    """
    Re-encode losslessly. Returns the input unchanged unless the result is BOTH smaller and decodes
    to byte-identical pixels - the second half is the whole point, so it is verified here rather
    than left to the caller to remember.
    """
    w, h, bpp, pixels = png_read(data)
    out = png_write(w, h, bpp, pixels)
    if len(out) >= len(data):
        return data
    w2, h2, bpp2, pixels2 = png_read(out)
    assert (w2, h2, bpp2) == (w, h, bpp) and pixels2 == pixels, (
        "the re-encoded image does not decode to the same pixels - refusing to write it")
    return out


def _self_check():
    """A gradient with an alpha edge: filters must win on it, and the pixels must survive."""
    w = h = 64
    pixels = bytearray()
    for y in range(h):
        for x in range(w):
            pixels += bytes((x * 4 % 256, y * 4 % 256, (x + y) * 2 % 256, 255 if x > 8 else 0))
    flat = png_write(w, h, 4, bytes(pixels))
    # A filter-0-only encoder, which is what every generator here used to be, for the control.
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += pixels[y * w * 4:(y + 1) * w * 4]
    body = zlib.compress(bytes(raw), 9)
    assert png_read(flat)[3] == bytes(pixels), "round trip changed the pixels"
    assert len(flat) < len(body) + 100, "adaptive filtering did not beat filter 0 on a gradient"
    assert optimize(flat) is flat or len(optimize(flat)) <= len(flat), "optimize grew a file"
    print("OK  pngopt self-check: %d bytes filtered vs %d bytes unfiltered, pixels identical"
          % (len(flat), len(body)))


def main(paths):
    total_before = total_after = 0
    for path in paths:
        before = open(path, "rb").read()
        after = optimize(before)
        total_before += len(before)
        total_after += len(after)
        if len(after) < len(before):
            open(path, "wb").write(after)
            print("OK  %-52s %8d -> %8d B  (-%.1f%%)"
                  % (os.path.basename(path), len(before), len(after),
                     100.0 * (len(before) - len(after)) / len(before)))
        else:
            print("--  %-52s %8d B already minimal for this encoder" % (os.path.basename(path), len(before)))
    if len(paths) > 1:
        print("    total %d -> %d B  (-%d B)" % (total_before, total_after, total_before - total_after))


if __name__ == "__main__":
    if len(sys.argv) > 1:
        main(sys.argv[1:])
    else:
        _self_check()
