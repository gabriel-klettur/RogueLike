def execute_slash_attack(player, direction):
    """
    Ejecuta un ataque básico cuerpo a cuerpo (slash),
    manejando lógica funcional (daño, colisiones, cooldowns).
    La parte visual se delega al sistema de spells.
    """
    state = player.state
    combat_range = 45  # distancia máxima para impactar
    damage = 10

    # Calcular centro del jugador
    cx = player.x + player.sprite_size[0] / 2
    cy = player.y + player.sprite_size[1] * 0.5

    # Normalizamos dirección
    direction = direction.normalize() if direction.length() != 0 else direction

    # Ejecutar animación visual
    state.systems.effects.spawn_slash_arc(player, direction)

    # Detección de colisión simple con enemigos
    for enemy in state.enemies:
        ex = enemy.x + enemy.sprite_size[0] / 2
        ey = enemy.y + enemy.sprite_size[1] * 0.5

        to_enemy = (ex - cx, ey - cy)
        dist = (to_enemy[0] ** 2 + to_enemy[1] ** 2) ** 0.5

        if dist <= combat_range:
            enemy.stats.take_damage(damage)
            print(f"⚔️ Golpeado {enemy.name}, daño: {damage}")
