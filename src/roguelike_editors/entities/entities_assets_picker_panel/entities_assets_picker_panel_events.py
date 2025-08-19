import pygame
import logging
import os

logger = logging.getLogger(__name__)

class EntitiesAssetsPickerPanelEventHandler:
    """Event handler para el picker de assets de entidades."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        # Conectar callbacks del FileSystemPicker a las acciones del modelo
        # Doble clic / Enter en archivo -> emitir on_asset_chosen
        def _normalize_path(p) -> str:
            # Convert to string and prefer project-relative with forward slashes
            try:
                p_str = str(p)
                cwd = os.getcwd()
                rel = os.path.relpath(p_str, cwd)
                use = rel if not rel.startswith('..') else p_str
                return use.replace('\\', '/')
            except Exception:
                try:
                    return str(p).replace('\\', '/')
                except Exception:
                    return str(p)

        def _commit_once(norm_path: str):
            # Avoid double-callbacks when select and open fire on double-click
            try:
                if getattr(self.model, '_committed_once', False):
                    return False
                self.model._committed_once = True
            except Exception:
                # Best-effort guard
                pass
            if self.model.on_asset_chosen:
                logger.debug(f" commit_once -> on_asset_chosen key={self.model.key}, path={norm_path}")
                self.model.on_asset_chosen(self.model.key, norm_path)
            return True

        def _on_open(path):
            norm = _normalize_path(path)
            _commit_once(norm)
        self.view.fs_view.on_open = _on_open
        # Selección (click o teclado): si es archivo, confirmar selección inmediatamente
        def _on_select(idx: int):
            try:
                entries = self.view.fs_view.model.entries
                if 0 <= idx < len(entries):
                    name, path, is_dir = entries[idx]
                    if not is_dir and self.model.on_asset_chosen:
                        norm = _normalize_path(path)
                        logger.debug(f" on_select choose asset for key={self.model.key}, path={norm}")
                        _commit_once(norm)
            except Exception:
                logger.exception("[EntitiesAssetsPicker] on_select handler failed")
        self.view.fs_view.on_select = _on_select

    def handle(self, event: pygame.event.Event) -> bool:
        """Delegar eventos a FileSystemPicker/PickerPanel y gestionar cierre/ocultación."""
        # Cerrar con ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.hide()
            return True

        # Rect del panel dibujado (incluyendo posibles labels/footer)
        if self.model.panel_rect is not None:
            panel_rect = self.model.panel_rect
        else:
            x, y = self.model.pos
            surf = self.view.fs_view.panel.surface
            w, h = surf.get_size()
            panel_rect = pygame.Rect(x, y, w, h)

        # Teclado: siempre delegar al picker cuando está visible
        if event.type == pygame.KEYDOWN:
            fs_view = getattr(self.view, 'fs_view', None)
            if hasattr(fs_view, 'handle_event'):
                fs_view.handle_event(event, self.model.pos)
            return True

        # Rueda/Movimiento/Clic: decidir por posición
        if event.type in (pygame.MOUSEMOTION, pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            # Eventos de ratón sin pos (MOUSEWHEEL) usan el mouse global para decidir
            mx, my = pygame.mouse.get_pos() if not hasattr(event, 'pos') else event.pos
            if panel_rect.collidepoint(mx, my):
                # Click dentro del panel: si es sobre una entrada conocida, aplicar selección/navegación
                if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                    for rect, entry, idx in getattr(self.view, 'entry_rects', []) or []:
                        if rect.collidepoint(mx, my):
                            try:
                                # entry: (name, path, is_dir)
                                name, path, is_dir = entry
                            except Exception:
                                break
                            # Selección con un solo clic
                            try:
                                setattr(self.model.fs_model, 'selected', path)
                            except Exception:
                                pass
                            # Doble clic: navegar (dir) o confirmar archivo
                            try:
                                dc = getattr(self, 'dc_detector', None)
                                if dc and hasattr(dc, 'is_double_click') and dc.is_double_click(idx):
                                    if is_dir:
                                        navigate = getattr(self.model.fs_model, 'navigate', None)
                                        if callable(navigate):
                                            navigate(idx)
                                    else:
                                        if self.model.on_asset_chosen:
                                            # Tests esperan el path tal cual (sin normalizar)
                                            self.model.on_asset_chosen(self.model.key, path)
                            except Exception:
                                logger.exception("[EntitiesAssetsPicker] double click handling failed")
                            return True
                # Delegar al FS picker (traduce coords internas y propaga a PickerPanel)
                fs_view = getattr(self.view, 'fs_view', None)
                if hasattr(fs_view, 'handle_event'):
                    fs_view.handle_event(event, self.model.pos)
                return True
            # Clic fuera -> ocultar
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                self.controller.hide()
                return True

        return False