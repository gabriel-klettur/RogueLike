"""Service: confirm and persist adding the current entity into the system."""
from __future__ import annotations

import logging

from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_editors.entities.services.constants import ADD_ENTITIES_ON_SYSTEM
from .entity_properties_service import load_entity_data, save_entity_data

logger = logging.getLogger(__name__)


def confirm_add_entity_on_system(controller) -> None:
    """Persist the currently selected entity and exit Add-Entities-On-System mode."""
    sel_id = getattr(controller.model, 'selected_id', None)
    if not sel_id:
        return
    try:
        is_player = sel_id in controller.model.player_stats
        if is_player:
            p_stats = controller.model.player_stats.get(sel_id, {})
            p_assets = controller.model.player_assets.get(sel_id, {}) if isinstance(controller.model.player_assets, dict) else {}
            entry = {'stats': p_stats, 'assets': p_assets}
            path, _, _ = load_entity_data(sel_id, controller.model.player_stats, controller.model.monsters)
            save_entity_data(sel_id, entry, path, controller.model.player_stats, controller.model.monsters)
            logger.debug("Player class '%s' confirmed and saved to JSON", sel_id)
            try:
                temp = controller.model.monsters.get(sel_id)
                if isinstance(temp, dict) and temp.get('__pending__'):
                    controller.model.monsters.pop(sel_id, None)
            except Exception:
                pass
        else:
            path, data, entry = load_entity_data(sel_id, controller.model.player_stats, controller.model.monsters)
            cur = controller.model.monsters.get(sel_id)
            if cur is not None:
                entry.update(cur)
                if isinstance(entry, dict):
                    entry.pop('__pending__', None)
                if isinstance(cur, dict):
                    cur.pop('__pending__', None)
            save_entity_data(sel_id, entry, path, controller.model.player_stats, controller.model.monsters)
            logger.debug("Hostile type '%s' confirmed and saved to JSON", sel_id)
            try:
                reload_monster_defs()
                logger.debug("Definiciones de hostiles recargadas tras confirmar")
            except Exception as e:
                logger.error("[WARN][PropertiesPanel] No se pudieron recargar definiciones de hostiles: %s", e)
    except Exception as e:
        logger.error("[ERROR][PropertiesPanel] Error al confirmar entidad '%s': %s", sel_id, e)

    try:
        arm = controller.editor_controller.model.add_remove_model
        if getattr(arm, 'active_tool', None) == ADD_ENTITIES_ON_SYSTEM:
            arm.active_tool = None
        controller.model.show_add_system_selector = False
        controller.model.entity_type_rect = None
        if hasattr(controller.model, 'confirm_button_rect'):
            controller.model.confirm_button_rect = None
        try:
            controller.editor_controller.exit_add_entities_on_system_mode()
        except Exception:
            pass
    except Exception:
        pass

    try:
        controller.editor_controller.render(controller.editor_controller.game.screen)
    except Exception:
        pass
