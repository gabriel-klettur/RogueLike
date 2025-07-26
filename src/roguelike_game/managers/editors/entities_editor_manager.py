import pygame
from pathlib import Path
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_engine.utils.loader import load_image, load_sprite_sheet
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_controller import EntityPickerPanelController
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_model import EntitiesToolBarPanelModel
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_view import EntitiesToolBarPanelView
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import EntitiesToolBarPanelEventHandler
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_controller import EntitiesToolBarPanelController
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_model import EntitiesAddRemovePanelModel
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_view import EntitiesAddRemovePanelView
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_events import EntitiesAddRemovePanelEventHandler
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_controller import EntitiesAddRemovePanelController
from roguelike_editors.entities.entities_title.entities_title_model import EntitiesTitleModel
from roguelike_editors.entities.entities_title.entities_title_controller import EntitiesTitleController

class EntitiesEditorManager:
    """
    Manager para el editor de entidades: carga datos, assets y delega a EntityPickerPanelController
    """
    def __init__(self, game):
        self.game = game
        font = game.font
        # Cargar datos de jugadores y monstruos
        players_path = Path('data') / 'entities' / 'players.json'
        players_root = load_from_json(str(players_path))
        player_stats = players_root.get('PLAYER_STATS', {})
        monsters_path = Path('data') / 'entities' / 'monsters.json'
        monsters = load_from_json(str(monsters_path))
        # Cargar assets (sprites "down")
        # Cargar assets de jugadores desde players.json
        assets = {}
        player_assets = players_root.get('PLAYER_ASSETS', {})
        orig_size = tuple(players_root.get('ORIGINAL_SPRITE_SIZE', [128,128]))
        for pid in player_stats:
            asset_info = player_assets.get(pid)
            if asset_info:
                try:
                    # Elegir primer asset
                    if isinstance(asset_info, str):
                        path = asset_info
                    elif isinstance(asset_info, dict):
                        path = list(asset_info.values())[0]
                    else:
                        path = None
                    if path:
                        frames = load_sprite_sheet(path, orig_size, columns=1)
                        assets[pid] = frames[0]
                        continue
                except Exception as e:
                    print(f'[EntityEditor] Error cargando sprite sheet de player {pid}: {e}')
            # fallback al sprite "down" clásico
            try:
                assets[pid] = load_image(f'assets/npc/player/{pid}/{pid}_1_down.png')
            except Exception as e:
                print(f'[EntityEditor] Error cargando sprite de player {pid}: {e}')
        for mid, mdef in monsters.items():
            path = mdef.get('sprites', {}).get('down')
            if path:
                try:
                    assets[mid] = load_image(path)
                except Exception as e:
                    print(f'[EntityEditor] Error cargando sprite de monster {mid}: {e}')
        # Instanciar controlador
        self.controller = EntityPickerPanelController(player_stats, monsters, assets, font)
        self.model = self.controller.model
        # Instanciar panel de propiedades
        self.properties_controller = EntityPropertiesPanelController(player_stats, monsters, font)
        # Exponer en el estado global
        game.state.entities_editor_state = self.model
        # Instanciar title MVC
        self.title_model = EntitiesTitleModel()
        self.title_controller = EntitiesTitleController(self, self.title_model, font)
        # Instanciar toolbar MVC
        self.toolbar_model = EntitiesToolBarPanelModel()
        self.toolbar_event_handler = EntitiesToolBarPanelEventHandler(self, self.toolbar_model)
        self.toolbar_view = EntitiesToolBarPanelView(self, self.toolbar_model)
        self.toolbar_controller = EntitiesToolBarPanelController(self, self.toolbar_model, self.toolbar_view, self.toolbar_event_handler)
        # Instanciar panel de añadir/eliminar entidades
        self.add_remove_model = EntitiesAddRemovePanelModel()
        self.add_remove_event_handler = EntitiesAddRemovePanelEventHandler(self, self.add_remove_model)
        self.add_remove_view = EntitiesAddRemovePanelView(self, self.add_remove_model)
        self.add_remove_controller = EntitiesAddRemovePanelController(self, self.add_remove_model, self.add_remove_view, self.add_remove_event_handler)
        # Camera panning via middle mouse button
        self.panning = False
        self.pan_start = (0, 0)
        self.pan_offset_start = (0, 0)

    def is_active(self, tool: str) -> bool:
        """
        Determina si la herramienta indicada está activa en el toolbar.
        """
        return self.toolbar_model.active_tool == tool

    def handle_event(self, event: pygame.event.Event) -> None:
        """Delegar evento al controlador"""
        # Camera panning via middle mouse button
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 2:
            self.panning = True
            self.pan_start = event.pos
            self.pan_offset_start = (self.game.camera.offset_x, self.game.camera.offset_y)
            return
        elif event.type == pygame.MOUSEMOTION and self.panning:
            dx = event.pos[0] - self.pan_start[0]
            dy = event.pos[1] - self.pan_start[1]
            self.game.camera.offset_x = self.pan_offset_start[0] - dx / self.game.camera.zoom
            self.game.camera.offset_y = self.pan_offset_start[1] - dy / self.game.camera.zoom
            return
        elif event.type == pygame.MOUSEBUTTONUP and event.button == 2 and self.panning:
            self.panning = False
            return
        # Handle zoom while panning
        elif event.type == pygame.MOUSEWHEEL and self.panning:
            current = int(self.game.camera.zoom)
            if event.y > 0:
                self.game.camera.zoom = current + 1
            elif event.y < 0:
                self.game.camera.zoom = max(1, current - 1)
            return

        # Priorizar eventos del title panel
        if self.title_controller.handle_event(event):
            return
        # Priorizar eventos del toolbar
        if self.toolbar_controller.handle_event(event):
            return
        # Priorizar eventos del panel de añadir/eliminar entidades
        if self.add_remove_controller.handle_event(event):
            return
        # Priorizar eventos del panel de propiedades
        if self.properties_controller.handle_event(event):
            return
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        """Delegar renderizado"""
        if not self.model.visible:
            return
        # Renderizar título, toolbar y luego la vista principal
        self.title_controller.render(screen)
        # Renderizar toolbar y luego la vista principal
        self.toolbar_controller.render(screen)
        self.add_remove_controller.render(screen)
        self.controller.draw(screen)
        # Sincronizar selección con panel de propiedades
        self.properties_controller.model.selected_id = self.model.selected_id
        # Dibujar panel de propiedades
        self.properties_controller.draw(screen)
