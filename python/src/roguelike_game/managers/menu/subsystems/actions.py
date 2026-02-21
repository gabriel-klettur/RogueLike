from __future__ import annotations

import logging
from datetime import datetime
from pathlib import Path

from roguelike_game.managers.map import MapManager
from roguelike_game.ecs.systems.spawner.spawner_placement_system import SpawnerPlacementSystem
from roguelike_game.ecs.systems.core.npc_restore_system import NpcRestoreSystem
from roguelike_game.ecs.systems.core.npc_respawn_system import NpcRespawnSystem
from roguelike_game.ecs.components.experience_component import ExperienceComponent

logger = logging.getLogger(__name__)


class MenuActions:
    """Game actions initiated from the menu (save/new/load legacy/finalize class)."""

    def __init__(self, game) -> None:
        self.game = game

    # ---------------- Basic ----------------
    def save_game(self) -> None:
        try:
            self.game.shutdown_manager.shutdown()
            logger.info("Partida guardada correctamente.")
        except Exception as e:
            logger.warning("Error al guardar partida: %s", e)

    def open_class_selector(self) -> None:
        g = self.game
        try:
            if hasattr(g, "class_selector") and g.class_selector:
                g.class_selector.set_background(
                    "assets/ui/character_selection/taberna.png",
                    scale_mode="cover",
                )
                g.class_selector.show = True
                try:
                    g.state.class_selector_visible = True
                except Exception:
                    pass
            logger.info("Selector de clase abierto (inicialización diferida hasta elegir clase)")
        except Exception as e:
            logger.error("Error al abrir selector de clase: %s", e)

    # ---------------- Finalize new game after class selection ----------------
    def finalize_new_game_with_class(self, class_key: str) -> None:
        g = self.game
        try:
            try:
                level_name = getattr(g.map, "name", None)
            except Exception:
                level_name = None
            if not level_name:
                try:
                    level_name = g.map.name
                except Exception:
                    level_name = None
            try:
                g.world.npc_memory = {}
                g.world.npc_inventories = {}
                g.world.player_inventory = None
            except Exception:
                pass
            try:
                if hasattr(g.world, "maps"):
                    g.world.maps.clear()
                if hasattr(g.world, "_pending_levels"):
                    g.world._pending_levels = {}
                g.world.current_level = None
            except Exception:
                pass
            try:
                try:
                    setattr(g.ecs.ecs_world, "skip_spawners_on_first_load", True)
                except Exception:
                    pass
                new_map = MapManager(level_name)
                g.map = new_map
                g.world.maps[level_name] = new_map
                g.world.current_level = level_name
                if hasattr(g.map, "_local_state"):
                    g.map._local_state["player_pos"] = None
                try:
                    ecs = g.ecs.ecs_world
                    try:
                        ecs.map_manager = new_map
                        ecs.invalidate_spatial_index()
                    except Exception:
                        pass
                    comps = ecs.components
                    for eid in list(comps.get("NPCTagComponent", {}).keys()):
                        ecs.remove_entity(eid)
                    for eid in list(comps.get("SpawnerConfig", {}).keys()):
                        ecs.remove_entity(eid)
                    for eid in list(comps.get("SpawnRequest", {}).keys()):
                        ecs.remove_entity(eid)
                    try:
                        for sys in getattr(ecs, "update_systems", []) or []:
                            if isinstance(sys, SpawnerPlacementSystem):
                                sys._loaded = False
                            elif isinstance(sys, NpcRestoreSystem):
                                try:
                                    sys._applied.clear()
                                except Exception:
                                    sys._applied = set()
                            elif isinstance(sys, NpcRespawnSystem):
                                try:
                                    sys._requested.clear()
                                except Exception:
                                    sys._requested = set()
                    except Exception:
                        pass
                except Exception:
                    pass
            except Exception:
                pass
            try:
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings

                tx = off_x + global_map_settings.zone_width // 2
                ty = off_y + global_map_settings.zone_height // 2
            except Exception:
                tx, ty = 0, 0
            g.map.spawn_player((tx, ty))
            px, py = g.map.get_spawn_pixel((tx, ty))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            try:
                g.player_manager.change_class(class_key)
            except Exception:
                pass
            try:
                from roguelike_game.ecs.components.inventory_component import InventoryComponent

                eid = g.ecs.ecs_world.player_entity
                inv = InventoryComponent(capacity=20, player_id="player")
                inv.add("gold", 10)
                g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                if hasattr(g, "world"):
                    g.world.player_inventory = inv.serialize()
            except Exception as e:
                logger.warning("No se pudo inicializar inventario de nuevo juego: %s", e)
            try:
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                xp_comp.xp = 0
                xp_comp.level = 0
            except Exception as e:
                logger.warning("No se pudo reiniciar experiencia de nuevo juego: %s", e)
            try:
                ts = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
                save_dir: Path = g.world.config.save_dir
                save_dir.mkdir(parents=True, exist_ok=True)
                slot_path = save_dir / f"partida_{ts}.json"
                g.world.current_save_path = str(slot_path)
                g.world.save_metadata = {
                    "name": f"partida_{ts}",
                    "created_at": datetime.now().isoformat(timespec="seconds"),
                    "last_played": datetime.now().isoformat(timespec="seconds"),
                }
            except Exception as e:
                logger.warning("No se pudo preparar slot de guardado: %s", e)
            try:
                if hasattr(g, "class_selector") and g.class_selector:
                    g.class_selector.show = False
                g.state.class_selector_visible = False
            except Exception:
                pass
            try:
                g.shutdown_manager.shutdown()
            except Exception:
                pass
            logger.info("Nuevo juego inicializado tras selección de clase: %s", class_key)
            try:
                aq = g.ecs.ecs_world.components.setdefault("AudioEventQueue", [])
                aq.append({"type": "enter_game_default", "duration_ms": 600})
            except Exception:
                pass
        except Exception as e:
            logger.error("Error al finalizar nuevo juego: %s", e)

    def load_game_legacy(self) -> None:
        g = self.game
        try:
            g.world.load_world()
            level = getattr(g.world, "current_level", None)
            if not level:
                try:
                    pdata = getattr(g, "world", None)
                except Exception:
                    pdata = None
                level = g.map.name
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            tile = g.map._local_state.get("player_pos")
            if tile is None:
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings

                tile = (
                    off_x + global_map_settings.zone_width // 2,
                    off_y + global_map_settings.zone_height // 2,
                )
                g.map.spawn_player(tile)
            px, py = g.map.get_spawn_pixel(tuple(tile))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            try:
                pdata = getattr(g.world, "player_inventory", None)
                if pdata:
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent

                    inv = InventoryComponent(capacity=pdata.get("capacity", 20), player_id=pdata.get("player_id"))
                    for slot in pdata.get("slots", []):
                        if slot:
                            inv.add(slot["item"], slot.get("quantity", 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)
            try:
                meta = getattr(g.world, "save_metadata", {}) or {}
                p = meta.get("player", {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get("xp") is not None:
                    xp_comp.xp = int(p["xp"])
                if p.get("level") is not None:
                    xp_comp.level = int(p["level"])
                meta.setdefault("player", {})
                meta["player"]["xp"] = int(xp_comp.xp)
                meta["player"]["level"] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
        except Exception as e:
            logger.error("Error al cargar partida: %s", e)
