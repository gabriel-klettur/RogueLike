"""Patch Unity AudioCatalog .asset YAML files with BPM/key data.

Reads ``tools/cache/audio/music_analysis.json`` (output of
``analyze_music.py``), locates every ``AudioCatalog.asset`` under the
Unity project, resolves each track's clip GUID → AudioClip filename via
the corresponding ``.mp3.meta`` file, and rewrites the entry in-place
adding/overwriting ``bpm``, ``firstBeatOffsetSec``, ``key`` and
``keyConfidence`` fields.

Idempotent — safe to re-run.

Usage (repo root, venv active)::

    python tools/audio/patch_audio_catalog_bpm.py
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Iterable

REPO_ROOT      = Path(__file__).resolve().parents[2]
ANALYSIS_JSON  = REPO_ROOT / "tools" / "cache" / "audio" / "music_analysis.json"
UNITY_ASSETS   = REPO_ROOT / "unity" / "Valkur" / "Assets"
MUSIC_DIR      = UNITY_ASSETS / "_Project" / "Audio" / "Music"

_GUID_RE       = re.compile(r"^guid:\s*([0-9a-fA-F]{32})", re.MULTILINE)
_CLIP_LINE_RE  = re.compile(
    r"^(?P<indent>\s*)clip:\s*\{[^}]*guid:\s*(?P<guid>[0-9a-fA-F]{32})[^}]*\}\s*$",
    re.MULTILINE,
)
_FIELD_PATTERNS = {
    "bpm":               re.compile(r"^\s*bpm:\s*[^\n]*\n", re.MULTILINE),
    "beatsPerBar":       re.compile(r"^\s*beatsPerBar:\s*[^\n]*\n", re.MULTILINE),
    "firstBeatOffsetSec": re.compile(r"^\s*firstBeatOffsetSec:\s*[^\n]*\n", re.MULTILINE),
    "key":               re.compile(r"^\s*key:\s*[^\n]*\n", re.MULTILINE),
    "keyConfidence":     re.compile(r"^\s*keyConfidence:\s*[^\n]*\n", re.MULTILINE),
}
_NEXT_ENTRY_RE = re.compile(r"^\s*-\s*id:\s", re.MULTILINE)
_END_TRACKS_RE = re.compile(r"^[A-Za-z_][\w]*:\s", re.MULTILINE)


def build_guid_to_stem() -> dict[str, str]:
    """Return ``{guid -> filename_stem}`` for every audio clip under MUSIC_DIR."""
    table: dict[str, str] = {}
    if not MUSIC_DIR.exists():
        return table
    for meta in MUSIC_DIR.rglob("*.meta"):
        try:
            text = meta.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        m = _GUID_RE.search(text)
        if not m:
            continue
        # AudioClip stem (drop .mp3.meta -> drop one suffix to .mp3, then stem)
        clip_path = meta.with_suffix("")  # removes .meta
        if not clip_path.exists():
            continue
        table[m.group(1).lower()] = clip_path.stem
    return table


def find_catalog_assets() -> Iterable[Path]:
    for asset in UNITY_ASSETS.rglob("AudioCatalog*.asset"):
        try:
            head = asset.read_text(encoding="utf-8", errors="ignore")[:4096]
        except OSError:
            continue
        # Heuristic: AudioCatalogSO assets have a "tracks:" YAML key.
        if "tracks:" in head:
            yield asset


def patch_entry(block: str, indent: str, data: dict) -> str:
    """Insert/overwrite bpm/key fields inside a single track entry block."""
    field_indent = indent  # same as `clip:` indent — siblings of `id:`/`title:`/`clip:`
    new_lines = []
    for field, fmt in (
        ("bpm",                f"{data.get('bpm', 0):.2f}"),
        ("firstBeatOffsetSec", f"{data.get('first_beat_offset_sec', 0):.3f}"),
        ("key",                f"{data.get('key', '')}"),
        ("keyConfidence",      f"{data.get('key_confidence', 0):.3f}"),
    ):
        new_lines.append(f"{field_indent}{field}: {fmt}\n")
    insert_text = "".join(new_lines)

    # Drop any existing copies of these fields first.
    for key, pattern in _FIELD_PATTERNS.items():
        if key == "beatsPerBar":
            continue  # never touch — user-controlled
        block = pattern.sub("", block)

    # Append new fields right after the last existing line of the entry.
    if not block.endswith("\n"):
        block += "\n"
    return block + insert_text


def split_track_entries(yaml_text: str) -> tuple[str, list[tuple[str, str]], str]:
    """Return (prefix, [(entry_text, guid), ...], suffix).

    Each entry_text starts at its leading ``- id:`` line and ends just
    before the next ``- id:`` line (or before the next sibling YAML key).
    """
    # Locate the tracks: block (allowing arbitrary leading indent).
    m = re.search(r"^(?P<ind> *)tracks:\s*\n", yaml_text, re.MULTILINE)
    if not m:
        return yaml_text, [], ""
    tracks_start = m.end()
    tracks_indent = len(m.group("ind"))

    # End of tracks block: next non-blank line whose indent <= tracks_indent.
    rest = yaml_text[tracks_start:]
    end_offset = len(rest)
    line_pos = 0
    for line in rest.splitlines(keepends=True):
        stripped = line.lstrip(" ")
        line_indent = len(line) - len(stripped)
        if stripped.strip() and line_indent <= tracks_indent and not stripped.startswith("- "):
            end_offset = line_pos
            break
        line_pos += len(line)
    tracks_end = tracks_start + end_offset

    prefix      = yaml_text[:tracks_start]
    tracks_body = yaml_text[tracks_start:tracks_end]
    suffix      = yaml_text[tracks_end:]

    # Split by entries (lines starting with `- id:` at any indent).
    entry_starts = [m.start() for m in re.finditer(r"^ *-\s+id:\s", tracks_body, re.MULTILINE)]
    entries: list[tuple[str, str]] = []
    for i, start in enumerate(entry_starts):
        end = entry_starts[i + 1] if i + 1 < len(entry_starts) else len(tracks_body)
        block = tracks_body[start:end]
        clip_m = _CLIP_LINE_RE.search(block)
        guid = clip_m.group("guid").lower() if clip_m else ""
        entries.append((block, guid))
    return prefix, entries, suffix


def patch_catalog(asset_path: Path, analysis: dict, guid_to_stem: dict[str, str]) -> tuple[int, int]:
    text = asset_path.read_text(encoding="utf-8")
    prefix, entries, suffix = split_track_entries(text)
    if not entries:
        return 0, 0

    updated = 0
    skipped = 0
    new_entries: list[str] = []
    for block, guid in entries:
        stem = guid_to_stem.get(guid)
        if stem is None:
            new_entries.append(block)
            skipped += 1
            continue
        data = analysis.get(stem)
        if data is None:
            # Try case-insensitive fallback.
            for k, v in analysis.items():
                if k.lower() == stem.lower():
                    data = v
                    break
        if data is None:
            new_entries.append(block)
            skipped += 1
            continue

        # Determine indent of the `id:` line.
        id_m = re.search(r"^(\s*-\s+)id:", block)
        # Sibling fields are indented to match `id:` content (after `- `).
        indent = " " * len(id_m.group(1)) if id_m else "    "
        new_entries.append(patch_entry(block, indent, data))
        updated += 1

    new_text = prefix + "".join(new_entries) + suffix
    if new_text != text:
        asset_path.write_text(new_text, encoding="utf-8", newline="\n")
    return updated, skipped


def main(argv: list[str] | None = None) -> int:
    if not ANALYSIS_JSON.exists():
        print(f"[patch] Missing {ANALYSIS_JSON}. Run analyze_music.py first.", file=sys.stderr)
        return 1
    analysis_doc = json.loads(ANALYSIS_JSON.read_text(encoding="utf-8"))
    analysis = analysis_doc.get("tracks", {})
    if not analysis:
        print("[patch] Analysis JSON has no 'tracks' entries.", file=sys.stderr)
        return 1

    guid_to_stem = build_guid_to_stem()
    print(f"[patch] {len(guid_to_stem)} audio clips indexed via .meta files")

    catalogs = list(find_catalog_assets())
    if not catalogs:
        print("[patch] No AudioCatalog*.asset files found.", file=sys.stderr)
        return 1

    total_updated = 0
    for asset in catalogs:
        updated, skipped = patch_catalog(asset, analysis, guid_to_stem)
        total_updated += updated
        rel = asset.relative_to(REPO_ROOT)
        print(f"  • {rel}: updated={updated} skipped={skipped}")

    print(f"[patch] Done. {total_updated} track entries patched across {len(catalogs)} catalog(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
