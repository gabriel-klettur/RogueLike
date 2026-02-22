"""Drop spawn_table_entries table

Revision ID: 3a1f2b7c1d2e
Revises: 9f2bd5a1e3d4
Create Date: 2025-10-21
"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
from sqlalchemy import inspect

# revision identifiers, used by Alembic.
revision = "3a1f2b7c1d2e"
down_revision = "9f2bd5a1e3d4"
branch_labels = None
depends_on = None


def _table_exists(conn, name: str) -> bool:
    insp = inspect(conn)
    try:
        return name in insp.get_table_names()
    except Exception:  # pragma: no cover
        return False


def upgrade() -> None:
    bind = op.get_bind()
    if _table_exists(bind, "spawn_table_entries"):
        op.drop_table("spawn_table_entries")


def downgrade() -> None:
    bind = op.get_bind()
    if not _table_exists(bind, "spawn_table_entries"):
        op.create_table(
            "spawn_table_entries",
            sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
            sa.Column("spawn_table_id", sa.String(), nullable=False),
            sa.Column("entity_id", sa.String(), nullable=False),
            sa.Column("weight", sa.Integer(), nullable=True),
            sa.Column("min_qty", sa.Integer(), nullable=True),
            sa.Column("max_qty", sa.Integer(), nullable=True),
            sa.ForeignKeyConstraint(["entity_id"], ["entities.id"]),
            sa.PrimaryKeyConstraint("id"),
        )
