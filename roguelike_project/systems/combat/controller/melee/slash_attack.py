def execute_slash_attack(player, direction):
    """
    Ejecuta un ataque básico cuerpo a cuerpo (slash),
    manejando lógica funcional (daño, cooldowns).
    La parte visual y la detección de colisiones con enemigos se delegan al sistema de efectos (SlashEffect).
    """
    state = player.state

    # Calcular dirección y normalizarla
    direction = direction.normalize() if direction.length() != 0 else direction

    # Ejecutar animación visual (con partículas) y detectar colisiones con enemigos a través del sistema de efectos
    state.systems.effects.spawn_slash_effect(player, direction)

    # El daño y la colisión lo maneja el sistema de combate
