#!/usr/bin/env python3
"""Build the chat-persona manifest the Unity importer reads.

The seven personas in ``tools/chat/personas`` are the ones Valkur shipped in its
Python incarnation, recovered from the ``archive/python-legacy-2026-05-06`` tag.
They are rich but they are *not* dialogue: Python drove every reply through a
language model, so nothing in them is a line an NPC can simply say.

This script turns that material into something an offline provider can speak,
without inventing a single word:

* ``greeting`` comes from the archived conversation logs — the first turn the
  NPC took, and only when it introduces the character BY NAME ("Bienvenido. Soy
  Abigail, gestora de cuentas del gremio.").  Those logs are contaminated:
  Roberto the mage opens one of his with Pavel the lumberjack's line about fresh
  timber, and they carry stock and receipt lines emitted by Python's shop
  systems.  Requiring the name is what makes leakage structurally impossible.
  A persona with no qualifying opening falls back to its own authored small talk.
* ``dialogue_lines`` are AUTHORED material only, in this order: ``humour.examples``,
  ``smalltalk.examples``, ``negotiation.phrases``, ``speech.catchphrases``, then
  the "Ejemplos de fraseo breve" block of the style prompt.  The transcripts are
  deliberately not mined for them, for the same leakage reason — a line spoken
  by the wrong character is worse than one line fewer.

Run:  python tools/chat/build_persona_manifest.py
Then: Unity ▸ Valkur ▸ Chat ▸ Import Personas
"""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PERSONA_DIR = ROOT / "personas"
PROMPT_DIR = ROOT / "prompts"
MEMORY_DIR = ROOT / "memories"
OUT_PATH = ROOT / "generated" / "chat_personas_manifest.json"

# A dialogue line longer than this is a paragraph, not a line. Measured against
# the archived transcripts: the recipe answers run 250-400 characters and read
# as an essay in a floating bubble, while every authored example is under 120.
MAX_LINE_CHARS = 170
MIN_LINE_CHARS = 12

# A greeting is allowed to run longer than a conversational line: it introduces
# the character and, for a vendor, usually names what they sell. Measured across
# the archived transcripts, the real greetings land between 74 and 186 characters
# and everything above 200 is an answer to a question rather than an opening.
GREETING_MAX_CHARS = 200

# Memory folders are named "<short>-<entity id>"; the short name is what maps a
# transcript back to its persona. Taken from the archived assignments.
SHORT_TO_PERSONA = {
    "gatita": "vendor_cheff_gatita",
    "valeria": "vendor_alchemist_valeria",
    "smith": "vendor_blacksmith_smith",
    "abigail": "vendor_banker_abigail",
    "pavel": "vendor_lumberjack_pavel",
    "roberto": "vendor_mague_roberto",
}


def read_json(path: Path):
    # The archived files are UTF-8; some carry a BOM from a Windows editor.
    return json.loads(path.read_text(encoding="utf-8-sig"))


def clean(text: str) -> str:
    return re.sub(r"\s+", " ", (text or "")).strip().strip('"')


# The transcripts interleave the character's voice with lines the Python build
# emitted from its stock and transaction systems ("Tengo 0 de madera a 1 oro la
# unidad.", "Hecho. Compraste 1x paella_01 por 1.0 gold."). They are not
# dialogue: they name item keys, quote live prices, and would have an NPC quoting
# a stock level that has not existed since the port.
_SYSTEM_LINE = re.compile(
    r"""(?xi)
    \bcompraste\b | \bvendiste\b | ^hecho[.,]        # transaction receipts
    | \b\d+(?:[.,]\d+)?\s*(?:oro|gold|monedas)\b     # quoted prices
    | \btengo\s+\d                                   # quoted stock levels
    | \b[a-z]+_\d+\b                                 # raw item keys, e.g. paella_01
    """
)


def usable(line: str) -> bool:
    if not (MIN_LINE_CHARS <= len(line) <= MAX_LINE_CHARS):
        return False
    return not _SYSTEM_LINE.search(line)


def prompt_examples(persona_id: str) -> list[str]:
    """The 'Ejemplos de fraseo breve' bullets of a style prompt, if it has any."""
    path = PROMPT_DIR / f"{persona_id}.txt"
    if not path.exists():
        return []

    out, collecting = [], False
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line.lower().startswith("ejemplos"):
            collecting = True
            continue
        if collecting and line.startswith("- "):
            out.append(clean(line[2:]))
        elif collecting and not line:
            continue
        elif collecting and not line.startswith("- "):
            break
    return out


