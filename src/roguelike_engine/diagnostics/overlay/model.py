from dataclasses import dataclass, field
import os
import json
from typing import Dict, List, Optional, Set, Tuple


@dataclass
class DiagnosticsOverlayModel:
    perf_log: Dict[str, List[float]]
    font_name: str = "Consolas"
    font_size: int = 12
    bg_color: Tuple[int, int, int, int] = (0, 0, 0, 180)
    text_color: Tuple[int, int, int] = (255, 255, 255)
    value_color: Tuple[int, int, int] = (200, 255, 200)
    padding_x: int = 10
    padding_y: int = 4
    spacing: int = 4
    border_colors: Dict[str, Tuple[int, int, int]] = field(default_factory=lambda: {
        'lobby': (255, 255, 255),
        'dungeon': (0, 255, 0),
        'global': (128, 0, 128),
    })
    border_width: int = 5
    update_interval: float = 0.2
    scroll_speed: int = 20

    # Runtime state
    panel_surf: Optional[object] = None
    panel_rect: Optional[object] = None
    last_update_time: float = 0.0
    scroll_offset: int = 0
    label_w: int = 0
    value_w: int = 0
    line_keys: List[str] = field(default_factory=list)
    # Parallel to line_keys; indentation level (0+). None for lines where level is unknown.
    line_levels: List[Optional[int]] = field(default_factory=list)
    collapsed_groups: Set[str] = field(default_factory=set)
    initially_collapsed: bool = True

    # Safety/config limits
    # Máximo de líneas a renderizar en el panel para evitar superficies gigantes
    max_lines: int = 400
    # Máximo de caracteres por campo (izquierda/derecha) para evitar text surfaces descomunales
    max_chars_per_field: int = 256
    # Límites duros del tamaño del surface (se combinarán con el tamaño de pantalla si existe)
    max_surface_width: int = 2000
    max_surface_height: int = 8000

    # Paging (para virtualizar contenido grande sin crear superficies enormes)
    paging_enabled: bool = True
    page_index: int = 0
    total_lines: int = 0
    lines_per_page: int = 0
    total_pages: int = 1

    def reset_panel(self):
        self.panel_surf = None
        self.panel_rect = None
        self.line_keys.clear()
        self.line_levels.clear()

    # --- Persistence helpers ---
    def _state_file_path(self) -> str:
        # src/roguelike_engine/diagnostics/overlay/model.py -> project_root/data/diagnostics/overlay_state.json
        here = os.path.abspath(os.path.dirname(__file__))
        # overlay -> diagnostics -> roguelike_engine -> src -> RogueLike (project root)
        project_root = os.path.abspath(os.path.join(here, '..', '..', '..', '..'))
        path = os.path.join(project_root, 'data', 'diagnostics')
        os.makedirs(path, exist_ok=True)
        return os.path.join(path, 'overlay_state.json')

    def load_persisted_state(self) -> None:
        try:
            fp = self._state_file_path()
            if os.path.exists(fp):
                with open(fp, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                cols = data.get('collapsed_groups', [])
                if isinstance(cols, list):
                    self.collapsed_groups = set(cols)
                    # Disable auto-collapse if we have a persisted state
                    self.initially_collapsed = False
        except Exception:
            # Fail silently; diagnostics overlay should not crash the game
            pass

    def save_persisted_state(self) -> None:
        try:
            fp = self._state_file_path()
            data = {
                'collapsed_groups': sorted(list(self.collapsed_groups)),
            }
            with open(fp, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception:
            # Fail silently to avoid impacting runtime
            pass
