import pygame
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel

class EntityPickerPanelView:
    """Renderiza UI del editor de entidades: jugador y monstruos."""
    def __init__(self, assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.blink_interval = 500
        # Configuración panel dinámico
        self.margin = 20
        self.cell_size = 64
        self.text_margin = 4
        self.columns = 10
        # Atributos para DraggablePanel (no usado aún)
        self.x = 10 + 32 + 4  # align with Tile Picker x
        # Posición Y justo debajo del title panel
        from roguelike_ui.widgets.title_panel import TitlePanel
        dummy = TitlePanel(text="", font=self.font, x=0, y=0)
        title_height = self.font.get_height() + dummy.padding_y * 2
        self.y = 10 + title_height           # align with Tile Picker y
        # self.panel = None


    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: EntityPickerPanelModel) -> None:
        # Solo si está visible
        if not model.visible:
            return
        # Panel background dinámico
        entity_ids = list(model.player_stats.keys()) + list(model.monsters.keys())
        from math import ceil
        rows = ceil(len(entity_ids) / self.columns)
        cell_height = self.cell_size + self.text_margin + self.font.get_height()
        # Ancho dinámico según contenido
        used_cols = min(self.columns, len(entity_ids))
        panel_w = self.margin + used_cols * self.cell_size + (used_cols + 1) * self.margin
        # Altura según contenido
        panel_h = self.margin + rows * (cell_height + self.margin)
        # Fondo semitransparente y borde redondeado
        bg_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        bg_surf.fill((0, 0, 0, 180))
        pygame.draw.rect(bg_surf, (255, 255, 255, 200), bg_surf.get_rect(), 2, border_radius=6)
        # Pintar fondo
        screen.blit(bg_surf, (self.x, self.y))
        
        
        
        

        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = self.columns

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
            x = self.x + margin + col*(cell_size+margin)
            y = self.y + margin + (row-scroll)*(cell_height+margin)
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
                x = self.x + margin + col*(cell_size+margin)
                y = self.y + margin + (row-scroll)*(cell_height+margin)
                pygame.draw.rect(screen, (255,255,0), (x-2,y-2,cell_size+4,cell_size+4), 3)



