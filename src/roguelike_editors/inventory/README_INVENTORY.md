# Inventory Editor (Editor de Inventario)

Herramienta in‑game para inspeccionar y modificar inventarios de jugador, monstruos y objetos en el suelo. Funciona como overlay y se integra con el runtime para reflejar cambios en vivo.

- Tecla de activación: F6 (toggle)
- Patrón: MVC por módulos y subpaneles
- Archivos clave:
  - `editor_controller.py`, `editor_view.py`, `editor_model.py`, `editor_events.py`
  - `data_controller.py` (carga/validación JSON y migraciones)
  - Submódulos: `left_panel/`, `right_panel/`, `inventory_title/`

## Estructura general

- **Model (`InventoryEditorModel`)**: estado de visibilidad, lado de edición (default/active), categoría actual, selecciones, drag&drop, scroll, y datos JSON `default_data`/`active_data`.
- **View (`InventoryEditorView`)**: dibuja título, tabs, panel izquierdo (lista), panel derecho (grid + selección de ítems), botones. Resuelve iconos de ítems y sincroniza inventario activo del jugador desde ECS cuando corresponde.
- **Controller (`InventoryEditorController`)**: orquesta subcontroladores y handlers de eventos; instancia título, panel izquierdo, grid y panel de selección.
- **Eventos (`InventoryEditorEventHandler`)**: enruta eventos a: panel izquierdo, panel de selección de ítems y grid/botones del panel derecho.
- **Datos (`DataController`)**: carga `data/inventory/**.json`, valida contra `schemas/inventory/*.json` (si `jsonschema` está instalado) y aplica migraciones en memoria.

## Apertura y comportamiento (F6)

- Al abrir con F6 se cargan/validan los JSON por categoría:
  - `data/inventory/defaults/inventory_<categoria>.json`
  - `data/inventory/active/inventory_<categoria>.json`
- Atributos preservados entre toggles (resumen): `default_data`, `active_data`, contexto de edición (propiedad/índice), drag & scroll, y modelos de paneles/UI. Se refresca la lista de entidades y se reinicia `selected_eid` al abrir.
- Soporte de ocultado temporal: `overlay_hidden_while_hold` permite ocultar visualmente el overlay mientras se mantiene pulsado en ciertos flujos (p. ej., foco de cámara).

## Panel izquierdo (`left_panel/`)

- Categorías: `player`, `monsters`, `map` (tabs superiores).
- Lista scrollable por categoría. Selección de entidad/registro y resaltado de grupos.
- Acciones:
  - Click en tabs para cambiar de categoría.
  - Doble‑click en línea `Pos:` centra la cámara en la entidad (el overlay puede ocultarse mientras se mantiene pulsado).
- Tabs laterales (lado derecho de la barra) para mostrar `Default`/`Active` cuando la categoría es `player` o `monsters`.

## Panel derecho (`right_panel/`)

Incluye el grid del inventario y el panel de selección de ítems.

- **Grid de inventario** (`inventory_items_panel/…/grid_*`)
  - Layout típico: 5 columnas, celdas de 50 px, margen configurable.
  - Drag & drop entre slots (cuando el runtime lo soporta); hover/selección visual.
  - Botones principales (arriba del grid):
    - `Show Default` y `Show Active` (conmutan el lado de edición)
    - `Save`: persiste cambios a los JSON de `defaults` o `active` según el lado activo
    - `Add Item`: abre el panel de selección (se resalta con borde amarillo mientras está abierto)
    - `Delete Item`: activa el modo eliminar con cantidad configurable

- **Eliminar ítems (Delete mode)** (`buttons/delete/*`)
  - Toggle con el botón `Delete Item` (se muestra input de cantidad).
  - Click en un slot válido elimina la cantidad indicada; sale automáticamente del modo al terminar.
  - Click fuera de ítems cancela y cierra el modo.

- **Panel de selección de ítems** (`item_selection_panel/*`)
  - Se renderiza debajo del grid/Save.
  - Tabs: `Default` (catálogo) y `Ground` (objetos en el suelo).
  - Controles: scroll, input de cantidad y botón `Add to Inventory`.
  - Comportamiento en pestaña `Ground`:
    - No se cierra automáticamente tras añadir (permite selecciones consecutivas).
    - Actualiza la cantidad o elimina la entrada si llega a 0.
    - Mantiene la selección cuando procede para agilizar varias tomas.

## Persistencia y validación

- Rutas por categoría (`DataController.paths`):
  - `player`: `defaults/ inventory_player.json`, `active/ inventory_player.json`
  - `monsters`: `defaults/ inventory_monsters.json`, `active/ inventory_monsters.json`
  - `map`: `defaults/ inventory_map.json`, `active/ inventory_map.json`
- Validación opcional con `jsonschema` usando `schemas/inventory/*.json`.
- Normalización especial para `map`: si el JSON activo trae una clave raíz `map`, se reescribe sin esa capa para mantener un formato plano.
- Migración de jugador por clases: si `player` (defaults) está en formato legado, se expande a `classes.{class_name}` usando `data/entities/new_players.json` y se clona `capacity/slots` por clase (solo en memoria).

## Sincronización en vivo con ECS

- Cuando el lado activo es `Active` y la categoría es `player`, la vista sincroniza los `slots` leyendo el `InventoryComponent` del ECS en cada frame (`_sync_active_player_from_ecs`). Así, nuevas recogidas o modificaciones in‑game se reflejan inmediatamente en el editor.

## Directorio y módulos relevantes

- `inventory_title/`: título/breadcrumbs del editor, devuelve `title_rect` para alinear paneles.
- `left_panel/`: tabs, lista y eventos de selección/foco de cámara.
- `right_panel/`:
  - `inventory_items_panel/`: grid, tabs (Default/Active), botones `Add/Delete/Save` y sus controladores/modelos/vistas.
  - `item_selection_panel/`: lista de ítems (Default/Ground), input de cantidad y confirmación.
- `docs/`: documentación de subpaneles.

## Limitaciones actuales

- Sin undo/redo.
- Sin búsqueda avanzada ni filtros en el panel de selección.
- Validación de JSON se hace en carga (no continua).

## Consejos de uso

1. Elige categoría en el panel izquierdo y una entidad/registro.
2. Conmuta `Default`/`Active` según quieras editar plantillas o estado en vivo.
3. Usa `Add Item` para abrir la selección (Default/Ground) y `Add to Inventory` para aplicar.
4. Usa `Delete Item` para eliminar rápidamente cantidades específicas.
5. `Save` para persistir los cambios al JSON correspondiente.

---

Este README resume el diseño, capacidades y flujo del Inventory Editor. Para más detalle, consulta `docs/inventory editor.md` y los README de `left_panel/` y `right_panel/`.
