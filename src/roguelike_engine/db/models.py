from __future__ import annotations

"""SQLAlchemy ORM models for the Roguelike project.

The schema is designed for a SQLite MVP while keeping future PostgreSQL migration in mind.
We favor natural keys (slug-like `id` strings) where they already exist in JSON.
Unknown/volatile attributes are preserved in `extra_json` to avoid over-modeling early.
"""

from sqlalchemy import Integer, String, Text, Float, ForeignKey, Boolean
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    """Declarative base for all ORM models."""


class Spell(Base):
    """Spells usable by the player or AI.

    Notes:
        - Tags stored as CSV for SQLite simplicity; can move to an association table later.
        - `extra_json` stores the full JSON definition for forward compatibility.
    """

    __tablename__ = "spells"

    id: Mapped[str] = mapped_column(String, primary_key=True)
    name: Mapped[str] = mapped_column(String)
    type: Mapped[str | None] = mapped_column(String, nullable=True)
    element: Mapped[str | None] = mapped_column(String, nullable=True)
    mana_cost: Mapped[float | None] = mapped_column(Float, nullable=True)
    cooldown_ms: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tags: Mapped[str | None] = mapped_column(String, nullable=True)  # CSV in SQLite
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class Entity(Base):
    """Generic entity definition (hostiles/NPCs) used by spawners and gameplay."""

    __tablename__ = "entities"

    id: Mapped[str] = mapped_column(String, primary_key=True)
    kind: Mapped[str] = mapped_column(String)
    name: Mapped[str] = mapped_column(String)
    level: Mapped[int | None] = mapped_column(Integer, nullable=True)
    hp: Mapped[int | None] = mapped_column(Integer, nullable=True)
    atk: Mapped[int | None] = mapped_column(Integer, nullable=True)
    def_: Mapped[int | None] = mapped_column("def", Integer, nullable=True)
    speed: Mapped[float | None] = mapped_column(Float, nullable=True)
    ai_behavior: Mapped[str | None] = mapped_column(String, nullable=True)
    loot_table_id: Mapped[str | None] = mapped_column(String, nullable=True)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class SpawnerInstance(Base):
    """Spawner instance placed on a map/zone (was `spawners`)."""

    __tablename__ = "spawners_instances"

    id: Mapped[str] = mapped_column(String, primary_key=True)
    map_id: Mapped[str | None] = mapped_column(String, nullable=True)
    x: Mapped[int | None] = mapped_column(Integer, nullable=True)
    y: Mapped[int | None] = mapped_column(Integer, nullable=True)
    radius: Mapped[int | None] = mapped_column(Integer, nullable=True)
    max_count: Mapped[int | None] = mapped_column(Integer, nullable=True)
    respawn_seconds: Mapped[int | None] = mapped_column(Integer, nullable=True)
    conditions_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    spawn_table_id: Mapped[str | None] = mapped_column(String, nullable=True)


class SpawnerTemplate(Base):
    """Spawner template definition migrated from JSON templates.

    Stores raw trigger/policy as JSON and preserves spawn_radius textual form.
    """

    __tablename__ = "spawner_templates"

    id: Mapped[str] = mapped_column(String, primary_key=True)
    spawner_type: Mapped[str | None] = mapped_column(String, nullable=True)
    spawner_shape: Mapped[str | None] = mapped_column(String, nullable=True)
    spawn_radius_text: Mapped[str | None] = mapped_column(String, nullable=True)
    defend_spawn: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    defend_leash: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    visible_in_game: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    trigger_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    policy_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    waves_id: Mapped[str | None] = mapped_column(String, nullable=True)


class SpawnerWaves(Base):
    """Waves sequences migrated from JSON waves catalog.

    Each row stores the spawns for a given `waves_id` and index.
    """

    __tablename__ = "spawner_waves"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    waves_id: Mapped[str] = mapped_column(String)
    idx: Mapped[int] = mapped_column(Integer)
    spawns_json: Mapped[str] = mapped_column(Text)

class BuildingInstance(Base):
    """Placed building instance on a map with references to assets/spawns."""

    __tablename__ = "building_instances"

    instance_id: Mapped[str] = mapped_column(String, primary_key=True)
    image_id: Mapped[str | None] = mapped_column(String, nullable=True)
    spawn_id: Mapped[str | None] = mapped_column(String, nullable=True)
    zone_id: Mapped[str | None] = mapped_column(String, nullable=True)


class BuildingCollision(Base):
    """Collision geometry attached to a building instance.

    `shape_wkt` stores WKT (Well-Known Text) for polygons, circles, etc.
    """

    __tablename__ = "building_collisions"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    instance_id: Mapped[str] = mapped_column(String, ForeignKey("building_instances.instance_id"))
    kind: Mapped[str | None] = mapped_column(String, nullable=True)
    shape_wkt: Mapped[str] = mapped_column(Text)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class ImportLog(Base):
    """Tracks imports for idempotent JSON->DB syncing via content hashes."""

    __tablename__ = "import_log"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    source_path: Mapped[str] = mapped_column(String)
    content_hash: Mapped[str] = mapped_column(String)
    imported_at: Mapped[str] = mapped_column(String)  # ISO-8601 string
    row_count: Mapped[int] = mapped_column(Integer)
    version: Mapped[str | None] = mapped_column(String, nullable=True)


class Item(Base):
    """Item catalog definition migrated from JSON files.

    Minimal stable columns are modeled; the rest is preserved in `extra_json`.
    Icons may come as small/large or as a list; we store structured forms in
    dedicated columns and keep the full payload in `extra_json`.
    """

    __tablename__ = "items"

    id: Mapped[str] = mapped_column(String, primary_key=True)
    name: Mapped[str | None] = mapped_column(String, nullable=True)
    description: Mapped[str | None] = mapped_column(Text, nullable=True)
    stackable: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    max_stack: Mapped[int | None] = mapped_column(Integer, nullable=True)
    z_layer: Mapped[int | None] = mapped_column(Integer, nullable=True)
    despawn_time: Mapped[int | None] = mapped_column(Integer, nullable=True)
    equip_slot: Mapped[str | None] = mapped_column(String, nullable=True)
    rarity: Mapped[str | None] = mapped_column(String, nullable=True)
    level_requirement: Mapped[int | None] = mapped_column(Integer, nullable=True)
    icon_small: Mapped[str | None] = mapped_column(String, nullable=True)
    icon_large: Mapped[str | None] = mapped_column(String, nullable=True)
    icon_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class ItemPrice(Base):
    """Item prices table with a single currency (gold) for now.

    Columns requested: id_item, buy_price, sell_price.
    """

    __tablename__ = "item_prices"

    id_item: Mapped[str] = mapped_column(String, ForeignKey("items.id"), primary_key=True)
    buy_price: Mapped[int] = mapped_column(Integer)
    sell_price: Mapped[int] = mapped_column(Integer)
