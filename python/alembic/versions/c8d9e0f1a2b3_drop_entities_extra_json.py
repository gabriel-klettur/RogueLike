"""Drop extra_json from entities (archived in entities_payload_archive)

Revision ID: c8d9e0f1a2b3
Revises: b7c9d0e1f2a3
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa

# revision identifiers, used by Alembic.
revision = "c8d9e0f1a2b3"
down_revision = "b7c9d0e1f2a3"
branch_labels = None
depends_on = None


def upgrade() -> None:
    try:
        op.drop_column("entities", "extra_json")
    except Exception:
        # Column may already be absent in some dev DBs
        pass


def downgrade() -> None:
    try:
        op.add_column("entities", sa.Column("extra_json", sa.Text(), nullable=True))
    except Exception:
        pass
