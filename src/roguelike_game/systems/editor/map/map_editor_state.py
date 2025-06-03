import pygame
from roguelike_engine.map.model.layer import Layer

class MapEditorState:
    """
    Estado para el Map Editor.
    """
    def __init__(self):
        self.active = False
        self.selected_zone = None
        self.hidden_zones: set[str] = set()
        self.add_zone_mode: bool = False  # Adding zone mode
        self.delete_zone_mode: bool = False  # Deleting zone mode
        self.confirm_delete_zone: bool = False  # Confirm deletion dialog active
        self.pending_delete_zone: str | None = None  # Zone awaiting deletion confirmation
        self.confirm_yes_rect: pygame.Rect | None = None  # Yes button for delete confirm
        self.confirm_no_rect: pygame.Rect | None = None   # No button for delete confirm
        # Paint Tiles confirmation dialog flags
        self.confirm_paint_tiles: bool = False  # Confirm painting all tiles in zone
        self.pending_paint_tiles_zone: str | None = None
        self.confirm_paint_yes_rect: pygame.Rect | None = None
        self.confirm_paint_no_rect: pygame.Rect | None = None
        # Confirm Clear Colliders dialog flags
        self.confirm_clear_colliders: bool = False
        self.pending_clear_colliders_zone: str | None = None
        self.confirm_clear_colliders_yes_rect: pygame.Rect | None = None
        self.confirm_clear_colliders_no_rect: pygame.Rect | None = None
        # Confirm Paint Colliders dialog flags
        self.confirm_paint_colliders: bool = False
        self.pending_paint_colliders_zone: str | None = None
        self.confirm_paint_colliders_yes_rect: pygame.Rect | None = None
        self.confirm_paint_colliders_no_rect: pygame.Rect | None = None
        self.dragging: str | None = None
        self.drag_offset: tuple[int,int] = (0, 0)
        # Toggle dropdown for zone visibility
        self.layers_view_open = False
        # Toggle tile layer visibility
        self.visible_layers: dict[Layer, bool] = {layer: True for layer in Layer}
        # Toggle building layer visibility
        self.show_buildings: bool = True
        self.show_colliders: bool = False
        self.renaming_zone: str | None = None  # Current zone being renamed
        self.rename_input: str = ""  # Buffer for rename text
        self.rename_input_rect: pygame.Rect | None = None  # Rect para caja de input
        self.rename_accept_rect: pygame.Rect | None = None  # Rect para botón aceptar
        # For manual double-click detection
        self.last_click_zone: str | None = None
        self.last_click_time: int = 0  # milliseconds
        # Modes for clearing/painting tiles and colliders
        self.paint_tiles_mode: bool = False  # Paint tiles in selected zone
        self.clear_colliders_mode: bool = False  # Clear colliders in selected zone
        self.paint_colliders_mode: bool = False  # Paint colliders in selected zone
        # Middle-click pan mode
        self.panning: bool = False
        # Starting mouse pos and offset for panning
        self.pan_start_mouse: tuple[int,int] = (0, 0)
        self.pan_start_offset: tuple[float,float] = (0.0, 0.0)
        # Rectangles for toolbar buttons
        self.paint_tiles_rect: pygame.Rect | None = None
        self.clear_colliders_rect: pygame.Rect | None = None
        self.paint_colliders_rect: pygame.Rect | None = None
        # Confirm add zone dialog flags
        self.confirm_add_zone: bool = False  # Confirm adding a new zone
        self.pending_add_zone_coords: tuple[int,int] | None = None  # Coords for pending zone
        self.confirm_add_yes_rect: pygame.Rect | None = None  # Yes button for add confirm
        self.confirm_add_no_rect: pygame.Rect | None = None   # No button for add confirm