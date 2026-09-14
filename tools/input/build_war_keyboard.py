"""
The War keyboard: every spell the grimoire teaches, on a key, in ValkurInputActions.

WHY A SCRIPT. The layout is a DESIGN, and a design written only as ninety bindings in a
103 KB JSON file cannot be read, reviewed or re-applied. The table below is the design; the
asset is its output. Re-running is idempotent.

THE LAYOUT, one rule per row, and Shift is always "the same idea, heavier or rarer":

  digits   1 2 3 4 5 6 7 8 9 0 - =   damage at range; bare = the school's staple,
                                     Shift = that school's stronger spell. 9 - = are beams.
  top      Q R T Y U                 movement (Q R T) and martial melee (Y U); the dash is
                                     NOT here — it is Space / Ctrl, the Dash action
  top      O [ ]                     summons and the rare ones
  home     F G H J K L               protection and healing
  home     ; ' \\                    the seven ki charges
  bottom   Z X C V B M , . /         area control; B draws a weapon (Shift+B the greatsword)

  mouse    LMB fireball, RMB slash, MMB laser_beam  (PlayerController, unchanged)

Every modifier is LEFT Shift. Right Shift, both Ctrls and Space are the dash, E interacts,
I / N / P / Tab / Delete / Esc / ` / F1 / F5 / F9 belong to other verbs, and WASD walks.
Free on purpose for the next spells: Shift with R, T, U, L, =, / and ].

Usage:
    python tools/input/build_war_keyboard.py            # write the asset
    python tools/input/build_war_keyboard.py --check    # verify only, exit 1 on drift
    python tools/input/build_war_keyboard.py --catalog  # print InputActionCatalog lines
"""
import json
import sys
import uuid
from pathlib import Path

ASSET = Path(__file__).resolve().parents[2] / "unity/Valkur/Assets/_Project/Resources/Input/ValkurInputActions.inputactions"
MODIFIER = "<Keyboard>/leftShift"
NAMESPACE = uuid.UUID("6f1d2c4e-8a53-4b7e-9c11-0a3e5d7b9f20")

