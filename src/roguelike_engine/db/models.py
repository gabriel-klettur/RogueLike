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

    # Flattened stats (hostiles/neutrals)
    faction: Mapped[str | None] = mapped_column(String, nullable=True)
    aggro_range: Mapped[int | None] = mapped_column(Integer, nullable=True)
    melee_range: Mapped[int | None] = mapped_column(Integer, nullable=True)
    melee_damage: Mapped[int | None] = mapped_column(Integer, nullable=True)
    melee_cooldown: Mapped[float | None] = mapped_column(Float, nullable=True)
    power: Mapped[int | None] = mapped_column(Integer, nullable=True)
    damage_duration: Mapped[float | None] = mapped_column(Float, nullable=True)
    chasing_speed: Mapped[float | None] = mapped_column(Float, nullable=True)
    feet_width_factor: Mapped[float | None] = mapped_column(Float, nullable=True)
    feet_height_factor: Mapped[float | None] = mapped_column(Float, nullable=True)
    spawn_padding: Mapped[int | None] = mapped_column(Integer, nullable=True)
    spawn_count: Mapped[int | None] = mapped_column(Integer, nullable=True)
    spawn_margin: Mapped[int | None] = mapped_column(Integer, nullable=True)
    death_dissapear_time: Mapped[float | None] = mapped_column(Float, nullable=True)
    damage_stop_probability: Mapped[float | None] = mapped_column(Float, nullable=True)
    chat_range: Mapped[int | None] = mapped_column(Integer, nullable=True)

    # Flattened player-specific stats
    max_strength: Mapped[int | None] = mapped_column(Integer, nullable=True)
    max_intelligence: Mapped[int | None] = mapped_column(Integer, nullable=True)
    max_dexterity: Mapped[int | None] = mapped_column(Integer, nullable=True)
    initial_strength: Mapped[int | None] = mapped_column(Integer, nullable=True)
    initial_intelligence: Mapped[int | None] = mapped_column(Integer, nullable=True)
    initial_dexterity: Mapped[int | None] = mapped_column(Integer, nullable=True)
    basic_speed: Mapped[float | None] = mapped_column(Float, nullable=True)
    basic_attack: Mapped[int | None] = mapped_column(Integer, nullable=True)
    basic_armor: Mapped[int | None] = mapped_column(Integer, nullable=True)
    basic_death_timer_duration: Mapped[float | None] = mapped_column(Float, nullable=True)
    drag_drop_range: Mapped[int | None] = mapped_column(Integer, nullable=True)
    dash_charges: Mapped[int | None] = mapped_column(Integer, nullable=True)
    mana_regen_per_second: Mapped[float | None] = mapped_column(Float, nullable=True)
    attack_duration: Mapped[float | None] = mapped_column(Float, nullable=True)

    # basic_trail sub-structure
    trail_interval: Mapped[float | None] = mapped_column(Float, nullable=True)
    trail_life_time: Mapped[float | None] = mapped_column(Float, nullable=True)
    trail_max_trails: Mapped[int | None] = mapped_column(Integer, nullable=True)

    # Patrol flattening
    patrol_id: Mapped[str | None] = mapped_column(String, nullable=True)
    patrol_radius_tiles: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_points: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_clockwise: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    patrol_width_tiles: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_height_tiles: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_points_per_edge: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_segments: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_step_tiles: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_amplitude_tiles: Mapped[int | None] = mapped_column(Integer, nullable=True)
    patrol_axis: Mapped[str | None] = mapped_column(String, nullable=True)



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
    # Icons
    icon_small: Mapped[str | None] = mapped_column(String, nullable=True)
    icon_large: Mapped[str | None] = mapped_column(String, nullable=True)
    icon_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    # Normalized gameplay columns (migrated from extra_json)
    threshold: Mapped[int | None] = mapped_column(Integer, nullable=True)
    experience: Mapped[int | None] = mapped_column(Integer, nullable=True)
    effect: Mapped[str | None] = mapped_column(String, nullable=True)
    durability: Mapped[int | None] = mapped_column(Integer, nullable=True)
    damage: Mapped[int | None] = mapped_column(Integer, nullable=True)
    attack_speed: Mapped[float | None] = mapped_column(Float, nullable=True)
    range: Mapped[int | None] = mapped_column(Integer, nullable=True)
    crit_chance: Mapped[float | None] = mapped_column(Float, nullable=True)
    crit_multiplier: Mapped[float | None] = mapped_column(Float, nullable=True)
    weight: Mapped[float | None] = mapped_column(Float, nullable=True)
    value: Mapped[int | None] = mapped_column(Integer, nullable=True)
    quest_id: Mapped[str | None] = mapped_column(String, nullable=True)
    # Scales
    scale_editor: Mapped[float | None] = mapped_column(Float, nullable=True)
    scale_map: Mapped[float | None] = mapped_column(Float, nullable=True)
    scale_inventory: Mapped[float | None] = mapped_column(Float, nullable=True)


class ItemPrice(Base):
    """Item prices table with a single currency (gold) for now.

    Columns requested: id_item, buy_price, sell_price.
    """

    __tablename__ = "item_prices"

    id_item: Mapped[str] = mapped_column(String, ForeignKey("items.id"), primary_key=True)
    buy_price: Mapped[int] = mapped_column(Integer)
    sell_price: Mapped[int] = mapped_column(Integer)


class EntityAssetSet(Base):
    """Assets for entities when using 'sets.sprites_set' (lists per action).

    One row per (entity, action, idx). Direction is optional for future-proofing.
    Scale/tint columns duplicate the per-action config for convenient querying.
    """

    __tablename__ = "entities_assets_set"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    entity_id: Mapped[str] = mapped_column(String, ForeignKey("entities.id"))
    action: Mapped[str | None] = mapped_column(String, nullable=True)
    direction: Mapped[str | None] = mapped_column(String, nullable=True)
    idx: Mapped[int | None] = mapped_column(Integer, nullable=True)
    path: Mapped[str | None] = mapped_column(String, nullable=True)
    scale: Mapped[float | None] = mapped_column(Float, nullable=True)
    tint_r: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tint_g: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tint_b: Mapped[int | None] = mapped_column(Integer, nullable=True)


class EntityPayloadArchive(Base):
    """Archive of full JSON payloads for entities.

    Keeps a copy of the original JSON per entity as a safety net when
    we progressively flatten schema. This allows dropping `entities.extra_json`
    without losing information.
    """

    __tablename__ = "entities_payload_archive"

    entity_id: Mapped[str] = mapped_column(String, ForeignKey("entities.id"), primary_key=True)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    content_hash: Mapped[str | None] = mapped_column(String, nullable=True)
    imported_at: Mapped[str | None] = mapped_column(String, nullable=True)


class EntityAssetNoSet(Base):
    """Assets for entities when using 'assets.no-sets' (single path per direction).

    One row per (entity, action, direction). Scale/tint per action are repeated
    in each row for easy filtering/joins.
    """

    __tablename__ = "entities_assets_no_set"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    entity_id: Mapped[str] = mapped_column(String, ForeignKey("entities.id"))
    action: Mapped[str | None] = mapped_column(String, nullable=True)
    direction: Mapped[str | None] = mapped_column(String, nullable=True)
    path: Mapped[str | None] = mapped_column(String, nullable=True)
    scale: Mapped[float | None] = mapped_column(Float, nullable=True)
    tint_r: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tint_g: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tint_b: Mapped[int | None] = mapped_column(Integer, nullable=True)