def transcripts_for(persona_id: str) -> list[list[dict]]:
    """Every archived conversation belonging to this persona, oldest file first."""
    shorts = [s for s, pid in SHORT_TO_PERSONA.items() if pid == persona_id]
    if not shorts:
        return []

    found = []
    for path in sorted(MEMORY_DIR.glob("*.json")):
        if path.stem.rsplit("-", 1)[0] not in shorts:
            continue
        found.append(read_json(path).get("ephemeral_history", []))
    return found


def build_persona(path: Path) -> dict:
    persona_id = path.stem
    src = read_json(path)

    speech = src.get("speech", {}) or {}
    humour = src.get("humor", {}) or {}
    smalltalk = src.get("smalltalk", {}) or {}
    negotiation = src.get("negotiation", {}) or {}
    knowledge = src.get("knowledge", {}) or {}
    traits = src.get("traits", {}) or {}
    moods = src.get("moods", {}) or {}
    style = src.get("style", {}) or {}

    histories = transcripts_for(persona_id)

    # The greeting is the shortest OPENING turn across this persona's
    # conversations — the first thing it said in each of them. Shortest rather
    # than first because the transcripts are keyed by entity id, so which file
    # sorts first is an accident: Valeria opened one conversation in 74
    # characters and another in 173, and only the arbitrary one was being taken.
    # NOTE the first entry of a history is the PLAYER's line — these logs record
    # a conversation the player started. The opening is the first turn the NPC
    # took in reply to it.
    openings = []
    for history in histories:
        first_reply = next(
            (clean(m.get("content", "")) for m in history if m.get("role") == "assistant"),
            "",
        )
        if first_reply:
            openings.append(first_reply)
    # A transcript opening is only accepted when it INTRODUCES this character by
    # name ("Bienvenido. Soy Abigail, gestora de cuentas del gremio."). That is a
    # blunt rule and it is the right one: the archived logs are contaminated —
    # Roberto the mage opens one of his with "¡Hola, corazón! ¿Buscas madera
    # fresca?", which is Pavel the lumberjack's line — and no amount of
    # greeting-word matching separates a real opening from a plausible one
    # belonging to somebody else. Requiring the name makes cross-persona leakage
    # structurally impossible; the cost is that a nameless-but-fine opening is
    # passed over in favour of the persona's own authored small talk.
    display_name = src.get("name", "")
    openings = [
        o
        for o in openings
        if MIN_LINE_CHARS <= len(o) <= GREETING_MAX_CHARS
        and not _SYSTEM_LINE.search(o)
        and display_name
        and display_name.lower() in o.lower()
    ]
    greeting = min(openings, key=len) if openings else ""

    if not greeting:
        # Felipondor was never talked to at all, so there is no transcript to
        # mine; Smith, Pavel and Roberto have transcripts whose openings do not
        # name them. Their own small talk opens on the player's arrival ("Ya he
        # perdido la cuenta de las hojas que han caído... pero no de tus
        # visitas."), which is what a greeting from them should be, and it is
        # authored rather than generated so it cannot belong to anyone else.
        fallbacks = [clean(x) for x in smalltalk.get("examples", [])] + [
            clean(x) for x in speech.get("catchphrases", [])
        ]
        fallbacks = [
            f
            for f in fallbacks
            if MIN_LINE_CHARS <= len(f) <= GREETING_MAX_CHARS and not _SYSTEM_LINE.search(f)
        ]
        greeting = fallbacks[0] if fallbacks else ""

    # Dialogue lines are AUTHORED material only. The transcripts are deliberately
    # not mined for them: they carry the same cross-persona leakage the greeting
    # rule above had to defend against, and a line that belongs to another
    # character is worse than one line fewer. Every entry below was written to
    # characterise this persona.
    lines, seen = [], set()
    for candidate in (
        [clean(x) for x in humour.get("examples", [])]
        + [clean(x) for x in smalltalk.get("examples", [])]
        + [clean(x) for x in negotiation.get("phrases", [])]
        + [clean(x) for x in speech.get("catchphrases", [])]
        + prompt_examples(persona_id)
    ):
        key = candidate.lower().rstrip(" .!?…")
        if not candidate or not usable(candidate) or candidate == greeting:
            continue

        # Exact-match dedup is not enough: the persona files and the style
        # prompts carry the same line with a different tail ("Hoy el bosque
        # estuvo generoso; yo también." and "…; yo también, corazón."). Keeping
        # both makes a ten-line repertoire feel like six. Whichever arrives first
        # wins, which is the authored persona over the prompt by construction.
        if any(key.startswith(s) or s.startswith(key) for s in seen):
            continue

        seen.add(key)
        lines.append(candidate)

    return {
        "personaId": persona_id,
        "displayName": src.get("name", persona_id),
        "greeting": greeting,
        "tone": src.get("tone", ""),
        "dialogueLines": lines,
        "style": {
            "useEmoji": bool(style.get("emoji", True)),
            "verbosity": style.get("verbosity", "medium"),
            "maxSentences": int(style.get("sentences_max", 3)),
        },
        "discountLimits": negotiation.get("discount_limits", {}) or {},
        "allowedItemTypes": knowledge.get("allowed_types", []) or [],
        "profile": {
            "origin": src.get("origin", ""),
            "background": src.get("background", ""),
            "goals": src.get("goals", []) or [],
            "humour": {
                "enabled": bool(humour.get("enabled", True)),
                "frequency": humour.get("frequency", "sometimes"),
                "topics": humour.get("topics", []) or [],
                "style": humour.get("style", ""),
                "examples": [clean(x) for x in humour.get("examples", [])],
            },
            "traits": {
                "positive": traits.get("positive", []) or [],
                "negative": traits.get("negative", []) or [],
                "quirks": traits.get("quirks", []) or [],
            },
            "speech": {
                "register": speech.get("register", "casual"),
                "slang": speech.get("slang", []) or [],
                "emojiPalette": speech.get("emoji_palette", []) or [],
                "fillerWords": speech.get("filler_words", []) or [],
                "catchphrases": [clean(x) for x in speech.get("catchphrases", [])],
                "punctuation": speech.get("punctuation", ""),
                "flirtStyle": speech.get("flirt_style", ""),
            },
            "boundaries": src.get("boundaries", []) or [],
            "knowledge": {
                "domain": knowledge.get("domain", []) or [],
                "allowedTypes": knowledge.get("allowed_types", []) or [],
                "catalogPolicy": knowledge.get("catalog_policy", ""),
                "tabooTopics": knowledge.get("taboo_topics", []) or [],
                "localLore": knowledge.get("local_lore", []) or [],
            },
            "moods": {
                "enabled": bool(moods.get("enabled", True)),
                "baseline": moods.get("baseline", "neutral"),
                "triggersUp": moods.get("triggers_up", []) or [],
                "triggersDown": moods.get("triggers_down", []) or [],
            },
            "negotiation": {
                "style": negotiation.get("style", ""),
                "phrases": [clean(x) for x in negotiation.get("phrases", [])],
            },
            "smallTalk": {
                "topicsPreferred": smalltalk.get("topics_preferred", []) or [],
                "topicsAvoid": smalltalk.get("topics_avoid", []) or [],
                "examples": [clean(x) for x in smalltalk.get("examples", [])],
            },
        },
    }


