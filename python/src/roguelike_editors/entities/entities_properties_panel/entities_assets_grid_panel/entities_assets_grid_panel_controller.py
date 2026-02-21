import logging
import time
from typing import Any, Dict, Optional

import pygame

from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_view import AssetsGridPanelView
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events import AssetsGridPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.services.assets_maps import (
    DIR_MAP as _DIR_TO_SPRITE,
    ui_state_to_internal,
)
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.factories.player.loader import load_and_scale_sprites
from roguelike_game.factories.player.config import ANIMATION_INTERVAL

logger = logging.getLogger(__name__)


class AssetsGridPanelController:
    """
    Controlador del panel de cuadrícula de assets dentro del panel de propiedades.

    Responsabilidades:
    - Detectar cambios de entidad o pestaña de estado y reconstruir animadores.
    - Avanzar fotogramas de animación mediante temporizadores.
    - Delegar el pintado a la vista.
    """

    def __init__(
        self,
        parent_controller: Any,  # EntityPropertiesPanelController
        font: pygame.font.Font,
    ) -> None:
        # Referencias a controladores y modelos superiores
        self.parent_controller = parent_controller
        self.parent_model = parent_controller.model

        # Modelo y vista propios
        self.model = AssetsGridPanelModel()
        self.view = AssetsGridPanelView(font)
        self.view.parent_model = self.parent_model

        # Manejador de eventos para el grid
        self.event_handler = AssetsGridPanelEventHandler(self)

    def draw(
        self,
        screen: pygame.Surface,
        entity_data: dict,
        px: int,
        py: int,
        pad: int,
        font_h: int,
        panel_w: int,
    ) -> None:
        """
        Dibuja las subpestañas y la cuadrícula de assets.

        1) Reconstruye animadores si cambió la entidad o el estado activo.
        2) Actualiza fotogramas en base a temporizadores.
        3) Delegación del renderizado a la vista.
        """
        ent_id = self.parent_model.selected_id
        active_state = (
            self.parent_controller.state_tabs_controller.model.active_state_tab
        )

        if self._should_rebuild(ent_id, active_state):
            self._rebuild_animators(ent_id, active_state)

        self._update_frames()

        # Delegación del dibujo a la vista
        self.view.draw(screen, self.model, entity_data, px, py, pad, font_h, panel_w)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """
        Envía el evento al manejador de eventos del grid.

        :return: True si el evento fue procesado, False en caso contrario.
        """
        return self.event_handler.handle(event)

    def _should_rebuild(
        self,
        entity_id: Optional[str],
        state: str,
    ) -> bool:
        """
        Comprueba si es necesario reconstruir los animadores.
        Se produce cuando cambia la entidad seleccionada o la pestaña de estado.
        """
        return (
            entity_id is not None
            and (
                entity_id != getattr(self.model, 'last_entity_id', None)
                or state != getattr(self.model, 'last_state_tab', None)
            )
        )

    def _rebuild_animators(self, entity_id: str, state: str) -> None:
        """
        Reconstruye todos los animadores para la entidad y estado indicados.

        1) Carga los sprites (jugador/monstruo) respetando la lógica de 'pendiente'.
        2) Traduce el estado de UI a estado interno.
        3) Crea un Animator por cada dirección válida.
        4) Reinicia temporizadores y fotogramas anteriores.
        """
        logger.debug(
            f"[DEBUG][AssetsGridPanel] Reconstruyendo animadores: "
            f"entity_id={entity_id}, state={state}"
        )

        # Carga de sprites calibrados para la entidad
        if entity_id in self.parent_model.player_stats:
            sprites: Dict[str, Dict[str, list]] = self._load_player_sprites(entity_id)
        else:
            sprites = self._load_monster_sprites(entity_id)

        internal_state = ui_state_to_internal(state)

        # Limpieza de datos previos
        self.model.animators.clear()
        self.model.timers.clear()
        self.model.last_frames.clear()

        # Creación de animadores en base a cada dirección disponible
        for grid_dir, sprite_dir in _DIR_TO_SPRITE.items():
            raw_frames = sprites.get(sprite_dir, {}).get(internal_state, [])
            frames = raw_frames[1:] if len(raw_frames) > 1 else []
            if not frames:
                continue

            asset_key = f"asset_{state}_{grid_dir}"
            animator = Animator(
                animations={internal_state: frames},
                current_state=internal_state,
            )
            self.model.animators[asset_key] = animator

        # Almacenamiento de estado para comparaciones futuras
        self.model.last_entity_id = entity_id
        self.model.last_state_tab = state

    def _is_pending_monster(self, entity_id: str) -> bool:
        """Devuelve True si el monstruo seleccionado está marcado como '__pending__'."""
        try:
            m_entry = (
                self.parent_model.monsters.get(entity_id)
                if isinstance(self.parent_model.monsters, dict)
                else None
            )
            return bool(isinstance(m_entry, dict) and m_entry.get('__pending__'))
        except Exception:
            return False

    def _load_player_sprites(self, entity_id: str) -> Dict[str, Dict[str, list]]:
        """Carga y escala sprites para jugador."""
        return load_and_scale_sprites(entity_id)

    def _load_monster_sprites(self, entity_id: str) -> Dict[str, Dict[str, list]]:
        """
        Carga sprites de monstruo desde la caché runtime del juego.
        Si la entidad es nueva y está en estado '__pending__', no toca la caché y
        devuelve un mapeo vacío para que la grilla muestre zonas de drop.
        """
        if self._is_pending_monster(entity_id):
            # No hay sprites precargados aún; mantenemos vacío para que la grilla muestre drop targets
            return {}

        from roguelike_game.factories.monster.cache import load_caches_for, _SPRITE_SURFACES
        load_caches_for([entity_id])
        raw_map = _SPRITE_SURFACES.get(entity_id, {})
        sprites: Dict[str, Dict[str, list]] = {}
        for flat_key, surf in raw_map.items():
            if not surf:
                continue
            parts = flat_key.split('_', 1)
            if len(parts) == 2:
                state_name, dir_code = parts
            else:
                state_name = 'idle'
                dir_code = parts[0]
            sprite_dir = _DIR_TO_SPRITE.get(dir_code, dir_code)
            sprites.setdefault(sprite_dir, {}).setdefault(state_name, []).append(surf)
        return sprites

    def _update_frames(self) -> None:
        """
        Avanza y almacena el fotograma actual de cada animador
        basado en su temporizador individual.
        """
        now = time.time()

        for key, animator in self.model.animators.items():
            timer = self.model.timers.get(key)

            if timer is None:
                # Primera ejecución para este animador
                timer = AnimationTimer(last_time=now, interval=ANIMATION_INTERVAL)
                self.model.timers[key] = timer
                frame = animator.next_frame()
                self.model.last_frames[key] = frame
            elif (now - timer.last_time) >= timer.interval:
                # Tiempo de avanzar fotograma
                timer.last_time = now
                frame = animator.next_frame()
                self.model.last_frames[key] = frame
