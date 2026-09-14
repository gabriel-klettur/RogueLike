"""Generate ElevenLabs sound-effect candidates for the ids in elevenlabs_prompts.json.

Each generation lands in staging/audio/sfx/<id>/ beside the Freesound candidates, is trimmed
and loudness-normalised exactly like them, and is appended to that id's candidates.json with
an "el<n>" key. fetch_sfx.py's listen page shows both sources side by side.

MONEY GUARD. The spend is capped three ways, and the run stops at the first one hit:
  * --max-spend (credits this run, default 15000),
  * a reserve that must stay unspent in the account (--reserve, default 20000),
  * the account itself: the subscription is re-read before every request, and a request whose
    estimated cost would cross either line is never sent.
The account also reports allowed_to_extend_character_limit; if it is ever true (usage-based
billing switched on, i.e. the plan could bill past its credits) this script refuses to run.

The key comes from ELEVENLABS_API_KEY (user scope on Windows is also checked).

Usage:
    python tools/audio/gen_elevenlabs.py                 # every id without ElevenLabs takes yet
    python tools/audio/gen_elevenlabs.py --only spell_dark_impact --force
    python tools/audio/gen_elevenlabs.py --dry-run       # estimate only, no request sent
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

import requests

try:
    import truststore
    truststore.inject_into_ssl()
except ImportError:
    pass

sys.path.insert(0, str(Path(__file__).parent))
from fetch_sfx import STAGING, process, write_listen_page  # noqa: E402

PROMPTS = Path(__file__).with_name("elevenlabs_prompts.json")
API = "https://api.elevenlabs.io/v1"
# Measured 2026-09-14: a 1.0 s generation moved the account counter by 20 credits.
CREDITS_PER_SECOND = 20
SAFETY_FACTOR = 1.5  # estimate high, so a pricing change cannot slip past the guard


def api_key() -> str:
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key and sys.platform == "win32":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
                key, _ = winreg.QueryValueEx(k, "ELEVENLABS_API_KEY")
        except OSError:
            key = None
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set")
    return key


def subscription(session: requests.Session) -> dict:
    for attempt in range(6):
        r = session.get(f"{API}/user/subscription", timeout=30)
        if r.status_code != 429:
            r.raise_for_status()
            return r.json()
        time.sleep(5 * (attempt + 1))
    r.raise_for_status()
    return r.json()


def estimate(dur: float) -> int:
    return math.ceil(max(dur, 0.5) * CREDITS_PER_SECOND * SAFETY_FACTOR)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--max-spend", type=int, default=15000)
    ap.add_argument("--reserve", type=int, default=20000)
    args = ap.parse_args()

    spec = json.loads(PROMPTS.read_text(encoding="utf-8"))
    entries = spec["entries"]
    if args.only:
        entries = [e for e in entries if e["id"] in set(args.only)]
    per_id = spec["candidates"]

    planned = sum(estimate(e["dur"]) * per_id for e in entries)
    print(f"{len(entries)} ids x {per_id} takes, estimated <= {planned} credits")
    if args.dry_run:
        return

    with requests.Session() as session:
        session.headers["xi-api-key"] = api_key()
        sub = subscription(session)
        if sub.get("allowed_to_extend_character_limit"):
            sys.exit("Usage-based billing is ON for this account: it could bill past the plan. "
                     "Switch it off before generating.")
        start_used = sub["character_count"]
        limit = sub["character_limit"]
        print(f"account: {sub['tier']} used {start_used}/{limit}")

        # The account counter lags a request or two behind, so the guard trusts whichever is
        # larger: what the account reports, or the running sum of our own high estimates.
        tally = 0
        requests_sent = 0
        account_used = start_used
        for e in entries:
            folder = STAGING / e["id"]
            folder.mkdir(parents=True, exist_ok=True)
            rec_path = folder / "candidates.json"
            rec = (json.loads(rec_path.read_text(encoding="utf-8")) if rec_path.exists()
                   else {"entry": {"id": e["id"], "query": e["prompt"], "group": "sfx", "max_dur": e["dur"]},
                         "candidates": []})
            existing = [c for c in rec["candidates"] if str(c["freesound_id"]).startswith("el")]
            if existing and not args.force:
                print(f"{e['id']:28s} skip ({len(existing)} takes)")
                continue
            if args.force:
                rec["candidates"] = [c for c in rec["candidates"] if not str(c["freesound_id"]).startswith("el")]

            made = 0
            for n in range(1, per_id + 1):
                # The balance endpoint is rate limited, so it is re-read every 10 generations;
                # between reads the local tally (always the higher estimate) carries the guard.
                if requests_sent % 10 == 0:
                    account_used = subscription(session)["character_count"]
                requests_sent += 1
                used = max(account_used, start_used + tally)
                cost = estimate(e["dur"])
                if used - start_used + cost > args.max_spend:
                    print(f"STOP: run spend cap {args.max_spend} reached (spent {used - start_used})")
                    write_listen_page()
                    return
                if limit - used - cost < args.reserve:
                    print(f"STOP: account reserve {args.reserve} reached (left {limit - used})")
                    write_listen_page()
                    return

                body = {"text": f"{e['prompt']}, {spec['style']}",
                        "duration_seconds": max(e["dur"], 0.5), "prompt_influence": 0.6}
                tally += cost
                r = session.post(f"{API}/sound-generation", json=body, timeout=120)
                for attempt in range(5):
                    if r.status_code != 429:
                        break
                    time.sleep(5 * (attempt + 1))
                    r = session.post(f"{API}/sound-generation", json=body, timeout=120)
                if r.status_code != 200:
                    print(f"{e['id']:28s} take {n} HTTP {r.status_code}: {r.text[:200]}")
                    if r.status_code in (401, 402, 403, 429):
                        write_listen_page()
                        return
                    continue
                raw = folder / f"raw_el{n}.mp3"
                raw.write_bytes(r.content)
                out = folder / f"{e['id']}__el{n}.ogg"
                ok = process(raw, out)
                rec["candidates"].append({
                    "freesound_id": f"el{n}", "name": f"ElevenLabs take {n}", "author": "ElevenLabs",
                    "license": "ElevenLabs paid plan (commercial)", "url": "https://elevenlabs.io",
                    "duration": body["duration_seconds"], "downloads": 0, "rating": None,
                    "file": out.name if ok else raw.name, "prompt": body["text"],
                })
                made += 1
            rec_path.write_text(json.dumps(rec, indent=2), encoding="utf-8")
            print(f"{e['id']:28s} {made} takes", flush=True)

        end = subscription(session)["character_count"]
        print(f"spent {end - start_used} credits, account used {end}/{limit}")
    print("listen page:", write_listen_page())


if __name__ == "__main__":
    main()