def build_assignments() -> list[dict]:
    """Entity-name → persona rows, straight from the archived assignments."""
    src = read_json(PERSONA_DIR / "assignments.json")
    rows = []
    for entity_name, row in src.items():
        # Python's own file carries one stale row keyed by an internal entity id
        # with a chat range of 140 — a pixel value from before the port. The
        # by-display-name rows are the real ones.
        chat_range = float(row.get("chat_range", 2))
        if chat_range > 20:
            continue
        rows.append(
            {
                "entityName": entity_name,
                "personaId": row.get("persona_id", ""),
                "role": row.get("role", "generic"),
                "chatRange": chat_range,
            }
        )
    return sorted(rows, key=lambda r: r["entityName"])


def main() -> None:
    personas = [
        build_persona(p)
        for p in sorted(PERSONA_DIR.glob("*.json"))
        if p.stem not in ("assignments", "persona.schema")
    ]
    assignments = build_assignments()

    # Each persona's role and chat range live in the assignments file, not in the
    # persona file, so fold them in here rather than making the C# importer join
    # two collections.
    by_id = {row["personaId"]: row for row in assignments}
    for persona in personas:
        row = by_id.get(persona["personaId"])
        persona["role"] = row["role"] if row else "generic"
        persona["chatRange"] = row["chatRange"] if row else 2.0

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUT_PATH.write_text(
        json.dumps(
            {"personas": personas, "assignments": assignments},
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )

    print(f"wrote {OUT_PATH.relative_to(ROOT.parent.parent)}")
    for persona in personas:
        print(
            f"  {persona['personaId']:<34} "
            f"role={persona['role']:<8} range={persona['chatRange']:<4} "
            f"lines={len(persona['dialogueLines']):<3} "
            f"greeting={'yes' if persona['greeting'] else 'NO'}"
        )
    print(f"  {len(assignments)} assignment rows")


if __name__ == "__main__":
    main()
