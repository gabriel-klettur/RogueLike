# Path: src/roguelike_engine/input/keyboard.py
import pygame, time
import roguelike_engine.config as config


from roguelike_game.entities.npc.factory import NPCFactory

def handle_keyboard(event, state, camera, clock, menu, entities, effects):
    if event.type == pygame.KEYDOWN:
        if event.key == pygame.K_ESCAPE:
            state.show_menu = not state.show_menu

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
                state.editor.active = not state.editor.active
                print("🛠️ Modo editor activado" if state.editor.active else "🛑 Modo editor desactivado")

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
            new_val = not getattr(state, "tile_editor_active", False)
            state.tile_editor_active = new_val
            if hasattr(state, "tile_editor_state"):
                tes = state.tile_editor_state
                tes.active = new_val
                if not new_val:
                    tes.picker_open    = False
                    tes.selected_tile  = None
                    tes.current_choice = None
            print("🟩 Tile-Editor ON" if new_val else "🟥 Tile-Editor OFF")
            return  # evitamos más atajos este frame