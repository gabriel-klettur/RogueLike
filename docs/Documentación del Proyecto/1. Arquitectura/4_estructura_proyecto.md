# 🗂 Estructura del Proyecto – RogueLike Top-down

Este documento describe la **estructura de carpetas y archivos** del proyecto RogueLike, explicando el **propósito funcional de cada módulo** y su rol dentro del juego. Esta organización busca mantener el proyecto modular, escalable y fácil de navegar para ambos desarrolladores.

---

## 🌳 Árbol de Carpetas

```bash
src.roguelike_project/
│
├── assets/              # Recursos audiovisuales
│   ├── characters/      # Sprites de personajes
│   ├── tiles/           # Tiles de mapas
│   ├── effects/         # Animaciones y efectos visuales
│   ├── ui/              # Iconos y elementos del HUD
│   └── sounds/          # Efectos de sonido y música (futuro)
│
├── components/          # Sistemas independientes de gran escala (futuro)
│
├── core/                # Motor del juego
│   ├── game/            # Game loop, estados globales
│   ├── render/          # Sistema de cámara, escalado, capas visuales
│   ├── logic/           # Control de eventos, entrada del usuario
│   └── camera.py        # Lógica de cámara central
│
├── docs/                # Documentación completa del proyecto
│
├── entities/            # Entidades del juego
│   ├── player/          # Clase principal de jugador
│   ├── remote_player/   # Jugadores online sincronizados
│   ├── enemies/         # NPCs hostiles
│   ├── projectiles/     # Bolas de fuego, flechas, etc.
│   └── obstacle.py      # Obstáculos y objetos fijos
│
├── map/                 # Lógica de mapas y generación procedural
│   ├── loader.py        # Carga de mapas desde texto
│   ├── generator.py     # Generador procedural
│   └── lobby_map.txt    # Ejemplo de mapa estático
│
├── network/             # Cliente WebSocket y lógica de sincronización
│   └── websocket_client.py
│
├── systems/             # Subsistemas grandes (PvP, habilidades, inventario...)
│
├── tests/               # Tests automáticos por módulo
│
├── ui/                  # Interfaces visuales dentro del juego
│   ├── menu.py          # Menú de pausa
│   ├── hud.py           # HUD inferior (items, hechizos)
│   └── minimap.py       # Mini mapa
│
├── utils/               # Funciones auxiliares y generales
│   ├── image_loader.py  # Carga de sprites
│   ├── math_utils.py    # Cálculos varios
│   └── config.py        # Configuración global (resolución, FPS, debug)
│
├── main.py              # Punto de entrada principal
├── launcher.py          # Launcher separado (futuro .exe)
├── requirements.txt     # Dependencias del entorno
└── README.md          # Descripción del proyecto
```

---

## 📌 Notas sobre la organización

- Cada carpeta clave (`core/`, `entities/`, `ui/`, etc.) incluye un `README.md` que explica brevemente su estructura interna.
- Todo lo relacionado a documentación debe mantenerse en `docs/` excepto el `README.md` de cada carpeta.
- Los assets están normalizados para facilitar la carga automatizada.
- Los sistemas futuros (quests, inventario, etc.) vivirán en `systems/`.
- Los tests automáticos se escriben por módulo y se ubican en `tests/` con prefijos como `test_*.py`. (!!!_Necesito Confirmar esto_!!!)

