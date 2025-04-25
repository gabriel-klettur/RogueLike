
# 🐞 27. Debug Tools – Herramientas de Depuración Visual y Comandos

Este documento detalla las herramientas disponibles para depurar el juego en tiempo real o en etapas de desarrollo, enfocándose en visualizaciones, atajos y comandos útiles.

---

## 🎯 Objetivo

- Permitir inspección en tiempo real del estado del juego.
- Facilitar la detección de errores visuales, de lógica y rendimiento.
- Probar funcionalidades de forma rápida sin requerir condiciones especiales.

---

## 🛠️ Herramientas Actuales

| Herramienta            | Función                                                         |
|------------------------|------------------------------------------------------------------|
| ✅ FPS Overlay          | Muestra los FPS en pantalla con fondo negro para mayor claridad. |
| ✅ Menú con ESC         | Permite cambiar personaje rápidamente.                           |
| ✅ Restauración (Q)     | Restaura la vida, energía y maná del jugador.                    |
| ✅ HUD visible          | Muestra barras de salud, maná y energía flotantes.               |

---

## ⚙️ Atajos de Teclado (Debug)

| Tecla        | Acción                                 |
|--------------|----------------------------------------|
| `ESC`        | Abre/cierra menú                       |
| `Q`          | Restaura recursos del jugador          |
| `F1` (futuro)| Activa/Desactiva modo debug            |
| `F2` (futuro)| Muestra info avanzada de entidades     |
| `F3` (futuro)| Muestra información del tile bajo cursor|

---

## 🧪 Visualizaciones Futuras

| Herramienta Propuesta       | Funcionalidad                                                                 |
|-----------------------------|------------------------------------------------------------------------------|
| 🟩 Mostrar hitboxes          | Rectángulos visibles alrededor de entidades/obstáculos.                      |
| 🧭 Mostrar dirección NPC     | Flecha indicando hacia dónde se mueven los NPCs.                             |
| 💬 ID y stats sobre NPCs    | Mostrar PID, HP, clase, etc. sobre cabeza de entidades.                      |
| 🔍 Modo de inspección       | Click derecho sobre entidad muestra detalles (futuro sistema de debug UI).   |
| 🎯 Mostrar target actual     | Resalta a quién apunta cada entidad hostil (enemigo/NPC).                    |
| 🌐 Estado conexión red      | Icono con color verde/amarillo/rojo según calidad de conexión.               |

---

## 🧰 Debug Logging (futuro)

Habilitar log por consola y archivo con detalles como:

- Movimiento del jugador.
- Mensajes enviados y recibidos por WebSocket.
- Cambios de estado importantes (menú, combate, muerte).

Estructura sugerida con `loguru`:

```python
from loguru import logger
logger.info(f"Jugador se movió a {player.x}, {player.y}")
```

---

## 🔄 Recarga en Tiempo Real (opcional)

Implementar:

- `R` para recargar mapa.
- `L` para volver al lobby.
- Hot reload de assets (sprites y mapas).

---

## 🐛 Comandos Manuales – Consola de Desarrollo (futuro)

Una consola interna en el cliente puede facilitar pruebas, administración e inspección avanzada del juego.

### 🎮 Información útil a mostrar en consola

| Categoría              | Información a mostrar                                                    |
|------------------------|---------------------------------------------------------------------------|
| 🧍 Jugador             | Posición actual (`x, y`), dirección, stats actuales (HP/MP/EN), nivel     |
| 🧭 Estado de juego     | FPS, zoom de cámara, estado de conexión, mapa actual                      |
| 🌐 Red                 | Ping, ID de conexión, mensajes enviados/recibidos, errores de socket      |
| 🧠 IA/NPCs             | Cantidad de NPCs activos, objetivos, comportamientos, estado de alerta    |
| 🔄 Eventos             | Qué eventos globales están activos, próximos spawns, clima                |
| 🎨 Recursos            | Sprites cargados, memoria usada, assets activos                           |
| 🧩 Mods                | Mods cargados, assets personalizados, tilemaps extendidos                 |
| 🛠️ Sistema             | Versión del juego, modo debug, errores no fatales                         |

### 🧰 Comandos posibles a implementar

| Comando                          | Descripción                                                          |
|----------------------------------|----------------------------------------------------------------------|
| `help`                           | Lista todos los comandos disponibles.                               |
| `spawn <npc> <x> <y>`            | Crea un NPC en la posición especificada.                            |
| `tp <x> <y>`                     | Teletransporta al jugador a coordenadas.                            |
| `regen`                          | Restaura vida, maná y energía al máximo.                            |
| `map reload`                     | Recarga el mapa actual.                                             |
| `map load lobby`                 | Carga un mapa específico.                                           |
| `set zoom <1.0>`                 | Ajusta el zoom de la cámara.                                        |
| `toggle hud`                     | Activa o desactiva el HUD.                                          |
| `toggle fps`                     | Muestra u oculta el contador de FPS.                                |
| `give gold <amount>`            | Da oro al jugador.                                                  |
| `summon ally <type>`            | Invoca un aliado temporal para testear combate.                     |
| `ai list`                        | Lista NPCs activos y sus estados.                                   |
| `ai stop`                        | Congela toda IA hostil.                                             |
| `profile memory`                | Muestra uso de memoria y cantidad de objetos cargados.              |

### 💡 Características adicionales deseables

- ✅ Autocompletado de comandos (tipo consola Godot/Unity).
- 🔒 Acceso restringido: sólo si `DEBUG = True`.
- 💬 Consola flotante o fija, estilo terminal integrada al cliente.
- 📝 Historial de comandos.
- 🛑 Capacidad de forzar "stop loop", reiniciar, o cerrar sesión.

---

## 📂 Organización de Código de Debug

Propuesta para separar herramientas:

```
src.roguelike_project/
├── core/
│   └── game/
│       ├── logic/
│       ├── render/
│       │   └── minimap.py
│       └── main.py
├── entities/
├── map/
├── network/
├── ui/
├── debug/                      ◀️ Carpeta propuesta
│   ├── __init__.py
│   ├── visual_overlay.py       # FPS, hitboxes, texto flotante
│   ├── developer_console.py    # Consola interna con comandos
│   ├── logger.py               # Wrapper para loguru o logging
│   └── profiler.py             # Medición de tiempos por función
└── settings.py                 # Incluir DEBUG = True
```

---

## 🧪 Modo Debug Activable

Agregar en `settings.py` o al inicio de `main.py`:

```python
DEBUG = True
```

Comportamientos si `DEBUG = True`:

- Mostrar FPS.
- Habilitar teclas de debug.
- Logs activados.
- Info extra sobre entidades.

---

## 📌 Recomendaciones

- Separar el código de debug con condicionales `if DEBUG`.
- Usar colores neutros para overlays (verde, blanco, gris).
- Evitar que interfiera con gameplay cuando está desactivado.
- Añadir opción para desactivarlo desde un menú oculto.

---
