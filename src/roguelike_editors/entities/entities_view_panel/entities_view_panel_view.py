import pygame
from roguelike_editors.entities.entities_view_panel.entities_view_panel_model import EntityViewPanelModel

class EntityViewPanelView:
    """Renderiza UI del editor de entidades: jugador y monstruos."""
    def __init__(self, assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.blink_interval = 500

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: EntityViewPanelModel) -> None:
        # Fondo semi-transparente
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        screen.blit(overlay, (0, 0))

        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = 12

        # Lista de entidades: primero clases de jugador, luego monstruos
        entity_ids = list(model.player_stats.keys()) + list(model.monsters.keys())
        total_rows = (len(entity_ids) + columns - 1) // columns
        scroll = max(0, min(model.scroll_index, total_rows - (sh - 2*margin)//(cell_height+margin)))

        # Dibujar grid de iconos
        for idx, ent_id in enumerate(entity_ids):
            col = idx % columns
            row = idx // columns
            if row < scroll or row >= scroll + max(1, (sh - 2*margin)//(cell_height+margin)):
                continue
            x = margin + col*(cell_size+margin)
            y = margin + (row-scroll)*(cell_height+margin)
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50,50,50), cell_rect)
            icon = self.assets.get(ent_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                # Aplicar tint si existe
                tint = None
                if ent_id in model.monsters:
                    tint = model.monsters.get(ent_id, {}).get("tint")
                elif ent_id in model.player_stats:
                    tint = model.player_stats.get(ent_id, {}).get("tint")
                if tint:
                    tinted = icon_surf.copy()
                    # tint puede ser [r,g,b] o [r,g,b,a]
                    color = tuple(tint) if len(tint) == 4 else (*tint, 255)
                    tinted.fill(color, special_flags=pygame.BLEND_RGBA_MULT)
                    icon_surf = tinted
                screen.blit(icon_surf, (x, y))

        # Resaltar seleccionado/hover
        active = model.selected_id or model.hovered_id
        if active in entity_ids:
            idx_h = entity_ids.index(active)
            col = idx_h % columns; row = idx_h//columns
            if scroll <= row < scroll + max(1, (sh - 2*margin)//(cell_height+margin)):
                x = margin + col*(cell_size+margin)
                y = margin + (row-scroll)*(cell_height+margin)
                pygame.draw.rect(screen, (255,255,0), (x-2,y-2,cell_size+4,cell_size+4), 3)

                # Preparar panel de propiedades
                if active in model.player_stats:
                    data = model.player_stats.get(active, {})
                else:
                    data = model.monsters.get(active, {})
                lines = [active] + [f"{k}: {v}" for k,v in data.items() if v is not None]
                max_w = max(self.font.size(line)[0] for line in lines)
                pad = 10
                panel_w = min(max_w+pad*2, sw-margin*2, 500)
                panel_h = min(len(lines)*(font_h+2)+pad*2, sh-margin*2)
                px = sw-panel_w-margin; py = margin
                info_surf = pygame.Surface((panel_w,panel_h), pygame.SRCALPHA); info_surf.fill((0,0,0,200))
                screen.blit(info_surf, (px,py))
                model.panel_rect = pygame.Rect(px,py,panel_w,panel_h)
                tx, ty = px+pad, py+pad
                model.property_entries.clear()
                for i,line in enumerate(lines):
                    color=(255,255,0) if i==0 else (200,200,200)
                    text = self._truncate_text(line, panel_w-pad*2)
                    if i>0:
                        # editable
                        key=line.split(': ',1)[0]
                    txt_surf = self.font.render(text, True, color)
                    screen.blit(txt_surf,(tx,ty))
                    if i>0:
                        rect=pygame.Rect(tx,ty,txt_surf.get_width(),font_h)
                        model.property_entries.append((rect,key))
                    ty+=font_h+2

                # Dibujar indicador edición
                if model.editing_property:
                    for rect,key in model.property_entries:
                        if key==model.editing_property:
                            er=rect.inflate(4,0)
                            pygame.draw.rect(screen,(128,0,128),er,2)
                            # blinking caret
                            t=pygame.time.get_ticks()
                            if (t%self.blink_interval)<(self.blink_interval//2):
                                pre=f"{key}: "; bx=er.x; by=er.y
                                # caret position
                                topleft=(bx+self.font.size(pre+model.editing_text[:model.editing_cursor])[0],by)
                                pygame.draw.line(screen,(255,255,255),topleft,(topleft[0],topleft[1]+font_h),2)
                elif model.focused_property:
                    for rect,key in model.property_entries:
                        if key==model.focused_property:
                            hl_rect=rect.inflate(4,0)
                            pygame.draw.rect(screen,(255,255,0),hl_rect,2)
                            break
