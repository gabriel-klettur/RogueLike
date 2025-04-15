# 🗺️ Sistema de Mapas Procedurales

El sistema de mapas está basado en una grilla de tiles (`Tile`) renderizados eficientemente en pantalla en función de la vista actual de la cámara.

---

## 📌 ¿Qué hace?

- Renderiza solo los tiles visibles en pantalla.
- Optimiza el rendimiento utilizando:
  - **Cacheo por zoom** para sprites escalados.
  - **Verificación de visibilidad** antes de dibujar cada tile.

---

## ⚙️ Funcionamiento

### 🔁 Ciclo de renderizado

1. En `Renderer.render_game`, se llama a `_render_tiles(...)`.
2. El método determina qué filas/columnas de tiles están en pantalla.
3. Solo esos tiles son procesados y dibujados.

### 📦 Clase `Tile`

```python
class Tile:
    def __init__(...):
        ...

    def render(...):
        ...