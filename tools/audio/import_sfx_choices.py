"""Copy the chosen Freesound candidates into Assets and write the manifest Unity imports.

The pick for each id is candidate 1 (fetch_sfx.py already ranks them) unless
tools/audio/sfx_choices.json names another Freesound id, e.g. {"ui_move": 528561}; an id
mapped to null is skipped. After running this, use Valkur > Audio > Import Downloaded SFX.

Also writes tools/audio/generated/sfx_credits.md: CC0 asks for nothing, but knowing where each
sound came from is what lets it be replaced or re-found later.
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGING = ROOT / "staging" / "audio" / "sfx"
CHOICES = Path(__file__).with_name("sfx_choices.json")
OUT_JSON = Path(__file__).with_name("generated") / "sfx_import.json"
ASSET_ROOT = "Assets/_Project/Audio/SFX"
UNITY_ASSETS = ROOT / "unity" / "Valkur"

# Ids the manifest stages but the game must not receive: something else already plays that
# moment and a second sound on the same event would double it.
NOT_IMPORTED = {"item_pickup"}


def folder_for(sound_id: str) -> str:
    for prefix, folder in (("spell_", "spells"), ("charge_", "spells"), ("root_", "spells"),
                           ("thrall_", "spells"), ("harvest_", "harvest"), ("inv_", "inventory"),
                           ("grimoire_", "grimoire"), ("ui_", "menu"), ("npc_", "npc"),
                           ("player_", "player"), ("spirit_", "player"), ("level_", "player")):
        if sound_id.startswith(prefix):
            return folder
    return "misc"


def main() -> None:
    choices = json.loads(CHOICES.read_text(encoding="utf-8")) if CHOICES.exists() else {}
    rows, credits = [], []
    for rec_path in sorted(STAGING.glob("*/candidates.json")):
        rec = json.loads(rec_path.read_text(encoding="utf-8"))
        entry, cands = rec["entry"], rec["candidates"]
        sid = entry["id"]
        if sid in NOT_IMPORTED or not cands:
            continue
        if sid in choices:
            if choices[sid] is None:
                continue
            pick = next((c for c in cands if c["freesound_id"] == choices[sid]), None)
            if pick is None:
                print(f"{sid}: choice {choices[sid]} is not a staged candidate, skipped")
                continue
        else:
            pick = cands[0]
        if not pick["file"].endswith(".ogg") or "__" not in pick["file"]:
            print(f"{sid}: candidate was not processed, skipped")
            continue

        rel = f"{ASSET_ROOT}/{folder_for(sid)}/{sid}.ogg"
        dest = UNITY_ASSETS / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(rec_path.parent / pick["file"], dest)
        rows.append({"id": sid, "group": entry["group"], "assetPath": rel})
        credits.append(f"| `{sid}` | [{pick['name']}]({pick['url']}) | {pick['author']} | {pick['license']} |")

    npc_death = [r["id"] for r in rows if r["id"].startswith("npc_death_")]
    level_up = "level_up" if any(r["id"] == "level_up" for r in rows) else ""
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps({"rows": rows, "npcDeathIds": npc_death, "levelUpId": level_up},
                                   indent=2), encoding="utf-8")

    # Kept outside Assets/: Unity would import it as a TextAsset nothing reads.
    credits_path = OUT_JSON.parent / "sfx_credits.md"
    credits_path.write_text(
        "# Downloaded sound effects\n\nFreesound picks are CC0 (no attribution required); ElevenLabs "
        "picks were generated on a paid plan, which grants commercial use. Picked by "
        "`tools/audio/import_sfx_choices.py`.\n\n| Id | Source | Author | License |\n|---|---|---|---|\n"
        + "\n".join(credits) + "\n", encoding="utf-8")
    print(f"{len(rows)} sounds copied, manifest {OUT_JSON}")


if __name__ == "__main__":
    main()
