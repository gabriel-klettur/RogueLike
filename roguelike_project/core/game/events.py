import pygame

def handle_events(state):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                state.show_menu = not state.show_menu
            elif event.key == pygame.K_q:
                state.player.restore_all()
            elif state.show_menu:
                result = state.menu.handle_input(event)
                if result:
                    execute_menu_option(result, state)

    if not state.show_menu:
        keys = pygame.key.get_pressed()
        dx = dy = 0
        if keys[pygame.K_UP]: dy = -1
        if keys[pygame.K_DOWN]: dy = 1
        if keys[pygame.K_LEFT]: dx = -1
        if keys[pygame.K_RIGHT]: dx = 1
        state.player.move(dx, dy, state.collision_mask, state.obstacles)

def execute_menu_option(selected, state):
    if selected == "Cambiar personaje":
        new_name = "valkyria" if state.player.character_name == "first_hero" else "first_hero"
        state.player.change_character(new_name)
        print(f"✅ Cambiado a personaje: {new_name}")
        state.show_menu = False
    elif selected == "Salir":
        state.running = False
