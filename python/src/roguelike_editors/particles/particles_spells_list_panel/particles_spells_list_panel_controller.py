from __future__ import annotations

import pygame
from typing import Any, List, Tuple

from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_game.config.spells_config import SPELLS, SPELLS_VERSION, SpellConfig

from .particles_spells_list_panel_model import ParticlesSpellsListPanelModel
from .particles_spells_list_panel_view import ParticlesSpellsListPanelView


class ParticlesSpellsListPanelController:
    """Controller for the spells-usage list panel.

    Finds and displays which spells use the currently selected particle preset
    from the picker. Non-interactive except for expand/collapse toggle.
    """

    def __init__(self, font: pygame.font.Font | None):
        self.model = ParticlesSpellsListPanelModel()
        self.view = ParticlesSpellsListPanelView(font)

    # ---- Public API ----
    def set_anchor_from_editor(self, editor_controller) -> None:
        """Place panel below the Properties panel when available, otherwise top-right."""
        # Default top-right
        try:
            game = getattr(editor_controller, "game", None)
            screen = getattr(game, "screen", None)
            if screen is not None:
                w = int(getattr(self.model, "width", 260))
                self.model.x = int(screen.get_width() - w - UI_MARGIN)
                self.model.y = int(UI_MARGIN)
        except Exception:
            pass

        # If properties panel is visible, place right under it
        try:
            props = getattr(editor_controller, "particles_properties_controller", None)
            pr = getattr(getattr(props, "view", None), "panel_rect", None)
            if pr is not None:
                self.model.x = int(pr.x)
                self.model.y = int(pr.bottom + UI_MARGIN)
        except Exception:
            pass

        # Sync picker selection id
        try:
            picker = getattr(editor_controller, "particles_picker_controller", None)
            pid = getattr(getattr(picker, "model", None), "selected_id", None)
            self.model.selected_preset_id = pid if isinstance(pid, str) else None
        except Exception:
            self.model.selected_preset_id = None

    def update_usages(self) -> None:
        pid = self.model.selected_preset_id
        if not isinstance(pid, str) or not pid:
            self.model.usages = []
            self.model._last_computed_for = None
            self.model._last_spells_version = SPELLS_VERSION
            return
        if self.model._last_computed_for == pid and self.model._last_spells_version == SPELLS_VERSION:
            return
        usages: List[Tuple[str, str]] = []
        for key, cfg in SPELLS.items():
            try:
                path_hits = self._find_preset_usages_in_cfg(cfg, pid)
                for p in path_hits:
                    usages.append((key, p))
            except Exception:
                continue
        # Sort by spell key for stable view
        usages.sort(key=lambda t: t[0])
        self.model.usages = usages
        self.model._last_computed_for = pid
        self.model._last_spells_version = SPELLS_VERSION

    def render(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)

    def handle_event(self, event: pygame.event.Event) -> bool:
        if not getattr(self.model, "visible", False):
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is None:
                return False
            try:
                if self.view.toggle_rect and self.view.toggle_rect.collidepoint((mx, my)):
                    self.model.expanded = not bool(self.model.expanded)
                    return True
            except Exception:
                pass
        return False

    # ---- Internals ----
    def _find_preset_usages_in_cfg(self, cfg: SpellConfig, preset_id: str) -> List[str]:
        paths: List[str] = []
        # Direct vfx preset (flattened left as string)
        try:
            if isinstance(cfg.vfx, str) and cfg.vfx == preset_id:
                paths.append("vfx.preset")
        except Exception:
            pass
        # Nested vfx object preserved in cfg.extra['vfx']
        try:
            vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
            if isinstance(vfx_obj, dict):
                for p in self._find_preset_paths_recursive(vfx_obj, preset_id, prefix="vfx"):
                    if p not in paths:
                        paths.append(p)
        except Exception:
            pass
        return paths

    def _find_preset_paths_recursive(self, node: Any, preset_id: str, prefix: str) -> List[str]:
        hits: List[str] = []
        if isinstance(node, dict):
            for k, v in node.items():
                if k == 'preset' and isinstance(v, str) and v == preset_id:
                    hits.append(f"{prefix}.preset")
                else:
                    hits.extend(self._find_preset_paths_recursive(v, preset_id, f"{prefix}.{k}"))
        elif isinstance(node, list):
            for idx, v in enumerate(node):
                hits.extend(self._find_preset_paths_recursive(v, preset_id, f"{prefix}[{idx}]"))
        return hits