# (control, shifted, spellKey, catalog label)
LAYOUT = [
    # ── Digits: damage at range ───────────────────────────────────────────────
    ("1", False, "darkball",            "Bola de oscuridad"),
    ("1", True,  "void_lance",          "Lanza del vacio"),
    ("2", False, "iceball",             "Bola de hielo"),
    ("2", True,  "ice_lance",           "Lanza de hielo"),
    ("3", False, "lightball",           "Bola de luz"),
    ("3", True,  "radiant_burst",       "Estallido radiante"),
    ("4", False, "charged_bolt",        "Dardo cargado"),
    ("4", True,  "flame_breath",        "Aliento igneo"),
    ("5", False, "lightning",           "Relampago"),
    ("5", True,  "chain_lightning",     "Rayo en cadena"),
    ("6", False, "seeking_shard",       "Esquirla rastreadora"),
    ("6", True,  "static_field",        "Campo estatico"),
    ("7", False, "boomerang",           "Bumeran"),
    ("7", True,  "thorn_burst",         "Estallido de espinas"),
    ("8", False, "scatter_volley",      "Andanada"),
    ("8", True,  "meteor_shower",       "Lluvia de meteoros"),
    ("9", False, "laser_beam_red",      "Laser rojo"),
    ("9", True,  "laser_beam_yellow",   "Laser amarillo"),
    ("0", False, "lightning_beam",      "Haz de rayos"),
    ("0", True,  "laser_beam_blue",     "Laser azul"),
    ("minus",  False, "laser_beam_white", "Laser blanco"),
    ("minus",  True,  "laser_beam_black", "Laser del vacio"),
    ("equals", False, "laser_beam_green", "Laser verde"),

    # ── Top row: movement and martial melee ───────────────────────────────────
    ("q", False, "teleport",            "Teleportacion"),
    ("q", True,  "shadow_step",         "Paso sombrio"),
    ("r", False, "leap_slam",           "Salto aplastante"),
    ("t", False, "glacial_step",        "Paso glacial"),
    ("y", False, "slash_stab",          "Estocada"),
    ("y", True,  "slash_cleave",        "Hendidura"),
    ("u", False, "slash_combo",         "Combo de tajos"),

    # ── Top row, right hand: summons and the rare ones ────────────────────────
    ("o", False, "summon_barbol",       "Invocar barbol"),
    ("o", True,  "summon_wolf",         "Invocar lobo"),
    ("leftBracket",  False, "raise_thrall",    "Alzar siervo"),
    ("leftBracket",  True,  "firework_launch", "Fuego artificial"),
    ("rightBracket", False, "charge_ki_void",  "Ki del vacio"),

    # ── Home row: protection and healing ──────────────────────────────────────
    ("f", False, "sphere_magic_shield", "Esfera de escudo"),
    ("f", True,  "guardian_light",      "Luz guardiana"),
    ("g", False, "healing_aura",        "Aura de curacion"),
    ("g", True,  "healing_totem",       "Totem sanador"),
    ("h", False, "blessing",            "Bendicion"),
    ("h", True,  "sanctuary",           "Santuario"),
    ("j", False, "barkskin",            "Piel de corteza"),
    ("j", True,  "frozen_ward",         "Egida helada"),
    ("k", False, "arcane_barrier",      "Barrera arcana"),
    ("k", True,  "wall_ice",            "Muro de hielo"),
    ("l", False, "war_cry",             "Grito de guerra"),

    # ── Home row, right hand: the ki charges ──────────────────────────────────
    ("semicolon", False, "charge_ki_spirit",  "Ki espiritual"),
    ("semicolon", True,  "charge_ki_azure",   "Ki azur"),
    ("quote",     False, "charge_ki_verdant", "Ki verde"),
    ("quote",     True,  "charge_ki_crimson", "Ki carmesi"),
    ("backslash", False, "charge_ki_solar",   "Ki solar"),
    ("backslash", True,  "charge_ki_violet",  "Ki violeta"),

    # ── Bottom row: area control, and the weapon on B ─────────────────────────
    ("z", False, "frost_nova",          "Nova de escarcha"),
    ("z", True,  "blizzard",            "Ventisca"),
    ("x", False, "entangle",            "Enmaranar"),
    ("x", True,  "spore_cloud",         "Nube de esporas"),
    ("c", False, "root_whip",           "Latigo de raices"),
    ("c", True,  "thunderclap",         "Trueno"),
    ("v", False, "smoke",               "Humo"),
    ("v", True,  "smoke_emitter",       "Emisor de humo"),
    ("b", False, "weapon_toggle",       "Guardar / sacar arma"),
    ("b", True,  "weapon_toggle_greatsword", "Sacar mandoble"),
    ("m", False, "arcane_flame",        "Llama arcana"),
    ("m", True,  "cinder_trail",        "Rastro de brasas"),
    ("comma",  False, "vortex_pull",    "Vortice atrayente"),
    ("comma",  True,  "vortex_push",    "Vortice repulsor"),
    ("period", False, "mine_basic",     "Mina"),
    ("period", True,  "puddle_lava",    "Charco de lava"),
    ("slash",  False, "curse_of_frailty", "Maldicion de fragilidad"),
]

# The action names the project already shipped keep their names, so every reference to them
# (tests, the Controls editor's saved masks, a player's controls.json) keeps working.
LEGACY_ACTION_NAMES = {
    "darkball": "SpellDarkball", "iceball": "SpellIceball", "lightball": "SpellLightball",
    "puddle_lava": "SpellPuddleLava", "mine_basic": "SpellMineBasic", "boomerang": "SpellBoomerang",
    "chain_lightning": "SpellChainLightning", "vortex_pull": "SpellVortexPull",
    "vortex_push": "SpellVortexPush", "flame_breath": "SpellFlameBreath", "teleport": "SpellTeleport",
    "lightning": "SpellLightning", "sphere_magic_shield": "SpellSphereMagicShield",
    "smoke": "SpellSmoke", "smoke_emitter": "SpellSmokeEmitter", "arcane_flame": "SpellArcaneFlame",
    "firework_launch": "SpellFireworkLaunch", "healing_aura": "SpellHealingAura",
    "meteor_shower": "SpellMeteorShower", "healing_totem": "SpellHealingTotem",
    "summon_barbol": "SpellSummonBarbol", "wall_ice": "SpellWallIce",
    "weapon_toggle": "SpellWeaponToggle", "weapon_toggle_greatsword": "SpellWeaponToggleGreatsword",
}

# Slots the layout does not carry. `slash` is the right click, and `dash` is the Dash action
# (Space, right Shift, both Ctrls) — a spell slot for either would be a second key competing
# with a verb that already has its own.
RETIRED_ACTIONS = {"SpellSlash", "SpellDash"}

