"""Flatten entities: add stats, patrol, and assets summary columns with backfill

Revision ID: d3e5f7a9b1c2
Revises: 1a2b3c4d5e6f
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
import json

# revision identifiers, used by Alembic.
revision = "d3e5f7a9b1c2"
down_revision = "1a2b3c4d5e6f"
branch_labels = None
depends_on = None


def upgrade() -> None:
    # Add flattened columns (all nullable)
    cols = [
        ("faction", sa.String()),
        ("aggro_range", sa.Integer()),
        ("melee_range", sa.Integer()),
        ("melee_damage", sa.Integer()),
        ("melee_cooldown", sa.Float()),
        ("power", sa.Integer()),
        ("damage_duration", sa.Float()),
        ("chasing_speed", sa.Float()),
        ("feet_width_factor", sa.Float()),
        ("feet_height_factor", sa.Float()),
        ("spawn_padding", sa.Integer()),
        ("spawn_count", sa.Integer()),
        ("spawn_margin", sa.Integer()),
        ("death_dissapear_time", sa.Float()),
        ("damage_stop_probability", sa.Float()),
        ("chat_range", sa.Integer()),
        # player-specific
        ("max_strength", sa.Integer()),
        ("max_intelligence", sa.Integer()),
        ("max_dexterity", sa.Integer()),
        ("initial_strength", sa.Integer()),
        ("initial_intelligence", sa.Integer()),
        ("initial_dexterity", sa.Integer()),
        ("basic_speed", sa.Float()),
        ("basic_attack", sa.Integer()),
        ("basic_armor", sa.Integer()),
        ("basic_death_timer_duration", sa.Float()),
        ("drag_drop_range", sa.Integer()),
        ("dash_charges", sa.Integer()),
        ("mana_regen_per_second", sa.Float()),
        ("attack_duration", sa.Float()),
        ("trail_interval", sa.Float()),
        ("trail_life_time", sa.Float()),
        ("trail_max_trails", sa.Integer()),
        # patrol
        ("patrol_id", sa.String()),
        ("patrol_radius_tiles", sa.Integer()),
        ("patrol_points", sa.Integer()),
        ("patrol_clockwise", sa.Boolean()),
        ("patrol_width_tiles", sa.Integer()),
        ("patrol_height_tiles", sa.Integer()),
        ("patrol_points_per_edge", sa.Integer()),
        ("patrol_segments", sa.Integer()),
        ("patrol_step_tiles", sa.Integer()),
        ("patrol_amplitude_tiles", sa.Integer()),
        ("patrol_axis", sa.String()),
        # assets summary
        ("assets_active_set", sa.String()),
        # sets.sprites_data_set
        ("assets_sets_scale_idle", sa.Float()),
        ("assets_sets_scale_walk", sa.Float()),
        ("assets_sets_scale_chase", sa.Float()),
        ("assets_sets_scale_cast", sa.Float()),
        ("assets_sets_scale_attack", sa.Float()),
        ("assets_sets_scale_damage", sa.Float()),
        ("assets_sets_scale_death", sa.Float()),
        ("assets_sets_tint_r", sa.Integer()),
        ("assets_sets_tint_g", sa.Integer()),
        ("assets_sets_tint_b", sa.Integer()),
        # no-sets.sprites_data_no-set
        ("assets_no_sets_scale_idle", sa.Float()),
        ("assets_no_sets_scale_walk", sa.Float()),
        ("assets_no_sets_scale_chase", sa.Float()),
        ("assets_no_sets_scale_cast", sa.Float()),
        ("assets_no_sets_scale_attack", sa.Float()),
        ("assets_no_sets_scale_damage", sa.Float()),
        ("assets_no_sets_scale_death", sa.Float()),
        ("assets_no_sets_tint_r", sa.Integer()),
        ("assets_no_sets_tint_g", sa.Integer()),
        ("assets_no_sets_tint_b", sa.Integer()),
        # no-sets sprite paths (idle)
        ("assets_no_sets_idle_s", sa.String()),
        ("assets_no_sets_idle_se", sa.String()),
        ("assets_no_sets_idle_e", sa.String()),
        ("assets_no_sets_idle_ne", sa.String()),
        ("assets_no_sets_idle_n", sa.String()),
        ("assets_no_sets_idle_nw", sa.String()),
        ("assets_no_sets_idle_w", sa.String()),
        ("assets_no_sets_idle_sw", sa.String()),
        # walk
        ("assets_no_sets_walk_s", sa.String()),
        ("assets_no_sets_walk_se", sa.String()),
        ("assets_no_sets_walk_e", sa.String()),
        ("assets_no_sets_walk_ne", sa.String()),
        ("assets_no_sets_walk_n", sa.String()),
        ("assets_no_sets_walk_nw", sa.String()),
        ("assets_no_sets_walk_w", sa.String()),
        ("assets_no_sets_walk_sw", sa.String()),
        # chase
        ("assets_no_sets_chase_s", sa.String()),
        ("assets_no_sets_chase_se", sa.String()),
        ("assets_no_sets_chase_e", sa.String()),
        ("assets_no_sets_chase_ne", sa.String()),
        ("assets_no_sets_chase_n", sa.String()),
        ("assets_no_sets_chase_nw", sa.String()),
        ("assets_no_sets_chase_w", sa.String()),
        ("assets_no_sets_chase_sw", sa.String()),
        # casting
        ("assets_no_sets_casting_s", sa.String()),
        ("assets_no_sets_casting_se", sa.String()),
        ("assets_no_sets_casting_e", sa.String()),
        ("assets_no_sets_casting_ne", sa.String()),
        ("assets_no_sets_casting_n", sa.String()),
        ("assets_no_sets_casting_nw", sa.String()),
        ("assets_no_sets_casting_w", sa.String()),
        ("assets_no_sets_casting_sw", sa.String()),
        # attack
        ("assets_no_sets_attack_s", sa.String()),
        ("assets_no_sets_attack_se", sa.String()),
        ("assets_no_sets_attack_e", sa.String()),
        ("assets_no_sets_attack_ne", sa.String()),
        ("assets_no_sets_attack_n", sa.String()),
        ("assets_no_sets_attack_nw", sa.String()),
        ("assets_no_sets_attack_w", sa.String()),
        ("assets_no_sets_attack_sw", sa.String()),
        # damage
        ("assets_no_sets_damage_s", sa.String()),
        ("assets_no_sets_damage_se", sa.String()),
        ("assets_no_sets_damage_e", sa.String()),
        ("assets_no_sets_damage_ne", sa.String()),
        ("assets_no_sets_damage_n", sa.String()),
        ("assets_no_sets_damage_nw", sa.String()),
        ("assets_no_sets_damage_w", sa.String()),
        ("assets_no_sets_damage_sw", sa.String()),
        # death
        ("assets_no_sets_death_s", sa.String()),
        ("assets_no_sets_death_se", sa.String()),
        ("assets_no_sets_death_e", sa.String()),
        ("assets_no_sets_death_ne", sa.String()),
        ("assets_no_sets_death_n", sa.String()),
        ("assets_no_sets_death_nw", sa.String()),
        ("assets_no_sets_death_w", sa.String()),
        ("assets_no_sets_death_sw", sa.String()),
    ]

    for name, typ in cols:
        op.add_column("entities", sa.Column(name, typ, nullable=True))

    # Backfill from extra_json
    bind = op.get_bind()
    res = bind.execute(sa.text("SELECT id, kind, extra_json FROM entities"))

    def _get(d, *keys, default=None):
        cur = d
        for k in keys:
            if cur is None:
                return default
            cur = cur.get(k) if isinstance(cur, dict) else None
        return cur if cur is not None else default

    def _as_int(x):
        try:
            return int(x) if x is not None else None
        except Exception:
            return None

    def _as_float(x):
        try:
            return float(x) if x is not None else None
        except Exception:
            return None

    def _tint_rgb(v):
        if isinstance(v, (list, tuple)) and len(v) == 3:
            return _as_int(v[0]), _as_int(v[1]), _as_int(v[2])
        return None, None, None

    def _copy_dirs(obj, prefix):
        # obj is a dict with keys s,se,e,ne,n,nw,w,sw
        fields = {}
        for d in ("s","se","e","ne","n","nw","w","sw"):
            fields[f"{prefix}_{d}"] = obj.get(d) if isinstance(obj, dict) else None
        return fields

    for row in res:
        ent_id = row[0]
        kind = row[1]
        raw = row[2]
        if not raw:
            continue
        try:
            data = json.loads(raw)
        except Exception:
            continue
        stats = _get(data, "stats", default={}) or {}
        patrol = _get(data, "patrol", default={}) or {}
        assets = _get(data, "assets", default={}) or {}
        sets_data = _get(assets, "sets", "sprites_data_set", default={}) or {}
        no_sets = _get(assets, "no-sets", default={}) or {}
        no_sets_data = _get(no_sets, "sprites_data_no-set", default={}) or {}

        upd = {}
        # stats common
        upd.update({
            "faction": _get(stats, "faction"),
            "aggro_range": _as_int(_get(stats, "aggro_range")),
            "melee_range": _as_int(_get(stats, "melee_range")),
            "melee_damage": _as_int(_get(stats, "melee_damage")),
            "melee_cooldown": _as_float(_get(stats, "melee_cooldown")),
            "power": _as_int(_get(stats, "power")),
            "damage_duration": _as_float(_get(stats, "damage_duration")),
            "chasing_speed": _as_float(_get(stats, "chasing_speed")),
            "feet_width_factor": _as_float(_get(stats, "feet_width_factor")),
            "feet_height_factor": _as_float(_get(stats, "feet_height_factor")),
            "spawn_padding": _as_int(_get(stats, "spawn_padding")),
            "spawn_count": _as_int(_get(stats, "spawn_count")),
            "spawn_margin": _as_int(_get(stats, "spawn_margin")),
            "death_dissapear_time": _as_float(_get(stats, "death_dissapear_time")),
            "damage_stop_probability": _as_float(_get(stats, "damage_stop_probability")),
            "chat_range": _as_int(_get(stats, "chat_range")),
        })
        # player specifics
        upd.update({
            "max_strength": _as_int(_get(stats, "max_strength")),
            "max_intelligence": _as_int(_get(stats, "max_intelligence")),
            "max_dexterity": _as_int(_get(stats, "max_dexterity")),
            "initial_strength": _as_int(_get(stats, "initial_strength")),
            "initial_intelligence": _as_int(_get(stats, "initial_intelligence")),
            "initial_dexterity": _as_int(_get(stats, "initial_dexterity")),
            "basic_speed": _as_float(_get(stats, "basic_speed")),
            "basic_attack": _as_int(_get(stats, "basic_attack")),
            "basic_armor": _as_int(_get(stats, "basic_armor")),
            "basic_death_timer_duration": _as_float(_get(stats, "basic_death_timer_duration")),
            "drag_drop_range": _as_int(_get(stats, "drag_drop_range")),
            "dash_charges": _as_int(_get(stats, "dash_charges")),
            "mana_regen_per_second": _as_float(_get(stats, "mana_regen_per_second")),
            "attack_duration": _as_float(_get(stats, "attack_duration")),
            "trail_interval": _as_float(_get(stats, "basic_trail", "interval")),
            "trail_life_time": _as_float(_get(stats, "basic_trail", "life_time")),
            "trail_max_trails": _as_int(_get(stats, "basic_trail", "max_trails")),
        })
        # patrol
        patrol_id = _get(patrol, "id")
        upd["patrol_id"] = patrol_id
        params = _get(patrol, "params", default={}) or {}
        upd.update({
            "patrol_radius_tiles": _as_int(params.get("radius_tiles")),
            "patrol_points": _as_int(params.get("points")),
            "patrol_clockwise": bool(params.get("clockwise")) if params.get("clockwise") is not None else None,
            "patrol_width_tiles": _as_int(params.get("width_tiles")),
            "patrol_height_tiles": _as_int(params.get("height_tiles")),
            "patrol_points_per_edge": _as_int(params.get("points_per_edge")),
            "patrol_segments": _as_int(params.get("segments")),
            "patrol_step_tiles": _as_int(params.get("step_tiles")),
            "patrol_amplitude_tiles": _as_int(params.get("amplitude_tiles")),
            "patrol_axis": params.get("axis"),
        })
        # assets
        upd["assets_active_set"] = _get(assets, "active_set")
        # sets data
        upd.update({
            "assets_sets_scale_idle": _as_float(_get(sets_data, "scale_idle")),
            "assets_sets_scale_walk": _as_float(_get(sets_data, "scale_walk")),
            "assets_sets_scale_chase": _as_float(_get(sets_data, "scale_chase")),
            "assets_sets_scale_cast": _as_float(_get(sets_data, "scale_cast")),
            "assets_sets_scale_attack": _as_float(_get(sets_data, "scale_attack")),
            "assets_sets_scale_damage": _as_float(_get(sets_data, "scale_damage")),
            "assets_sets_scale_death": _as_float(_get(sets_data, "scale_death")),
        })
        r, g, b = _tint_rgb(_get(sets_data, "tint"))
        upd.update({"assets_sets_tint_r": r, "assets_sets_tint_g": g, "assets_sets_tint_b": b})
        # no-sets data
        upd.update({
            "assets_no_sets_scale_idle": _as_float(_get(no_sets_data, "scale_idle")),
            "assets_no_sets_scale_walk": _as_float(_get(no_sets_data, "scale_walk")),
            "assets_no_sets_scale_chase": _as_float(_get(no_sets_data, "scale_chase")),
            "assets_no_sets_scale_cast": _as_float(_get(no_sets_data, "scale_cast")),
            "assets_no_sets_scale_attack": _as_float(_get(no_sets_data, "scale_attack")),
            "assets_no_sets_scale_damage": _as_float(_get(no_sets_data, "scale_damage")),
            "assets_no_sets_scale_death": _as_float(_get(no_sets_data, "scale_death")),
        })
        r2, g2, b2 = _tint_rgb(_get(no_sets_data, "tint"))
        upd.update({"assets_no_sets_tint_r": r2, "assets_no_sets_tint_g": g2, "assets_no_sets_tint_b": b2})
        # paths
        for action in ("idle","walk","chase","casting","attack","damage","death"):
            obj = _get(no_sets, action, default={}) or {}
            upd.update(_copy_dirs(obj, f"assets_no_sets_{action}"))

        # Build dynamic UPDATE
        set_clause = ", ".join([f"{k} = :{k}" for k in upd.keys()])
        upd["_id"] = ent_id
        bind.execute(sa.text(f"UPDATE entities SET {set_clause} WHERE id = :_id"), upd)

    # Indexes
    op.create_index("ix_entities_kind", "entities", ["kind"], unique=False)
    op.create_index("ix_entities_faction", "entities", ["faction"], unique=False)
    op.create_index("ix_entities_ai_behavior", "entities", ["ai_behavior"], unique=False)


def downgrade() -> None:
    # Drop indexes first
    for ix in ("ix_entities_ai_behavior", "ix_entities_faction", "ix_entities_kind"):
        try:
            op.drop_index(ix, table_name="entities")
        except Exception:
            pass

    # Drop added columns
    col_names = [
        "faction","aggro_range","melee_range","melee_damage","melee_cooldown","power","damage_duration",
        "chasing_speed","feet_width_factor","feet_height_factor","spawn_padding","spawn_count","spawn_margin",
        "death_dissapear_time","damage_stop_probability","chat_range",
        "max_strength","max_intelligence","max_dexterity","initial_strength","initial_intelligence","initial_dexterity",
        "basic_speed","basic_attack","basic_armor","basic_death_timer_duration","drag_drop_range","dash_charges",
        "mana_regen_per_second","attack_duration","trail_interval","trail_life_time","trail_max_trails",
        "patrol_id","patrol_radius_tiles","patrol_points","patrol_clockwise","patrol_width_tiles","patrol_height_tiles",
        "patrol_points_per_edge","patrol_segments","patrol_step_tiles","patrol_amplitude_tiles","patrol_axis",
        "assets_active_set",
        "assets_sets_scale_idle","assets_sets_scale_walk","assets_sets_scale_chase","assets_sets_scale_cast",
        "assets_sets_scale_attack","assets_sets_scale_damage","assets_sets_scale_death","assets_sets_tint_r",
        "assets_sets_tint_g","assets_sets_tint_b",
        "assets_no_sets_scale_idle","assets_no_sets_scale_walk","assets_no_sets_scale_chase","assets_no_sets_scale_cast",
        "assets_no_sets_scale_attack","assets_no_sets_scale_damage","assets_no_sets_scale_death","assets_no_sets_tint_r",
        "assets_no_sets_tint_g","assets_no_sets_tint_b",
        "assets_no_sets_idle_s","assets_no_sets_idle_se","assets_no_sets_idle_e","assets_no_sets_idle_ne",
        "assets_no_sets_idle_n","assets_no_sets_idle_nw","assets_no_sets_idle_w","assets_no_sets_idle_sw",
        "assets_no_sets_walk_s","assets_no_sets_walk_se","assets_no_sets_walk_e","assets_no_sets_walk_ne",
        "assets_no_sets_walk_n","assets_no_sets_walk_nw","assets_no_sets_walk_w","assets_no_sets_walk_sw",
        "assets_no_sets_chase_s","assets_no_sets_chase_se","assets_no_sets_chase_e","assets_no_sets_chase_ne",
        "assets_no_sets_chase_n","assets_no_sets_chase_nw","assets_no_sets_chase_w","assets_no_sets_chase_sw",
        "assets_no_sets_casting_s","assets_no_sets_casting_se","assets_no_sets_casting_e","assets_no_sets_casting_ne",
        "assets_no_sets_casting_n","assets_no_sets_casting_nw","assets_no_sets_casting_w","assets_no_sets_casting_sw",
        "assets_no_sets_attack_s","assets_no_sets_attack_se","assets_no_sets_attack_e","assets_no_sets_attack_ne",
        "assets_no_sets_attack_n","assets_no_sets_attack_nw","assets_no_sets_attack_w","assets_no_sets_attack_sw",
        "assets_no_sets_damage_s","assets_no_sets_damage_se","assets_no_sets_damage_e","assets_no_sets_damage_ne",
        "assets_no_sets_damage_n","assets_no_sets_damage_nw","assets_no_sets_damage_w","assets_no_sets_damage_sw",
        "assets_no_sets_death_s","assets_no_sets_death_se","assets_no_sets_death_e","assets_no_sets_death_ne",
        "assets_no_sets_death_n","assets_no_sets_death_nw","assets_no_sets_death_w","assets_no_sets_death_sw",
    ]
    for name in col_names:
        try:
            op.drop_column("entities", name)
        except Exception:
            pass
