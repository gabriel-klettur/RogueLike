# Inventory Grid View

Esta documentación cubre la clase `InventoryGridView` en `inventory_grid_view.py`, responsable de renderizar la cuadrícula de inventario dentro del editor.

## Funcionalidades

1. **_get_slots(model)**
   - Selecciona datos de inventario según el modo (`default`/`active`) y categoría (`player`/`monsters`).
   - Retorna la lista de slots para la entidad seleccionada.

2. **_get_grid_origin(panel_rect)**
   - Calcula la posición inicial `(x, y)` del grid basado en el rectángulo del panel de la lista (scroll panel) y los márgenes definidos.

3. **_draw_slots(overlay, slots, grid_origin_x, grid_origin_y, mx, my)**
   - Dibuja cada slot en una cuadrícula de 5 columnas.
   - Renderiza un rectángulo de fondo y, si se hace hover, aplica borde amarillo.
   - Muestra el icono del ítem y la cantidad en cada slot.

4. **_draw_show_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my)**
   - Dibuja los botones "Show Default" y "Show Active" debajo del grid.
   - Resalta con borde amarillo al hacer hover.
   - Retorna un dict con los rects: `{'show_default', 'show_active'}`.

5. **_draw_save_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my)**
   - Dibuja los botones "Save Default" y "Save Active" debajo de los botones de mostrar.
   - Resalta con borde amarillo al hacer hover.
   - Retorna un dict con los rects: `{'save_default', 'save_active'}`.

6. **draw(overlay, model, panel_rect)**
   - Orquesta el flujo de renderizado:
     - Obtiene la lista de slots.
     - Calcula la posición del grid.
     - Llama a los métodos privados para dibujar slots y botones.
     - Devuelve un dict con todos los rects de los botones para su manejo de eventos.

## Uso

En el `InventoryEditorView`, se instancia y utiliza así:
```python
self.grid_view = InventoryGridView(
    font, slot_size, margin, button_size,
    get_item_image_func, images, logger
)
# En _draw_grid del editor:
rects = self.grid_view.draw(overlay, model, panel_rect)
# Se guardan rects para eventos click/hover
```

---

Para más detalles de la implementación, consulta `inventory_grid_view.py`.