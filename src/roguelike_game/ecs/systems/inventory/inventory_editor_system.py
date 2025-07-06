import pygame

from roguelike_game.ecs.components.input_component import InputComponent


class InventoryEditorSystem:
    """
    ECS system that manages the inventory editor UI overlay (toggle with F6).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.active = False
        self.selected_eid = None

    def update(self, world, *args):
        # Toggle editor mode
        for eid, inp in world.components.get('InputComponent', {}).items():
            if getattr(inp, 'toggle_editor', False):
                self.active = not self.active
                if self.active:
                    print("[InventoryEditorOpened]")
                else:
                    print("[InventoryEditorClosed]")
                inp.toggle_editor = False
        # TODO: handle UI interactions when active

    def render(self, world, surface, camera=None):
        if not self.active:
            return
        # Draw semi-transparent overlay
        overlay = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Title
        font = pygame.font.SysFont(None, 24)
        text = font.render("Inventory Editor Mode", True, (255, 255, 255))
        overlay.blit(text, (10, 10))
        # TODO: draw grids, slots, buttons
        surface.blit(overlay, (0, 0))
