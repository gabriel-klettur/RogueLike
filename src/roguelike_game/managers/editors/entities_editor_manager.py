import pygame
from pathlib import Path
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.controller.editor_controller import EntityEditorController

class EntitiesEditorManager:
    """
    Manager para el editor de entidades: carga datos, assets y delega a EntityEditorController
    """
    def __init__(self, game):
        self.game = game
        font = game.font
        # Cargar datos de jugadores y monstruos
        players_path = Path('data') / 'players.json'
        players_root = load_from_json(str(players_path))
        player_stats = players_root.get('PLAYER_STATS', {})
        monsters_path = Path('data') / 'monsters.json'
        monsters = load_from_json(str(monsters_path))
        # Cargar assets (sprites "down")
        assets = {}
        for pid in player_stats:
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
        self.controller = EntityEditorController(player_stats, monsters, assets, font)
        self.model = self.controller.model
        # Exponer en el estado global
        game.state.entities_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        """Delegar evento al controlador"""
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        """Delegar renderizado"""
        self.controller.draw(screen)
