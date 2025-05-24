from .components.position import Position
from .components.sprite import Sprite
from .systems.render_system import RenderSystem
from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

class NPCWorld:
    def __init__(self, screen):
        self.screen = screen
        self.entities = []
        self.components = {
            'Position': {},
            'Sprite': {}
        }
        self.systems = [RenderSystem(screen)]

        # Calculate lobby center
        lobby_x, lobby_y = calculate_lobby_offset()
        zone_w, zone_h = global_map_settings.zone_size
        cx = lobby_x + zone_w // 2
        cy = lobby_y + zone_h // 2

        # Spawn one NPC at center
        self.spawn_npc(cx, cy)

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid

    def spawn_npc(self, cx, cy):
        print("[ECS]: Spawning NPC at tile", cx, cy)
        eid = self.create_entity()
        # Instantiate sprite and center on tile
        sprite = Sprite("assets/npc/monsters/barbol/barbol_1_down.png")
        # Calculate pixel position centered on tile center
        px = cx * TILE_SIZE - sprite.image.get_width() // 2
        py = cy * TILE_SIZE - sprite.image.get_height() // 2
        self.components['Position'][eid] = Position(px, py)
        self.components['Sprite'][eid] = sprite

    def get_entities_with(self, *component_types):
        for eid in self.entities:
            if all(eid in self.components[ctype] for ctype in component_types):
                yield eid

    def update(self):
        # No dynamic logic yet
        pass

    def render(self, screen, camera):
        # Render all ECS entities anchored to map using camera
        for system in self.systems:
            system.update(self, screen, camera)