from __future__ import annotations

import json
import logging
import uuid
from pathlib import Path
from typing import List, Dict, Any

from roguelike_engine.world.models import WorldSnapshot
from roguelike_game.ecs.systems.spawner.spawner_placement_system import (
    SpawnerPlacementSystem,
)
from roguelike_game.ecs.systems.core.npc_restore_system import NpcRestoreSystem
from roguelike_game.ecs.systems.core.npc_respawn_system import NpcRespawnSystem
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.utils.inventory_sync import write_active_for_player

logger = logging.getLogger(__name__)


class SaveService:
    """Domain service for game save operations (list, load, rename, delete)."""

    def __init__(self, game) -> None:
        self.game = game

    # ---------------- Queries ----------------
    def list_saves(self) -> List[Dict[str, Any]]:
        g = self.game
        save_dir: Path = g.world.config.save_dir
        save_dir.mkdir(parents=True, exist_ok=True)
        entries: List[Dict[str, Any]] = []
        for path in sorted(save_dir.glob("partida_*.json"), reverse=True):
            try:
                data = g.world.repository.load_from_path(str(path))
            except Exception:
                data = {}
            meta = data.get("meta") or {}
            label = meta.get("name") or path.stem
            entries.append({"path": str(path), "label": label, "meta": meta})
        return entries

    def format_meta_lines(self, meta: Dict[str, Any]) -> List[str]:
        if not meta:
            return ["Sin metadatos", "Pulsa Enter para cargar"]
        lines: List[str] = []
        lines.append(f"Nombre: {meta.get('name', '-')}")
        lines.append(f"Creada: {meta.get('created_at', '-')}")
        lines.append(f"Última vez: {meta.get('last_played', '-')}")
        p = meta.get("player", {}) or {}
        lines.append(f"Nivel: {p.get('level', '-')}")
        lines.append(f"XP: {p.get('xp', '-')}")
        it = meta.get("items_summary", {}) or {}
        lines.append(f"Pilas: {it.get('stacks', 0)}")
        top = it.get("top_items") or []
        if top:
            lines.append("Items: " + ", ".join([str(x) for x in top]))
        return lines

    # ---------------- Commands ----------------
    def rename_save(self, path: str, new_name: str) -> Dict[str, Any]:
        g = self.game
        try:
            data = g.world.repository.load_from_path(str(path))
        except Exception:
            data = {}
        meta = data.get("meta") or {}
        meta["name"] = new_name
        data["meta"] = meta
        repo = g.world.repository
        snapshot = WorldSnapshot(
            version=data.get("version", 1),
            player=data.get("player"),
            npcs=data.get("npcs", {}),
            levels=data.get("levels", {}),
            player_inventory=data.get("player_inventory"),
            npc_inventories=data.get("npc_inventories"),
            meta=data.get("meta"),
        )
        repo.save_to_path(str(path), snapshot)
        return meta

    def delete_save(self, path: str) -> None:
        p = Path(path)
        if p.exists():
            p.unlink()

    def load_save(self, path: str) -> None:
        g = self.game
        try:
            # Load world and current level
            g.world.load_world(path)
            level = getattr(g.world, "current_level", None) or g.map.name
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level

            # Reset ECS NPCs/spawners state to avoid duplicates
            try:
                ecs = g.ecs.ecs_world
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
                            try:
                                sys._loaded = False
                            except Exception:
                                pass
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
                try:
                    ecs.components["NPCInventorySnapshot"] = dict(getattr(g.world, "npc_inventories", {}) or {})
                except Exception:
                    pass
                try:
                    ecs.invalidate_spatial_index()
                except Exception:
                    pass
            except Exception:
                pass

            # Ensure player spawn position
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

            # Restore player inventory (ensure valid player_id)
            try:
                pdata = getattr(g.world, "player_inventory", None)
                if pdata:
                    def _valid_uuid(x):
                        try:
                            uuid.UUID(str(x))
                            return True
                        except Exception:
                            return False

                    pid = pdata.get("player_id")
                    if not _valid_uuid(pid):
                        try:
                            eid = g.ecs.ecs_world.player_entity
                            active_path = Path("data/inventory/active/inventory_player.json")
                            active = json.loads(active_path.read_text(encoding="utf-8")) if active_path.exists() else {}
                            apid = (active.get(str(eid)) or {}).get("player_id")
                            if not _valid_uuid(apid):
                                apid = active.get("player_id")
                            pid = apid if _valid_uuid(apid) else str(uuid.uuid4())
                        except Exception:
                            pid = str(uuid.uuid4())
                        try:
                            pdata["player_id"] = pid
                            repo = g.world.repository
                            data = repo.load_from_path(str(path))
                            data.setdefault("player_inventory", {})
                            data["player_inventory"]["player_id"] = pid
                            snapshot = WorldSnapshot(
                                version=data.get("version", 1),
                                player=data.get("player"),
                                npcs=data.get("npcs", {}),
                                levels=data.get("levels", {}),
                                player_inventory=data.get("player_inventory"),
                                npc_inventories=data.get("npc_inventories"),
                                meta=data.get("meta"),
                            )
                            repo.save_to_path(str(path), snapshot)
                            g.world.player_inventory = data.get("player_inventory", pdata)
                        except Exception:
                            pass
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent

                    inv = InventoryComponent(capacity=pdata.get("capacity", 20), player_id=pdata.get("player_id"))
                    for slot in pdata.get("slots", []):
                        if slot:
                            inv.add(slot["item"], slot.get("quantity", 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                    try:
                        snap = inv.serialize() if hasattr(inv, "serialize") else {}
                        if "player_id" not in snap:
                            snap["player_id"] = pdata.get("player_id")
                        write_active_for_player(eid, snap)
                    except Exception:
                        pass
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)

            # Restore XP metadata/component
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

            # Stop menu music and enqueue sfx
            from roguelike_game.managers.menu.subsystems.music import MusicManager
            try:
                music = getattr(self.game, "_menu_music_mgr", None)
                if music and isinstance(music, MusicManager):
                    music.stop_music(fade_ms=None)
            except Exception:
                pass
            try:
                aq = g.ecs.ecs_world.components.setdefault("AudioEventQueue", [])
                aq.append({"type": "enter_game_default", "duration_ms": 600})
            except Exception:
                pass

            logger.info("Partida cargada desde %s", path)
        except Exception as e:
            logger.error("Error al cargar partida desde lista: %s", e)
