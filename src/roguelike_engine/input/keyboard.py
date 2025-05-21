# Path: src/roguelike_engine/input/keyboard.py
import pygame, time
import roguelike_engine.config.config as config
from roguelike_engine.map.events import handle_expand_dungeon

from roguelike_game.entities.npc.factory import NPCFactory

def handle_keyboard(event, state, camera, clock, menu, entities, effects, tiles_editor, map_manager):
    if event.type == pygame.KEYDOWN:
        
        # F3: añadir mazmorra a la izquierda y recargar mapa
        if handle_expand_dungeon(event, map_manager, entities):
            return

        if event.key == pygame.K_ESCAPE:
            menu.show_menu = not menu.show_menu

        elif event.key == pygame.K_q:
            entities.player.restore_all()
            effects.spawn_healing_aura(clock, entities)
            entities.player.stats.last_restore_time = time.time()

        elif menu.show_menu:
            result = menu.handle_input(event)
            if result:
                menu.execute_menu_option(result, state)

        # ---------- HABILIDADES DEL JUGADOR ---------- #
        elif event.key == pygame.K_1:
            if entities.player.stats.activate_shield():
                effects.spawn_magic_shield(entities)
                entities.player.stats.last_shield_time = time.time()

        elif event.key == pygame.K_f:
            effects.spawn_firework(camera, entities)
            entities.player.stats.last_firework_time = time.time()

        elif event.key == pygame.K_r:
            effects.spawn_smoke_emitter(entities)
            entities.player.stats.last_smoke_time = time.time()

        elif event.key == pygame.K_t:
            effects.spawn_smoke(camera, entities)
            entities.player.stats.last_smoke_time = time.time()

        elif event.key == pygame.K_z:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / camera.zoom + camera.offset_x
            world_y = my / camera.zoom + camera.offset_y
            effects.spawn_lightning((world_x, world_y), entities)
            entities.player.stats.last_lightning_time = time.time()

        elif event.key == pygame.K_x:
            mx, my = pygame.mouse.get_pos()
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y
            effects.spawn_arcane_flame(wx, wy)

        elif event.key == pygame.K_v:
            mx, my = pygame.mouse.get_pos()
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y
            px, py = effects._player_center(entities.player)
            dir_vec = pygame.math.Vector2(wx - px, wy - py)
            if dir_vec.length():
                dir_vec.normalize_ip()
            effects.spawn_dash(entities.player, dir_vec)
            entities.player.stats.last_dash_time = time.time()

        elif event.key == pygame.K_e:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / camera.zoom + camera.offset_x
            world_y = my / camera.zoom + camera.offset_y
            px, py = effects._player_center(entities.player)
            dir_vec = pygame.math.Vector2(world_x - px, world_y - py)
            if dir_vec.length():
                dir_vec.normalize_ip()
            effects.spawn_slash(dir_vec, entities)
            entities.player.stats.last_slash_time = time.time()

        # ---------- TEST / DEBUG ---------- #
        elif event.key == pygame.K_F10:
            if hasattr(state, "editor"):
                # alternamos el editor y también arrancamos el picker
                new_val = not state.editor.active
                state.editor.active        = new_val
                state.editor.picker_active = new_val
                print("🛠️ Building Editor ON (picker abierto)"  if new_val else
                      "🛑 Building Editor OFF (picker cerrado)")

        elif event.key == pygame.K_F9:
            config.DEBUG = not config.DEBUG
            print(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")

        # ---------- Monster Spawner (F7) --------- #
        elif event.key == pygame.K_F7:
            print("Monster positions:")

            for entity in entities.enemies:
                print(f"- {entity.name} at ({entity.x}, {entity.y})")            

            mouse_x, mouse_y = pygame.mouse.get_pos()
            world_x = round(mouse_x / camera.zoom + camera.offset_x)
            world_y = round(mouse_y / camera.zoom + camera.offset_y)
            print(f"Spawning enemy at {world_x}, {world_y}")
            entities.enemies.append( NPCFactory.create("elite", world_x, world_y)) 
            

        # ---------- TILE-EDITOR (F8) --------- #
        elif event.key == pygame.K_F8:
            # Alternamos el flag global (ya existe en state)
            new_val = not tiles_editor.editor_state.active
            tiles_editor.editor_state.active = new_val

            # Sincronizamos el estado interno del editor
            tiles_editor.editor_state.active = new_val            

            # Al cerrar, limpiamos sub-estado
            if not new_val:
                state.tile_editor_state.picker_open    = False
                state.tile_editor_state.selected_tile  = None
                state.tile_editor_state.current_choice = None

            print("🟩 Tile-Editor ON" if new_val else "🟥 Tile-Editor OFF")
            return  # evitamos más atajos este frame