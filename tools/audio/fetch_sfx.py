"""Stage CC0 sound-effect candidates from Freesound for every id in sfx_manifest.json.

For each manifest entry this searches Freesound (license filtered to CC0, duration capped),
ranks the hits, downloads the best few HQ previews into staging/audio/sfx/<id>/, trims leading
and trailing silence, loudness-normalises them and writes an .ogg next to the raw file. It
also writes candidates.json per id and a listen.html page to pick from.

Nothing here touches Assets/: choosing a candidate and importing it into AudioCatalog.asset is
a separate step, so a bad search can never land in the game unheard.

The API key is read from the FREESOUND_API_KEY environment variable (user scope on Windows is
also checked), never from the repository.

Usage:
    python tools/audio/fetch_sfx.py              # every entry not staged yet
    python tools/audio/fetch_sfx.py --only ui_move spell_mine_explode
    python tools/audio/fetch_sfx.py --force      # re-fetch entries already staged
"""

from __future__ import annotations

import argparse
import html
import json
import os
import subprocess
import sys
import time
from pathlib import Path

import requests

try:
    # Use the operating system's certificate store: on this machine Python's bundled roots do
    # not validate freesound.org while Windows does (an intercepting antivirus/proxy root).
    import truststore
    truststore.inject_into_ssl()
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("sfx_manifest.json")
STAGING = ROOT / "staging" / "audio" / "sfx"
API = "https://freesound.org/apiv2/search/text/"
CANDIDATES_PER_ID = 4
REQUEST_GAP_SEC = 1.1  # Freesound allows 60 requests a minute


def api_key() -> str:
    key = os.environ.get("FREESOUND_API_KEY")
    if not key and sys.platform == "win32":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
                key, _ = winreg.QueryValueEx(k, "FREESOUND_API_KEY")
        except OSError:
            key = None
    if not key:
        sys.exit("FREESOUND_API_KEY is not set")
    return key


def search(session: requests.Session, key: str, entry: dict) -> list[dict]:
    params = {
        "query": entry["query"],
        "filter": f'license:"Creative Commons 0" duration:[0.05 TO {entry["max_dur"]}]',
        "fields": "id,name,duration,license,username,url,previews,avg_rating,num_ratings,num_downloads,tags",
        "page_size": 30,
    }
    # The key travels in a header, so a failed request never prints it inside the URL.
    r = session.get(API, params=params, headers={"Authorization": f"Token {key}"}, timeout=30)
    r.raise_for_status()
    return r.json().get("results", [])


def rank(hits: list[dict]) -> list[dict]:
    # Downloads say what other people found usable; a rating needs a few votes to mean anything.
    def score(h: dict) -> float:
        rating = h.get("avg_rating") or 0.0
        votes = h.get("num_ratings") or 0
        trusted = rating if votes >= 3 else 3.0
        return (h.get("num_downloads") or 0) ** 0.5 * (0.5 + trusted / 5.0)
    return sorted(hits, key=score, reverse=True)


def process(raw: Path, out: Path) -> bool:
    af = ("silenceremove=start_periods=1:start_threshold=-50dB,areverse,"
          "silenceremove=start_periods=1:start_threshold=-50dB,areverse,"
          "loudnorm=I=-16:TP=-1.5:LRA=11")
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-i", str(raw), "-af", af,
           "-ar", "48000", "-c:a", "libvorbis", "-q:a", "6", str(out)]
    return subprocess.run(cmd).returncode == 0 and out.exists()


def fetch_entry(session: requests.Session, key: str, entry: dict, force: bool) -> str:
    folder = STAGING / entry["id"]
    record = folder / "candidates.json"
    if record.exists() and not force:
        return "skip"
    folder.mkdir(parents=True, exist_ok=True)

    hits = rank(search(session, key, entry))
    time.sleep(REQUEST_GAP_SEC)
    # A narrow query ("ui hover tick") can match one file; widen it a word at a time from the
    # front, keeping the last word, which carries the noun, until there is a real choice.
    words = entry["query"].split()
    while len(hits) < CANDIDATES_PER_ID and len(words) > 1:
        words = words[1:]
        seen = {h["id"] for h in hits}
        extra = search(session, key, {**entry, "query": " ".join(words)})
        time.sleep(REQUEST_GAP_SEC)
        hits += rank([h for h in extra if h["id"] not in seen])
    chosen = []
    for h in hits[:CANDIDATES_PER_ID]:
        url = h["previews"].get("preview-hq-ogg") or h["previews"].get("preview-hq-mp3")
        ext = ".ogg" if url.endswith(".ogg") else ".mp3"
        raw = folder / f"raw_{h['id']}{ext}"
        data = session.get(url, timeout=60)
        data.raise_for_status()
        raw.write_bytes(data.content)
        out = folder / f"{entry['id']}__{h['id']}.ogg"
        ok = process(raw, out)
        chosen.append({
            "freesound_id": h["id"], "name": h["name"], "author": h["username"],
            "license": h["license"], "url": h["url"], "duration": h["duration"],
            "downloads": h.get("num_downloads"), "rating": h.get("avg_rating"),
            "file": out.name if ok else raw.name,
        })
    record.write_text(json.dumps({"entry": entry, "candidates": chosen}, indent=2), encoding="utf-8")
    return f"{len(chosen)} candidates" if chosen else "NO RESULTS"


def write_listen_page() -> Path:
    rows = []
    for folder in sorted(p for p in STAGING.iterdir() if p.is_dir()):
        rec = folder / "candidates.json"
        if not rec.exists():
            continue
        data = json.loads(rec.read_text(encoding="utf-8"))
        cells = []
        for i, c in enumerate(data["candidates"], 1):
            src = f"{folder.name}/{c['file']}"
            cells.append(
                f'<div class="c"><b>{i}</b> {html.escape(c["name"])[:40]}<br>'
                f'<small>{c["duration"]:.2f}s · {c["downloads"]} dl · '
                f'<a href="{c["url"]}" target="_blank">{html.escape(c["author"])}</a></small><br>'
                f'<audio controls preload="none" src="{html.escape(src)}"></audio></div>')
        if not cells:
            cells.append('<div class="c"><i>sin resultados</i></div>')
        rows.append(f'<section><h3>{folder.name}</h3><p>{html.escape(data["entry"]["query"])}</p>'
                    f'<div class="row">{"".join(cells)}</div></section>')
    page = STAGING / "listen.html"
    page.write_text(
        "<!doctype html><meta charset=utf-8><title>Valkur SFX candidates</title>"
        "<style>body{font:14px system-ui;background:#16161a;color:#ddd;margin:16px}"
        "section{border-bottom:1px solid #333;padding:8px 0}h3{margin:0}p{margin:2px 0 6px;color:#888}"
        ".row{display:flex;flex-wrap:wrap;gap:10px}.c{background:#222;padding:6px;border-radius:6px}"
        "a{color:#8ab}audio{width:260px}</style>" + "".join(rows), encoding="utf-8")
    return page


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    key = api_key()
    entries = json.loads(MANIFEST.read_text(encoding="utf-8"))["entries"]
    if args.only:
        entries = [e for e in entries if e["id"] in set(args.only)]
    STAGING.mkdir(parents=True, exist_ok=True)

    with requests.Session() as session:
        for e in entries:
            try:
                status = fetch_entry(session, key, e, args.force)
            except requests.HTTPError as ex:
                status = f"HTTP {ex.response.status_code}"
                time.sleep(5)
            print(f"{e['id']:28s} {status}", flush=True)
    print("listen page:", write_listen_page())


if __name__ == "__main__":
    main()
