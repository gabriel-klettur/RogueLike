from __future__ import annotations
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Tuple, Any
import os
import copy
import json

from roguelike_engine.config.config import ASSETS_DIR
from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services.ecs_snapshot import snapshot_entity, restore_entity
from roguelike_editors.entities.services.spawn_services import spawn_entity
from roguelike_editors.entities.entities_properties_panel.services.entity_properties_service import (
    load_entity_data,
    save_entity_data,
    convert_value,
)
from roguelike_editors.entities.entities_properties_panel.services.ecs_update_service import (
    update_player_assets,
    update_monster_assets,
    update_player_stats,
    update_monster_stats,
)
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_engine.utils.loader import load_image, load_sprite_sheet
from roguelike_game.factories.monster.sprite_loader import create_sprite_component
from roguelike_game.config.players_config import PLAYER_ASSETS


def _abs_to_rel_asset_path(path: str) -> str:
    abs_path = Path(path).resolve()
    assets_root = Path(ASSETS_DIR).resolve()
    try:
        rel = abs_path.relative_to(assets_root)
        return f"assets/{rel.as_posix()}"
    except ValueError:
        return str(path).replace("\\", "/")


@dataclass
class SpawnEntityCommand(Command):
    controller: Any
    etype: str
    tx: int
    ty: int
    description: str = "Spawn entity"
    eid: Optional[int] = None

    def apply(self) -> None:
        game = self.controller.game
        eid = spawn_entity(game, self.etype, self.tx, self.ty, self.controller.model.player_stats)
        self.eid = eid
        world = game.ecs.ecs_world
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()

    def undo(self) -> None:
        if self.eid is None:
            return
        world = self.controller.game.ecs.ecs_world
        world.remove_entity(self.eid)
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()


@dataclass
class DeleteEntityCommand(Command):
    controller: Any
    eid: int
    description: str = "Delete entity"
    _snapshot: Any = None

    def apply(self) -> None:
        world = self.controller.game.ecs.ecs_world
        self._snapshot = snapshot_entity(world, self.eid)
        world.remove_entity(self.eid)
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()

    def undo(self) -> None:
        world = self.controller.game.ecs.ecs_world
        if self._snapshot is not None:
            restore_entity(world, self._snapshot)
            if hasattr(world, 'invalidate_spatial_index'):
                world.invalidate_spatial_index()


@dataclass
class EditPropertyCommand(Command):
    controller: Any
    ent_id: str
    key: str
    new_text: str
    description: str = "Edit property"
    _old_value: Any = None
    _new_value: Any = None

    def _get_nested(self, root: dict, dotted: str) -> Any:
        parts = dotted.split('.')
        cur = root
        for i, p in enumerate(parts):
            if not isinstance(cur, dict):
                return None
            if i == len(parts) - 1:
                return cur.get(p)
            cur = cur.get(p, {})

    def _set_nested(self, root: dict, dotted: str, value: Any) -> None:
        parts = dotted.split('.')
        cur = root
        for i, p in enumerate(parts):
            if i == len(parts) - 1:
                cur[p] = value
            else:
                nxt = cur.get(p)
                if not isinstance(nxt, dict):
                    nxt = {}
                    cur[p] = nxt
                cur = nxt

    def _write_value(self, value: Any) -> None:
        # controller is EntityPropertiesPanelController
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        if self.ent_id in self.controller.model.player_stats:
            stats = entry.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(stats, self.key, value)
            else:
                stats[self.key] = value
            # persist and update in-memory
            save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
            # update in-memory mirror
            dst = self.controller.model.player_stats.setdefault(self.ent_id, {})
            if '.' in self.key:
                self._set_nested(dst, self.key, value)
            else:
                dst[self.key] = value
            # propagate to ECS
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            update_player_stats(ecs_world, self.ent_id, self.key, value)
        else:
            stats = entry.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(stats, self.key, value)
            else:
                stats[self.key] = value
            save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
            # update in-memory
            m = self.controller.model.monsters.setdefault(self.ent_id, {})
            dst = m.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(dst, self.key, value)
            else:
                dst[self.key] = value
            # refresh monster defs/sprites and ECS updates
            try:
                from roguelike_editors.entities.entities_properties_panel import entities_properties_panel_controller as epc_mod
                epc_mod.reload_monster_defs()
            except Exception:
                # Fallback to direct factory reload if controller module isn't available
                try:
                    from roguelike_game.factories.monster.config import reload_monster_defs as _reload_defs
                    _reload_defs()
                except Exception:
                    pass
            monster_cache._loaded_variants.discard(self.ent_id)
            monster_cache._SPRITE_SURFACES.pop(self.ent_id, None)
            monster_cache._DEATH_SURFACES.pop(self.ent_id, None)
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            update_monster_stats(ecs_world, self.ent_id, self.key, value)

    def apply(self) -> None:
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        stats = entry.setdefault('stats', {})
        if '.' in self.key:
            self._old_value = copy.deepcopy(self._get_nested(stats, self.key))
        else:
            self._old_value = copy.deepcopy(stats.get(self.key))
        # Type-aware conversion
        self._new_value = convert_value(self.new_text, self._old_value)
        self._write_value(self._new_value)
        # Clear edit state in UI
        self.controller._reset_edit_state()

    def undo(self) -> None:
        self._write_value(self._old_value)


