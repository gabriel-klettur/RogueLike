# Fase 0 - Baseline y paridad funcional

## Commit baseline

- Tag: `python-baseline-v1`
- Branch: `developing_v2`
- Commit: `d4279eb1`

## KPIs baseline (Python/Pygame)

- Resolucion: 1600x800
- FPS target: 60
- Optimizaciones activas: spawn budget (3/frame), asset sharing, spatial hash, frustum culling, entities set

## Flujos criticos jugables

| # | Flujo | Descripcion |
|---|-------|-------------|
| 1 | Movimiento | WASD + colision mundo + buildings + NPC separation |
| 2 | Combate melee | Click/tecla -> hitbox -> damage -> death -> drop |
| 3 | Spells | Cast -> projectile/area -> damage -> cooldown |
| 4 | Loot | Drop en suelo -> pickup -> inventario |
| 5 | Inventario | Abrir/cerrar -> drag -> consume -> transfer |
| 6 | IA/FSM | Idle -> patrol -> aggro -> chase -> attack -> flee -> death |
| 7 | Spawner | Trigger por proximidad -> spawn waves -> budget |
| 8 | Save/Load | Autosave + shutdown -> posicion + HP + inventario + NPC memory |
| 9 | Cambio de mapa | Portal -> cargar nuevo nivel -> restaurar NPCs |
| 10 | HUD | Barras HP/MP/XP + nameplates + target + toasts |

## Matriz de paridad funcional

| Capacidad Python | Prioridad | Estado Unity |
|------------------|-----------|--------------|
| Player movement + collision | P0 | pendiente |
| Camera follow | P0 | pendiente |
| Tilemap render + sorting Y/Z | P0 | pendiente |
| Melee combat | P0 | pendiente |
| Spell system (fireball, dash, etc) | P0 | pendiente |
| FSM/AI (Idle, Patrol, Aggro, Attack, Flee, Death) | P0 | pendiente |
| Spawn system + budget | P0 | pendiente |
| Inventory + pickup + drop | P0 | pendiente |
| Save/Load + autosave | P0 | pendiente |
| Map transitions (portals) | P1 | pendiente |
| Buildings + collision | P1 | pendiente |
| Spawner editor | P1 | pendiente |
| HUD (HP, MP, XP bars) | P1 | pendiente |
| Nameplates | P1 | pendiente |
| Particles/VFX | P1 | pendiente |
| Lighting 2D | P1 | pendiente |
| Audio system | P2 | pendiente |
| Chat/Vendor system | P2 | pendiente |
| Combo system | P2 | pendiente |
| Experience/leveling | P2 | pendiente |
| Minimap | P2 | pendiente |
| Tiles editor | P3 | pendiente |
| Buildings editor | P3 | pendiente |
| Map editor | P3 | pendiente |
| Entities debug editor | P3 | pendiente |
| Spells editor | P3 | pendiente |
| Particles editor | P3 | pendiente |
| Console overlay | P3 | pendiente |

## Inventario de assets Python

| Tipo | Cantidad | Extensiones |
|------|----------|-------------|
| Sprites | 1326 | .png |
| Audio | 65 | .wav, .mp3, .ogg, .flac |
| Source art | 47 | .aseprite |
| Images misc | 9+9+3 | .gif, .jpg, .avif |
| Archives | 4 | .zip |
| Docs | 2 | .md, .docx |

## Criterios de aceptacion

La migracion se considera completa cuando:

1. Todos los flujos P0 funcionan en Unity sin errores bloqueantes.
2. 100% de assets .png y audio migrados con trazabilidad.
3. Saves de Python son importables o migrables.
4. Tests automatizados cubren flujos P0.
5. Build Windows x64 reproducible.
