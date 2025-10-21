"""Create entities_payload_archive and backfill from entities.extra_json

Revision ID: b7c9d0e1f2a3
Revises: a6b7c8d9e0f1
Create Date: 2025-10-21

"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
from datetime import datetime, timezone
import json

# revision identifiers, used by Alembic.
revision = "b7c9d0e1f2a3"
down_revision = "a6b7c8d9e0f1"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "entities_payload_archive",
        sa.Column("entity_id", sa.String(), sa.ForeignKey("entities.id"), primary_key=True),
        sa.Column("extra_json", sa.Text(), nullable=True),
        sa.Column("content_hash", sa.String(), nullable=True),
        sa.Column("imported_at", sa.String(), nullable=True),
    )
    op.create_index(
        "ix_entities_payload_archive_hash",
        "entities_payload_archive",
        ["content_hash"],
        unique=False,
    )

    # Backfill from entities.extra_json
    bind = op.get_bind()
    rows = bind.execute(sa.text("SELECT id, extra_json FROM entities")).fetchall()
    now = datetime.now(timezone.utc).isoformat()

    def _hash(s: str | None) -> str | None:
        if not s:
            return None
        try:
            import hashlib
            return hashlib.sha256(s.encode("utf-8")).hexdigest()
        except Exception:
            return None

    values = []
    for ent_id, payload in rows:
        values.append({
            "entity_id": ent_id,
            "extra_json": payload,
            "content_hash": _hash(payload),
            "imported_at": now,
        })

    if values:
        t = sa.table(
            "entities_payload_archive",
            sa.column("entity_id", sa.String()),
            sa.column("extra_json", sa.Text()),
            sa.column("content_hash", sa.String()),
            sa.column("imported_at", sa.String()),
        )
        op.bulk_insert(t, values)


def downgrade() -> None:
    op.drop_index("ix_entities_payload_archive_hash", table_name="entities_payload_archive")
    op.drop_table("entities_payload_archive")
