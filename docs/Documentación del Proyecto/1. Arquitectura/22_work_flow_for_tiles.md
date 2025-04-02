# 🧱 22. Work Flow for Tiles – Diseño, Testing y Agregado de Tiles

Este documento detalla el proceso profesional y escalable para diseñar, testear y agregar tiles nuevos al sistema del juego, tanto en la lógica como en el renderizado.

---

## 🎯 Objetivo

Establecer una **guía estandarizada y repetible** para incorporar nuevos tiles de forma segura, visualmente coherente y compatible con el sistema de colisiones y renderizado.

---

## 🪄 Paso a Paso – Creación de un Tile

### 1️⃣ Diseño Visual

- **Resolución estándar:** `128x128 px` (escala base del juego).
- **Formato preferido:** `.png` con fondo transparente.
- **Carpetas destino:**
  ```
  assets/tiles/
  assets/tiles/walls/
  assets/tiles/ground/
  ```

### 2️⃣ Nombre de archivo

Convención: `<tipo>_<variante>.png`  
Ejemplos:
- `wall_stone_01.png`
- `ground_grass_02.png`
- `trap_spike_01.png`

Esto permite asociarlo fácilmente desde un sistema de metadatos o desde `tileset.json`.

---

## 🧪 Testing Visual

### Manual

1. Colocar temporalmente el tile en una **zona de pruebas** del mapa.
2. Renderizar sobre un fondo neutro (gris oscuro o negro).
3. Verificar:
   - Escalado correcto con zoom.
   - Alineación perfecta en rejilla.
   - Buena visibilidad con otros tiles adyacentes.

### Automático (Planeado)

- Implementar script para testear transparencia, tamaño exacto, y artefactos visuales.

---

## 🧠 Asociación con Lógica

### 1. Lógica ASCII

Si el tile tiene función jugable (colisión, daño, spawn…), debe tener un **símbolo lógico** (`#`, `.`, `^`, etc.) en el mapa base (`map_loader.py`).

### 2. Capa Visual Extendida

En `tileset.json` (planeado):

```json
{
  "W1": {
    "type": "wall",
    "sprite": "assets/tiles/walls/wall_stone_01.png",
    "collides": true
  },
  "G2": {
    "type": "ground",
    "sprite": "assets/tiles/ground/grass_02.png",
    "collides": false
  }
}
```

---

## 🧩 Agregado al Mapa

### Mapa Base

1. Insertar en el mapa lógico:
   ```text
   ...#.#..
   ..#..#..
   ```

2. El símbolo `#` indicará colisión en la `collision_mask`.

### Mapa Visual (futuro)

1. Crear mapa extendido:
   ```text
   W1 G2 W3 W4
   G1 G2 W2 G1
   ```

2. Cargarlo con `tilemap_render.py`.

---

## 🗂️ Organización de Archivos

```bash
assets/
└── tiles/
    ├── walls/
    │   ├── wall_stone_01.png
    │   └── wall_stone_02.png
    ├── ground/
    │   ├── grass_01.png
    │   └── sand_01.png
    ├── decorations/
    ├── traps/
    └── tileset.json   # Planeado: metadatos de cada tile
```

---

## ♻️ Mantenimiento de Tiles

- **Checklist por tile nuevo:**
  - [ ] Está en carpeta correspondiente
  - [ ] Tiene nombre descriptivo y variante
  - [ ] Tiene tamaño 128x128 px
  - [ ] Fue probado visualmente
  - [ ] Tiene lógica asociada (si aplica)

---

## 💡 Extensiones Futuras

| Mejora                    | Propósito                                                   |
|---------------------------|-------------------------------------------------------------|
| 🧠 Clasificación por bioma | Facilitar mapas temáticos (bosque, desierto, mazmorra).     |
| 🧩 Tile modular            | Soporte para auto-tiling y transiciones suaves.             |
| 📐 Soporte para altitud    | Simulación de niveles de altura o plataformas.              |
| 🧪 Tests automáticos       | Validar assets (tamaño, transparencia, consistencia visual).|
| 🛠️ Editor visual           | UI para probar combinaciones y generar mapas manuales.      |

---
