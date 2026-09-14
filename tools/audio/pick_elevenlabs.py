"""Choose one ElevenLabs take per id and record it in sfx_choices.json.

Nobody has listened to these yet, so the pick is measured rather than guessed: each processed
take's duration after silence trimming and its mean loudness are read with ffprobe/ffmpeg, and
a take is rejected when trimming left less than 35 % of what was asked for (the model produced
mostly silence) or when it is near-silent. Among the survivors the take whose trimmed length is
closest to the requested one wins.

An id the author already set by hand in sfx_choices.json is never overwritten; this only fills
ids that have no choice yet.
"""

from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGING = ROOT / "staging" / "audio" / "sfx"
PROMPTS = Path(__file__).with_name("elevenlabs_prompts.json")
CHOICES = Path(__file__).with_name("sfx_choices.json")


def duration(path: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                          "-of", "csv=p=0", str(path)], capture_output=True, text=True).stdout
    try:
        return float(out.strip())
    except ValueError:
        return 0.0


def mean_volume(path: Path) -> float:
    err = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(path), "-af", "volumedetect",
                          "-f", "null", "-"], capture_output=True, text=True).stderr
    m = re.search(r"mean_volume:\s*(-?[\d.]+) dB", err)
    return float(m.group(1)) if m else -99.0


def main() -> None:
    spec = json.loads(PROMPTS.read_text(encoding="utf-8"))
    choices = json.loads(CHOICES.read_text(encoding="utf-8")) if CHOICES.exists() else {}
    for e in spec["entries"]:
        sid = e["id"]
        if sid in choices:
            print(f"{sid:28s} kept author choice {choices[sid]}")
            continue
        rec_path = STAGING / sid / "candidates.json"
        if not rec_path.exists():
            continue
        best, best_err = None, None
        for c in json.loads(rec_path.read_text(encoding="utf-8"))["candidates"]:
            if not str(c["freesound_id"]).startswith("el") or not c["file"].endswith(".ogg"):
                continue
            f = STAGING / sid / c["file"]
            d, vol = duration(f), mean_volume(f)
            if d < 0.35 * e["dur"] or vol < -40:
                continue
            err = abs(d - e["dur"])
            if best is None or err < best_err:
                best, best_err = c["freesound_id"], err
        if best:
            choices[sid] = best
            print(f"{sid:28s} -> {best}")
        else:
            print(f"{sid:28s} no usable take, Freesound pick stays")
    CHOICES.write_text(json.dumps(dict(sorted(choices.items())), indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
