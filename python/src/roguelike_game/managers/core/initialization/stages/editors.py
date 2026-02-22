from __future__ import annotations

from roguelike_game.managers.editors.buildings_editor_manager import (
    BuildingEditorManager,
)
from roguelike_game.managers.editors.entities_editor_manager import (
    EntitiesEditorManager,
)
from roguelike_game.managers.editors.inventory_editor_manager import (
    InventoryEditorManager,
)
from roguelike_game.managers.editors.items_editor_manager import ItemsEditorManager
from roguelike_game.managers.editors.map_editor_manager import MapEditorManager
from roguelike_game.managers.editors.particles_editor_manager import (
    ParticlesEditorManager,
)
from roguelike_game.managers.editors.spawner_editor_manager import (
    SpawnerEditorManager,
)
from roguelike_game.managers.editors.spells_editor_manager import (
    SpellsEditorManager,
)
from roguelike_game.managers.editors.tiles_editor_manager import TilesEditorManager
from roguelike_game.managers.editors.lighting_editor_manager import (
    LightingEditorManager,
)

from ..types import InitContext


def init_buildings_editor(ctx: InitContext) -> None:
    ctx.game.buildings_editor = BuildingEditorManager(ctx.game)


def init_tile_editor(ctx: InitContext) -> None:
    ctx.game.tiles_editor = TilesEditorManager(ctx.game)


def init_map_editor(ctx: InitContext) -> None:
    ctx.game.map_editor = MapEditorManager(ctx.game)


def init_inventory_editor(ctx: InitContext) -> None:
    ctx.game.inventory_editor = InventoryEditorManager(ctx.game)


def init_entities_editor(ctx: InitContext) -> None:
    ctx.game.entities_editor = EntitiesEditorManager(ctx.game)


def init_spells_editor(ctx: InitContext) -> None:
    ctx.game.spells_editor = SpellsEditorManager(ctx.game)


def init_spawner_editor(ctx: InitContext) -> None:
    ctx.game.spawner_editor = SpawnerEditorManager(ctx.game)


def init_particles_editor(ctx: InitContext) -> None:
    ctx.game.particles_editor = ParticlesEditorManager(ctx.game)


def init_item_editor(ctx: InitContext) -> None:
    ctx.game.item_editor = ItemsEditorManager(ctx.game)


def init_lighting_editor(ctx: InitContext) -> None:
    ctx.game.lighting_editor = LightingEditorManager(ctx.game)
