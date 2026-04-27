"""Analyze music files: estimate BPM (tempo) and musical key.

Scans audio files (mp3 / wav / ogg / flac) under one or more roots and writes
a JSON report consumed by the Unity ``MusicAnalysisImporter`` editor tool to
patch ``AudioCatalogSO`` track entries with ``bpm`` and ``key`` metadata.

Default behaviour scans the Valkur Unity music folder
(``unity/Valkur/Assets/_Project/Audio/Music``) and writes the report to
``python/data/audio/music_analysis.json``.

Algorithms
----------
* **BPM**: ``librosa.beat.beat_track`` (PLP-based onset envelope tracking).
* **Key**: chromagram (CQT) averaged over the track, correlated against the
  Krumhansl–Schmuckler major/minor key profiles. Returns the best-scoring
  ``"<Tonic> major|minor"`` plus a confidence margin over the 2nd best.

Usage (from repo root, with venv active)::

    pip install -r python/requirements.txt
    python python/scripts/analyze_music.py
    python python/scripts/analyze_music.py --root path/to/music --out report.json --force

Then in Unity: ``Valkur > Audio > Import BPM/Key Analysis`` to apply.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Iterable

try:
    import numpy as np  # type: ignore
    import librosa  # type: ignore
except ImportError as exc:  # pragma: no cover - dependency check
    print(
        "[analyze_music] Missing dependency: "
        f"{exc.name}. Install with: pip install -r python/requirements.txt",
        file=sys.stderr,
    )
    sys.exit(2)


# Krumhansl–Schmuckler key profiles (major / minor).
_MAJOR_PROFILE = np.array(
    [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
)
_MINOR_PROFILE = np.array(
    [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
)
_PITCH_CLASSES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
_AUDIO_EXTS = {".mp3", ".wav", ".ogg", ".flac", ".m4a"}


def estimate_bpm(y: np.ndarray, sr: int) -> tuple[float, float]:
    """Return ``(bpm, first_beat_offset_sec)`` for an audio buffer."""
    tempo, beats = librosa.beat.beat_track(y=y, sr=sr, units="frames")
    bpm = float(np.atleast_1d(tempo)[0])
    offset = 0.0
    if len(beats) > 0:
        first_beat = librosa.frames_to_time(beats[:1], sr=sr)
        offset = float(first_beat[0])
    return round(bpm, 2), round(offset, 3)


def estimate_key(y: np.ndarray, sr: int) -> tuple[str, float]:
    """Return ``("<Tonic> <mode>", confidence)`` using K–S correlation.

    Confidence is the gap between the best and second-best correlation,
    normalised to [0,1]. Higher = more confident.
    """
    chroma = librosa.feature.chroma_cqt(y=librosa.effects.harmonic(y), sr=sr)
    chroma_mean = chroma.mean(axis=1)

    correlations: list[tuple[float, str]] = []
    for tonic in range(12):
        major_rotated = np.roll(_MAJOR_PROFILE, tonic)
        minor_rotated = np.roll(_MINOR_PROFILE, tonic)
        correlations.append(
            (float(np.corrcoef(chroma_mean, major_rotated)[0, 1]),
             f"{_PITCH_CLASSES[tonic]} major")
        )
        correlations.append(
            (float(np.corrcoef(chroma_mean, minor_rotated)[0, 1]),
             f"{_PITCH_CLASSES[tonic]} minor")
        )
    correlations.sort(key=lambda x: x[0], reverse=True)
    best_score, best_key = correlations[0]
    second_score = correlations[1][0]
    confidence = max(0.0, min(1.0, best_score - second_score))
    return best_key, round(confidence, 3)


def iter_audio_files(roots: Iterable[Path]) -> Iterable[Path]:
    for root in roots:
        if not root.exists():
            print(f"[analyze_music] Skipping missing root: {root}")
            continue
        if root.is_file():
            if root.suffix.lower() in _AUDIO_EXTS:
                yield root
            continue
        for path in sorted(root.rglob("*")):
            if path.is_file() and path.suffix.lower() in _AUDIO_EXTS:
                yield path


def analyze_file(path: Path) -> dict:
    """Load + analyse a single audio file. Returns a result dict."""
    # mono load is sufficient for tempo + chroma; 22050 Hz keeps it fast.
    y, sr = librosa.load(str(path), sr=22050, mono=True)
    duration = float(len(y) / sr) if sr > 0 else 0.0
    bpm, offset = estimate_bpm(y, sr)
    key, conf = estimate_key(y, sr)
    return {
        "filename": path.name,
        "stem": path.stem,
        "path": str(path),
        "duration_sec": round(duration, 3),
        "bpm": bpm,
        "first_beat_offset_sec": offset,
        "key": key,
        "key_confidence": conf,
    }


def default_roots() -> list[Path]:
    repo = Path(__file__).resolve().parents[2]
    return [repo / "unity" / "Valkur" / "Assets" / "_Project" / "Audio" / "Music"]


def default_output() -> Path:
    repo = Path(__file__).resolve().parents[2]
    return repo / "python" / "data" / "audio" / "music_analysis.json"


def load_existing(out_path: Path) -> dict:
    if not out_path.exists():
        return {"version": 1, "tracks": {}}
    try:
        with out_path.open("r", encoding="utf-8") as fh:
            data = json.load(fh)
        if not isinstance(data, dict) or "tracks" not in data:
            return {"version": 1, "tracks": {}}
        return data
    except (OSError, json.JSONDecodeError):
        return {"version": 1, "tracks": {}}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument(
        "--root", action="append", type=Path,
        help="Folder (or single audio file) to scan. May be repeated. "
             "Defaults to the Unity music folder.",
    )
    parser.add_argument(
        "--out", type=Path, default=default_output(),
        help="Output JSON path (default: python/data/audio/music_analysis.json).",
    )
    parser.add_argument(
        "--force", action="store_true",
        help="Re-analyse files that already have an entry.",
    )
    parser.add_argument(
        "--limit", type=int, default=0,
        help="Process at most N new files (0 = no limit).",
    )
    args = parser.parse_args(argv)

    roots: list[Path] = args.root if args.root else default_roots()
    out_path: Path = args.out
    out_path.parent.mkdir(parents=True, exist_ok=True)

    report = load_existing(out_path)
    tracks: dict = report.get("tracks", {})

    files = list(iter_audio_files(roots))
    if not files:
        print(f"[analyze_music] No audio files found under: {roots}")
        return 1

    print(f"[analyze_music] Scanning {len(files)} files...")
    processed = 0
    skipped = 0
    failed: list[str] = []

    for path in files:
        key = path.stem
        if not args.force and key in tracks:
            skipped += 1
            continue
        if args.limit and processed >= args.limit:
            break
        t0 = time.time()
        try:
            result = analyze_file(path)
        except Exception as exc:  # noqa: BLE001
            print(f"  ! {path.name}: FAILED ({exc})")
            failed.append(path.name)
            continue
        elapsed = time.time() - t0
        tracks[key] = result
        processed += 1
        print(
            f"  + {path.name}: {result['bpm']:6.2f} BPM | "
            f"{result['key']:>9} (conf {result['key_confidence']:.2f}) "
            f"| {elapsed:5.2f}s"
        )

        # Save incrementally — analysing 24 mp3s can take a few minutes.
        report["tracks"] = tracks
        report["version"] = 1
        report["generated_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        with out_path.open("w", encoding="utf-8") as fh:
            json.dump(report, fh, indent=2)

    print(
        f"[analyze_music] Done. analysed={processed} skipped={skipped} "
        f"failed={len(failed)} -> {out_path}"
    )
    if failed:
        print("[analyze_music] Failed files:")
        for name in failed:
            print(f"  - {name}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
