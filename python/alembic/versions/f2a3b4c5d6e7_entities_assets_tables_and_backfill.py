"""Add entities assets tables for sets and no-sets with backfill

Revision ID: f2a3b4c5d6e7
Revises: e7f8a9b0c1d2
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
import json

# revision identifiers, used by Alembic.
revision = "f2a3b4c5d6e7"
down_revision = "e7f8a9b0c1d2"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "entities_assets_set",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("entity_id", sa.String(), sa.ForeignKey("entities.id"), nullable=False),
        sa.Column("action", sa.String(), nullable=True),
        sa.Column("direction", sa.String(), nullable=True),
        sa.Column("idx", sa.Integer(), nullable=True),
        sa.Column("path", sa.String(), nullable=True),
        sa.Column("scale", sa.Float(), nullable=True),
        sa.Column("tint_r", sa.Integer(), nullable=True),
        sa.Column("tint_g", sa.Integer(), nullable=True),
        sa.Column("tint_b", sa.Integer(), nullable=True),
    )
    op.create_index("ix_entities_assets_set_entity", "entities_assets_set", ["entity_id"], unique=False)
    op.create_index("ix_entities_assets_set_entity_action", "entities_assets_set", ["entity_id", "action"], unique=False)

    op.create_table(
        "entities_assets_no_set",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("entity_id", sa.String(), sa.ForeignKey("entities.id"), nullable=False),
        sa.Column("action", sa.String(), nullable=True),
        sa.Column("direction", sa.String(), nullable=True),
        sa.Column("path", sa.String(), nullable=True),
        sa.Column("scale", sa.Float(), nullable=True),
        sa.Column("tint_r", sa.Integer(), nullable=True),
        sa.Column("tint_g", sa.Integer(), nullable=True),
        sa.Column("tint_b", sa.Integer(), nullable=True),
    )
    op.create_index("ix_entities_assets_no_set_entity", "entities_assets_no_set", ["entity_id"], unique=False)
    op.create_index("ix_entities_assets_no_set_entity_action", "entities_assets_no_set", ["entity_id", "action"], unique=False)

    # Backfill from entities.extra_json
    bind = op.get_bind()
    res = bind.execute(sa.text("SELECT id, extra_json FROM entities"))

    def _get(d, *path, default=None):
        cur = d
        for k in path:
            if cur is None:
                return default
            cur = cur.get(k) if isinstance(cur, dict) else None
        return cur if cur is not None else default

    def _as_float(x):
        try:
            return float(x) if x is not None else None
        except Exception:
            return None

    def _as_int(x):
        try:
            return int(x) if x is not None else None
        except Exception:
            return None

    def _tint_rgb(v):
        if isinstance(v, (list, tuple)) and len(v) == 3:
            return _as_int(v[0]), _as_int(v[1]), _as_int(v[2])
        return None, None, None

    DIR_MAP = {
        "north": "n", "south": "s", "east": "e", "west": "w",
        "northeast": "ne", "northwest": "nw", "southeast": "se", "southwest": "sw",
        # pass-through canonical keys handled below
    }

    def _canon_dir(k: str | None) -> str | None:
        if not k:
            return None
        k2 = k.lower()
        return DIR_MAP.get(k2, k2 if k2 in {"s","se","e","ne","n","nw","w","sw"} else None)

    set_rows = []
    no_set_rows = []

    for ent_id, raw in res:
        if not raw:
            continue
        try:
            data = json.loads(raw)
        except Exception:
            continue

        assets = _get(data, "assets", default={}) or {}
        # sets branch
        sets = _get(assets, "sets", default={}) or {}
        sprites_set = _get(sets, "sprites_set", default={}) or {}
        sets_data = _get(sets, "sprites_data_set", default={}) or {}
        # no-sets branch
        no_sets = _get(assets, "no-sets", default={}) or {}
        no_sets_data = _get(no_sets, "sprites_data_no-set", default={}) or {}

        # Precompute tints
        s_tr, s_tg, s_tb = _tint_rgb(_get(sets_data, "tint"))
        n_tr, n_tg, n_tb = _tint_rgb(_get(no_sets_data, "tint"))

        # sets.sprites_set: arrays per action
        for action, items in sprites_set.items():
            act = "casting" if action == "cast" else action
            if not isinstance(items, list):
                continue
            # scale per action if available
            scale = _as_float(_get(sets_data, f"scale_{act}"))
            for idx, path in enumerate(items):
                set_rows.append({
                    "entity_id": ent_id,
                    "action": act,
                    "direction": None,
                    "idx": _as_int(idx),
                    "path": path,
                    "scale": scale,
                    "tint_r": s_tr, "tint_g": s_tg, "tint_b": s_tb,
                })

        # assets.no-sets: actions -> directions
        for action, obj in no_sets.items():
            if action == "sprites_data_no-set":
                continue
            act = "casting" if action == "cast" else action
            if not isinstance(obj, dict):
                continue
            scale = _as_float(_get(no_sets_data, f"scale_{act}"))
            for dir_key, path in obj.items():
                dir_can = _canon_dir(dir_key)
                if dir_can is None:
                    continue
                no_set_rows.append({
                    "entity_id": ent_id,
                    "action": act,
                    "direction": dir_can,
                    "path": path,
                    "scale": scale,
                    "tint_r": n_tr, "tint_g": n_tg, "tint_b": n_tb,
                })

    if set_rows:
        t_set = sa.table(
            "entities_assets_set",
            sa.column("entity_id", sa.String()),
            sa.column("action", sa.String()),
            sa.column("direction", sa.String()),
            sa.column("idx", sa.Integer()),
            sa.column("path", sa.String()),
            sa.column("scale", sa.Float()),
            sa.column("tint_r", sa.Integer()),
            sa.column("tint_g", sa.Integer()),
            sa.column("tint_b", sa.Integer()),
        )
        op.bulk_insert(t_set, set_rows)

    if no_set_rows:
        t_nset = sa.table(
            "entities_assets_no_set",
            sa.column("entity_id", sa.String()),
            sa.column("action", sa.String()),
            sa.column("direction", sa.String()),
            sa.column("path", sa.String()),
            sa.column("scale", sa.Float()),
            sa.column("tint_r", sa.Integer()),
            sa.column("tint_g", sa.Integer()),
            sa.column("tint_b", sa.Integer()),
        )
        op.bulk_insert(t_nset, no_set_rows)


def downgrade() -> None:
    op.drop_index("ix_entities_assets_no_set_entity_action", table_name="entities_assets_no_set")
    op.drop_index("ix_entities_assets_no_set_entity", table_name="entities_assets_no_set")
    op.drop_table("entities_assets_no_set")

    op.drop_index("ix_entities_assets_set_entity_action", table_name="entities_assets_set")
    op.drop_index("ix_entities_assets_set_entity", table_name="entities_assets_set")
    op.drop_table("entities_assets_set")
