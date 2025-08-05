import pygame
from typing import Optional
from pathlib import Path
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_engine.utils.loader import load_image, load_sprite_sheet
from roguelike_game.factories.monster.sprite_loader import create_sprite_component

from roguelike_game.config.players_config import PLAYER_ASSETS
from roguelike_editors.entities.entities_title.entities_title_model import EntitiesTitleModel
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_model import EntitiesToolBarPanelModel
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_model import EntitiesAddRemovePanelModel
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel


class EntitiesEditorModel:
    """
    Modelo central del editor de entidades con estado global y submodelos.
    """
    def __init__(self, data_dir: Path = Path('data')):
        # Editor global
        self.active: bool = False
        # Carga de datos JSON
        players_path = data_dir / 'entities' / 'new_players.json'
        players_root = load_from_json(str(players_path))
        # Extraer clases de jugador anidadas
        classes = players_root.get('players', {}).get('classes', {})
        # Guardar configuración de clases para cargar sprites idle
        self.classes = classes
        # Cargar assets completos desde config
        self.player_assets = PLAYER_ASSETS
        self.player_stats = {cls: cfg.get('stats', {}) for cls, cfg in classes.items()}
        self.orig_size = tuple(players_root.get('ORIGINAL_SPRITE_SIZE', [128, 128]))
        monsters_path = data_dir / 'entities' / 'new_monsters.json'
        monsters_root = load_from_json(str(monsters_path))
        # Only extract nested monster classes
        self.monsters = monsters_root.get('monsters', {}).get('classes', {})
        # Carga de assets
        self.assets: dict[str, pygame.Surface] = {}
        # Jugadores
        for pid in self.player_stats:
            # Cargar primer frame de idle de new_players.json
            cfg = self.classes.get(pid, {})
            idle_list = cfg.get('assets', {}).get('sets', {}).get('sprites_set', {}).get('idle', [])
            if idle_list:
                path = idle_list[0]
                try:
                    frames = load_sprite_sheet(path, self.orig_size, columns=1)
                    self.assets[pid] = frames[0]
                    continue
                except Exception:
                    pass
            # Fallback: assets no-sets o recurso por defecto
            asset_info = self.player_assets.get(pid)
            path = None
            if isinstance(asset_info, str):
                path = asset_info
            elif isinstance(asset_info, dict):
                path = next(iter(asset_info.values()), None)
            if path:
                try:
                    frames = load_sprite_sheet(path, self.orig_size, columns=1)
                    self.assets[pid] = frames[0]
                    continue
                except Exception:
                    pass
            try:
                self.assets[pid] = load_image(f'assets/npc/player/{pid}/{pid}_1_down.png')
            except Exception:
                pass
                # Monstruos: cargar imagenes de idle y aplicar tint desde JSON
        # Monstruos: cargar sprites tinted con factory
        for mid in self.monsters.keys():
            try:
                sprite, _ = create_sprite_component(mid)
                self.assets[mid] = sprite.image
            except Exception:
                pass
        # submodelos MVC
        self.title_model = EntitiesTitleModel()
        self.toolbar_model = EntitiesToolBarPanelModel()
        self.add_remove_model = EntitiesAddRemovePanelModel()
        self.picker_model = EntityPickerPanelModel(self.player_stats, self.monsters, self.assets)
        self.properties_model = EntityPropertiesPanelModel(self.player_stats, self.player_assets, self.monsters)
        # Cámara y arrastre
        self.panning: bool = False
        self.pan_start: tuple[int, int] = (0, 0)
        self.pan_offset_start: tuple[float, float] = (0.0, 0.0)
        # Spawn mode para entidades en el mapa
        self.spawn_mode_active: bool = False  # indica si estamos en modo colocación
        self.spawn_entity_type: Optional[str] = None  # id de entidad a colocar
        # Delete mode para entidades en el mapa
        self.delete_mode_active: bool = False  # indica si estamos en modo borrado

    @property
    def visible(self) -> bool:
        """
        Alias para compatibilidad: visible == active
        """
        return self.active

    @visible.setter
    def visible(self, value: bool):
        """
        Setter para compatibilidad: visible == active
        """
        self.active = value