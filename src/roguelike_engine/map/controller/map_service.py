import logging
import random
from typing import Optional, List, Tuple, Dict

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.zone.zone_model import Zone
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.loader.factory import get_map_loader
from roguelike_engine.map.model.map_model import Map
from roguelike_engine.map.utils import (
    generate_lobby_matrix,
    calculate_lobby_offset,
    calculate_dungeon_offset,
    find_lobby_exit
)
from roguelike_engine.map.model.generator.dungeon import DungeonGenerator

logger = logging.getLogger(__name__)

class MapService:
    """
    Servicio para generación, carga y fusión de mapas utilizando zonas:
    'lobby', 'dungeon' y un 'world' que agrupa todo.
    """
    def __init__(
        self,
        generator_name: str = "dungeon",
        loader_name: str = "text",
        exporter=None,
    ):
        self.generator = get_generator(generator_name)
        self.loader = get_map_loader(loader_name)
        self.exporter = exporter

    def build_map(self, map_name: Optional[str] = None) -> Map:
        key = map_name or "global_map"
        # 1) Calcular offsets (auto-ajuste si corresponde)
        offsets = global_map_settings.zone_offsets
        # Determine actual keys for base zones in case they were renamed
        # Lobby
        if 'lobby' not in offsets:
            dyn_offs = global_map_settings._dynamic_offsets()
            dyn_lobby = dyn_offs['lobby']
            lobby_key = next((k for k, v in offsets.items() if v == dyn_lobby), None)
            if lobby_key is None:
                raise KeyError("'lobby' not found in zone offsets")
        else:
            lobby_key = 'lobby'
        # Dungeon
        if 'dungeon' not in offsets:
            dyn_offs = global_map_settings._dynamic_offsets()
            dyn_dungeon = dyn_offs['dungeon']
            dungeon_key = next((k for k, v in offsets.items() if v == dyn_dungeon), None)
            if dungeon_key is None:
                raise KeyError("'dungeon' not found in zone offsets")
        else:
            dungeon_key = 'dungeon'
        # 2) Crear zona 'world' con dimensiones actualizadas
        world = Zone(
            key,
            (0, 0),
            global_map_settings.global_width,
            global_map_settings.global_height
        )
        # 3) Generar y colocar 'lobby'
        lobby_rows = generate_lobby_matrix()
        lobby_w = len(lobby_rows[0]) if lobby_rows else global_map_settings.zone_width
        lobby_h = len(lobby_rows)
        lobby = Zone(lobby_key, offsets[lobby_key], lobby_w, lobby_h)
        lobby.set_matrix_from_rows(lobby_rows)
        self._merge_zone_into_world(world, lobby)
        # 4) Generar y colocar 'dungeon'
        raw_map, dungeon_meta = self.generator.generate(
            width=global_map_settings.zone_width,
            height=global_map_settings.zone_height,
            return_rooms=True,
        )
        dungeon = Zone(dungeon_key, offsets[dungeon_key], global_map_settings.zone_width, global_map_settings.zone_height)
        dungeon_rows = ["".join(r) for r in raw_map]
        dungeon.set_matrix_from_rows(dungeon_rows)
        self._merge_zone_into_world(world, dungeon)
        # Conectar túneles entre lobby y dungeon
        self._connect_tunnels_in_world(
            world,
            offsets[lobby_key],
            dungeon,
            dungeon_meta.get("rooms", [])
        )
        # 5) Generar y colocar zonas adicionales
        self._place_additional_zones(world)
        # 6) Serializar matriz global a filas de texto (rectangulares)
        target_w = world.width
        rows: List[str] = []
        for row in world.matrix:
            s = "".join(row)
            if len(s) > target_w:
                s = s[:target_w]
            elif len(s) < target_w:
                # Rellenar con muros para mantener rectangularidad
                s = s + ("#" * (target_w - len(s)))
            rows.append(s)
        # 7) Cargar capas y tiles
        _, tiles_by_layer, layers = self.loader.load(rows, key)
        # 8) Preparar metadata final
        result_meta = {"lobby_offset": offsets[lobby_key], **dungeon_meta}
        return Map(rows, layers, tiles_by_layer, result_meta, key)

    def _place_lobby_zone(self, world: Zone) -> Tuple[int, int]:
        rows = generate_lobby_matrix()
        offset = calculate_lobby_offset()
        lw = len(rows[0]) if rows else global_map_settings.zone_width
        lh = len(rows)
        lobby = Zone("lobby", offset, lw, lh)
        lobby.set_matrix_from_rows(rows)
        self._merge_zone_into_world(world, lobby)
        return offset

    def _place_dungeon_zone(
        self,
        world: Zone,
        lobby_offset: Tuple[int, int]
    ) -> Dict[str, object]:
        raw_map, metadata = self.generator.generate(
            width=global_map_settings.zone_width,
            height=global_map_settings.zone_height,
            return_rooms=True,
        )
        offset = calculate_dungeon_offset(lobby_offset)
        dungeon = Zone("dungeon", offset, global_map_settings.zone_width, global_map_settings.zone_height)
        dungeon_rows = ["".join(r) for r in raw_map]
        dungeon.set_matrix_from_rows(dungeon_rows)
        self._merge_zone_into_world(world, dungeon)
        # Conectar túneles
        self._connect_tunnels_in_world(
            world,
            lobby_offset,
            dungeon,
            metadata.get("rooms", [])
        )
        return {"offset": offset, "metadata": metadata}

    def _merge_zone_into_world(self, world: Zone, zone: Zone) -> None:
        # Copy zone tiles into world, clipping to world bounds to avoid IndexError
        world_h = len(world.matrix)
        world_w = len(world.matrix[0]) if world_h > 0 else 0
        for y in range(zone.height):
            gy = zone.offset_y + y
            if gy < 0 or gy >= world_h:
                continue
            for x in range(zone.width):
                gx = zone.offset_x + x
                if gx < 0 or gx >= world_w:
                    continue
                world.matrix[gy][gx] = zone.matrix[y][x]

    def _connect_tunnels_in_world(
        self,
        world: Zone,
        lobby_offset: Tuple[int, int],
        dungeon: Zone,
        rooms: List[Tuple[int, int, int, int]]
    ) -> None:
        # Punto de salida en el lobby (global coords)
        local_exit = find_lobby_exit(
            generate_lobby_matrix(),
            global_map_settings.dungeon_connect_side
        )
        ex = lobby_offset[0] + local_exit[0]
        ey = lobby_offset[1] + local_exit[1]

        # Centros de habitaciones en coords globales
        centers = [
            ((r[0] + r[2]) // 2 + dungeon.offset_x,
             (r[1] + r[3]) // 2 + dungeon.offset_y)
            for r in rooms
        ]
        if not centers:
            return
        bx, by = min(centers, key=lambda c: abs(c[0] - ex) + abs(c[1] - ey))

        # Dibujar túneles en la matriz del world
        if random.random() < 0.5:
            DungeonGenerator._horiz_tunnel(world.matrix, ex, bx, ey)
            DungeonGenerator._vert_tunnel(world.matrix, ey, by, bx)
        else:
            DungeonGenerator._vert_tunnel(world.matrix, ey, by, ex)
            DungeonGenerator._horiz_tunnel(world.matrix, ex, bx, by)

    def _place_additional_zones(self, world: Zone) -> None:
        """
        Genera y coloca zonas adicionales definidas en la configuración,
        conectándolas con sus zonas padre.
        """
        offsets = global_map_settings.zone_offsets
        for zone_name, (parent, side) in global_map_settings.additional_zones.items():
            # Evitar duplicar zonas base
            if zone_name in ("lobby", "dungeon"):
                continue
            # Si estamos usando zones.json, solo colocar zonas adicionales que estén
            # explícitamente definidas en offsets; no derivar nuevas.
            if global_map_settings.use_zones_json and zone_name not in offsets:
                logger.debug(f"Omitiendo zona adicional '{zone_name}' (use_zones_json=True y no definida en zones.json)")
                continue
            # Resolver nombre efectivo del padre en offsets (manejar mayúsculas/minúsculas
            # y posibles renombrados cuando se usa zones.json)
            parent_key = parent
            parent_offset = offsets.get(parent_key)
            if parent_offset is None:
                # Si es una zona base, mapear al key real usando offsets dinámicos
                if parent_key in ("lobby", "dungeon"):
                    dyn_offs = global_map_settings._dynamic_offsets()
                    dyn_parent_off = dyn_offs[parent_key]
                    resolved = next((k for k, v in offsets.items() if v == dyn_parent_off), None)
                    if resolved:
                        parent_key = resolved
                        parent_offset = offsets[parent_key]
            if parent_offset is None:
                # Intento adicional: coincidencia case-insensitive de nombre
                ci_match = next((k for k in offsets.keys() if k.lower() == parent.lower()), None)
                if ci_match:
                    parent_key = ci_match
                    parent_offset = offsets[parent_key]
            if parent_offset is None:
                logger.warning(f"Zona padre '{parent}' no definida para zona adicional '{zone_name}'.")
                continue
            # Obtener/derivar offset de la zona si no está en offsets
            offset = offsets.get(zone_name)
            if offset is None:
                if global_map_settings.use_zones_json:
                    # En modo JSON no derivamos zonas implícitas
                    logger.debug(f"Sin offset definido para '{zone_name}' y use_zones_json=True; se omite")
                    continue
                offset = global_map_settings.calculate_offset(parent_offset, side)
                # Añadirlo al mapeo local de offsets para que el loader integre overlays si aplica
                offsets[zone_name] = offset
            # Si la zona es vacía (nombre empieza con 'empty'), generar zona de suelo caminable
            if zone_name.startswith("empty"):
                zone = Zone(zone_name, offset, global_map_settings.zone_width, global_map_settings.zone_height)
                zone_rows = ["." * global_map_settings.zone_width for _ in range(global_map_settings.zone_height)]
                zone.set_matrix_from_rows(zone_rows)
                self._merge_zone_into_world(world, zone)
                continue
            # Generar mapa para zona adicional
            raw_map, metadata_zone = self.generator.generate(
                width=global_map_settings.zone_width,
                height=global_map_settings.zone_height,
                return_rooms=True,
            )
            zone = Zone(zone_name, offset, global_map_settings.zone_width, global_map_settings.zone_height)
            zone_rows = ["".join(r) for r in raw_map]
            zone.set_matrix_from_rows(zone_rows)
            self._merge_zone_into_world(world, zone)
            # Conectar túneles entre padre y zona adicional (si hay parent_offset)
            if side == "bottom":
                exit_x = parent_offset[0] + global_map_settings.zone_width // 2
                exit_y = parent_offset[1] + global_map_settings.zone_height
                entry_x = offset[0] + global_map_settings.zone_width // 2
                entry_y = offset[1]
            elif side == "top":
                exit_x = parent_offset[0] + global_map_settings.zone_width // 2
                exit_y = parent_offset[1]
                entry_x = offset[0] + global_map_settings.zone_width // 2
                entry_y = offset[1] + global_map_settings.zone_height
            elif side == "left":
                exit_x = parent_offset[0]
                exit_y = parent_offset[1] + global_map_settings.zone_height // 2
                entry_x = offset[0] + global_map_settings.zone_width
                entry_y = offset[1] + global_map_settings.zone_height // 2
            else:  # right
                exit_x = parent_offset[0] + global_map_settings.zone_width
                exit_y = parent_offset[1] + global_map_settings.zone_height // 2
                entry_x = offset[0]
                entry_y = offset[1] + global_map_settings.zone_height // 2
            # Dibujar túneles (si el padre fue resuelto)
            if parent_offset is None:
                logger.debug(f"No se pudo resolver zona padre para '{zone_name}', omitimos túneles")
                continue
            if random.random() < 0.5:
                DungeonGenerator._horiz_tunnel(world.matrix, exit_x, entry_x, exit_y)
                DungeonGenerator._vert_tunnel(world.matrix, exit_y, entry_y, entry_x)
            else:
                DungeonGenerator._vert_tunnel(world.matrix, exit_y, entry_y, exit_x)
                DungeonGenerator._horiz_tunnel(world.matrix, exit_x, entry_x, entry_y)
            logger.info(f"Zona adicional '{zone_name}' conectada con '{parent}' en el lado '{side}'.")