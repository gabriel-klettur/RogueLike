import os
import uuid
import json

from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.utils.map_utils import get_zone_offset
from roguelike_game.ecs.components.item_models import load_items
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_engine.utils.loader import load_image

import logging
logger = logging.getLogger(__name__)

class DeathDropSystem:
    """
    Sistema ECS que maneja el dropeo de ítems al morir NPCs o Player.
    """
    def __init__(self, perf_log=None,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_player.json'),
                 drop_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json'),
                 xp_item_id: str = 'experience_orb', xp_quantity: int = 1,
                 max_search_radius: int = 12,
                 temp_drop_layer_ms: int = 0):
        self.perf_log = perf_log
        self.active_monster_path = active_monster_path
        self.active_player_path = active_player_path
        self.drop_manager = ItemDropManager(drop_path)
        self.processed = set()
        # Configuración de orbe de experiencia y búsqueda de tiles
        self.xp_item_id = xp_item_id
        self.xp_quantity = xp_quantity
        self.max_search_radius = max_search_radius
        # Si > 0, persistir temp_z_layer en vez de z_layer fijo
        self.temp_drop_layer_ms = temp_drop_layer_ms
        # Cargar modelos de ítems para poder calcular tamaño del sprite del orbe
        try:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
            self.items = load_items(items_path)
        except Exception:
            self.items = {}

    def update(self, world, *args):
        comps = world.components
        self.world = world
        inv_store = comps.get('InventoryComponent', {})
        death_store = comps.get('DeathTimer', {})
        pos_store = comps.get('Position', {})
        sprite_store = comps.get('Sprite', {})
        scale_store = comps.get('Scale', {})

        # Procesar entidades que acaban de morir
        for eid in list(death_store.keys()):
            if eid in self.processed:
                continue
            inv = inv_store.get(eid)
            pos = pos_store.get(eid)
            if not inv or not pos:
                continue

            # Calcular tile y zona usando el centro visual del sprite (Sprite + Scale)
            sprite = sprite_store.get(eid)
            scale_comp = scale_store.get(eid)
            scale_factor = getattr(scale_comp, 'scale', 1.0) if scale_comp else 1.0
            if sprite and hasattr(sprite, 'image'):
                try:
                    sw = sprite.image.get_width()
                    sh = sprite.image.get_height()
                    center_px = pos.x + (sw * scale_factor) / 2
                    center_py = pos.y + (sh * scale_factor) / 2
                except Exception:
                    # Fallback si algo falla con el sprite
                    center_px = pos.x + TILE_SIZE / 2
                    center_py = pos.y + TILE_SIZE / 2
            else:
                # Fallback si no hay sprite/scale
                center_px = pos.x + TILE_SIZE / 2
                center_py = pos.y + TILE_SIZE / 2

            center_g_tx = int(center_px // TILE_SIZE)
            center_g_ty = int(center_py // TILE_SIZE)
            zone_id = get_zone_for_tile(center_g_tx, center_g_ty)
            offx, offy = get_zone_offset(zone_id)
            center_l_tx = center_g_tx - offx
            center_l_ty = center_g_ty - offy

            # Construir ocupación existente (JSON y entidades vivas)
            occupied = self._collect_occupied_tiles(zone_id, offx, offy)

            # Map manager para validar walkability
            map_manager = getattr(world, 'map_manager', None)

            # Elegir tile para orbe de XP: centro si es caminable y libre, si no el más cercano
            orb_local = None
            if map_manager and map_manager.is_walkable(center_g_tx, center_g_ty) and (center_l_tx, center_l_ty) not in occupied:
                orb_local = (center_l_tx, center_l_ty)
            else:
                for g_tx, g_ty in self._iter_spiral_tiles(center_g_tx, center_g_ty, self.max_search_radius):
                    l_tx, l_ty = g_tx - offx, g_ty - offy
                    if (l_tx, l_ty) in occupied:
                        continue
                    if map_manager and not map_manager.is_walkable(g_tx, g_ty):
                        continue
                    orb_local = (l_tx, l_ty)
                    break

            # Marcar orbe como ocupado para evitar solapamientos
            if orb_local is not None:
                occupied.add(orb_local)

            # Dispersar stacks alrededor en espiral evitando ocupados y no-caminables
            logger.debug(f"[DeathDropSystem] eid={eid} dropping {[(s.item_id, s.quantity) for s in inv.slots if s]} in zone='{zone_id}'")
            spiral_iter = self._iter_spiral_tiles(center_g_tx, center_g_ty, self.max_search_radius)
            # Consumir el centro si ya se usó para orbe para no repetirlo
            # (el generador comienza por el centro)
            first_center = next(spiral_iter, None)
            # Crear drops de inventario
            for stack in inv.slots:
                if not stack:
                    continue
                placed_local = None
                # Buscar siguiente candidato libre y caminable
                for g_tx, g_ty in spiral_iter:
                    l_tx, l_ty = g_tx - offx, g_ty - offy
                    if (l_tx, l_ty) in occupied:
                        continue
                    if map_manager and not map_manager.is_walkable(g_tx, g_ty):
                        continue
                    placed_local = (l_tx, l_ty)
                    break
                if placed_local is None:
                    # Si no hay hueco en radio, como fallback, usar centro aunque solape (último recurso)
                    placed_local = (center_l_tx, center_l_ty)
                occupied.add(placed_local)
                drop_id = str(uuid.uuid4())
                if self.temp_drop_layer_ms > 0:
                    self.drop_manager.create_drop(
                        drop_id,
                        stack.item_id,
                        stack.quantity,
                        zone_id,
                        tile={'x': placed_local[0], 'y': placed_local[1]},
                        temp_z_layer={'layer': Z_LAYERS.get('building_low', 3), 'ttl_ms': int(self.temp_drop_layer_ms)}
                    )
                else:
                    self.drop_manager.create_drop(
                        drop_id,
                        stack.item_id,
                        stack.quantity,
                        zone_id,
                        tile={'x': placed_local[0], 'y': placed_local[1]},
                        z_layer=Z_LAYERS.get('building_low', 3)
                    )

            # Crear orbe de XP al final (si está configurado)
            if self.xp_item_id and orb_local is not None:
                xp_drop_id = str(uuid.uuid4())
                created = False
                try:
                    # Determinar centro objetivo en píxeles:
                    # - si orb_local coincide con el tile del centro visual del sprite -> usar centro del sprite
                    # - si no, centrar en el tile elegido (walkable más cercano)
                    if orb_local == (center_l_tx, center_l_ty):
                        target_cx, target_cy = center_px, center_py
                    else:
                        g_tx = orb_local[0] + offx
                        g_ty = orb_local[1] + offy
                        target_cx = g_tx * TILE_SIZE + TILE_SIZE // 2
                        target_cy = g_ty * TILE_SIZE + TILE_SIZE // 2

                    # Determinar tamaño del sprite del orbe (icon_small o icon) y su escala de mapa
                    model = self.items.get(self.xp_item_id)
                    icon_path = None
                    if model:
                        icon_path = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
                        if isinstance(icon_path, list):
                            icon_path = icon_path[0]
                    if icon_path:
                        surf = load_image(icon_path)
                        sw, sh = surf.get_size()
                        scale_factor = getattr(model, 'scale_map', 1.0) if model else 1.0
                        final_w = int(sw * scale_factor)
                        final_h = int(sh * scale_factor)
                        top_left_x = int(target_cx - final_w // 2)
                        top_left_y = int(target_cy - final_h // 2)
                        if self.temp_drop_layer_ms > 0:
                            self.drop_manager.create_drop(
                                xp_drop_id,
                                self.xp_item_id,
                                int(self.xp_quantity) if isinstance(self.xp_quantity, int) else 1,
                                zone_id,
                                position={'x': top_left_x, 'y': top_left_y},
                                temp_z_layer={'layer': Z_LAYERS.get('building_low', 3), 'ttl_ms': int(self.temp_drop_layer_ms)}
                            )
                        else:
                            self.drop_manager.create_drop(
                                xp_drop_id,
                                self.xp_item_id,
                                int(self.xp_quantity) if isinstance(self.xp_quantity, int) else 1,
                                zone_id,
                                position={'x': top_left_x, 'y': top_left_y},
                                z_layer=Z_LAYERS.get('building_low', 3)
                            )
                        created = True
                except Exception as e:
                    logger.debug(f"[DeathDropSystem] XP orb pixel-center failed, falling back to tile: {e}")
                if not created:
                    # Fallback: por tile
                    try:
                        if self.temp_drop_layer_ms > 0:
                            self.drop_manager.create_drop(
                                xp_drop_id,
                                self.xp_item_id,
                                int(self.xp_quantity) if isinstance(self.xp_quantity, int) else 1,
                                zone_id,
                                tile={'x': orb_local[0], 'y': orb_local[1]},
                                temp_z_layer={'layer': Z_LAYERS.get('building_low', 3), 'ttl_ms': int(self.temp_drop_layer_ms)}
                            )
                        else:
                            self.drop_manager.create_drop(
                                xp_drop_id,
                                self.xp_item_id,
                                int(self.xp_quantity) if isinstance(self.xp_quantity, int) else 1,
                                zone_id,
                                tile={'x': orb_local[0], 'y': orb_local[1]},
                                z_layer=Z_LAYERS.get('building_low', 3)
                            )
                    except Exception as e:
                        logger.warning(f"[DeathDropSystem] No se pudo crear orbe XP: {e}")

            # Vaciar inventario y persistir
            inv.slots = [None] * inv.capacity
            self._persist_inventory(eid, inv)
            self.processed.add(eid)

    def _persist_inventory(self, eid: int, inv: InventoryComponent):
        inst = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
        if inst:
            key = inst.instance_id
        else:
            key = str(eid)
        # Leer y actualizar JSON de monstruos
        try:
            with open(self.active_monster_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            data = {}
        if key in data:
            data[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_monster_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
        # Leer y actualizar JSON de jugador
        try:
            with open(self.active_player_path, 'r', encoding='utf-8') as f:
                pdata = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            pdata = {}
        if key in pdata:
            pdata[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_player_path, 'w', encoding='utf-8') as f:
                json.dump(pdata, f, indent=2)

    # Helpers
    def _collect_occupied_tiles(self, zone_id: str, offx: int, offy: int) -> "Set[Tuple[int, int]]":
        """Recoge tiles ocupados (locales a la zona) por drops persistidos y entidades activas."""
        occupied: "Set[Tuple[int, int]]" = set()
        # 1) JSON persistido
        try:
            drops = self.drop_manager._data or {}
            for _, data in drops.items():
                if data.get('zone_id') != zone_id:
                    continue
                if 'tile' in data:
                    lt = data['tile']
                    occupied.add((int(lt['x']), int(lt['y'])))
                elif 'position' in data:
                    pos = data['position']
                    gtx = int(pos['x'] // TILE_SIZE)
                    gty = int(pos['y'] // TILE_SIZE)
                    occupied.add((gtx - offx, gty - offy))
        except Exception:
            pass
        # 2) Entidades ya spawneadas
        comps = getattr(self.world, 'components', {})
        phys = comps.get('PhysicalItemComponent', {})
        positions = comps.get('Position', {})
        for eid, pic in list(phys.items()):
            try:
                if getattr(pic, 'zone_id', None) != zone_id:
                    continue
                p = positions.get(eid)
                if not p:
                    continue
                gtx = int(p.x // TILE_SIZE)
                gty = int(p.y // TILE_SIZE)
                occupied.add((gtx - offx, gty - offy))
            except Exception:
                continue
        return occupied

    def _iter_spiral_tiles(self, cx: int, cy: int, max_radius: int):
        """Genera tiles (globales) en espiral por anillos alrededor de (cx,cy)."""
        # r = 0 -> centro
        yield (cx, cy)
        for r in range(1, max_radius + 1):
            x0, x1 = cx - r, cx + r
            y0, y1 = cy - r, cy + r
            # Borde superior e inferior
            for x in range(x0, x1 + 1):
                yield (x, y0)
                yield (x, y1)
            # Borde izquierdo y derecho (sin esquinas para evitar duplicados)
            for y in range(y0 + 1, y1):
                yield (x0, y)
                yield (x1, y)
