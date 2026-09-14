"""Loudness-normalise every sound effect under Assets/_Project/Audio/SFX so they sit in one mix.

Why it exists: the SFX came from three places (hand-imported packs, Freesound, ElevenLabs) and
arrived 20+ dB apart. The previous pass ran ffmpeg `loudnorm` (integrated LUFS), which is gated
at 400 ms and simply cannot measure a 70 ms UI tick: half the clips came back as -70 LUFS and
were left wherever the limiter put them. Some legacy sword clashes decode past 0 dBFS.

What it does, per clip:
  1. Reads the MASTER copy (staging/audio/sfx_masters/, created on first run from what is in
     Assets). Every run starts from the master, so re-running never stacks processing.
  2. Measures MOMENTARY MAX loudness (BS.1770 K-weighting, 400 ms window, 10 ms hop). A clip
     shorter than the window is measured over its whole length. This is the number a player
     hears: how loud the loudest moment of the sound is.
  3. Applies a gain toward its category target, a 30 Hz high-pass (rumble / DC nobody hears
     on laptop speakers but that eats headroom) and a peak limiter at -1.5 dBFS.
  4. Re-measures and corrects the residual the limiter took (up to 3 passes).

Targets are relative to the MUSIC, which is mastered hot (median integrated -13.3 LUFS across
the 24 tracks): with the Music and SFX sliders at the same value, an impact lands a few LU above
the music bed, a UI tick a few below. Music files are NOT touched here (re-encoding an mp3 moves
its encoder delay and would shift the baked beatTimes).

Usage:  python tools/audio/normalize_sfx.py [--report]     (--report measures, writes nothing)
Then reimport in Unity (refresh_unity scope=all). A report lands in tools/audio/generated/.
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SFX_DIR = ROOT / "unity" / "Valkur" / "Assets" / "_Project" / "Audio" / "SFX"
MASTERS = ROOT / "staging" / "audio" / "sfx_masters"
REPORT = Path(__file__).with_name("generated") / "sfx_loudness_report.md"

SR = 48000
PEAK_LIMIT_DB = -1.5
MAX_LIMITING_DB = 9.0
HIGHPASS_HZ = 30

# BS.1770-4 K-weighting at 48 kHz (pre-filter shelf + RLB high-pass).
K_STAGE1 = (1.53512485958697, -2.69169618940638, 1.19839281085285, 1.0, -1.69065929318241, 0.73248077421585)
K_STAGE2 = (1.0, -2.0, 1.0, 1.0, -1.99004745483398, 0.99007225036621)

# (match predicate on the relative path, target momentary-max LUFS, category label). First match wins.
CATEGORIES = [
    (lambda p: "clash/" in p, -10.0, "impacto metal"),
    (lambda p: any(k in p for k in ("impact", "explode", "meteor_fall", "slash_hit", "lightning")), -10.0, "impacto hechizo"),
    (lambda p: p.startswith("player/damage/") or p.startswith("npc/"), -11.0, "voz / golpe criatura"),
    (lambda p: any(k in p for k in ("player_death", "player_revive", "spirit_enter", "level_up")), -11.5, "evento jugador"),
    (lambda p: "breath_loop" in p or "arcane_flame_tick" in p or "heal_tick" in p, -15.0, "bucle / tick hechizo"),
    (lambda p: p.startswith("spells/") or "fireball" in p, -12.5, "lanzamiento hechizo"),
    (lambda p: p.startswith("harvest/"), -13.0, "recoleccion"),
    (lambda p: "pickup_rare" in p or "capstone" in p or "coinsplash" in p, -14.0, "recompensa"),
    (lambda p: p.startswith(("inventory/", "grimoire/", "menu/")), -16.0, "interfaz"),
]
DEFAULT_TARGET = (-13.0, "otros")


def category_for(rel: str) -> tuple[float, str]:
    for match, target, label in CATEGORIES:
        if match(rel):
            return target, label
    return DEFAULT_TARGET


def decode(path: Path) -> np.ndarray:
    """Decode to float32 at 48 kHz, shape (channels, samples)."""
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                            "stream=channels", "-of", "csv=p=0", str(path)],
                           capture_output=True, text=True, check=True)
    ch = int(probe.stdout.strip().split(",")[0] or 1)
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "-ac", str(ch),
                          "-ar", str(SR), "-"], capture_output=True, check=True).stdout
    data = np.frombuffer(raw, dtype=np.float32)
    return data.reshape(-1, ch).T.astype(np.float64)


def biquad(x: np.ndarray, c: tuple) -> np.ndarray:
    b0, b1, b2, a0, a1, a2 = c
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    xs = x.tolist()
    out = y.tolist()
    for i, xi in enumerate(xs):
        yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        out[i] = yi
        x2, x1, y2, y1 = x1, xi, y1, yi
    return np.asarray(out)


def momentary_max(audio: np.ndarray) -> tuple[float, float]:
    """(momentary max LUFS, sample peak dBFS)."""
    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    weighted = np.stack([biquad(biquad(ch, K_STAGE1), K_STAGE2) for ch in audio])
    n = weighted.shape[1]
    win = min(int(0.4 * SR), n)
    hop = int(0.01 * SR)
    if win <= 0:
        return -70.0, -120.0
    sq = weighted ** 2
    csum = np.concatenate([np.zeros((sq.shape[0], 1)), np.cumsum(sq, axis=1)], axis=1)
    starts = np.arange(0, n - win + 1, hop) if n > win else np.array([0])
    ms = (csum[:, starts + win] - csum[:, starts]) / win
    total = ms.sum(axis=0)
    best = float(total.max())
    lufs = -0.691 + 10 * math.log10(best) if best > 0 else -70.0
    return lufs, (20 * math.log10(peak) if peak > 0 else -120.0)


def render(master: Path, dest: Path, gain_db: float) -> None:
    limit = 10 ** (PEAK_LIMIT_DB / 20)
    af = (f"highpass=f={HIGHPASS_HZ},volume={gain_db:.3f}dB,"
          f"alimiter=limit={limit:.4f}:attack=1:release=60:level=false:asc=1")
    rate = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                           "stream=sample_rate", "-of", "csv=p=0", str(master)],
                          capture_output=True, text=True, check=True).stdout.strip().split(",")[0]
    codec = ["-c:a", "libvorbis", "-q:a", "7"] if dest.suffix == ".ogg" else ["-c:a", "pcm_s16le"]
    tmp = dest.with_name(dest.stem + ".normtmp" + dest.suffix)
    # alimiter works at 4x internally only on its own terms; resample up for the limiter so
    # inter-sample overs are caught, then back to the file's own rate.
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(master), "-af",
                    f"aresample=192000,{af},aresample={rate}", *codec, str(tmp)], check=True)
    tmp.replace(dest)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", action="store_true", help="measure only, write nothing")
    args = ap.parse_args()

    files = sorted(p for p in SFX_DIR.rglob("*") if p.suffix.lower() in (".ogg", ".wav"))
    rows = []
    for path in files:
        rel = path.relative_to(SFX_DIR).as_posix()
        master = MASTERS / rel
        if not master.exists():
            if args.report:
                master = path
            else:
                master.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, master)
        target, label = category_for(rel)
        before, before_peak = momentary_max(decode(master))
        if args.report:
            rows.append((rel, label, target, before, before_peak, before, before_peak, 0.0))
            print(f"{before:7.1f} LUFS  pk {before_peak:6.1f}  -> {target:5.1f}  {rel}")
            continue

        # A clip that is all transient (a 70 ms tick peaking near 0 dBFS) can only get louder by
        # being limited, and 12 dB of limiting turns a click into a flat buzz. Allow at most
        # MAX_LIMITING_DB of reduction beyond the clip's own headroom; it lands short of target
        # instead, and the report lists it.
        max_gain = (PEAK_LIMIT_DB - min(before_peak, 0.0)) + MAX_LIMITING_DB
        gain = min(target - before, max_gain)
        after = after_peak = before
        for _ in range(3):
            render(master, path, gain)
            after, after_peak = momentary_max(decode(path))
            residual = target - after
            if abs(residual) < 0.3 or (residual > 0 and gain >= max_gain):
                break
            gain = min(gain + residual, max_gain)
        rows.append((rel, label, target, before, before_peak, after, after_peak, gain))
        print(f"{before:7.1f} -> {after:6.1f} LUFS (obj {target:5.1f})  pk {after_peak:5.1f}  gain {gain:+5.1f}  {rel}")

    if args.report:
        return
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    lines = ["# SFX loudness report", "",
             "Generated by `tools/audio/normalize_sfx.py`. Momentary max loudness (BS.1770, 400 ms).", "",
             "| Clip | Categoria | Objetivo | Antes | Pico antes | Despues | Pico despues | Ganancia |",
             "|---|---|---|---|---|---|---|---|"]
    for rel, label, target, b, bp, a, apk, g in rows:
        lines.append(f"| `{rel}` | {label} | {target:.1f} | {b:.1f} | {bp:.1f} | {a:.1f} | {apk:.1f} | {g:+.1f} dB |")
    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    off = [r for r in rows if abs(r[5] - r[2]) >= 1.0]
    print(f"\n{len(rows)} clips, {len(off)} fuera de +-1 LU del objetivo. Informe: {REPORT}")
    for r in off:
        print(f"  {r[0]}: {r[5]:.1f} (obj {r[2]:.1f})")


if __name__ == "__main__":
    sys.exit(main())
