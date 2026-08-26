#!/usr/bin/env python3
"""extract_aseprite_frames.py — Export every frame of an .aseprite file to PNG.

Why this exists
---------------
Aseprite is where the tile art is authored, and a single .aseprite document
routinely holds several frames of the same template (a blob island drawn with
three different shore treatments, say). Those frames are the pack's variants.
Getting them out normally means opening the GUI and exporting by hand, which is
how variants end up half-exported: `Tiles/new/` arrived with `dirt_sand1.png`
and `grass_sand1.png` as 347-byte fully transparent files -- frame 0 of a
document whose real content sits in frames 1..3 -- while the three sand/ocean
frames that WERE complete had never been exported at all.

Reading the frames directly removes that failure mode: whatever the document
holds, the pipeline sees.

Scope
-----
Deliberately a minimal reader, not an Aseprite clone. It supports what Valkur's
tile sources actually are:

  * 32-bit RGBA colour depth (`depth == 32`)
  * raw (celtype 0) and zlib-compressed (celtype 2) image cels
  * linked cels (celtype 1), which reference an earlier frame's cel
  * multiple layers, composited bottom-to-top with normal-blend alpha-over,
    honouring per-layer and per-cel opacity and the layer visibility flag

Indexed and grayscale documents, tilemap cels, blend modes other than normal,
and group layers are rejected loudly rather than mis-rendered -- a silently
wrong composite is worse than a refusal, because it slices cleanly and only
shows up as wrong colour in game.

Fully transparent frames are skipped by default (`--skip-empty`, on unless
`--keep-empty` is passed): an empty frame is a scratch frame, and exporting it
puts a blank tile into a pack that the auto-tile analyser then has to explain.

Usage:
  python tools/atlas/extract_aseprite_frames.py --source path/to/sheet.aseprite --list
  python tools/atlas/extract_aseprite_frames.py --source path/to/sheet.aseprite \\
      --out-dir some/dir --name-pattern "sand_ocean_v{n}" --execute
"""

from __future__ import annotations

import argparse
import struct
import sys
import zlib
from pathlib import Path

try:
    from PIL import Image
except ImportError:  # pragma: no cover - environment guard
    sys.exit("Pillow required:  pip install Pillow")

REPO_ROOT = Path(__file__).resolve().parents[2]

ASEPRITE_MAGIC = 0xA5E0
FRAME_MAGIC = 0xF1FA

CHUNK_LAYER = 0x2004
CHUNK_CEL = 0x2005

LAYER_TYPE_NORMAL = 0
LAYER_TYPE_GROUP = 1
LAYER_TYPE_TILEMAP = 2
LAYER_FLAG_VISIBLE = 1
BLEND_NORMAL = 0

CEL_RAW = 0
CEL_LINKED = 1
CEL_COMPRESSED = 2
CEL_COMPRESSED_TILEMAP = 3


class AsepriteError(RuntimeError):
    pass


def _read_header(data: bytes):
    if len(data) < 128:
        raise AsepriteError("file is shorter than the 128-byte header")
    file_size, magic, frames, width, height, depth = struct.unpack("<IHHHHH", data[:14])
    if magic != ASEPRITE_MAGIC:
        raise AsepriteError(f"bad magic 0x{magic:04x} (expected 0x{ASEPRITE_MAGIC:04x})")
    if depth != 32:
        raise AsepriteError(
            f"colour depth is {depth}-bit; only 32-bit RGBA is supported. "
            "Convert the document to RGB colour mode in Aseprite and re-save.")
    return frames, width, height


def _iter_chunks(data: bytes, offset: int):
    """Yield (chunk_type, payload_bytes) for one frame, and the next frame offset."""
    frame_bytes, frame_magic, old_count, _duration, _pad, new_count = struct.unpack(
        "<IHHHIH", data[offset:offset + 16])
    if frame_magic != FRAME_MAGIC:
        raise AsepriteError(f"bad frame magic 0x{frame_magic:04x} at offset {offset}")
    count = new_count if new_count else old_count
    pos = offset + 16
    chunks = []
    for _ in range(count):
        size, ctype = struct.unpack("<IH", data[pos:pos + 6])
        if size < 6:
            raise AsepriteError(f"chunk size {size} at offset {pos} is impossible")
        chunks.append((ctype, data[pos + 6:pos + size]))
        pos += size
    return chunks, offset + frame_bytes


