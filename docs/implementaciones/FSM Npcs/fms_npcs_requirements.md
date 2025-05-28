# Requisitos FSM para NPCs

Este documento especifica los sensores, variables y parámetros necesarios para la FSM de los NPCs.

## Sensores y Variables Globales
- **DistanciaJugador**: distancia al jugador (unidades de mapa)
- **VidaActual**: vida restante del NPC
- **RangoDeteccion** (`detectionRange`): distancia máxima para detectar al jugador
- **RangoAtaque** (`attackRange`): distancia máxima para atacar al jugador
- **UmbralHuida** (`fleeThreshold`): porcentaje de vida bajo el cual el NPC considera huir
- **ChanceHuir** (`fleeChance`): probabilidad de huir al estar por debajo del umbral
- **ChanceEsquivar** (`dodgeChance`): probabilidad de ejecutar una esquiva durante el ataque

## Parámetros por Monstruo
Los campos `detectionRange` y `attackRange` se mapearán de `aggro_range` y `melee_range` en `data/monsters.json`. Los campos `fleeThreshold`, `fleeChance` y `dodgeChance` deberán añadirse.

| Monster ID | HP  | Speed | detectionRange | attackRange | fleeThreshold | fleeChance | dodgeChance |
|------------|-----|-------|----------------|-------------|---------------|------------|-------------|
| barbol     | 100 | 1.0   | 20             | 5           | 0.3           | 0.5        | 0.2         |