@dataclass
class SetAssetCommand(Command):
    controller: Any
    ent_id: str
    cell_key: str  # e.g., 'asset_idle_down'
    new_path: str  # absolute or relative; will be normalized
    description: str = "Set asset"
    _old_value: Any = None

    def _get_current_value(self, entry: dict) -> Any:
        parts = self.cell_key.split("_")
        if len(parts) != 3 or parts[0] != 'asset':
            return None
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            return copy.deepcopy(sprites_set.get(state))  # usually list like [path]
        else:
            no_sets = assets.setdefault('no-sets', {})
            return copy.deepcopy(no_sets.setdefault(state, {}).get(direction))

    def _write_value(self, entry: dict, rel_path: str) -> None:
        parts = self.cell_key.split("_")
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            sprites_set[state] = [rel_path]
        else:
            no_sets = assets.setdefault('no-sets', {})
            state_no_set = no_sets.setdefault(state, {})
            state_no_set[direction] = rel_path
        # Remove legacy key if present
        entry.pop('sprites', None)

    def _persist_and_update(self, entry: dict, path: str) -> None:
        save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        ecs_world = self.controller.editor_controller.game.ecs.ecs_world
        if self.ent_id in self.controller.model.player_stats:
            # Update in-memory player assets mirror
            self.controller.model.player_assets[self.ent_id] = entry.get('assets', {})
            update_player_assets(ecs_world, self.ent_id)
        else:
            # Refresh monster ECS
            update_monster_assets(ecs_world, self.ent_id)
            # Keep in-memory monsters model in sync so flatten_entity_data reads fresh assets
            try:
                self.controller.model.monsters[self.ent_id] = entry
            except Exception:
                pass

        # UI tweaks similar to controller flow
        self.controller.assets_picker_controller.hide()
        self.controller.grid_controller.model.last_entity_id = None
        self.controller.grid_controller.model.last_state_tab = None
        # Clear thumbnails to force reload of updated paths
        try:
            self.controller.grid_controller.view.thumbnail_cache.clear()
        except Exception:
            pass
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def undo(self) -> None:
        if not self._saved_entry:
            return
        # Persist back
        self._persist_rename(self.new_id, self.old_id)
        # Update in-memory dict preserving order back (players or monsters)
        is_player = self.new_id in self.controller.model.player_stats
        target = self.controller.model.player_stats if is_player else self.controller.model.monsters
        new_order = {}
        for k, v in target.items():
            if k == self.new_id:
                new_order[self.old_id] = v
            else:
                new_order[k] = v
        target.clear()
        target.update(new_order)
        # If player, also re-key editor model 'classes' dict and player_assets back
        try:
            if is_player:
                classes = self.controller.editor_controller.model.classes
                if self.new_id in classes:
                    classes[self.old_id] = classes.pop(self.new_id)
        except Exception:
            pass
        # Re-key assets back
        try:
            assets = self.controller.editor_controller.model.assets
            if self.new_id in assets:
                assets[self.old_id] = assets.pop(self.new_id)
        except Exception:
            pass
        # Re-key player_assets back if applicable
        try:
            if is_player:
                p_assets = self.controller.model.player_assets
                if self.new_id in p_assets:
                    p_assets[self.old_id] = p_assets.pop(self.new_id)
        except Exception:
            pass
        # Sync picker selection/hover back
        try:
            picker_model = self.controller.editor_controller.picker_controller.model
            if picker_model.selected_id == self.new_id:
                picker_model.selected_id = self.old_id
            if picker_model.hovered_id == self.new_id:
                picker_model.hovered_id = self.old_id
        except Exception:
            pass
        # Restore selection
        self.controller.model.selected_id = self.old_id
        # Reload monster definitions/caches back to old id and refresh UI (monsters only)
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache.load_caches_for([self.old_id])
            except Exception:
                pass
        try:
            self.controller.grid_controller.model.last_entity_id = None
            self.controller.grid_controller.model.last_state_tab = None
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def apply(self) -> None:
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        self._old_value = self._get_current_value(entry)
        rel = _abs_to_rel_asset_path(self.new_path)
        self._write_value(entry, rel)
        self._persist_and_update(entry, path)

    def undo(self) -> None:
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        # Restore previous value
        parts = self.cell_key.split("_")
        if len(parts) != 3 or parts[0] != 'asset':
            return
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            sprites_set[state] = copy.deepcopy(self._old_value) if self._old_value is not None else []
        else:
            no_sets = assets.setdefault('no-sets', {})
            state_no_set = no_sets.setdefault(state, {})
            if self._old_value is None:
                state_no_set.pop(direction, None)
            else:
                state_no_set[direction] = self._old_value
        save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        ecs_world = self.controller.editor_controller.game.ecs.ecs_world
        if self.ent_id in self.controller.model.player_stats:
            self.controller.model.player_assets[self.ent_id] = entry.get('assets', {})
            update_player_assets(ecs_world, self.ent_id)
        else:
            update_monster_assets(ecs_world, self.ent_id)
            # Mirror back into in-memory monsters dict for immediate UI reflect
            try:
                self.controller.model.monsters[self.ent_id] = entry
            except Exception:
                pass
        # Clear thumbnails to avoid stale previews after undo
        try:
            self.controller.grid_controller.view.thumbnail_cache.clear()
        except Exception:
            pass