def _parse_layer(payload: bytes):
    flags, layer_type, _child, _w, _h, blend, opacity = struct.unpack("<HHHHHHB", payload[:13])
    name_len = struct.unpack("<H", payload[16:18])[0]
    name = payload[18:18 + name_len].decode("utf-8", "replace")
    return {
        "visible": bool(flags & LAYER_FLAG_VISIBLE),
        "type": layer_type,
        "blend": blend,
        "opacity": opacity,
        "name": name,
    }


def _parse_cel(payload: bytes):
    layer_index, x, y, opacity, cel_type = struct.unpack("<HhhBH", payload[:9])
    cel = {"layer": layer_index, "x": x, "y": y, "opacity": opacity, "type": cel_type}
    body = payload[16:]
    if cel_type == CEL_RAW:
        w, h = struct.unpack("<HH", body[:4])
        cel["size"] = (w, h)
        cel["pixels"] = body[4:4 + w * h * 4]
    elif cel_type == CEL_LINKED:
        cel["link_frame"] = struct.unpack("<H", body[:2])[0]
    elif cel_type == CEL_COMPRESSED:
        w, h = struct.unpack("<HH", body[:4])
        cel["size"] = (w, h)
        cel["pixels"] = zlib.decompress(body[4:])
    elif cel_type == CEL_COMPRESSED_TILEMAP:
        raise AsepriteError(
            "tilemap cels are not supported. Flatten the tilemap layer to a normal "
            "layer in Aseprite before exporting.")
    else:
        raise AsepriteError(f"unknown cel type {cel_type}")
    return cel


def _composite(frame_cels, layers, width, height):
    """Alpha-over the frame's cels bottom layer first. Returns an RGBA Image."""
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for layer_index in sorted(frame_cels):
        layer = layers[layer_index]
        if not layer["visible"]:
            continue
        cel = frame_cels[layer_index]
        w, h = cel["size"]
        cel_img = Image.frombytes("RGBA", (w, h), cel["pixels"])

        # Per-layer x per-cel opacity, both 0..255. Scaling the alpha channel is
        # the whole of normal-blend opacity; any other blend mode was rejected
        # while parsing the layer chunks.
        factor = (layer["opacity"] / 255.0) * (cel["opacity"] / 255.0)
        if factor < 1.0:
            alpha = cel_img.getchannel("A").point(lambda a, f=factor: int(a * f))
            cel_img.putalpha(alpha)

        canvas.alpha_composite(cel_img, dest=(cel["x"], cel["y"]))
    return canvas


def read_frames(path: Path):
    """Return (width, height, [RGBA Image per frame]) for an .aseprite file."""
    data = path.read_bytes()
    frame_count, width, height = _read_header(data)

    layers = []
    # Aseprite writes cel chunks per frame; a linked cel points at the frame
    # whose cel it reuses, so keep every frame's cels around while parsing.
    cels_by_frame = []

    offset = 128
    for frame_index in range(frame_count):
        chunks, offset = _iter_chunks(data, offset)
        frame_cels = {}
        for ctype, payload in chunks:
            if ctype == CHUNK_LAYER:
                layer = _parse_layer(payload)
                if layer["type"] == LAYER_TYPE_GROUP:
                    raise AsepriteError(
                        f"layer '{layer['name']}' is a group. Flatten groups before exporting.")
                if layer["type"] == LAYER_TYPE_TILEMAP:
                    raise AsepriteError(
                        f"layer '{layer['name']}' is a tilemap layer, which is not supported.")
                if layer["blend"] != BLEND_NORMAL:
                    raise AsepriteError(
                        f"layer '{layer['name']}' uses blend mode {layer['blend']}; only Normal "
                        "is supported. A wrong composite slices cleanly and only shows up as "
                        "wrong colour in game, so this refuses rather than guesses.")
                layers.append(layer)
            elif ctype == CHUNK_CEL:
                cel = _parse_cel(payload)
                if cel["type"] == CEL_LINKED:
                    src = cels_by_frame[cel["link_frame"]].get(cel["layer"])
                    if src is None:
                        raise AsepriteError(
                            f"frame {frame_index} links to a cel on layer {cel['layer']} of frame "
                            f"{cel['link_frame']}, which has none.")
                    cel = dict(src, opacity=cel["opacity"])
                frame_cels[cel["layer"]] = cel
        cels_by_frame.append(frame_cels)

    images = [_composite(fc, layers, width, height) for fc in cels_by_frame]
    return width, height, images


