"""Rename spawners -> spawners_instances and add spawner_templates + spawner_waves

Revision ID: 9f2bd5a1e3d4
Revises: b6bc709b38e0
Create Date: 2025-10-21
"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
from sqlalchemy import inspect

# revision identifiers, used by Alembic.
revision = "9f2bd5a1e3d4"
down_revision = "b6bc709b38e0"
branch_labels = None
depends_on = None


def _table_exists(conn, name: str) -> bool:
    insp = inspect(conn)
    try:
        return name in insp.get_table_names()
    except Exception:
        return False


def upgrade() -> None:
    bind = op.get_bind()

    # Rename spawners -> spawners_instances if needed
    if _table_exists(bind, "spawners") and not _table_exists(bind, "spawners_instances"):
        op.rename_table("spawners", "spawners_instances")

    # Create spawner_templates if not exists
    if not _table_exists(bind, "spawner_templates"):
        op.create_table(
            "spawner_templates",
            sa.Column("id", sa.String(), primary_key=True),
            sa.Column("spawner_type", sa.String(), nullable=True),
            sa.Column("spawner_shape", sa.String(), nullable=True),
            sa.Column("spawn_radius_text", sa.String(), nullable=True),
            sa.Column("defend_spawn", sa.Boolean(), nullable=True),
            sa.Column("defend_leash", sa.Boolean(), nullable=True),
            sa.Column("visible_in_game", sa.Boolean(), nullable=True),
            sa.Column("trigger_json", sa.Text(), nullable=True),
            sa.Column("policy_json", sa.Text(), nullable=True),
            sa.Column("waves_id", sa.String(), nullable=True),
        )

    # Create spawner_waves if not exists
    if not _table_exists(bind, "spawner_waves"):
        op.create_table(
            "spawner_waves",
            sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
            sa.Column("waves_id", sa.String(), nullable=False),
            sa.Column("idx", sa.Integer(), nullable=False),
            sa.Column("spawns_json", sa.Text(), nullable=False),
        )


def downgrade() -> None:
    bind = op.get_bind()

    # Drop spawner_waves and spawner_templates if they exist
    if _table_exists(bind, "spawner_waves"):
        op.drop_table("spawner_waves")
    if _table_exists(bind, "spawner_templates"):
        op.drop_table("spawner_templates")

    # Rename back spawners_instances -> spawners if appropriate
    if _table_exists(bind, "spawners_instances") and not _table_exists(bind, "spawners"):
        op.rename_table("spawners_instances", "spawners")