@dataclass
class ToggleActiveSetCommand(Command):
    controller: Any
    ent_id: str
    description: str = "Toggle active asset set"
    _old_active: Optional[str] = None

    def _set_active(self, target: str) -> None:
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        assets = entry.setdefault('assets', {})
        assets['active_set'] = target
        save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        # Update in-memory mirrors
        if self.ent_id in self.controller.model.player_stats:
            self.controller.model.player_assets.setdefault(self.ent_id, {})['active_set'] = target
        else:
            self.controller.model.monsters.setdefault(self.ent_id, {}).setdefault('assets', {})['active_set'] = target
        # Notify controller (refresh grid/ECS)
        self.controller._on_active_set_toggled(self.ent_id)

    def apply(self) -> None:
        path, data, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        curr = assets.get('active_set', default_active)
        self._old_active = curr
        new_val = 'no-sets' if curr == 'sets' else 'sets'
        self._set_active(new_val)

    def undo(self) -> None:
        if self._old_active is not None:
            self._set_active(self._old_active)


@dataclass
class RenameEntityCommand(Command):
    controller: Any  # EntityPropertiesPanelController
    old_id: str
    new_id: str
    description: str = "Rename entity id"
    _saved_entry: Any = None

    def _persist_rename(self, src_id: str, dst_id: str) -> None:
        # Generic: resolve source file and section by membership
        path, _, entry = load_entity_data(src_id, self.controller.model.player_stats, self.controller.model.monsters)
        root = load_from_json(path)
        section = 'players' if src_id in self.controller.model.player_stats else 'monsters'
        classes = root.setdefault(section, {}).setdefault('classes', {})
        # Rebuild dict to preserve insertion order while replacing the key in-place
        new_classes = {}
        for k, v in classes.items():
            if k == src_id:
                new_classes[dst_id] = entry
            else:
                new_classes[k] = v
        root.setdefault(section, {})['classes'] = new_classes
        # If renaming a player class, also update DEFAULT_CLASS if it pointed to the old id
        if section == 'players':
            try:
                if root.get('DEFAULT_CLASS') == src_id:
                    root['DEFAULT_CLASS'] = dst_id
            except Exception:
                pass
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def apply(self) -> None:
        if not self.old_id or not self.new_id or self.old_id == self.new_id:
            return
        # Disallow conflicts
        if self.new_id in self.controller.model.player_stats or self.new_id in self.controller.model.monsters:
            # Conflict: do nothing
            return
        # Save current entry for undo
        _, _, entry = load_entity_data(self.old_id, self.controller.model.player_stats, self.controller.model.monsters)
        self._saved_entry = copy.deepcopy(entry)
        # Persist to JSON
        self._persist_rename(self.old_id, self.new_id)
        # Update in-memory dict preserving order (replace key in place) for players or monsters
        is_player = self.old_id in self.controller.model.player_stats
        target = self.controller.model.player_stats if is_player else self.controller.model.monsters
        new_order = {}
        for k, v in target.items():
            if k == self.old_id:
                new_order[self.new_id] = v
            else:
                new_order[k] = v
        target.clear()
        target.update(new_order)
        # If player, also re-key editor model 'classes' dict and player_assets mapping
        try:
            if is_player:
                classes = self.controller.editor_controller.model.classes
                if self.old_id in classes:
                    classes[self.new_id] = classes.pop(self.old_id)
        except Exception:
            pass
        # Re-key assets dict so picker keeps the same icon under the new id
        try:
            assets = self.controller.editor_controller.model.assets
            if self.old_id in assets:
                assets[self.new_id] = assets.pop(self.old_id)
        except Exception:
            pass
        # Re-key player_assets mapping if applicable
        try:
            if is_player:
                p_assets = self.controller.model.player_assets
                if self.old_id in p_assets:
                    p_assets[self.new_id] = p_assets.pop(self.old_id)
        except Exception:
            pass
        # Sync picker selection/hover if pointing to the renamed id
        try:
            picker_model = self.controller.editor_controller.picker_controller.model
            if picker_model.selected_id == self.old_id:
                picker_model.selected_id = self.new_id
            if picker_model.hovered_id == self.old_id:
                picker_model.hovered_id = self.new_id
        except Exception:
            pass
        # Update selection in UI
        self.controller.model.selected_id = self.new_id
        # Reload monster definitions/caches so new id is recognized by animators and lookups (monsters only)
        if not is_player:
            try:
                from roguelike_game.factories.monster.config import reload_monster_defs
                from roguelike_game.factories.monster import cache as monster_cache
                reload_monster_defs()
                monster_cache.load_caches_for([self.new_id])
            except Exception:
                pass
        # Reset grid caches to rebuild animators under new id and redraw UI
        try:
            self.controller.grid_controller.model.last_entity_id = None
            self.controller.grid_controller.model.last_state_tab = None
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

