"""Add items and item_prices tables

Revision ID: 1a2b3c4d5e6f
Revises: 9f2bd5a1e3d4
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
from sqlalchemy import inspect

# revision identifiers, used by Alembic.
revision = "1a2b3c4d5e6f"
down_revision = "9f2bd5a1e3d4"
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

    # Create items table if not exists
    if not _table_exists(bind, "items"):
        op.create_table(
            "items",
            sa.Column("id", sa.String(), primary_key=True),
            sa.Column("name", sa.String(), nullable=True),
            sa.Column("description", sa.Text(), nullable=True),
            sa.Column("stackable", sa.Boolean(), nullable=True),
            sa.Column("max_stack", sa.Integer(), nullable=True),
            sa.Column("z_layer", sa.Integer(), nullable=True),
            sa.Column("despawn_time", sa.Integer(), nullable=True),
            sa.Column("equip_slot", sa.String(), nullable=True),
            sa.Column("rarity", sa.String(), nullable=True),
            sa.Column("level_requirement", sa.Integer(), nullable=True),
            sa.Column("icon_small", sa.String(), nullable=True),
            sa.Column("icon_large", sa.String(), nullable=True),
            sa.Column("icon_json", sa.Text(), nullable=True),
            sa.Column("extra_json", sa.Text(), nullable=True),
        )

    # Create item_prices table if not exists
    if not _table_exists(bind, "item_prices"):
        op.create_table(
            "item_prices",
            sa.Column("id_item", sa.String(), sa.ForeignKey("items.id"), primary_key=True),
            sa.Column("buy_price", sa.Integer(), nullable=False),
            sa.Column("sell_price", sa.Integer(), nullable=False),
        )


def downgrade() -> None:
    bind = op.get_bind()

    if _table_exists(bind, "item_prices"):
        op.drop_table("item_prices")
    if _table_exists(bind, "items"):
        op.drop_table("items")