# Keys that belong to another verb in War and must never receive a spell.
RESERVED = {"w", "a", "s", "d", "e", "i", "n", "p", "tab", "delete", "escape", "backquote",
            "space", "enter", "numpadEnter", "leftShift", "rightShift", "leftCtrl", "rightCtrl",
            "leftAlt", "f1", "f5", "f9", "upArrow", "downArrow", "leftArrow", "rightArrow"}


def action_name(spell_key):
    if spell_key in LEGACY_ACTION_NAMES:
        return LEGACY_ACTION_NAMES[spell_key]
    return "Spell" + "".join(p.capitalize() for p in spell_key.split("_"))


def stable_id(*parts):
    return str(uuid.uuid5(NAMESPACE, "/".join(parts)))


def validate():
    errors = []
    seen_slots, seen_spells = set(), set()
    for control, shifted, spell, _ in LAYOUT:
        slot = (control, shifted)
        if slot in seen_slots:
            errors.append(f"two spells on {'Shift+' if shifted else ''}{control}")
        if spell in seen_spells:
            errors.append(f"{spell} is on two keys")
        if control in RESERVED:
            errors.append(f"{spell} lands on reserved key {control}")
        seen_slots.add(slot)
        seen_spells.add(spell)
    return errors


def apply(doc):
    gameplay = next(m for m in doc["maps"] if m["name"] == "Gameplay")
    by_name = {a["name"]: a for a in gameplay["actions"]}
    layout_actions = {action_name(s) for _, _, s, _ in LAYOUT}

    # Old plain bindings, to keep a binding id stable when a spell stays on the same key.
    old_plain = {}
    for b in gameplay["bindings"]:
        if not b["isComposite"] and not b["isPartOfComposite"]:
            old_plain.setdefault(b["action"], {})[b["path"]] = b["id"]

    for name in RETIRED_ACTIONS:
        by_name.pop(name, None)
    gameplay["actions"] = [a for a in gameplay["actions"] if a["name"] not in RETIRED_ACTIONS]

    for _, _, spell, _ in LAYOUT:
        name = action_name(spell)
        if name in by_name:
            continue
        action = {
            "name": name, "type": "Button", "id": stable_id("action", name),
            "expectedControlType": "Button", "processors": "", "interactions": "",
        }
        gameplay["actions"].append(action)
        by_name[name] = action

    touched = layout_actions | RETIRED_ACTIONS
    kept = [b for b in gameplay["bindings"] if b["action"] not in touched]

    for control, shifted, spell, _ in LAYOUT:
        name = action_name(spell)
        path = f"<Keyboard>/{control}"
        if not shifted:
            bid = old_plain.get(name, {}).get(path) or stable_id("binding", name, path)
            kept.append(binding("", bid, path, name))
            continue
        kept.append(binding("Shift", stable_id("chord", name), "OneModifier", name, composite=True))
        kept.append(binding("modifier", stable_id("chord-mod", name), MODIFIER, name, part=True))
        kept.append(binding("binding", stable_id("chord-key", name), path, name, part=True))

    gameplay["bindings"] = kept
    return doc


def binding(name, bid, path, action, composite=False, part=False):
    return {
        "name": name, "id": bid, "path": path, "interactions": "", "processors": "",
        "groups": "", "action": action, "isComposite": composite, "isPartOfComposite": part,
    }


def main():
    errors = validate()
    if errors:
        print("Layout errors:\n  " + "\n  ".join(errors))
        return 1

    if "--catalog" in sys.argv:
        for control, shifted, spell, label in LAYOUT:
            key = ("Shift+" if shifted else "") + control
            print(f'            AddSpell(list, "{action_name(spell)}", "{spell}", "{label}");  // {key}')
        return 0

    raw = ASSET.read_text(encoding="utf-8")
    doc = apply(json.loads(raw))
    out = json.dumps(doc, indent=4, ensure_ascii=False) + "\n"

    if "--check" in sys.argv:
        if out != raw:
            print("ValkurInputActions does not match the War keyboard layout.")
            return 1
        print(f"OK: {len(LAYOUT)} spells on the War keyboard.")
        return 0

    ASSET.write_text(out, encoding="utf-8", newline="\n")
    print(f"Wrote {len(LAYOUT)} spell bindings "
          f"({sum(1 for l in LAYOUT if l[1])} on Shift) to {ASSET.name}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
