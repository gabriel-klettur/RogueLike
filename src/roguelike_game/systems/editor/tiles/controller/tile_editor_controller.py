
# Path: src/roguelike_game/systems/editor/tiles/controller/tile_editor_controller.py
from pathlib import Path

from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP

from roguelike_game.systems.editor.tiles.controller.tools.tile_picker_controller import TilePickerController
from roguelike_game.systems.editor.tiles.controller.tools.tile_toolbar_controller import TileToolbarController

from roguelike_engine.map.overlay.overlay_manager import save_overlay
from roguelike_engine.utils.loader import load_image



class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, editor_state, picker_state):        
        self.editor  = editor_state     # instancia de TileEditorControllerState
        self.picker = TilePickerController(editor_state, picker_state)
        self.toolbar = TileToolbarController(editor_state)

    def select_tile_at(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if tile:
            self.editor.selected_tile = tile
            # Abrimos la paleta en el estado del picker
            self.picker.picker_state.open = True
            self.picker.picker_state.current_choice = None
            self.editor.scroll_offset = 0

    def apply_brush(self, mouse_pos, camera, map):
        """
        Pinta el sprite seleccionado sobre el tile bajo el ratón
        y persiste el código de overlay (quitando el prefijo "tiles/").
        """
        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile or not self.editor.current_choice:
            return

        # 2) Carga el nuevo sprite
        sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 3) Calcula el código de overlay sin el prefijo "tiles/"
        full = Path(self.editor.current_choice).with_suffix('')  # ej. "tiles/dungeon/dungeon_1"
        try:
            code = full.relative_to("tiles").as_posix()            # "dungeon/dungeon_1"
        except ValueError:
            code = full.as_posix()                                 # si no empieza por "tiles/"

        tile.overlay_code = code

        # 4) Persiste el overlay en la matriz
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE

        # Si aún no hay overlay, inicialízalo
        if map.overlay is None:
            h = len(map.tiles)
            w = len(map.tiles[0]) if h else 0
            map.overlay = [["" for _ in range(w)] for _ in range(h)]

        map.overlay[row][col] = code
        save_overlay(map.name, map.overlay)
        print(f"📝 Overlay actualizado: ({row}, {col}) = '{code}'")

    def apply_eyedropper(self, mouse_pos, camera, map):
        """
        Selecciona el sprite bajo el cursor, lo aplica al tile y guarda el overlay igual que el brush.
        """
        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return

        # 2) Determinar código de overlay o tipo base
        code = tile.overlay_code or tile.tile_type

        # 3) Mapear código a nombre de asset en OVERLAY_CODE_MAP o DEFAULT_TILE_MAP
        if code in OVERLAY_CODE_MAP:
            asset_name = OVERLAY_CODE_MAP[code]
        else:
            asset_name = DEFAULT_TILE_MAP.get(code)
        if not asset_name:
            return

        # 4) Ruta relativa para el picker y brush (sin prefijo 'assets/')
        choice_path = f"tiles/{asset_name}.png"
        self.editor.current_choice = choice_path
        self.editor.current_tool = "brush"

        # 5) Cargar y asignar sprite al tile
        sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 6) Fijar overlay_code al código original
        tile.overlay_code = code

        # 7) Persistir overlay en la matriz y archivo
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        if map.overlay is None:
            h = len(map.tiles)
            w = len(map.tiles[0]) if h else 0
            map.overlay = [["" for _ in range(w)] for _ in range(h)]
        map.overlay[row][col] = code
        save_overlay(map.name, map.overlay)
        print(f"📝 Overlay actualizado: ({row}, {col}) = '{code}'")



    def _tile_under_mouse(self, mouse_pos, camera, map):
        mx, my = mouse_pos
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(map.tiles) and 0 <= col < len(map.tiles[0]):
            return map.tiles[row][col]
        return None