import random
import math

from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.factories.monster.cache import load_caches_for, _SPRITE_SURFACES
from roguelike_game.factories.monster.physics import calculate_position, create_collider_components
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_game.ecs.utils.spawn_utils import find_spawn_positions
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
from typing import Any

import logging
logger = logging.getLogger(__name__)

# Extra padding para seguridad de spawn en tiles
SPAWN_PADDING_EXTRA = 1

class SpawnNPCManager:
    """
    Deprecated: Legacy initial NPC spawn manager.
    All NPCs must be spawned via the JSON spawner system (SpawnerRuntimeSystem + SpawnSystem).
    This class is kept only for backward compatibility and will be removed in a future cleanup.
    """
    def __init__(self, world):
        """
        :param world: instancia de ECSWorld (se asume que ya tiene spatial_index, map_manager y buildings).
        """
        logger.warning(
            "SpawnNPCManager is deprecated; NPCs must be spawned via the JSON spawner system (SpawnerRuntimeSystem)."
        )
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

        # 2) Cargar definiciones y preparar prototipos de colliders
        # Variantes de barbol a spawnear
        barbol_variants = [k for k in MONSTER_DEFS if k.startswith("barbol")]
        # Cargar sprites solo para estas variantes
        load_caches_for(barbol_variants)
        # Precompute prototipos de sprite y colliders
        proto_sprites: dict[str, Any] = {}
        proto_colliders: dict[str, Any] = {}
        from roguelike_game.factories.monster.sprite_loader import create_sprite_component
        for variant in barbol_variants:
            cfg_var = MONSTER_DEFS[variant]
            # Crear un sprite seguro (usa placeholder si no hay assets)
            try:
                sprite, _ = create_sprite_component(variant)
                dummy = type("Proto", (), {})()
                dummy.image = getattr(sprite, 'image', None)
            except Exception:
                import pygame
                dummy = type("Proto", (), {})()
                ph = pygame.Surface((16, 16), pygame.SRCALPHA)
                ph.fill((0, 0, 0, 255))
                dummy.image = ph
            proto_sprites[variant] = dummy
            proto_colliders[variant] = create_collider_components(dummy, cfg_var)

        # Calcular padding de spawn según collider de pies de la variante base
        feet = proto_colliders.get("barbol").colliders.get("feet")
        # Usar la AABB del collider (genérico: rect o círculo) para derivar un radio conservador
        rect = build_collider_rect(0, 0, feet)
        radius = max(rect.width, rect.height) / 2
        neighbor_padding = math.ceil(radius / TILE_SIZE) + SPAWN_PADDING_EXTRA

        spawned_rects = []

        # 3) Spawn en LOBBY
        positions = find_spawn_positions(self.map_manager, self.buildings, lobby_offset,
                                         zone_size, neighbor_padding=neighbor_padding, sample_count=10)
        logger.debug(f" Lobby: candidatos={len(positions)}")
        for tx, ty in positions:
            variant = random.choice(barbol_variants)
            cfg_var = MONSTER_DEFS[variant]
            dummy = proto_sprites[variant]
            px, py = calculate_position(tx, ty, cfg_var, dummy)
            rects_var = [build_collider_rect(px, py, c) for c in proto_colliders[variant].colliders.values()]
            # Evitar colisiones con NPCs previos y elementos estáticos (mapa y edificios)
            blocked = False
            for r in rects_var:
                # Chequear NPCs anteriores
                for old in spawned_rects:
                    if r.colliderect(old):
                        blocked = True
                        break
                if blocked:
                    break
                # Chequear colisiones estáticas vía spatial_index
                for s in self.world.get_solid_tiles_for_rect(r):
                    if r.colliderect(s):
                        blocked = True
                        break
                if blocked:
                    break
            if blocked:
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
        logger.debug(f" Empty Left: candidatos={len(empty_positions)}")
        for tx, ty in empty_positions:
            variant = random.choice(barbol_variants)
            cfg_var = MONSTER_DEFS[variant]
            dummy = proto_sprites[variant]
            px, py = calculate_position(tx, ty, cfg_var, dummy)
            rects_var = [build_collider_rect(px, py, c) for c in proto_colliders[variant].colliders.values()]
            # Evitar colisiones con NPCs previos y elementos estáticos (mapa y edificios)
            blocked = False
            for r in rects_var:
                for old in spawned_rects:
                    if r.colliderect(old):
                        blocked = True
                        break
                if blocked:
                    break
                for s in self.world.get_solid_tiles_for_rect(r):
                    if r.colliderect(s):
                        blocked = True
                        break
                if blocked:
                    break
            if blocked:
                continue
            spawned_rects.extend(rects_var)
            eid_req = self.world.create_entity()
            self.world.components['SpawnRequest'][eid_req] = SpawnRequest(prototype=variant, position=(tx, ty))