@dataclass
class DeleteEntityDefinitionCommand(Command):
    controller: Any  # EntityPropertiesPanelController
    ent_id: str
    description: str = "Delete entity definition"
    _saved_entry: Any = None
    _saved_index: Optional[int] = None
    _section: Optional[str] = None  # 'players' or 'hostiles'
    _path: Optional[str] = None
    _saved_default: Any = None

    def _persist_delete(self) -> None:
        # Resolve file and section by membership
        path, _, entry = load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        self._path = path
        root = load_from_json(path)
        self._section = 'players' if self.ent_id in self.controller.model.player_stats else 'hostiles'
        classes = root.setdefault(self._section, {}).setdefault('classes', {})
        # Save state for undo
        self._saved_entry = copy.deepcopy(entry)
        keys = list(classes.keys())
        try:
            self._saved_index = keys.index(self.ent_id)
        except ValueError:
            self._saved_index = None
        # For players remember DEFAULT_CLASS
        if self._section == 'players':
            self._saved_default = root.get('DEFAULT_CLASS')
        # Rebuild dict without the deleted id (preserve order)
        new_classes = {}
        for k, v in classes.items():
            if k != self.ent_id:
                new_classes[k] = v
        root.setdefault(self._section, {})['classes'] = new_classes
        # Adjust DEFAULT_CLASS if needed
        if self._section == 'players':
            try:
                if root.get('DEFAULT_CLASS') == self.ent_id:
                    first = next(iter(new_classes.keys()), None)
                    if first is None:
                        root.pop('DEFAULT_CLASS', None)
                    else:
                        root['DEFAULT_CLASS'] = first
            except Exception:
                pass
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def _remove_in_memory(self) -> None:
        # Remove from in-memory models and assets
        is_player = self.ent_id in self.controller.model.player_stats
        if is_player:
            self.controller.model.player_stats.pop(self.ent_id, None)
            try:
                self.controller.model.player_assets.pop(self.ent_id, None)
            except Exception:
                pass
            try:
                classes = self.controller.editor_controller.model.classes
                classes.pop(self.ent_id, None)
            except Exception:
                pass
        else:
            self.controller.model.monsters.pop(self.ent_id, None)
        # Remove from global assets used by picker icons
        try:
            assets = self.controller.editor_controller.model.assets
            assets.pop(self.ent_id, None)
        except Exception:
            pass
        # Sync picker selection/hover if pointing to the deleted id
        try:
            picker_model = self.controller.editor_controller.picker_controller.model
            if picker_model.selected_id == self.ent_id:
                picker_model.selected_id = None
            if picker_model.hovered_id == self.ent_id:
                picker_model.hovered_id = None
        except Exception:
            pass
        # Refresh monster defs and caches when deleting a monster
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache._loaded_variants.discard(self.ent_id)
                monster_cache._SPRITE_SURFACES.pop(self.ent_id, None)
                monster_cache._DEATH_SURFACES.pop(self.ent_id, None)
            except Exception:
                pass

    def apply(self) -> None:
        # Validate membership
        if not (self.ent_id in self.controller.model.player_stats or self.ent_id in self.controller.model.monsters):
            return
        self._persist_delete()
        self._remove_in_memory()
        # Clear selection in Properties Panel UI
        try:
            self.controller.model.hovered_entity_id = None
            if self.controller.model.selected_id == self.ent_id:
                self.controller.model.selected_id = None
        except Exception:
            pass
        # Try to force a redraw
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def _persist_restore(self) -> None:
        if not (self._path and self._section and self._saved_entry is not None):
            return
        root = load_from_json(self._path)
        classes = root.setdefault(self._section, {}).setdefault('classes', {})
        # Reinsert at original index if available, else append
        new_classes = {}
        inserted = False
        if self._saved_index is None:
            new_classes.update(classes)
            new_classes[self.ent_id] = self._saved_entry
        else:
            for i, (k, v) in enumerate(classes.items()):
                if i == self._saved_index:
                    new_classes[self.ent_id] = self._saved_entry
                    inserted = True
                new_classes[k] = v
            if not inserted:
                new_classes[self.ent_id] = self._saved_entry
        root.setdefault(self._section, {})['classes'] = new_classes
        # Restore DEFAULT_CLASS if section is players
        if self._section == 'players':
            try:
                if self._saved_default is None:
                    root.pop('DEFAULT_CLASS', None)
                else:
                    root['DEFAULT_CLASS'] = self._saved_default
            except Exception:
                pass
        with open(self._path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def _restore_in_memory(self) -> None:
        # Restore in-memory dicts preserving index
        is_player = (self._section == 'players')
        if is_player:
            stats = copy.deepcopy(self._saved_entry.get('stats', {}))
            target = self.controller.model.player_stats
        else:
            target = self.controller.model.monsters
        new_order = {}
        items = list(target.items())
        if self._saved_index is None:
            new_order.update(target)
            if is_player:
                new_order[self.ent_id] = stats
            else:
                new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
        else:
            inserted = False
            for i, (k, v) in enumerate(items):
                if i == self._saved_index:
                    if is_player:
                        new_order[self.ent_id] = stats
                    else:
                        new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
                    inserted = True
                new_order[k] = v
            if not inserted:
                if is_player:
                    new_order[self.ent_id] = stats
                else:
                    new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
        target.clear()
        target.update(new_order)
        # Restore supporting mirrors
        if is_player:
            try:
                self.controller.model.player_assets[self.ent_id] = copy.deepcopy(self._saved_entry.get('assets', {}))
            except Exception:
                pass
            try:
                classes = self.controller.editor_controller.model.classes
                classes[self.ent_id] = copy.deepcopy(self._saved_entry)
            except Exception:
                pass
        # Restore icon into assets used by picker
        try:
            assets_map = self.controller.editor_controller.model.assets
            if is_player:
                try:
                    cfg = self.controller.editor_controller.model.classes.get(self.ent_id, {})
                    idle_list = cfg.get('assets', {}).get('sets', {}).get('sprites_set', {}).get('idle', [])
                    if idle_list:
                        path = idle_list[0]
                        orig_size = tuple(self.controller.editor_controller.model.orig_size)
                        frames = load_sprite_sheet(path, orig_size, columns=1)
                        assets_map[self.ent_id] = frames[0]
                    else:
                        asset_info = PLAYER_ASSETS.get(self.ent_id)
                        path = None
                        if isinstance(asset_info, str):
                            path = asset_info
                        elif isinstance(asset_info, dict):
                            path = next(iter(asset_info.values()), None)
                        if path:
                            frames = load_sprite_sheet(path, tuple(self.controller.editor_controller.model.orig_size), columns=1)
                            assets_map[self.ent_id] = frames[0]
                        else:
                            try:
                                assets_map[self.ent_id] = load_image(f"assets/npc/player/{self.ent_id}/{self.ent_id}_1_down.png")
                            except Exception:
                                pass
                except Exception:
                    pass
            else:
                try:
                    sprite, _ = create_sprite_component(self.ent_id)
                    assets_map[self.ent_id] = sprite.image
                except Exception:
                    pass
        except Exception:
            pass
        # Refresh monster defs caches when restoring monster
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache.load_caches_for([self.ent_id])
            except Exception:
                pass
        # Attempt redraw
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def undo(self) -> None:
        if self._saved_entry is None:
            return
        self._persist_restore()
        self._restore_in_memory()
