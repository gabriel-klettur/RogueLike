import pygame
from roguelike_editors.entities.services.constants import UI_MARGIN


class ParticlesPropertiesPanelView:
    """Simple properties panel view to display selected particle instance info."""

    def __init__(self, font: pygame.font.Font | None):
        self.font = font or pygame.font.SysFont("consolas", 16)
        self.title_font = self.font
        self.panel_rect: pygame.Rect | None = None

    def draw(self, screen: pygame.Surface, model) -> None:
        if not getattr(model, "visible", False):
            return
        # Base panel rect
        x = int(getattr(model, "x", 0))
        y = int(getattr(model, "y", 0))
        w = int(getattr(model, "width", 260))
        pad = int(getattr(model, "padding", 8))

        # Dynamic content: instance rows and picker rows
        inst_rows = []
        if getattr(model, "selected_id", None) is not None or isinstance(getattr(model, "entry", None), dict):
            inst_rows = [
                ("ID", str(getattr(model, "selected_id", ""))),
                ("Preset", str((model.entry or {}).get("preset_id", ""))),
                ("Zone", str((model.entry or {}).get("zone", ""))),
                ("rel_x", str((model.entry or {}).get("rel_x", ""))),
                ("rel_y", str((model.entry or {}).get("rel_y", ""))),
            ]

        pick_rows = []
        if isinstance(getattr(model, "picker_selected_id", None), str):
            pid = str(getattr(model, "picker_selected_id"))
            pdef = getattr(model, "picker_selected_def", {}) or {}
            name = pdef.get("name", "")
            typ = pdef.get("type", "")
            kind = None
            preset_from_vfx = None
            try:
                vfx = pdef.get("vfx", {}) if isinstance(pdef.get("vfx"), dict) else {}
                preset_from_vfx = vfx.get("preset")
                parts = vfx.get("particles") if isinstance(vfx.get("particles"), dict) else None
                if isinstance(parts, dict):
                    kind = parts.get("kind")
            except Exception:
                kind = None
            pick_rows = [
                ("PresetID", pid),
                ("Name", str(name)),
                ("Type", str(typ)),
            ]
            if preset_from_vfx:
                pick_rows.append(("From", str(preset_from_vfx)))
            if kind:
                pick_rows.append(("Kind", str(kind)))

        # Compute dynamic height
        row_h = self.font.get_height() + 4
        title_h = self.title_font.get_height() + 6
        sections_h = 0
        # Title + first divider
        sections_h += title_h + UI_MARGIN
        # Instance section header + rows (only if present)
        if inst_rows:
            sections_h += self.font.get_height() + 4  # header height
            sections_h += len(inst_rows) * row_h + UI_MARGIN // 2
        # Picker section header + rows (only if present)
        if pick_rows:
            sections_h += self.font.get_height() + 4  # header height
            sections_h += len(pick_rows) * row_h

        h = sections_h + pad * 2
        panel = pygame.Rect(x, y, w, h)
        self.panel_rect = panel

        # Background and border
        pygame.draw.rect(screen, (20, 20, 22), panel, border_radius=8)
        pygame.draw.rect(screen, (70, 70, 80), panel, width=1, border_radius=8)

        # Title
        title = self.title_font.render("PARTICLE PROPERTIES", True, (230, 230, 240))
        screen.blit(title, (panel.x + pad, panel.y + pad))

        # Divider under title
        try:
            pygame.draw.line(
                screen, (90, 90, 100),
                (panel.x + pad, panel.y + pad + title_h),
                (panel.right - pad, panel.y + pad + title_h),
                width=1,
            )
        except Exception:
            pass

        # Cursor for content
        y_cursor = panel.y + pad + title_h + UI_MARGIN // 2
        key_col = panel.x + pad
        val_col = panel.x + pad + 88

        # Instance section
        if inst_rows:
            hdr = self.font.render("SELECTED INSTANCE", True, (210, 210, 220))
            screen.blit(hdr, (key_col, y_cursor))
            y_cursor += self.font.get_height() + 4
            for key, val in inst_rows:
                try:
                    ksurf = self.font.render(f"{key}:", True, (200, 200, 210))
                    vsurf = self.font.render(str(val), True, (230, 230, 230))
                    screen.blit(ksurf, (key_col, y_cursor))
                    screen.blit(vsurf, (val_col, y_cursor))
                    y_cursor += row_h
                except Exception:
                    pass
            # Divider between sections (if picker section will follow)
            if pick_rows:
                try:
                    pygame.draw.line(
                        screen, (80, 80, 90),
                        (panel.x + pad, y_cursor),
                        (panel.right - pad, y_cursor),
                        width=1,
                    )
                except Exception:
                    pass
                y_cursor += UI_MARGIN // 2

        # Picker section
        if pick_rows:
            hdr = self.font.render("PICKER SELECTION", True, (210, 210, 220))
            screen.blit(hdr, (key_col, y_cursor))
            y_cursor += self.font.get_height() + 4
            for key, val in pick_rows:
                try:
                    ksurf = self.font.render(f"{key}:", True, (200, 200, 210))
                    vsurf = self.font.render(str(val), True, (230, 230, 230))
                    screen.blit(ksurf, (key_col, y_cursor))
                    screen.blit(vsurf, (val_col, y_cursor))
                    y_cursor += row_h
                except Exception:
                    pass
