# Inventory Editor

El Inventory Editor es una herramienta in-game para inspeccionar y modificar inventarios de jugadores, monstruos y objetos en el suelo del mapa. Sigue un patrón MVC completo:

- **Model**: gestiona los datos JSON (`default_data`, `active_data`), estado de UI, selección de entidad y flujos.
- **View**: dibuja el panel izquierdo, el panel derecho, el grid de slots, botones y paneles de selección.
- **Controller / Event Handlers**: procesan teclas (F6) y eventos de ratón, delegando en controladores especializados (left_panel, right_panel).

## Activación

- Pulsa **F6** para abrir y cerrar el editor.
- Al abrir, carga y valida (si `jsonschema` está presente) los archivos:
  - `data/inventory/defaults/inventory_<categoria>.json`
  - `data/inventory/active/inventory_<categoria>.json`

## Panel Izquierdo

Muestra la lista según la categoría (`player`, `monsters`, `map`). Permite:
- Cambiar categoría con un click en pestañas.
- Listado scrollable.
- Seleccionar entidad o elemento.
- Resaltado permanente y hover de grupos.
- Doble-click en línea `Pos:` centra la cámara.

(Ver detalles en `left_panel/readme.md`)

## Panel Derecho

Gestiona el grid de inventario de la entidad seleccionada. Incluye:
- Slots de inventario con drag&drop.
- Botones **Add Item**, **Delete Item** (pendiente), **Show Default**, **Show Active**, **Save**.
- Panel de selección de ítems con pestañas **Default** y **Ground**, scroll, input de cantidad y confirmación.

(Ver detalles en `right_panel/readme.md`)

## Persistencia

- **Save** persiste en los JSON de defaults o inventory.
- Los paths se definen en `config`: `DATA_DIR/defaults` y `DATA_DIR/inventory`.

## Limitaciones

- **Delete Item** está presente en la UI pero no elimina todavía.
- No hay undo/redo.
- Validación de JSON solo al inicio.
- No búsqueda avanzada ni filtros.
- Solo números enteros para cantidades.

---

Este documento resume qué hace y qué no hace el Inventory Editor, sirviendo de guía para desarrolladores y usuarios avanzados.