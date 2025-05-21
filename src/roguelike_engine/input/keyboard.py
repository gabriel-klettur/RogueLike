# Path: src/roguelike_engine/input/keyboard.py
import pygame, time
import roguelike_engine.config.config as config
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

from roguelike_game.entities.npc.factory import NPCFactory

def handle_keyboard(event, state, camera, clock, menu, entities, effects, tiles_editor, map_manager):
    if event.type == pygame.KEYDOWN:
        # F3: añadir mazmorra a la izquierda y recargar mapa
        if event.key == pygame.K_F3:
            base = 'extra_dungeon'
            # determinar índice más alto usado
            max_idx = 0
            for k in global_map_settings.additional_zones:
                if k == base:
                    idx = 1
                elif k.startswith(base) and k[len(base):].isdigit():
                    idx = int(k[len(base):])
                else:
                    continue
                max_idx = max(max_idx, idx)
            new_idx = max_idx + 1
            # nombre y padre según índice
            if new_idx == 1:
                new_key = base
                parent_key = 'lobby'
            else:
                new_key = f"{base}{new_idx}"
                parent_key = base if new_idx == 2 else f"{base}{new_idx-1}"
            # guardar posición del jugador antes de recarga
            px, py = entities.player.x, entities.player.y
            tx, ty = int(px)//TILE_SIZE, int(py)//TILE_SIZE
            old_off = global_map_settings.zone_offsets
            current_zone = None
            for z,(ox,oy) in old_off.items():
                if ox <= tx < ox + global_map_settings.zone_width and oy <= ty < oy + global_map_settings.zone_height:
                    current_zone = z
                    break
            rel_x = tx - old_off.get(current_zone,(0,0))[0]
            rel_y = ty - old_off.get(current_zone,(0,0))[1]
            sub_x = px - tx * TILE_SIZE
            sub_y = py - ty * TILE_SIZE
            global_map_settings.additional_zones[new_key] = (parent_key, 'left')
            # limpiar cache y recargar mapa
            global_map_settings.__dict__.pop('zone_offsets', None)
            map_manager.reload_map()
            # ajustar posición del jugador en coords de mundo
            if current_zone:
                new_off = global_map_settings.zone_offsets[current_zone]
                off_x, off_y = new_off
                new_tx = off_x + rel_x
                new_ty = off_y + rel_y
                entities.player.x = new_tx * TILE_SIZE + sub_x
                entities.player.y = new_ty * TILE_SIZE + sub_y
            print(f"🗺️ Añadida zona '{new_key}' conectada a '{parent_key}' y recargando mapa...")
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