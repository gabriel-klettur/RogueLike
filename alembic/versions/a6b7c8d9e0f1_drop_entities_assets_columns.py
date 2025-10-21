"""Drop asset columns from entities now that assets live in child tables

Revision ID: a6b7c8d9e0f1
Revises: f2a3b4c5d6e7
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa

# revision identifiers, used by Alembic.
revision = "a6b7c8d9e0f1"
down_revision = "f2a3b4c5d6e7"
branch_labels = None
depends_on = None


ASSET_COLS = [
    # summary
    "assets_active_set",
    # sets config
    "assets_sets_scale_idle","assets_sets_scale_walk","assets_sets_scale_chase","assets_sets_scale_cast",
    "assets_sets_scale_attack","assets_sets_scale_damage","assets_sets_scale_death",
    "assets_sets_tint_r","assets_sets_tint_g","assets_sets_tint_b",
    # no-sets config
    "assets_no_sets_scale_idle","assets_no_sets_scale_walk","assets_no_sets_scale_chase","assets_no_sets_scale_cast",
    "assets_no_sets_scale_attack","assets_no_sets_scale_damage","assets_no_sets_scale_death",
    "assets_no_sets_tint_r","assets_no_sets_tint_g","assets_no_sets_tint_b",
    # no-sets paths by action/direction
    # idle
    "assets_no_sets_idle_s","assets_no_sets_idle_se","assets_no_sets_idle_e","assets_no_sets_idle_ne",
    "assets_no_sets_idle_n","assets_no_sets_idle_nw","assets_no_sets_idle_w","assets_no_sets_idle_sw",
    # walk
    "assets_no_sets_walk_s","assets_no_sets_walk_se","assets_no_sets_walk_e","assets_no_sets_walk_ne",
    "assets_no_sets_walk_n","assets_no_sets_walk_nw","assets_no_sets_walk_w","assets_no_sets_walk_sw",
    # chase
    "assets_no_sets_chase_s","assets_no_sets_chase_se","assets_no_sets_chase_e","assets_no_sets_chase_ne",
    "assets_no_sets_chase_n","assets_no_sets_chase_nw","assets_no_sets_chase_w","assets_no_sets_chase_sw",
    # casting
    "assets_no_sets_casting_s","assets_no_sets_casting_se","assets_no_sets_casting_e","assets_no_sets_casting_ne",
    "assets_no_sets_casting_n","assets_no_sets_casting_nw","assets_no_sets_casting_w","assets_no_sets_casting_sw",
    # attack
    "assets_no_sets_attack_s","assets_no_sets_attack_se","assets_no_sets_attack_e","assets_no_sets_attack_ne",
    "assets_no_sets_attack_n","assets_no_sets_attack_nw","assets_no_sets_attack_w","assets_no_sets_attack_sw",
    # damage
    "assets_no_sets_damage_s","assets_no_sets_damage_se","assets_no_sets_damage_e","assets_no_sets_damage_ne",
    "assets_no_sets_damage_n","assets_no_sets_damage_nw","assets_no_sets_damage_w","assets_no_sets_damage_sw",
    # death
    "assets_no_sets_death_s","assets_no_sets_death_se","assets_no_sets_death_e","assets_no_sets_death_ne",
    "assets_no_sets_death_n","assets_no_sets_death_nw","assets_no_sets_death_w","assets_no_sets_death_sw",
]


def upgrade() -> None:
    for col in ASSET_COLS:
        try:
            op.drop_column("entities", col)
        except Exception:
            # Be resilient if column already missing (e.g., dev DBs)
            pass


def downgrade() -> None:
    # Downgrade re-creates columns as nullable TEXT/NUMERIC defaults for compatibility,
    # but does not repopulate values.
    type_map = {
        # summary
        "assets_active_set": sa.String(),
        # sets config
        "assets_sets_scale_idle": sa.Float(),
        "assets_sets_scale_walk": sa.Float(),
        "assets_sets_scale_chase": sa.Float(),
        "assets_sets_scale_cast": sa.Float(),
        "assets_sets_scale_attack": sa.Float(),
        "assets_sets_scale_damage": sa.Float(),
        "assets_sets_scale_death": sa.Float(),
        "assets_sets_tint_r": sa.Integer(),
        "assets_sets_tint_g": sa.Integer(),
        "assets_sets_tint_b": sa.Integer(),
        # no-sets config
        "assets_no_sets_scale_idle": sa.Float(),
        "assets_no_sets_scale_walk": sa.Float(),
        "assets_no_sets_scale_chase": sa.Float(),
        "assets_no_sets_scale_cast": sa.Float(),
        "assets_no_sets_scale_attack": sa.Float(),
        "assets_no_sets_scale_damage": sa.Float(),
        "assets_no_sets_scale_death": sa.Float(),
        "assets_no_sets_tint_r": sa.Integer(),
        "assets_no_sets_tint_g": sa.Integer(),
        "assets_no_sets_tint_b": sa.Integer(),
    }
    for col in ASSET_COLS:
        try:
            op.add_column("entities", sa.Column(col, type_map.get(col, sa.String()), nullable=True))
        except Exception:
            pass
