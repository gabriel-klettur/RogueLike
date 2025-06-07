# Path: src/roguelike_game/ecs/core/spawn_manager.py

from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.factories.monster.cache import _load_caches_once
from roguelike_game.ecs.factories.monster.sprite_loader import create_sprite_component
from roguelike_game.ecs.factories.monster.physics import calculate_position, create_collider_components
from roguelike_game.ecs.factories.monster.config import MONSTER_DEFS
from roguelike_game.ecs.utils.spawn_utils import find_spawn_positions
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
import random
import math
from roguelike_engine.config.config_tiles import TILE_SIZE

# Extra padding para seguridad de spawn en tiles
SPAWN_PADDING_EXTRA = 1

class SpawnNPCManager:
    def __init__(self, world):
        """
        :param world: instancia de ECSWorld (se asume que ya tiene spatial_index, map_manager y buildings).
        """
        self.world = world
        self.map_manager = world.map_manager
        self.buildings = world.buildings

    def spawn_npc_initial(self):
        """
        Lógica de spawn inicial de NPCs, exactamente lo que antes estaba en _spawn_initial_npcs de ECSWorld,
        pero referenciando self.world para crear entidades y asignar componentes.
        """
        # 1) Preparar datos comunes
        lobby_offset = calculate_lobby_offset()
        zone_size = global_map_settings.zone_size

        # 2) Cargar definiciones y sprite base
        _load_caches_once()
        cfg = MONSTER_DEFS["barbol"]
        sprite, _ = create_sprite_component("barbol")
        # Cálculo automático del padding basado en collider de pies
        multi_for_padding = create_collider_components(sprite, cfg)
        feet_for_padding = multi_for_padding.colliders.get("feet")
        # Radio en tiles basado en la dimensión mayor de feet
        radius = max(feet_for_padding.width, feet_for_padding.height) / 2
        neighbor_padding = math.ceil(radius / TILE_SIZE) + SPAWN_PADDING_EXTRA
        spawned_rects = []
        # Variantes de barbol a spawnear
        barbol_variants = [k for k in MONSTER_DEFS if k.startswith("barbol")]

        # 3) Spawn en LOBBY
        positions = find_spawn_positions(self.map_manager, self.buildings, lobby_offset,
                                         zone_size, neighbor_padding=neighbor_padding, sample_count=10)
        print(f"[SpawnManager][Spawn] Lobby: candidatos={len(positions)}")
        for tx, ty in positions:
            variant = random.choice(barbol_variants)
            cfg_var = MONSTER_DEFS[variant]
            sprite_var, _ = create_sprite_component(variant)
            px, py = calculate_position(tx, ty, cfg_var, sprite_var)
            multi_var = create_collider_components(sprite_var, cfg_var)
            # Colisiones de todos los colliders del NPC
            rects_var = [build_collider_rect(px, py, c) for c in multi_var.colliders.values()]
            # Evitar superposición con NPCs previos
            if any(r.colliderect(old) for r in rects_var for old in spawned_rects):
                continue
            # Evitar colisión con edificios
            if any(r.colliderect(brect) for r in rects_var for b in self.buildings for brect in b.collision_tiles):
                continue
            # Evitar colisión con tiles sólidos
            if any(r.colliderect(tile.rect) for r in rects_var for tile in self.map_manager.solid_tiles):
                continue
            # Registrar colisiones del NPC recién spawneado
            spawned_rects.extend(rects_var)
            eid_req = self.world.create_entity()
            self.world.components['SpawnRequest'][eid_req] = SpawnRequest(prototype=variant, position=(tx, ty))

        # 4) Spawn en EMPTY_LEFT (si existe)
        offsets = global_map_settings.zone_offsets
        empty_offset = offsets.get('empty_left')
        if not empty_offset:
            return

        empty_positions = find_spawn_positions(self.map_manager, self.buildings, empty_offset,
                                               zone_size, neighbor_padding=neighbor_padding, sample_count=100)
        print(f"[SpawnManager][Spawn] Empty Left: candidatos={len(empty_positions)}")
        for tx, ty in empty_positions:
            variant = random.choice(barbol_variants)
            cfg_var = MONSTER_DEFS[variant]
            sprite_var, _ = create_sprite_component(variant)
            px, py = calculate_position(tx, ty, cfg_var, sprite_var)
            multi_var = create_collider_components(sprite_var, cfg_var)
            rects_var = [build_collider_rect(px, py, c) for c in multi_var.colliders.values()]
            if any(r.colliderect(old) for r in rects_var for old in spawned_rects):
                continue
            if any(r.colliderect(brect) for r in rects_var for b in self.buildings for brect in b.collision_tiles):
                continue
            if any(r.colliderect(tile.rect) for r in rects_var for tile in self.map_manager.solid_tiles):
                continue
            spawned_rects.extend(rects_var)
            eid_req = self.world.create_entity()
            self.world.components['SpawnRequest'][eid_req] = SpawnRequest(prototype=variant, position=(tx, ty))
