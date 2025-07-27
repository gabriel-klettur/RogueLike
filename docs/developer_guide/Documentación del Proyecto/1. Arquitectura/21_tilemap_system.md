

# 🗺️ 21. Tilemap System – Generación Procedural, Tipos de Tile y Fusión de Mapas

Este documento describe la arquitectura de mapas del juego, combinando generación procedural con mapas hechos a mano. Se detallan los tipos de tile, la gestión de colisiones y la futura expansión visual con múltiples capas.

---

## 🎯 Objetivo

Construir mapas ricos, variados y detallados, que combinen lógica de juego (colisiones, navegación) con un sistema visual altamente personalizable.

---

## 🧱 Estructura Lógica del Mapa

El mapa base es una matriz de caracteres (`str`).  
Cada carácter representa un tipo lógico de tile:

| Símbolo | Tipo Lógico       | Transitable | Descripción                                 |
|---------|-------------------|-------------|---------------------------------------------|
| `#`     | Muro              | ❌ No       | Impide paso y visión.                       |
| `.`     | Suelo             | ✅ Sí       | Libre tránsito.                             |
| `+`     | Puerta cerrada    | ❌ No       | Requiere acción para abrir.                 |
| `/`     | Puerta abierta    | ✅ Sí       | Permite tránsito.                           |
| `^`     | Trampa            | ✅ Sí       | Activadores o efectos especiales.           |
| `~`     | Agua              | ⚠️ Opcional | Puede ralentizar o impedir paso.            |
| `@`     | Spawn del jugador | ✅ Sí       | Punto de aparición o respawn.               |

Estos valores son fácilmente personalizables desde un archivo externo o tileset definido por el motor.

---

## 🧬 Sistema de Capas para Variantes de Tiles (Planeado)

Para soportar hasta 100 variantes visuales por tipo, se usará un sistema en **dos capas**:

### 1️⃣ Capa Lógica (Actual)

- Se mantiene para colisiones, navegación, IA, generación procedural.
- Ejemplo de mapa base:

```
# . # #
. . # .
```

### 2️⃣ Capa Visual Extendida (Futura)

- Mapa paralelo de **2 caracteres por tile**:

```
W1 G1 W3 W4
G1 G2 W2 G1
```

- Donde:
  - Primera letra indica el tipo (`W`=wall, `G`=ground, etc.)
  - Número representa la variante visual (sprite/estilo).

#### Beneficios:
- Mejor estética (sin repeticiones).
- Mapas únicos y ricos en detalles.
- Total compatibilidad hacia atrás.
- Soporte para editor visual.

---

## 🔄 Generación Procedural

Utilizamos `generate_dungeon_map()` desde `map_generator.py` para:

- Crear habitaciones aleatorias.
- Conectarlas mediante túneles.
- Evitar superposición.

### Funciones clave:

- `intersect()` – evita que las salas se solapen.
- `center_of()` – obtiene el centro de una sala.
- `create_h_corridor()` y `create_v_corridor()` – conecta salas.

---

## 🧩 Fusión con Mapas Hechos a Mano

La función `merge_handmade_with_generated()`:

- Permite integrar mapas diseñados manualmente (ej. lobby).
- Controla el punto exacto donde se "pega" el mapa (`offset_x/y`).
- Es compatible con la generación procedural.

Esto permite tener zonas controladas y otras generadas automáticamente en una sola partida.

---

## 🧠 Hitboxes y Colisiones

- Cada tile `#` es tratado como **sólido**.
- Se genera una **máscara de colisión (`collision_mask`)** derivada del mapa lógico.
- Esto se usa para detección de colisiones, renderizado, pathfinding y lógica de juego.

---

## 📂 Estructura Profesional Sugerida

Conforme crezca el sistema, proponemos modularizar así la carpeta `map/`:

```bash
map/
├── __init__.py
├── generator/
│   ├── procedural.py          # Generador procedural
│   └── handmade_merger.py     # Fusión procedural + manual
├── loader/
│   └── text_map_loader.py     # Carga desde archivos o strings
├── logic/
│   └── tile_definitions.py    # Tipos de tile (sólido, decorativo, daño, etc.)
├── handmade_maps/             # ✅ Carpeta para mapas hechos a mano
│   ├── __init__.py
│   ├── lobby_map.py           # Mapa del lobby principal
│   └── [futuros mapas].py     # Castillos, ciudades, eventos especiales
└── visual/
    └── tilemap_render.py      # Futuro sistema visual por capas
```

### Ventajas:

- Separación clara de responsabilidades.
- Escalable para biomas, mapas de usuario, mods, herramientas gráficas.
- Facilita testing y mantenimiento individual de módulos.

---

## 🧪 Extensiones Futuras

| Mejora                           | Propósito                                                                 |
|----------------------------------|--------------------------------------------------------------------------|
| 🧱 Auto-tiling                   | Ajuste automático de sprites según contexto (esquinas, bordes, etc.).   |
| 🌍 Regiones y biomas             | Afectan visual y mecánicamente el entorno.                              |
| 🔥 Tiles con efectos             | Lava, hielo, ácido: modifican movimiento o daño.                        |
| 🧬 Generación por semilla        | Mundos reproducibles con una clave.                                     |
| 🧭 Soporte de altura/elevación   | Simulación de desniveles, plataformas (futuro gráfico).                 |
| 🧠 IA contextual por tile        | Enemigos que reaccionan según el entorno.                               |
| 💾 Tiles dinámicos/destruibles  | Paredes rompibles, trampas, elementos interactivos.                     |

---
