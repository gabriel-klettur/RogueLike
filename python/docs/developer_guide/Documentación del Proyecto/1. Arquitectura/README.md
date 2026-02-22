
# 📚 Índice Maestro: Documentación de Arquitectura – Proyecto RogueLike

---

## 🧭 0. Base del Proyecto

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `0_contexto_base.md`                    | Objetivo general del juego, visión del equipo, tecnologías clave.         |
| `1_tipo_de_desarrollo.md`              | Enfoque iterativo-incremental sin fechas, uso de Kanban.                 |
| `2_especificacion_requisitos.md`       | Requisitos funcionales y no funcionales del MVP.                         |
| `3_diseno_general.md`                  | Componentes principales (jugador, mapa, entidades, red).                 |

---

## ⚙️ 1. Arquitectura Técnica

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `4_estructura_proyecto.md`             | Árbol de carpetas del proyecto y propósito de cada módulo.               |
| `5_event_flow.md`                      | Flujo de eventos desde el input hasta renderizado.                      |
| `6_game_loop.md`                       | Explicación del ciclo principal (`main.py`) y sus fases.                |
| `7_state_management.md`                | Cómo se gestionan los estados (`GameState`, `PlayerState`, etc.).       |
| `8_networking.md`                      | Arquitectura cliente-servidor, WebSocket, sincronización de entidades.  |
| `9_save_system.md`                     | Cómo y qué se guarda (local/multijugador), formatos (`json`, `sqlite`). |
| `10_asset_pipeline.md`                 | Convenciones, carpetas, cómo añadir/modificar sprites/sonidos.          |
| `11_audio_system.md`                   | Organización del audio, herramientas, triggers de sonido.                |
| `12_physics_and_collisions.md`         | Cómo se manejan las colisiones físicas y lógicas.                       |
| `13_ui_architecture.md`                | Menús, HUD, navegación y cómo se integran con el juego.                 |

---

## 🧠 2. Mecánicas y Sistemas de Juego

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `14_player_system.md`                   | Stats, habilidades, cooldowns, interfaz visual.                         |
| `15_npc_system.md`                      | Tipos de NPCs, IA básica, detección, comportamiento.                    |
| `16_ai_systems.md`                      | Árboles de decisión, FSM, comportamiento enemigo complejo.              |
| `17_combat_system.md`                   | Daño, tipos de ataque, hitboxes, PvE/PvP.                                |
| `18_progression_system.md`             | Sistema de niveles, habilidades, progresión del jugador.                |
| `19_quest_system.md`                    | Diseño de misiones, flujo de tareas, recompensas.                       |
| `20_city_upgrade_system.md`            | Progresión del pueblo, mejoras compartidas, economía del juego.         |

---

## 🗺️ 3. Mapas y Entorno

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `21_tilemap_system.md`                 | Generación procedural, fusión de mapas, tipos de tile.                  |
| `22_work_flow_for_tiles.md`           | Cómo diseñar, testear y agregar tiles al sistema.                       |
| `23_minimap_system.md`                | Generación, render y sincronización del minimapa.                       |
| `24_world_events.md`                  | Eventos globales en el mundo, bosses, spawn, clima (futuro).            |

---

## 🧪 4. Soporte para Testing y Debug

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `25_testing_manual.md`                 | Casos de prueba por sistema y validaciones manuales.                    |
| `26_testing_automatizado.md`           | Testing con `unittest`, qué módulos son testeables.                     |
| `27_debug_tools.md`                    | Herramientas visuales y comandos de depuración.                         |

---

## 📦 5. Producción y Distribución

| Archivo                                  | Propósito                                                                 |
|-----------------------------------------|---------------------------------------------------------------------------|
| `28_build_pipeline.md`                 | Cómo crear ejecutables (`pyinstaller`, `.spec`).                         |
| `29_roadmap_futuro.md`                 | Cambios planeados más allá del MVP (ej. PvP, gremios, tiempo real).     |

---