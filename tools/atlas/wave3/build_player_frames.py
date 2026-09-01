#!/usr/bin/env python3
"""Cut the side-view player sheets into aligned, mirrored, game-sized frames.

The three characters staged under ``staging/players/`` are drawn **one direction
only**, and which direction that is has to be MEASURED per sheet rather than assumed.
The wave4 sheets face LEFT - the same way ``knight_red``'s did - while every wave5
sheet (the elf's archer and bard loadouts) faces RIGHT, so a single global constant
would have baked half the elf backwards. See ``EAST_FACING_SHEETS``. Measuring matters: reading it backwards is invisible in every
still frame and produces a character that faces away from the cursor in game, because
``Direction.East`` is +X (``DirectionalAnimator.FrameLogic`` resolves 0 degrees to East)
so the east buckets must hold the RIGHT-facing copy. Valkur's ``DirectionalAnimator`` never
flips a sprite — ``ChaseState`` says so in as many words, and ``PlayerController``
only touches ``flipX`` when there is no animator at all — so the left-facing half
of a 2-direction rig has to exist as its own sprite. This tool bakes it.

Relationship to the other two tools
-----------------------------------
* ``slice_prop_sheet.py`` owns the segmentation. Run it first; this tool reads the
  ``*.slices.json`` it writes. Cutting a frame out of the sheet by grid cell alone
  would let a neighbouring frame's axe bleed into it.
* ``wave2/build_knight_frames.py`` is this tool's direct ancestor and still owns
  ``knight_red`` (a monster, six authored turnaround poses, art facing LEFT). The
  differences that made a copy cheaper than a parameter: the grid here is inferred
  per sheet rather than declared, the source art faces RIGHT, the output is a
  player rather than a monster, and the manifest carries an eight-bucket direction
  layout that the monster manifest expresses per-direction instead.

Do NOT reach for ``slice_prop_sheet.py``'s crops directly. It trims every crop
tight to its own alpha, which is right for a prop and wrong for a cycle: the cape
and the axe move the bounding box every frame, so a tight-trimmed walk jitters and
the feet leave the ground.

Alignment
---------
Two anchors put each frame on one canvas shared by every frame of the state:

* ``anchor_x`` = the CELL's centre, never the body's. Anchoring on the body would
  cancel the very motion the animation is made of — a walk's hip sway, a slash's
  lunge — and leave the character marching on the spot.
* ``anchor_y`` = the lowest body pixel across the row, so the feet land on the same
  line in every frame. Taken from each frame's LARGEST connected component,
  because a trailing cape tip or a dropped weapon sits below the boots in some
  frames and would drag the ground line down with it. A frame that genuinely
  leaves the ground (the elf's jump attack) floats above the line, which is the
  point.

The row matters: a 4x2 sheet draws two independent ground lines, so the ground is
computed per row.

Grid inference
--------------
The knight tool declared ``(cols, rows)`` per sheet. Here it is inferred: item
centres are clustered into rows by their vertical gaps, and the cell index comes
from each item's position in ``slice_prop_sheet``'s reading order — NOT from where
its centre happens to land. That distinction matters for exactly one shipped
sheet: ``knight_unarmed_death_7f``, where the knight falls and slides a full
half-cell to the left, so a position-derived cell index puts two frames in one
cell and leaves another empty. Deriving from reading order keeps the slide as
motion, which is what a death animation is. A frame whose centre escapes its own
cell is reported as a warning, since on a walk cycle it would mean the row
clustering picked the wrong grid.

Sizing
------
Frames land in ``Art/Characters/<key>/<state>/`` — one subfolder per animation
state, so a character folder holding hundreds of frames stays navigable — where
``ValkurAssetPostprocessor`` forces PPU 64 and a bottom-centre pivot (it keys on
``/Characters/`` anywhere in the path, so the extra level costs nothing).
The five characters already in the game stand 115 px tall at their tallest frame, so ``TARGET_BODY_PX`` matches that exactly:
a swapped-in character keeps the same world height, and every melee range,
projectile spawn offset and camera lead tuned against the old art still reads.
Each state is scaled by its own factor derived from the TALLEST body in the
sheet — the one frame where the character stands upright — because the source
families were rendered at visibly different sizes.

Usage
-----
    python slice_prop_sheet.py --all --sheet-dir <staging/players/knight_wave4> --out <slices>
    python slice_prop_sheet.py --all --sheet-dir <staging/players/barbarian_wave4> --out <slices>
    python slice_prop_sheet.py --all --sheet-dir <staging/players/elf_wave4> --out <slices>
    python slice_prop_sheet.py --all --sheet-dir <staging/players/elf_wave5> --out <slices>         --config wave5/elf_wave5.slices.json
    python wave3/build_player_frames.py <slices> [--dry-run]

All four staging folders feed ONE slices directory, because this tool builds every
player in ``PLAYERS`` in a single pass and writes one manifest for all of them.
``elf_wave5`` needs the config: one of its sheets draws the loosed ARROW as its own
object and another draws the summoned bow detached from the hand that conjures it,
and neither is a frame.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ART_ROOT = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Art", "Characters")
MANIFEST_PATH = os.path.join(REPO, "tools", "atlas", "generated",
                             "player_frames_manifest_wave3.json")
ART_ROOT_UNITY = "Assets/_Project/Art/Characters"

# The five characters already in the game stand 115 px at their tallest frame
# (measured across all 40 frames of barbarian_idle.png). Matching it keeps every
# range and offset tuned against the old art valid.
TARGET_BODY_PX = 115
ALPHA_SOLID = 190     # the core threshold slice_prop_sheet segments on
ALPHA_KEEP = 16       # keep soft edges, drop the haze

# S, SE, E, NE, N, NW, W, SW -- the order BuildEightDirectionalSet slices, and the
# order the manifest's sprite lists must be written in.
DIRECTIONS = ["south", "southEast", "east", "northEast",
              "north", "northWest", "west", "southWest"]

# Most authored art in these waves faces WEST, so the west half of the rig is the
# authored copy and the east half is its mirror - hence the `w`/`e` suffixes rather
# than a left/right pair, matching wave2/build_knight_frames.py.
#
# South and north are ambiguous in a 2-direction rig. Both take the EAST copy, so a
# player walking or aiming straight up and straight down keeps one consistent
# silhouette instead of flipping as the cursor crosses the vertical.
DEFAULT_SOURCE_FACING = "w"
BUCKET_FACING = {
    "south": "e", "southEast": "e", "east": "e", "northEast": "e", "north": "e",
    "northWest": "w", "west": "w", "southWest": "w",
}

# Sheets whose art faces EAST instead, so the AUTHORED copy fills the `e` half and
# the mirror fills the `w` half. Which way a sheet points has to be measured, not
# assumed: the whole wave5 set (archer and bard) is drawn facing right while every
# wave4 sheet of the same character is drawn facing left, and reading it backwards
# is invisible in every still frame, every contact sheet and every count in the
# manifest -- it shows up only in play, as a character that faces AWAY from the
# cursor, because Direction.East is +X. Measure it off the HEAD (the ear points
# back, the face points forward), never off the silhouette.
#
# `facing_of` keys on the stem, so a sheet added to a wave inherits nothing by
# accident.
EAST_FACING_SHEETS = {
    "elf_archer_idle", "elf_archer_walking", "elf_archer_running",
    "elf_archer_attack", "elf_archer_cast",
    "elf_bard_idle", "elf_bard_walking", "elf_bard_walking_2", "elf_bard_running",
    "elf_bard_cast", "elf_bard_spellcasting_1", "elf_bard_spellcasting_2",
    "elf_spellcasting_4", "elf_spellcasting_5",
}


def facing_of(stem: str) -> tuple[str, str]:
    """(authored suffix, mirrored suffix) for one staged sheet."""
    if stem in EAST_FACING_SHEETS:
        return "e", "w"
    return DEFAULT_SOURCE_FACING, "e" if DEFAULT_SOURCE_FACING == "w" else "w"

# Multiplies the automatic scale for one staged sheet. The AI that drew these
# rendered every sheet at its own zoom, and the median-height reference below can
# only normalise a sheet whose character stands upright in at least half its
# frames. Where it does not -- a swing that is crouched or lunging from the first
# frame to the last -- the automatic answer is wrong by exactly the amount the
# pose is compressed, and no statistic recovers it from the pixels. So it is
# declared, the way tools/atlas/wave2/classify.py declares its prop table rather
# than guessing. 1.0 (absent) means the automatic reference was right.
#
# Calibrate against the character's IDLE sheet: place the two side by side and
# match the head, not the bounding box.
# Which frame of a sheet means "how big is this character". Frame 0 by default,
# because every sheet staged before wave5 opens on a neutral pose - see the long
# note in build_state() for the two statistics that look more robust and are not.
#
# A sheet that opens MID-POSE declares its reference here rather than correcting the
# damage afterwards with a SCALE_OVERRIDE multiplier, because the number stays a
# measurement instead of becoming a judgement: the two entries below were read off
# the per-frame foot-to-crown plateau, where four or five frames of the sheet agree
# with each other to within 2px and the opening frame disagrees by 30-40%.
# The shipped state name for a staged stem, where dropping the character prefix
# does not give it. `knight_idle_armed` would otherwise ship as `idle_armed`; the
# barbarian's own second loadout already shipped as `armed_idle`, and one character
# naming its loadouts the other way round is the kind of difference that is invisible
# until someone greps for it.
STATE_NAME_OVERRIDE: dict[str, str] = {
    "knight_idle_armed":          "armed_idle",
    "knight_walking_armed":       "armed_walking",
    "knight_running_armed":       "armed_running",
    "knight_attack_1_armed":      "armed_attack_1",
    "knight_equipment_daw_armed": "armed_equip",
}


REFERENCE_FRAME: dict[str, int] = {
    # Opens with the casting arm thrown straight up, and body_box measures to the
    # top of the raised HAND -- 478px against the 344-346px plateau that frames
    # 3,4,5,7 agree on. Normalising on 478 rendered the elf a head shorter than his
    # own idle.
    "elf_spellcasting_4": 4,
    # The opposite error: opens in a deep crouch, reaching along the floor, at 309px
    # against the 397-403px plateau of frames 4,5,6. Normalising on 309 rendered him
    # visibly oversized.
    "elf_spellcasting_5": 5,
}

SCALE_OVERRIDE: dict[str, float] = {
    # This sheet opens ALREADY AIRBORNE -- frame 0 is the crouch-and-leap, not a
    # stance -- so its foot line sits above the ground the rest of the row lands
    # on and the frame-0 reference under-measures the elf by 14.8%. Every other
    # state of all three characters lands within 3% of its own idle without help.
    "elf_attack_jump_8f": 0.871,

    # ── barbarian wave4 ──────────────────────────────────────────────────────
    # This wave breaks the assumption the frame-0 reference rests on. Its axe idle
    # stands upright at 444px while every combat sheet OPENS in a crouched guard
    # (313px on the overhead swing, 226px on the leap), so frame 0 no longer means
    # the same thing from sheet to sheet and normalising it makes the same
    # character up to 30% different in size between two of his own animations.
    #
    # These were measured, not judged. The head is the one part of a character that
    # a crouch, a lunge and a stride all leave alone, so each sheet's zoom is the
    # scale at which the IDLE sheet's head best correlates with that sheet's frames
    # (normalised cross-correlation, swept 0.40-2.2x, taking the three
    # best-correlating frames of each sheet -- a head thrown back mid-swing
    # correlates badly at every scale and its argmax is noise). The method
    # recovers 1.010 for the idle sheet against itself, which is the check that it
    # measures what it claims to. Values within 4% of 1.0 are inside that noise and
    # are left out rather than written down as if they were knowledge.
    "barbarian_armed_walking_2": 1.130,
    "barbarian_armed_running_2": 0.946,
    "barbarian_armed_attack_2": 0.892,
    "barbarian_spellcasting_1": 0.954,
    "barbarian_spellcasting_2": 0.929,
    "barbarian_spellcasting_3": 0.933,
    "barbarian_spellcasting_4": 0.956,
    "barbarian_spellcasting_5": 0.936,
}


# ── What each player ships ────────────────────────────────────────────────────
#
# `states` maps an EntityAssetConfig slot to the staged sheet that fills it.
# `variants` are extra attacks, exposed through EntityAssetConfig.attackVariants
# rather than new AnimState values -- a new enum value missing from
# PlayerController.Movement's revert whitelist is entered and never left, while a
# variant INDEX under the existing Attack state inherits both whitelists for free.
# Index 0 is what a picker falls back to, so the default swing goes first.
#
# `staged` names the sheets deliberately NOT shipped, with the reason, so the next
# person does not have to re-derive it from the art.
PLAYERS = {
    # knight -> dwarf. Replaced wholesale by the wave4 set (unity/downloads/assets/dwaft).
    #
    # The UNARMED loadout ships, and it is the only one that can: the fourteen unarmed
    # sheets fill every slot with no gaps, while the five `dwarf_armed_*` sheets cover only
    # locomotion and one attack -- there is no armed hurt, death or cast. Shipping any of
    # them beside the rest would pop the sword and shield in and out of the character's
    # hands the moment he is hit, dies or casts. `dwarf_armed_equipment_daw` is the
    # unarmed-to-armed transition, so the art clearly anticipates an equip system; there
    # isn't one, and inventing it is not an art import's job. The armed five are staged
    # under staging/players/knight_wave4_armed/ waiting for it.
    "dwarf": {
        "source": "knight_wave4",
        "states": {
            "idle":    "knight_idle",
            "walk":    "knight_walking",
            "chase":   "knight_running",
            "cast":    "knight_spellcasting_1",
            "attack":  "knight_punch",
            "damage":  "knight_hit_reaction",
            "death":   "knight_die",
            "recover": "knight_knockdown_recovery",
        },
        # Rotated per swing by PlayerController.NextVariant; index 0 is the fallback.
        # charging_sprint is a shoulder-first lunge, not a run -- knight_running is the
        # locomotion cycle -- so it belongs here rather than in `chase`.
        "variants": [
            # punch and kick reach the player through SpellDefinition.usesAttackAnimation:
            # AnimState.Attack is the swing state, and before that flag existed only
            # slash_regular ever entered it -- which, being reserved for armed_slash, left
            # these two authored animations rendering no frame in the whole game. vortex_push
            # and vortex_pull are the two shipped spells that are a SHOVE rather than a
            # conjuring, so they are the honest owners; move the reservation in the Inspector
            # if a better-fitting spell turns up.
            ("punch",  "knight_punch", ["vortex_push", "anim_punch"]),
            ("kick",   "knight_kick",  ["vortex_pull", "anim_kick"]),
            ("charge", "knight_charging_sprint"),
            # Reserved for slash_regular, which is the ONE slash that runs through
            # AnimState.Attack instead of AnimState.Cast (RegularSlashAttack keeps its own
            # authored implementation). Last in the list so `punch` stays index 0, the
            # fallback a picker lands on.
            ("armed_slash", "knight_attack_1_armed", ["slash_regular"]),
        ],
        # Five casting animations, rotated per cast. spellcasting_1 doubles as the base
        # `cast` slot so the character still casts with real art if the variants are lost.
        # Every variant also answers to an `anim_*` AnimationProbe, so each animation can be
        # selected and watched in the Spells Editor without casting the gameplay spell that
        # owns it. A variant may claim several spells; the lookup takes the first match, and
        # the gameplay one is deliberately listed first.
        "cast_variants": [
            # Every casting animation is pinned to a spell. Before this, three of the five
            # rotated anonymously: they DID render, but which spell wore which pose changed
            # from cast to cast, so no spell had a look of its own. The three added here are
            # existing bound spells rather than new ones -- the animation is the only thing
            # being decided, and inventing damage and cost to justify a pose would be the
            # tail wagging the dog.
            ("spell_1", "knight_spellcasting_1", ["lightning", "anim_spellcasting_1"]),
            ("spell_2", "knight_spellcasting_2", ["healing_aura", "anim_spellcasting_2"]),
            ("spell_3", "knight_spellcasting_3", ["fireball", "anim_spellcasting_3"]),
            ("spell_4", "knight_spellcasting_4", ["iceball", "anim_spellcasting_4"]),
            ("spell_5", "knight_spellcasting_5", ["meteor_shower", "anim_spellcasting_5"]),
            # The draw. Reserved for the loadout-toggle spell rather than rotated, so it
            # plays when the weapon is drawn or stowed and never as an ordinary cast.
            ("armed_equip", "knight_equipment_daw_armed", ["weapon_toggle", "anim_armed_equip"]),
            # Every slash the player casts: a slash IS the weapon swing, so it is the one
            # action that must never render empty-handed, armed loadout or not. slash_regular
            # is missing on purpose -- it is the one slash that runs through AnimState.Attack,
            # and it is reserved on the attack variant of the same name instead.
            ("armed_slash", "knight_attack_1_armed",
             ["slash", "slash_cleave", "slash_combo", "slash_stab", "anim_armed_attack_1"]),
            # The dash reads as a shoulder-first lunge, which is what this sheet draws. It is
            # also an attack variant (`charge`); one sheet, two lists, built once.
            #
            # Compressed 4x and held on the last frame, because the dash is not 1.2 s long.
            # In real gameplay DashExecutor teleports the body with a single MovePosition and
            # its streak and ground wake last 0.14 s; eight charge frames at the normal
            # 0.15 s each would still be lunging a full second after the character arrived.
            # At 4x they run in 0.30 s, which sits just inside the 0.35 s cast window, and the
            # landing pose holds the remainder instead of the lunge starting over.
            ("charge", "knight_charging_sprint", ["dash", "anim_charging_sprint"], {"speed": 4.0, "hold": True}),
        ],
        # The sword-and-shield loadout. NOT a second character and NOT a replacement: it
        # overrides the four states it has art for and the other six keep the unarmed set,
        # which is what an override list buys over a second EntityAssetConfig. It has no
        # hurt, death, cast or recover art and never will -- those six are drawn once,
        # unarmed, and shared.
        "loadouts": [
            ("armed", {
                "idle":   "knight_idle_armed",
                "walk":   "knight_walking_armed",
                "chase":  "knight_running_armed",
                "attack": "knight_attack_1_armed",
            }),
        ],
        "staged": {
            "wave3 knight_*_8f sheets": "the previous dwarf set, superseded wholesale by "
                                        "this one; kept in staging/players/knight/ as the "
                                        "record of what the character looked like before",
        },
    },
    # barbarian -> barbarian. Replaced wholesale by the wave4 set
    # (unity/downloads/assets/barbarian), which arrives as two loadouts and neither
    # is complete: the AXE loadout draws idle, two walks, a run and two overhead
    # attacks; the UNARMED one draws a walk, a run, a punch, a kick and five
    # spellcasts. Nothing in either draws a hurt, a death or a rise.
    #
    # The axe loadout therefore owns everything a player does while holding the axe,
    # and the unarmed casts fill the one slot it cannot reach. That is a deliberate
    # reversal of the wave3 decision to leave `cast` empty, and the trade is worth
    # stating: casting now drops the axe for the length of the spell, where before
    # it fell back to WALKING IN PLACE with five casting animations sitting unused
    # on disk. A cast is the one action where empty hands read as intent -- both
    # hands are doing something in every frame of all five -- and it is rare next to
    # walking and swinging. To go back, delete the `cast` state and `cast_variants`
    # below; nothing else depends on them.
    #
    # The unarmed punch, kick, walk and run stay staged for the opposite reason:
    # PlayerController.NextVariant rotates a variant per swing, so shipping the two
    # unarmed attacks beside the two axe ones would make every other swing drop the
    # weapon -- the same pop, at combat frequency instead of spell frequency.
    "barbarian": {
        "source": "barbarian_wave4",
        "states": {
            "idle":   "barbarian_armed_idle_2",
            # Two takes on the axe walk shipped in the wave. This one keeps the axe
            # inside its own cell in all eight frames and its ground line drifts 4px
            # against the other's 6; `barbarian_armed_walking` stays staged as the
            # alternate take rather than being deleted.
            "walk":   "barbarian_armed_walking_2",
            "chase":  "barbarian_armed_running_2",
            "cast":   "barbarian_spellcasting_1",
            "attack": "barbarian_armed_attack",
        },
        # Rotated per swing by PlayerController.NextVariant; index 0 is the fallback,
        # so the grounded overhead goes first and the leap follows it.
        "variants": [
            ("overhead", "barbarian_armed_attack"),
            ("leap",     "barbarian_armed_attack_2"),
        ],
        # Rotated per cast. spellcasting_1 doubles as the base `cast` slot so the
        # character still casts with real art if the variants are ever lost.
        "cast_variants": [
            ("spell_1", "barbarian_spellcasting_1"),
            ("spell_2", "barbarian_spellcasting_2"),
            ("spell_3", "barbarian_spellcasting_3"),
            ("spell_4", "barbarian_spellcasting_4"),
            ("spell_5", "barbarian_spellcasting_5"),
        ],
        "staged": {
            "barbarian_armed_walking": "the other take on the axe walk; see `walk` "
                                       "above for why this one lost",
            "barbarian_walking / _running / _punch / _kick": "the unarmed loadout's "
                                        "locomotion and melee. Shipping them beside "
                                        "the axe would pop the weapon out of the "
                                        "character's hands on every other swing",
            "damage/death/recover": "NO SOURCE ART EXISTS in either loadout -- all "
                                    "three fall back through EntityAnimationBinder. "
                                    "GrayscaleDeath still greys the corpse, so death "
                                    "reads, but the pose does not sell it",
            "wave3 barbarian_axe_* sheets": "the previous barbarian set, superseded "
                                            "wholesale by this one; kept in "
                                            "staging/players/barbarian/ as the record "
                                            "of what the character looked like before",
        },
    },
    # elf -> elven. Replaced wholesale by the wave4 set (unity/downloads/assets), which
    # is the only character where every slot has purpose-drawn art AND there is art left
    # over: three punches, three spellcasts and a rise from the floor.
    "elven": {
        # Two staging folders now: wave4 drew the whole unarmed character, wave5 added
        # the archer and bard loadouts. They disagree on which way the art faces, which
        # is why facing is per SHEET rather than per wave -- see EAST_FACING_SHEETS.
        "source": ["elf_wave4", "elf_wave5"],
        "states": {
            "idle":    "elf_idle",
            "walk":    "elf_walking",
            "chase":   "elf_run",
            "cast":    "elf_spellcasting_3",
            "attack":  "elf_punch",
            "damage":  "elf_hit_reaction",
            "death":   "elf_die",
            # The eighth state. DeathSequenceController.ReviveRoutine plays it once the
            # body is solid again and the corpse is gone.
            "recover": "elf_knockdown_recovery",
        },
        # Rotated per swing by PlayerController.NextVariant. Index 0 is the fallback, so
        # the plain punch goes first and the two showier moves follow.
        #
        # `bow` is the wave5 archer swing, and shipping it here is a DELIBERATE reversal
        # of the rule the barbarian entry above states: rotating a variant per swing means
        # the bow appears in the elf's empty hands every fourth attack and vanishes again.
        # It was asked for explicitly, so the trade is taken with its eyes open -- and it
        # goes LAST, so index 0 stays the unarmed punch that matches the idle pose.
        "variants": [
            ("punch",     "elf_punch"),
            ("kick",      "elf_kick_1"),
            ("run_punch", "elf_run_punch"),
            ("bow",       "elf_archer_attack"),
        ],
        # Rotated per cast the same way. spellcasting_3 doubles as the base `cast` slot so
        # a character that somehow loses its variants still casts with real art.
        #
        # The five unarmed casts come first and the four wave5 loadout casts follow, for
        # the same reason the bow goes last: the front of the list is what a fallback
        # reaches for. `summon_bow` and `summon_lute` are the two sheets that draw the
        # weapon being conjured out of nothing, which is the one place a weapon appearing
        # in an empty hand is the animation rather than a glitch in it.
        "cast_variants": [
            ("spell_3", "elf_spellcasting_3"),
            ("spell_1", "elf_spellcasting_1"),
            ("spell_2", "elf_spellcasting_2"),
            ("spell_4", "elf_spellcasting_4"),
            ("spell_5", "elf_spellcasting_5"),
            ("summon_bow",  "elf_archer_cast"),
            ("summon_lute", "elf_bard_cast"),
            ("bard_1",      "elf_bard_spellcasting_1"),
            ("bard_2",      "elf_bard_spellcasting_2"),
        ],
        "staged": {
            "wave3 elf_* sheets": "the previous elven set, superseded wholesale by this "
                                  "one; kept in staging/players/elf/ as the record of what "
                                  "the character looked like before",
            "elf_archer_idle / _walking / _running": "the archer loadout's LOCOMOTION. "
                                  "EntityAnimationBinder builds variant lists for exactly "
                                  "two states -- Attack and Cast -- and PlayerController."
                                  "NextVariant only rotates one per action, so an idle or "
                                  "walk variant has no selector and would never render a "
                                  "frame. Shipping them needs a loadout system, not an "
                                  "import",
            "elf_bard_idle / _walking / _walking_2 / _running": "the bard loadout's "
                                  "locomotion; same reason as the archer's",
        },
    },
}


# ── Geometry ──────────────────────────────────────────────────────────────────

# A row of the body counts as "standing on something" once it is at least this
# fraction of the body's widest row. Boots seen from the side are a broad shape;
# an axe blade sweeping through the floor, a trailing cape tip and a thrown-back
# leg are all slivers. See foot_line() for why the distinction decides the anchor.
FOOT_WIDTH_FRACTION = 0.15


def body_mask(patch: np.ndarray):
    """The largest solid component -- the body, not the debris beside it."""
    solid = patch[..., 3] >= ALPHA_SOLID
    labels, n = ndimage.label(solid, structure=np.ones((3, 3)))
    if n == 0:
        return None
    biggest = int(np.bincount(labels.ravel())[1:].argmax()) + 1
    return labels == biggest


def own_object_only(patch: np.ndarray, cell: tuple[float, float, float, float],
                    x0: int, y0: int):
    """Erase whatever inside the box belongs to a NEIGHBOURING frame.

    A box is one frame's own bounding rectangle and these sheets overlap: the next
    pose's axe blade reaches back across the boundary into this one's rectangle, so
    cutting the rectangle out of the sheet brings a floating steel crescent with it.
    It is not a corner case -- measured over the shipped sheets, the knight's
    charging sprint carries a neighbour's boot worth 7% of its own mass and the
    barbarian's axe run carries 1636px of the next frame's blade. Both are baked
    into the sprites the game ships today.

    Ownership is decided the way ``slice_prop_sheet.py`` decides it: cores first,
    then every soft pixel joins the core it is NEAREST, so a blade's anti-aliased
    edge leaves with the blade. Two things are kept:

    * the frame's own object -- the largest core, by construction, since the box
      was built around it;
    * any other core that is fully INSIDE the box and whose centroid falls inside
      this frame's own CELL -- both axes of it. That is the dust a sprint kicks up
      and the sparks a slam throws: disconnected from the body, but this frame's.
      A neighbour's fragment fails both halves -- it is centred one cell over, and
      it is a piece of a body that continues past the box, so it runs into the
      box's edge. The edge test is what the cell test alone misses: on the axe run
      the frames overlap so far that a sliver of the next blade lands inside this
      cell, and it survived a centroid-only rule as a floating shard beside the
      runner.

    The cell test has to be two-dimensional, and on a 4x2 sheet that is not
    pedantry. The archer's bow is taller than the gap between the two rows, so on
    ``elf_archer_attack`` the bow drawn in the row ABOVE reaches down into this
    frame's box, in the SAME COLUMN -- an x-only test waves it straight through,
    and it shipped as a brown arc floating over the archer's head in two frames of
    eight. Nothing else in the wave is tall enough to cross a row, which is why it
    took a bow to find it.

    Returns the cleaned patch and the core pixels dropped, so a sheet whose object
    is genuinely in two pieces reports loudly instead of losing half of itself.
    """
    core = patch[..., 3] >= ALPHA_SOLID
    labels, n = ndimage.label(core, structure=np.ones((3, 3)))
    if n <= 1:
        return patch, 0

    counts = np.bincount(labels.ravel())[1:]
    mine = int(counts.argmax()) + 1
    keep_labels = {mine}
    x_lo, x_hi, y_lo, y_hi = cell
    height, width = labels.shape
    for lab in range(1, n + 1):
        if lab == mine:
            continue
        ys, xs = np.nonzero(labels == lab)
        touches_edge = (xs.min() == 0 or xs.max() == width - 1
                        or ys.min() == 0 or ys.max() == height - 1)
        in_cell = (x_lo <= x0 + float(xs.mean()) < x_hi
                   and y_lo <= y0 + float(ys.mean()) < y_hi)
        if not touches_edge and in_cell:
            keep_labels.add(lab)

    dropped = int(sum(counts[lab - 1] for lab in range(1, n + 1) if lab not in keep_labels))
    if dropped == 0:
        return patch, 0

    _, (iy, ix) = ndimage.distance_transform_edt(labels == 0, return_indices=True)
    owner = np.where(labels == 0, labels[iy, ix], labels)
    keep = np.isin(owner, list(keep_labels))

    out = patch.copy()
    out[..., 3] = np.where(keep, out[..., 3], 0)
    # A transparent pixel keeps whatever RGB the generator left in it; zeroing it
    # stops the atlas packer's alpha dilation from smearing a neighbour's steel
    # back over this frame's silhouette.
    out[..., :3] = np.where(out[..., 3:4] == 0, 0, out[..., :3])
    return out, dropped


def body_box(patch: np.ndarray):
    """Bounding box of the largest solid component -- the body, not its debris."""
    mask = body_mask(patch)
    if mask is None:
        return None
    ys, xs = np.nonzero(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def foot_line(patch: np.ndarray):
    """The row the character is STANDING on, which is not its lowest pixel.

    The weapon is held, so it belongs to the same connected component as the
    body: on a downward swing the axe head reaches the floor and the body's
    bounding box bottom follows it several dozen pixels past the boots. Anchoring
    the state on that made the whole character float for the rest of the swing --
    visible as the barbarian's overhead attack lifting off the ground halfway
    through, and the elf's jump attack never coming back down.

    So the anchor is the lowest row with real horizontal EXTENT, not the lowest
    row with any pixel at all. A pair of boots is wide; a blade edge, a cape tip
    and an outstretched leg are not. A frame that is genuinely airborne has no
    wide row down there either, so it anchors high and floats -- which is the
    point of a jump.
    """
    mask = body_mask(patch)
    if mask is None:
        return None
    widths = mask.sum(axis=1)
    threshold = max(3.0, widths.max() * FOOT_WIDTH_FRACTION)
    rows = np.nonzero(widths >= threshold)[0]
    if rows.size == 0:
        return None
    return int(rows.max()) + 1


def infer_grid(items, sheet_h):
    """(rows, cols) from the vertical gaps between item centres.

    Returns None when the rows come out ragged, which means the sheet is not the
    even grid every alignment step below assumes.
    """
    heights = sorted(b["sheet_box"][3] - b["sheet_box"][1] for b in items)
    median_h = heights[len(heights) // 2]

    # Key on the centre alone -- a tuple sort would fall through to comparing the
    # item dicts whenever two centres tie, which they do on any even row.
    centred = sorted(items, key=lambda b: (b["sheet_box"][1] + b["sheet_box"][3]) / 2)
    def cy(b):
        return (b["sheet_box"][1] + b["sheet_box"][3]) / 2

    rows = [[centred[0]]]
    for item in centred[1:]:
        if cy(item) - cy(rows[-1][-1]) > median_h * 0.6:
            rows.append([item])
        else:
            rows[-1].append(item)

    counts = {len(r) for r in rows}
    if len(counts) != 1:
        return None
    cols = counts.pop()
    if len(rows) * cols != len(items):
        return None
    return len(rows), cols


def build_state(slices_root: str, stem: str):
    """Aligned frames for one animation state, at source resolution."""
    with open(os.path.join(slices_root, f"{stem}.slices.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)

    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    sheet_h, sheet_w = sheet.shape[:2]
    items = manifest["items"]

    grid = infer_grid(items, sheet_h)
    if grid is None:
        raise SystemExit(f"{stem}: rows came out ragged -- not an even grid")
    rows, cols = grid
    cell_w, cell_h = sheet_w / cols, sheet_h / rows

    frames = []
    strays: list[tuple[int, int]] = []
    for item in items:
        x0, y0, x1, y1 = item["sheet_box"]
        # The cell comes from slice_prop_sheet's reading order, not from where the
        # centre lands: a death animation slides its body out of its own cell on
        # purpose, and that translation is the animation.
        index = item["index"]
        row, col = divmod(index, cols)

        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        landed_col, landed_row = int(cx // cell_w), int(cy // cell_h)
        if (landed_row, landed_col) != (row, col):
            print(f"  note: {stem}#{index} sits in cell r{landed_row}c{landed_col}, "
                  f"not its own r{row}c{col} -- the pose translates that far")

        # The object's own pixels only, read back out of the sheet through its box,
        # so a neighbouring frame's weapon never bleeds in. The box is a rectangle
        # and the frames overlap, so the cut alone does not achieve that -- masking
        # to the frame's own object does, and it runs BEFORE the geometry below,
        # because a neighbour's blade lying at boot height would otherwise drag the
        # ground line down with it.
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        patch, stray = own_object_only(
            patch,
            (col * cell_w, (col + 1) * cell_w, row * cell_h, (row + 1) * cell_h),
            x0, y0)
        if stray:
            strays.append((index, stray))
        bb = body_box(patch)
        feet = foot_line(patch)
        if bb is None or feet is None:
            raise SystemExit(f"{stem}#{index} has no solid body")
        frames.append({"index": index, "row": row, "col": col, "patch": patch,
                       "x0": x0, "y0": y0, "body": bb, "feet": feet})

    if strays:
        worst = max(strays, key=lambda s: s[1])
        print(f"  note: {stem} dropped {sum(s[1] for s in strays)}px of neighbouring "
              f"frames across {len(strays)} frames (worst #{worst[0]}: {worst[1]}px)")

    # One ground line per row; one anchor column per cell.
    ground = {}
    for f in frames:
        ground[f["row"]] = max(ground.get(f["row"], 0), f["y0"] + f["feet"])
    for f in frames:
        f["anchor_x"] = (f["col"] + 0.5) * cell_w
        f["anchor_y"] = ground[f["row"]]

    # Canvas: the widest reach any frame needs from its anchor, so every frame of
    # the state shares one geometry.
    left = max(int(round(f["anchor_x"] - f["x0"])) for f in frames)
    right = max(int(round(f["x0"] + f["patch"].shape[1] - f["anchor_x"])) for f in frames)
    up = max(f["anchor_y"] - f["y0"] for f in frames)

    # Nothing is reserved BELOW the ground line, so the canvas bottom IS the ground
    # line. ValkurAssetPostprocessor forces a (0.5, 0) pivot on everything under
    # Art/Characters/, and a pivot only lands on the feet if the feet are the
    # bottom row; reserving space under them would float the whole character by
    # that many pixels, silently and per state. What that clips is the handful of
    # pixels a cape tip or a trailing weapon draws below the boot line -- which in
    # a top-down view is drawn into the floor anyway. It is reported because a
    # large number there would mean the ground line, not the debris, is wrong.
    overhang = max(0, max(f["y0"] + f["patch"].shape[0] - f["anchor_y"] for f in frames))

    canvas_w, canvas_h = left + right, up
    out = []
    for f in sorted(frames, key=lambda x: x["index"]):
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = int(round(f["x0"] - f["anchor_x"] + left))
        py = int(round(f["y0"] - f["anchor_y"] + up))
        ph, pw = f["patch"].shape[:2]
        # Clip the paste to the canvas on every side: py + ph runs past the bottom
        # by `overhang`, and a pose that lunges can run past left/right too.
        sy0, sx0 = max(0, -py), max(0, -px)
        sy1, sx1 = min(ph, canvas_h - py), min(pw, canvas_w - px)
        if sy1 > sy0 and sx1 > sx0:
            canvas[py + sy0:py + sy1, px + sx0:px + sx1] = f["patch"][sy0:sy1, sx0:sx1]
        out.append(canvas)

    # The scale reference is one frame's foot-to-crown height - frame 0 unless
    # REFERENCE_FRAME says otherwise. Every sheet before wave5 opens on a neutral
    # standing pose, and a neutral pose is the only frame whose
    # height means "how big is this character", because the AI rendered each sheet
    # at its own zoom and every later frame is compressed or extended by its pose.
    #
    # The two statistics that look more robust both fail, in opposite directions,
    # and measurably: the tallest box is weapon-inclusive (the axe raised overhead
    # shares a connected component with the hands holding it), so it normalised
    # the barbarian's overhead swing to a 59px character against a 115px idle;
    # the median is dominated by whatever the sheet spends most of its frames
    # doing, so on a death -- four of seven frames lying down -- it took the height
    # of a PRONE body as the standing reference and rendered the knight at 405x263.
    # Frame 0 is upright in both.
    #
    # A sheet that opens mid-pose declares a REFERENCE_FRAME instead, and where even
    # that leaves the character visibly wrong next to its own idle, a SCALE_OVERRIDE.
    ref_index = REFERENCE_FRAME.get(stem, 0)
    ref = min(frames, key=lambda f: abs(f["index"] - ref_index))
    reference_body = ref["feet"] - ref["body"][1]
    return out, reference_body, (rows, cols), overhang


def resample(canvas: np.ndarray, scale: float) -> Image.Image:
    h, w = canvas.shape[:2]
    target = (max(1, round(w * scale)), max(1, round(h * scale)))
    # Premultiplied ('RGBa') so downscaling never averages the zeroed RGB of a
    # transparent pixel into the edges and rings the character with a dark halo.
    img = (Image.fromarray(canvas, "RGBA").convert("RGBa")
           .resize(target, Image.LANCZOS).convert("RGBA"))
    arr = np.array(img)
    arr[arr[..., 3] < 6] = 0
    return Image.fromarray(arr, "RGBA")


# ── Driver ────────────────────────────────────────────────────────────────────

def declared(player: dict, field: str):
    """Normalise a variant declaration to (key, stem, spell_keys).

    A declaration is `(key, stem)` or `(key, stem, [spell, ...])`. The optional third
    element is the DEFAULT reservation: it is what the importer writes when it creates the
    variant, and an authored value on the asset always wins over it, the way
    ``TilesetRulesetImporter`` treats a ruleset's terrain names. Declaring it here rather
    than only in the Inspector is what lets a brand-new variant arrive already pinned to its
    spell -- the alternative is shipping the animation and then remembering to pin it, which
    is exactly the kind of second step that does not happen.
    """
    for row in player.get(field, []):
        key, stem = row[0], row[1]
        spells = list(row[2]) if len(row) > 2 else []
        pacing = dict(row[3]) if len(row) > 3 else {}
        yield key, stem, spells, pacing


def sheets_for(player: dict) -> dict:
    """Every distinct sheet stem this player needs, state slots and variants alike."""
    stems = dict(player["states"])
    for key, stem, _spells, _pacing in declared(player, "variants"):
        stems[f"variant:{key}"] = stem
    for key, stem, _spells, _pacing in declared(player, "cast_variants"):
        stems[f"cast:{key}"] = stem
    for key, states in player.get("loadouts", []):
        for slot, stem in states.items():
            stems[f"loadout:{key}:{slot}"] = stem
    return stems


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("slices_root", help="Directory slice_prop_sheet.py wrote its "
                                        "*.slices.json and crops into")
    ap.add_argument("--dry-run", action="store_true",
                    help="Report what would be written without touching any file")
    ap.add_argument("--only", action="append", metavar="PLAYER",
                    help="Build only these player keys (repeatable). The manifest is then "
                         "MERGED into the one on disk rather than replacing it, so the "
                         "players left out keep the record of how they were built. Exists "
                         "because a character's staging can be mid-wave -- rebuilding every "
                         "player to reship one of them silently reissues the others from "
                         "whatever happens to be staged at that moment.")
    args = ap.parse_args()

    selected = PLAYERS
    if args.only:
        unknown = [k for k in args.only if k not in PLAYERS]
        if unknown:
            print(f"unknown player key(s): {', '.join(unknown)}; known: {', '.join(PLAYERS)}")
            return 2
        selected = {k: v for k, v in PLAYERS.items() if k in args.only}

    manifest = {
        "generator": "tools/atlas/wave3/build_player_frames.py",
        "generatedFrom": os.path.basename(args.slices_root.rstrip("/\\")),
        "targetBodyPx": TARGET_BODY_PX,
        "players": [],
    }
    total_pngs = 0

    for player_key, player in selected.items():
        print(f"\n=== {player_key}  (from staging/players/{player['source']}/) ===")
        out_dir = os.path.join(ART_ROOT, player_key)
        if not args.dry_run:
            os.makedirs(out_dir, exist_ok=True)

        # One sheet can fill a state slot AND a variant (the default attack does
        # both), so build each distinct stem once and reuse the written sprites.
        built: dict[str, list[str]] = {}
        # Each state owns a subfolder under the character. Recorded here because
        # `built` holds only the sprite-name templates, and the manifest path
        # needs the folder too.
        state_dirs: dict[str, str] = {}
        entry = {"playerKey": player_key, "states": [],
                 "attackVariants": [], "castVariants": [], "loadouts": []}

        for slot, stem in sheets_for(player).items():
            if stem in built:
                continue
            canvases, body, (rows, cols), overhang = build_state(args.slices_root, stem)
            scale = (TARGET_BODY_PX / body) * SCALE_OVERRIDE.get(stem, 1.0)
            # Drop the source character prefix and the staging suffixes: the
            # staged name carries a frame count (`_8f`) and sometimes an alternate
            # take marker (`_v2`) that identify a FILE IN downloads/, not a
            # shipped animation state.
            state_name = STATE_NAME_OVERRIDE.get(stem)
            if state_name is None:
                state_name = stem.split("_", 1)[1] if "_" in stem else stem
                state_name = re.sub(r"_\d+f(_v\d+)?$", "", state_name)

            state_dirs[stem] = state_name
            state_out = os.path.join(out_dir, state_name)
            if not args.dry_run:
                os.makedirs(state_out, exist_ok=True)

            source_facing, mirrored_facing = facing_of(stem)
            names = []
            for i, canvas in enumerate(canvases):
                img = resample(canvas, scale)
                for facing, image in ((source_facing, img),
                                      (mirrored_facing, img.transpose(Image.FLIP_LEFT_RIGHT))):
                    name = f"{player_key}_{state_name}_{facing}{i}"
                    if not args.dry_run:
                        image.save(os.path.join(state_out, f"{name}.png"))
                    total_pngs += 1
                names.append(f"{player_key}_{state_name}")
            built[stem] = [f"{player_key}_{state_name}_{{facing}}{i}"
                           for i in range(len(canvases))]
            clipped = round(overhang * scale)
            authored = "west" if source_facing == "w" else "east"
            mirrored = "east" if source_facing == "w" else "west"
            print(f"  {stem:38s} {cols}x{rows} grid, {len(canvases)} frames x2 "
                  f"(authored {authored} + mirrored {mirrored}), body {body}px -> "
                  f"{round(body * scale)}px (x{scale:.3f}), frame "
                  f"{resample(canvases[0], scale).size}"
                  + (f", clipped {clipped}px below the ground line" if clipped else ""))

        def bucket_list(stem: str) -> list[str]:
            """framesPerDirection * 8 sprite names, in S,SE,E,NE,N,NW,W,SW order."""
            templates = built[stem]
            out = []
            for direction in DIRECTIONS:
                facing = BUCKET_FACING[direction]
                for tpl in templates:
                    out.append(f"{ART_ROOT_UNITY}/{player_key}/{state_dirs[stem]}/"
                               f"{tpl.format(facing=facing)}.png")
            return out

        for slot, stem in player["states"].items():
            entry["states"].append({
                "state": slot,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
            })
        for key, stem, spells, pacing in declared(player, "variants"):
            entry["attackVariants"].append({
                "key": key,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
                "spellKeys": spells,
                "animationSpeedMultiplier": pacing.get("speed", 1.0),
                "holdLastFrame": bool(pacing.get("hold", False)),
            })

        for key, stem, spells, pacing in declared(player, "cast_variants"):
            entry["castVariants"].append({
                "key": key,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
                "spellKeys": spells,
                "animationSpeedMultiplier": pacing.get("speed", 1.0),
                "holdLastFrame": bool(pacing.get("hold", False)),
            })

        for key, states in player.get("loadouts", []):
            entry["loadouts"].append({
                "key": key,
                "states": [{
                    "state": slot,
                    "framesPerDirection": len(built[stem]),
                    "sprites": bucket_list(stem),
                } for slot, stem in states.items()],
            })

        entry["stagedNotShipped"] = player["staged"]
        manifest["players"].append(entry)

    if not args.dry_run:
        os.makedirs(os.path.dirname(MANIFEST_PATH), exist_ok=True)
        if args.only and os.path.exists(MANIFEST_PATH):
            # Merge: keep every player this run did not build, in their original order, and
            # replace the ones it did. Rewriting with only the selected players would delete
            # the record of how the others were produced -- the sheets under staging/ are
            # gitignored, so that record is all that survives them.
            with open(MANIFEST_PATH, encoding="utf-8") as fh:
                previous = json.load(fh)
            built_by_key = {pl["playerKey"]: pl for pl in manifest["players"]}
            merged, seen = [], set()
            for old_player in previous.get("players", []):
                pkey = old_player["playerKey"]
                merged.append(built_by_key.get(pkey, old_player))
                seen.add(pkey)
            merged.extend(pl for pl in manifest["players"] if pl["playerKey"] not in seen)
            manifest["players"] = merged
            manifest["generatedFrom"] = (f"{previous.get('generatedFrom')} + "
                                         f"{manifest['generatedFrom']} "
                                         f"({', '.join(sorted(built_by_key))})")
        with open(MANIFEST_PATH, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)
            fh.write("\n")

    verb = "would write" if args.dry_run else "wrote"
    print(f"\n{verb} {total_pngs} PNGs under {os.path.relpath(ART_ROOT, REPO)}")
    print(f"{verb} manifest {os.path.relpath(MANIFEST_PATH, REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
