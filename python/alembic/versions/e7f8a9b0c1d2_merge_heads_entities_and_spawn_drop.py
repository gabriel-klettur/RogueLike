"""Merge heads: unify branches (drop spawn_table_entries) and entities flatten

Revision ID: e7f8a9b0c1d2
Revises: 3a1f2b7c1d2e, d3e5f7a9b1c2
Create Date: 2025-10-21

This is a no-op merge revision to resolve multiple heads.
"""
from __future__ import annotations

from alembic import op  # noqa: F401
import sqlalchemy as sa  # noqa: F401

# revision identifiers, used by Alembic.
revision = "e7f8a9b0c1d2"
down_revision = ("3a1f2b7c1d2e", "d3e5f7a9b1c2")
branch_labels = None
depends_on = None


def upgrade() -> None:
    # No-op: this revision only merges heads.
    pass


def downgrade() -> None:
    # No-op: splitting a merge isn't supported automatically.
    pass