def main():
    ap = argparse.ArgumentParser(description="Export every frame of an .aseprite file to PNG.")
    ap.add_argument("--source", required=True, help="Path to the .aseprite file.")
    ap.add_argument("--out-dir", help="Directory to write the PNGs into (required unless --list).")
    ap.add_argument("--name-pattern", default="{stem}_f{n}",
                    help="Output name without extension. '{stem}' is the source file name, "
                         "'{n}' the 1-based index among EXPORTED frames (default: '{stem}_f{n}').")
    ap.add_argument("--names",
                    help="Comma-separated explicit output names, one per EXPORTED frame, used "
                         "instead of --name-pattern. Variants of a pack want describing "
                         "('sand_ocean_surf'), not numbering: tools/atlas/audit_asset_conventions.py "
                         "rejects a '_v1' suffix as an iteration marker, and rightly so -- these "
                         "are siblings, not revisions of each other.")
    ap.add_argument("--keep-empty", action="store_true",
                    help="Also export fully transparent frames (skipped by default).")
    group = ap.add_mutually_exclusive_group(required=True)
    group.add_argument("--list", action="store_true", help="Report the frames without writing.")
    group.add_argument("--execute", action="store_true", help="Write the PNGs.")
    args = ap.parse_args()

    source = Path(args.source)
    if not source.is_absolute():
        source = (REPO_ROOT / source).resolve()
    if not source.exists():
        print(f"ERROR: source not found: {source}", file=sys.stderr)
        return 1

    try:
        width, height, images = read_frames(source)
    except (AsepriteError, struct.error, zlib.error) as exc:
        print(f"ERROR: could not read {source.name}: {exc}", file=sys.stderr)
        return 2

    print("=" * 70)
    print("  ASEPRITE FRAME EXPORT " + ("(LIST)" if args.list else "(EXECUTE)"))
    print("=" * 70)
    print(f"  Source: {source}")
    print(f"  Canvas: {width}x{height}   frames: {len(images)}")
    print()

    explicit_names = None
    if args.names:
        explicit_names = [n.strip() for n in args.names.split(",") if n.strip()]

    exported = 0
    for index, img in enumerate(images):
        opaque = img.getchannel("A").getextrema()[1] > 0
        if not opaque and not args.keep_empty:
            print(f"  frame {index}: fully transparent -- skipped")
            continue
        exported += 1
        if explicit_names is not None:
            if exported > len(explicit_names):
                print(f"ERROR: --names lists {len(explicit_names)} name(s) but frame {index} is "
                      f"export #{exported}. Add a name or pass --keep-empty consistently.",
                      file=sys.stderr)
                return 1
            name = explicit_names[exported - 1]
        else:
            name = args.name_pattern.format(stem=source.stem, n=exported, i=index)
        print(f"  frame {index}: -> {name}.png")
        if args.execute:
            out_dir = Path(args.out_dir) if args.out_dir else None
            if out_dir is None:
                print("ERROR: --out-dir is required with --execute", file=sys.stderr)
                return 1
            if not out_dir.is_absolute():
                out_dir = (REPO_ROOT / out_dir).resolve()
            out_dir.mkdir(parents=True, exist_ok=True)
            img.save(out_dir / f"{name}.png", "PNG")

    print()
    if explicit_names is not None and exported != len(explicit_names):
        print(f"ERROR: --names lists {len(explicit_names)} name(s) but {exported} frame(s) were "
              f"exported. Nothing partial should ship, so this is an error, not a warning.",
              file=sys.stderr)
        return 1

    print(f"  {exported} frame(s) {'written' if args.execute else 'would be written'}.")
    if not args.execute:
        print("  Re-run with --execute (and --out-dir) to write them.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
