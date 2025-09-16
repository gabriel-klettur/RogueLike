import pygame
pygame.font.init()
import os
import logging
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_picker_panel.spells_editor_view import SpellEditorView
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.spells.spells_picker_panel.spells_editor_events import SpellEditorEventHandler
from roguelike_editors.spells.spells_properties_panel.spells_properties_panel_controller import (
    SpellsPropertiesPanelController,
)
from roguelike_engine.utils.loader import load_image, _IMAGE_CACHE
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_model import (
    SpellsToolBarPanelModel,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_view import (
    SpellsToolBarPanelView,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_events import (
    SpellsToolBarPanelEventHandler,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_controller import (
    SpellsToolBarPanelController,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_model import (
    SpellsAddRemovePanelModel,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_view import (
    SpellsAddRemovePanelView,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_events import (
    SpellsAddRemovePanelEventHandler,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_controller import (
    SpellsAddRemovePanelController,
)
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_game.config.spells_config import reload_spells
from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewSmoke,
    ParticlePreviewSmokeBurst,
    ParticlePreviewFirework,
    ParticlePreviewLightning,
    ParticlePreviewAura,
    ParticlePreviewDash,
    ParticlePreviewSlash,
    ParticlePreviewLaser,
    ParticlePreviewExplosion,
    ParticlePreviewArcaneFlame,
    ParticlePreviewHealingAura,
    ParticlePreviewTeleport,
)
from roguelike_editors.particles.services.preview_builder import build_preview_for_definition
from roguelike_editors.spells.spells_tutorial_panel.spells_tutorial_panel_controller import (
    SpellsTutorialPanelController,
)

logger = logging.getLogger(__name__)
# Env-gated spelling editor preview debug
LOG_SPELLS_PREVIEW_DEBUG = (
    os.getenv("RL_SPELLS_PREVIEW_DEBUG") == "1"
    or os.getenv("RL_SPELLS_EDITOR_DEBUG") == "1"
)

class SpellEditorController:
    """Controller for Spell Editor UI."""
    def __init__(self, spells: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = SpellEditorModel(spells=spells.copy(), assets=assets)
        self.view = SpellEditorView(assets, font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        self.event_handler = SpellEditorEventHandler(self)
        # Cache of built previews per spell id
        self._particle_previews: dict[str, object] = {}
        # Track last frame we emitted a provider-call debug to avoid spamming across providers
        self._last_preview_debug_frame: int = -1
        # Throttle timestamp for frame-id debug logs (ms)
        self._last_frameid_log_ts: int = 0
        # Toolbar MVC
        self.spells_toolbar_model = SpellsToolBarPanelModel()
        self.spells_toolbar_view = SpellsToolBarPanelView(controller=self, model=self.spells_toolbar_model)
        self.spells_toolbar_events = SpellsToolBarPanelEventHandler(controller=self, model=self.spells_toolbar_model)
        self.spells_toolbar_controller = SpellsToolBarPanelController(self, self.spells_toolbar_model, self.spells_toolbar_view, self.spells_toolbar_events)
        # Ensure ToolbarView uses the panel controller for active-state checks
        if hasattr(self.spells_toolbar_view, 'widget'):
            self.spells_toolbar_view.widget.controller = self.spells_toolbar_controller
        # Add/Remove MVC
        self.spells_add_remove_model = SpellsAddRemovePanelModel()
        self.spells_add_remove_view = SpellsAddRemovePanelView(controller=self, model=self.spells_add_remove_model)
        self.spells_add_remove_events = SpellsAddRemovePanelEventHandler(controller=self, model=self.spells_add_remove_model)
        self.spells_add_remove_controller = SpellsAddRemovePanelController(self, self.spells_add_remove_model, self.spells_add_remove_view, self.spells_add_remove_events)
        if hasattr(self.spells_add_remove_view, 'widget'):
            self.spells_add_remove_view.widget.controller = self.spells_add_remove_controller

        # Properties panel MVC
        self.spells_properties_controller = SpellsPropertiesPanelController(self.model.spells, font)
        # Link back so properties panel can query preview providers and selection from this controller
        try:
            self.spells_properties_controller.editor_controller = self
        except Exception:
            pass
        # Tutorial panel controller (overlay)
        try:
            self.spells_tutorial = SpellsTutorialPanelController(self)
        except Exception:
            self.spells_tutorial = None

        # Provide callbacks
        def _get_assets_anchor_rect():
            """Return an anchor rect so the Assets picker appears BELOW and ALIGNED to the Spells Picker panel.
            Primary anchor: the picker's grid_rect (left x, bottom y + margin, same width).
            Fallbacks: asset cell rect, then properties panel rect.
            """
            # Prefer aligning to the Spells Picker grid
            try:
                grid_rect = getattr(self.view, 'grid_rect', None)
                if grid_rect is not None:
                    # Build a rect whose top-left is just below the grid, aligned on the left, and with same width
                    return pygame.Rect(grid_rect.x, grid_rect.bottom + UI_MARGIN, grid_rect.w, 0)
            except Exception:
                pass
            # Fallback to the properties panel cell rect if present
            try:
                cell = getattr(self.spells_properties_controller.model, 'asset_cell_rect', None)
                if cell:
                    return cell
            except Exception:
                pass
            # Final fallback to the properties panel rect
            try:
                return getattr(self.spells_properties_controller.model, 'panel_rect', None)
            except Exception:
                return None

        def _on_asset_changed(spell_id: str, new_asset_path: str) -> None:
            try:
                img = load_image(new_asset_path)
                self.model.assets[spell_id] = img
                # Keep view dict in sync
                try:
                    self.view.assets[spell_id] = img
                except Exception:
                    pass
                # Hot-reload game spells so new casts use updated sprite/scale
                try:
                    reload_spells()
                except Exception:
                    pass
                # Rebuild preview providers in case this change toggles preview behavior
                try:
                    self._rebuild_particle_preview_providers()
                except Exception:
                    pass
            except Exception:
                # Leave asset unchanged on failure
                pass

        def _on_after_commit_edit(key: str, old_id: str, new_id: str | None, value):
            # Handle id rename to keep model and assets in sync
            if key == 'id' and new_id and new_id != old_id:
                try:
                    if old_id in self.model.spells:
                        self.model.spells[new_id] = self.model.spells.pop(old_id)
                    if old_id in self.model.assets:
                        self.model.assets[new_id] = self.model.assets.pop(old_id)
                    if self.model.selected_id == old_id:
                        self.model.selected_id = new_id
                    if self.model.hovered_id == old_id:
                        self.model.hovered_id = new_id
                except Exception:
                    pass
            # Hot-reload game spells so runtime immediately reflects edits
            try:
                reload_spells()
            except Exception:
                pass
            # Rebuild previews so picker reflects particle setting or params
            try:
                self._rebuild_particle_preview_providers()
            except Exception:
                pass

        self.spells_properties_controller.get_assets_anchor_rect = _get_assets_anchor_rect
        self.spells_properties_controller.on_asset_changed = _on_asset_changed
        self.spells_properties_controller.on_after_commit_edit = _on_after_commit_edit

        # Frame id used to ensure previews update only once per frame across all views
        self._render_frame_id: int = 0

        # Provide a left-anchor provider so the picker grid sits to the right of Add/Remove panel
        def _picker_left_anchor_x() -> int | None:
            try:
                # Only when picker is visible (tied to 'spells_on_map')
                if not getattr(self.model, 'picker_visible', False):
                    return None
                # Need toolbar widget to compute base position
                tb_widget = getattr(self.spells_toolbar_view, 'widget', None)
                if tb_widget is None:
                    return None
                tb_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
                tb_w, _ = tb_widget.panel.surface.get_size()
                # Add/Remove width (even if not yet rendered this frame)
                arm_widget = getattr(self.spells_add_remove_view, 'widget', None)
                if arm_widget is None:
                    return tb_pos[0] + tb_w + UI_MARGIN
                arm_w, _ = arm_widget.panel.surface.get_size()
                # Picker left is to the right of Add/Remove panel
                return tb_pos[0] + tb_w + UI_MARGIN + arm_w + UI_MARGIN
            except Exception:
                return None

        try:
            self.view.get_picker_left_anchor_x = _picker_left_anchor_x
        except Exception:
            pass

        # Initialize particle previews for spells with vfx.preview == 'particles'
        try:
            self._rebuild_particle_preview_providers()
        except Exception:
            pass

    def handle_event(self, event: pygame.event.Event) -> None:
        # Route to toolbar first, then add/remove, only if editor visible
        if self.model.visible:
            # Tutorial overlay consumes clicks inside its panel and ESC while active
            try:
                if getattr(self, 'spells_tutorial', None) is not None and self.spells_tutorial.is_active():
                    if self.spells_tutorial.handle_event(event):
                        return
            except Exception:
                pass
            if self.spells_toolbar_controller.handle_event(event):
                return
            if self.spells_add_remove_controller.handle_event(event):
                return
            # Then properties panel
            if self.spells_properties_controller.handle_event(event):
                return
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        # Advance frame id once per controller draw call
        self._render_frame_id += 1
        if LOG_SPELLS_PREVIEW_DEBUG and logger.isEnabledFor(logging.DEBUG):
            now_ms = pygame.time.get_ticks()
            if now_ms - getattr(self, "_last_frameid_log_ts", 0) >= 1000:
                try:
                    logger.debug("[SpellsEditor] frame_id=%d", self._render_frame_id)
                except Exception:
                    pass
                try:
                    self._last_frameid_log_ts = now_ms
                except Exception:
                    pass
        self.view.draw(screen, self.model)
        # Draw toolbar and add/remove on top only when visible
        if self.model.visible:
            self.spells_toolbar_controller.render(screen)
            self.spells_add_remove_controller.render(screen)
            # Update context and draw properties panel only when picker is visible and not in delete mode
            if getattr(self.model, 'picker_visible', False) and not getattr(self.model, 'delete_mode_active', False):
                self.spells_properties_controller.update_context(
                    self.model.spells, self.model.selected_id, self.model.hovered_id
                )
                title_rect = getattr(self.view, 'title_rect', None)
                # Anchor properties to the right of the picker grid like Entities editor
                grid_rect = getattr(self.view, 'grid_rect', None)
                if grid_rect is not None:
                    left_x = grid_rect.right + UI_MARGIN
                    top_y = grid_rect.y
                    try:
                        self.spells_properties_controller.view.set_anchor(left_x, top_y)
                    except Exception:
                        pass
                else:
                    try:
                        self.spells_properties_controller.view.set_anchor(None, None)
                    except Exception:
                        pass
                self.spells_properties_controller.draw(screen, title_rect=title_rect)

        # Blink yellow border around the Spells Picker panel while in add or remove modes
        # to guide user action (duplicate-on-selection or delete-on-click).
        try:
            active_tool = getattr(self.spells_add_remove_model, 'active_tool', None)
            if active_tool in ('add_spell', 'remove_spell') and getattr(self.model, 'picker_visible', False):
                panel_rect = getattr(self.model, 'panel_rect', None)
                if panel_rect:
                    now = pygame.time.get_ticks()
                    if (now // 500) % 2 == 0:
                        pygame.draw.rect(screen, (255, 255, 0), panel_rect.inflate(6, 6), 3)
        except Exception:
            pass

        # Render tutorial overlay on top
        try:
            if getattr(self, 'spells_tutorial', None) is not None:
                self.spells_tutorial.render(screen)
        except Exception:
            pass

    def reload_sprites_from_spells(self) -> None:
        """Clear cached images and reload sprites for all spells in the picker.

        This ensures that changes to sprite files/paths are reflected immediately
        after pressing the Reload button in the toolbar.
        """
        # Clear global image cache to avoid stale PNGs
        try:
            _IMAGE_CACHE.clear()
        except Exception:
            pass
        # Rebuild per-spell sprite surfaces
        for sid, sdef in list(self.model.spells.items()):
            try:
                path = None
                # Prefer flattened 'sprite' if present
                v = sdef.get('sprite')
                if isinstance(v, str) and v:
                    path = v
                else:
                    # Fallback to nested vfx.sprite.path
                    vfx = sdef.get('vfx') if isinstance(sdef.get('vfx'), dict) else None
                    if isinstance(vfx, dict):
                        spr = vfx.get('sprite') if isinstance(vfx.get('sprite'), dict) else None
                        if isinstance(spr, dict):
                            p = spr.get('path')
                            if isinstance(p, str) and p:
                                path = p
                if not path:
                    continue
                img = load_image(path)
                self.model.assets[sid] = img
                try:
                    self.view.assets[sid] = img
                except Exception:
                    pass
            except Exception:
                # Continue best-effort reload for remaining spells
                pass

    def _commit_edit(self) -> None:
        if not self.model.editing_property:
            return
        sid = self.model.selected_id or self.model.hovered_id
        if not sid:
            return
        key = self.model.editing_property
        new_text = self.model.editing_text
        # JSON path
        path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
        root = load_from_json(path)
        entry = root.get(sid, {})
        old_val = entry.get(key)
        # Convert type
        try:
            if isinstance(old_val, bool):
                converted = new_text.lower() in ("true", "1", "yes")
            elif isinstance(old_val, int):
                converted = int(new_text)
            elif isinstance(old_val, float):
                converted = float(new_text)
            else:
                converted = new_text
        except ValueError:
            converted = new_text
        entry[key] = converted
        # Persist changes
        save_to_json(path, sid, entry)
        # Hot-reload runtime spells config so new casts reflect changes
        try:
            reload_spells()
        except Exception:
            pass
        # Rebuild previews in case vfx.preview or particle params changed
        try:
            self._rebuild_particle_preview_providers()
        except Exception:
            pass
        # Update model
        self.model.spells[sid] = entry
        # Reset editing
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0

    # --- Internal: particle preview wiring ---
    def _is_particle_spell(self, sdef: dict) -> bool:
        try:
            vfx = sdef.get('vfx', {}) or {}
            # Explicit flag
            if vfx.get('preview') == 'particles':
                return True
            # Implicit: if particles config exists and is a dict, assume particle preview
            parts = vfx.get('particles')
            if isinstance(parts, dict) and len(parts) > 0:
                return True
            # Inferred by spell type: these are inherently VFX-heavy and we provide lightweight previews
            stype = sdef.get('type')
            if stype in ('lightning', 'aura', 'beam', 'dash', 'slash', 'arcane_flame', 'firework', 'firework_launch', 'smoke_emitter', 'smoke', 'teleport', 'sphere_magic_shield'):
                return True
            # Fallback by id substring
            sid = sdef.get('id') or ''
            sid_l = str(sid).lower()
            for kw in ('aura', 'beam', 'laser', 'dash', 'slash', 'lightning', 'firework', 'smoke', 'flame', 'teleport', 'shield'):
                if kw in sid_l:
                    return True
            return False
        except Exception:
            return False

    def _build_preview_for_spell(self, spell_id: str, sdef: dict) -> None:
        logger = logging.getLogger(__name__)
        if not self._is_particle_spell(sdef):
            # Remove any existing provider/cache
            self._particle_previews.pop(spell_id, None)
            self.view.preview_providers.pop(spell_id, None)
            return
        # Build preview using centralized builder (resolves presets + overrides)
        preview_obj = build_preview_for_definition(sdef)

        if preview_obj is not None:
            # Replace cache and provider
            self._particle_previews[spell_id] = preview_obj
            # Ensure the preview updates state only once per frame across grid and properties panel
            last_frame_seen: int = -1
            # Render at a stable internal simulation size to avoid resetting preview state
            # when different views request different sizes within the same frame.
            sim_size: tuple[int, int] | None = None
            last_base_frame_id: int = -1
            last_base_surface: pygame.Surface | None = None

            def provider(size: tuple[int, int], dt_ms: int) -> pygame.Surface:
                nonlocal last_frame_seen, sim_size, last_base_frame_id, last_base_surface
                frame_id = getattr(self, "_render_frame_id", 0)

                # Choose a stable simulation size: grow to the largest requested so far
                req_w, req_h = max(1, int(size[0])), max(1, int(size[1]))
                sim_changed = False
                if sim_size is None:
                    sim_size = (req_w, req_h)
                    sim_changed = True
                else:
                    new_sim = (max(sim_size[0], req_w), max(sim_size[1], req_h))
                    if new_sim != sim_size:
                        sim_size = new_sim
                        sim_changed = True

                # Gate time so we only advance simulation once per frame
                effective_dt = dt_ms if frame_id != last_frame_seen else 0
                if (
                    LOG_SPELLS_PREVIEW_DEBUG
                    and logger.isEnabledFor(logging.DEBUG)
                    and effective_dt > 0
                    and getattr(self, "_last_preview_debug_frame", -1) != frame_id
                ):
                    try:
                        logger.debug(
                            "[SpellsPreviewCall] %s: frame=%d dt_ms=%d size=%s sim_size=%s",
                            spell_id,
                            frame_id,
                            effective_dt,
                            (req_w, req_h),
                            sim_size,
                        )
                    except Exception:
                        pass
                    try:
                        self._last_preview_debug_frame = frame_id
                    except Exception:
                        pass
                last_frame_seen = frame_id

                # Render base surface at sim_size. Re-render if advancing time this frame,
                # or if simulation size changed, or if we don't have a cached base surface yet.
                need_base_render = (
                    effective_dt > 0 or sim_changed or last_base_surface is None or last_base_frame_id != frame_id
                )
                if need_base_render:
                    base = preview_obj.render(sim_size, effective_dt if effective_dt > 0 else 0)
                    last_base_surface = base
                    last_base_frame_id = frame_id
                else:
                    base = last_base_surface

                # Return scaled copy when requested size differs from sim_size
                if (req_w, req_h) != sim_size:
                    try:
                        return pygame.transform.smoothscale(base, (req_w, req_h))
                    except Exception:
                        # Fallback to basic scale if smoothscale is unavailable
                        return pygame.transform.scale(base, (req_w, req_h))
                return base
            self.view.preview_providers[spell_id] = provider
            if LOG_SPELLS_PREVIEW_DEBUG and logger.isEnabledFor(logging.DEBUG):
                try:
                    logger.debug(
                        "[SpellsPreview] %s: kind=%s, color=%s, provider=%s",
                        spell_id,
                        kind,
                        color if color_explicit else 'default',
                        type(preview_obj).__name__,
                    )
                except Exception:
                    pass
        else:
            # Remove if cannot build
            self._particle_previews.pop(spell_id, None)
            self.view.preview_providers.pop(spell_id, None)

    def _rebuild_particle_preview_providers(self) -> None:
        # Remove providers for spells that no longer exist
        for sid in list(self.view.preview_providers.keys()):
            if sid not in self.model.spells:
                self.view.preview_providers.pop(sid, None)
                self._particle_previews.pop(sid, None)
        # Rebuild/add providers for current spells
        for sid, sdef in self.model.spells.items():
            if not isinstance(sdef, dict):
                continue
            self._build_preview_for_spell(sid, sdef)
