from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE
from datetime import datetime
from pathlib import Path
from roguelike_game.utils.inventory_sync import write_active_for_player
from roguelike_game.utils.inventory_registry import publish_inventory

import logging
logger = logging.getLogger(__name__)

class ShutdownManager:
    """
    Se encarga de todo lo necesario antes de cerrar el juego:
     - Guardar posición del jugador en el mapa actual.
     - Actualizar WorldManager (maps, current_level, etc.).
     - Serializar y guardar el mundo en disco.
    """
    def __init__(self, game):
        self.game = game

    def shutdown(self):
        g = self.game
        try:
            # 1) Obtener la entidad del jugador
            eid = g.ecs.ecs_world.player_entity
            pos = g.ecs.ecs_world.components["Position"][eid]

            # 2) Calcular coordenadas de tile usando el centro del collider 'feet'
            w, h = RENDERED_SPRITE_SIZE
            fh = h // 4
            half_fh = fh // 2
            feet_cx = pos.x + w//2
            feet_cy = pos.y + (h - half_fh)

            tx = int(feet_cx // TILE_SIZE)
            ty = int(feet_cy // TILE_SIZE)

            # 3) Hacer spawn del jugador en el mapa (para que guarde la nueva posición)
            g.map.spawn_player((tx, ty))

            # 4) Actulizar WorldManager
            g.world.maps[g.map.name] = g.map
            g.world.current_level     = g.map.name

            # 4b) Persistir inventario del jugador si existe
            try:
                inv = g.ecs.ecs_world.components.get("InventoryComponent", {}).get(eid)
                if inv is not None and hasattr(inv, "serialize"):
                    g.world.player_inventory = inv.serialize()
                    # Sincronizar también el perfil activo
                    try:
                        write_active_for_player(eid, g.world.player_inventory)
                    except Exception:
                        pass
                    # Publicar snapshot en registro versionado (opcional)
                    try:
                        publish_inventory(g.world.player_inventory)
                    except Exception:
                        pass
            except Exception:
                pass

            # 4c) Preparar metadatos del guardado: nombre, timestamps, xp, nivel, items
            try:
                # Nombre de guardado: si no existe, usar nombre basado en archivo
                meta = dict(g.world.save_metadata or {})
                # created_at: mantener si ya existe, si no, setear ahora
                created = meta.get("created_at") or datetime.now().isoformat(timespec='seconds')
                # last_played: siempre actualizar
                last_played = datetime.now().isoformat(timespec='seconds')
                # name: mantener si existe, si no, derivar de nombre de archivo del slot si existe
                slot_path = g.world.current_save_path
                default_name = Path(slot_path).stem if slot_path else "partida"
                meta_name = meta.get("name") or default_name

                # Extraer xp/nivel del jugador
                xp_val = None
                level_val = None
                try:
                    xp_comp = g.ecs.ecs_world.components.get("ExperienceComponent", {}).get(eid)
                    if xp_comp is not None:
                        xp_val = getattr(xp_comp, 'xp', None)
                        level_val = getattr(xp_comp, 'level', None)
                except Exception:
                    pass

                # Resumen de items: contar stacks y listar primeros 5 ids
                items_summary = {}
                try:
                    if inv is not None:
                        slots = getattr(inv, 'slots', [])
                        stacks = [s for s in slots if s]
                        items_summary = {
                            "stacks": len(stacks),
                            "top_items": [getattr(s, 'item_id', None) for s in stacks[:5]]
                        }
                except Exception:
                    pass

                meta.update({
                    "name": meta_name,
                    "created_at": created,
                    "last_played": last_played,
                    "player": {
                        "xp": xp_val,
                        "level": level_val,
                    },
                    "items_summary": items_summary,
                })
                g.world.save_metadata = meta
            except Exception:
                pass

            # 5) Salvar el mundo en disco
            g.world.save_world()

        except Exception as exc:
            logger.warning(f"No se pudo guardar al cerrar: {exc}")