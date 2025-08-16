# Items Editor (Editor de Ítems)

Editor in‑game para gestionar ítems del proyecto: navegar el catálogo, editar propiedades y assets, crear/eliminar entradas del sistema, y colocar/eliminar drops en el mapa en tiempo real.

## Atajos de teclado

- F7: abrir/cerrar el Items Editor (`items_editor_events.py`)
- Esc: cerrar si está visible

## Componentes principales (MVC por panel)

- **Título** — `items_title_panel/ItemsTitleView`
  - Muestra el título y sirve de ancla vertical para el resto de paneles.

- **Toolbar (superior)** — `items_tool_bar_panel/*`
  - Herramientas (en el orden definido por el modelo): `['items_on_map', 'undo', 'redo']`.
  - `items_on_map`: toggle principal que muestra/oculta el Picker y el panel inferior.
  - `undo`/`redo`: reservados para historial (si el proyecto lo habilita).

- **Sub‑toolbar Add/Remove** — `items_add_remove_panel/*`
  - Herramientas: `['add_item', 'remove_item', 'add_item_on_system']`.
  - `add_item`: entra en modo colocar en el mapa (spawn). Primero selecciona un ítem en el Picker; luego clic izquierdo sobre el mapa para spawnear un drop. Si el clic cae dentro del panel de Inventario UI, se añade al inventario del jugador y se persiste.
  - `remove_item`: entra en modo borrar. Clic en el mapa elimina el drop bajo el cursor. Clic sobre el Picker elimina la entrada seleccionada del sistema (items.json).
  - `add_item_on_system`: alta de un ítem en el sistema usando el Panel de Propiedades (borrador guiado por esquema). Al confirmar, se persiste en `data/items/items.json`, se sale del modo y se re‑muestra el Picker.

- **Picker (catálogo de ítems)** — `items_picker_panel/*`
  - Grid reutilizable `PickerPanel` con celdas de 64×64, hasta 12 columnas, scroll con rueda.
  - Selección con clic izquierdo (sin abrir edición). Doble clic abre edición inline en Propiedades.
  - Clic derecho dentro del grid: spawnea al instante el ítem seleccionado en la posición del jugador.
  - El Picker se puede ocultar temporalmente en modos como `add_item_on_system` para dar más espacio al Panel de Propiedades.

- **Panel de Propiedades** — `items_properties_panel/*`
  - Muestra y edita las propiedades y assets del ítem seleccionado.
  - Soporta modo "alta en sistema" (borrador): renderiza todos los campos definidos por el esquema y permite confirmar la creación aun sin selección previa en el Picker.
  - Diseño con tamaño fijo y scroll vertical cuando el contenido excede el panel.

- **Panel de Instancias (inferior)** — `items_instances_panel/*`
  - Lista de drops activos leídos de `data/inventory/active/inventory_map.json` con `MapItemsUI` y editor de parámetros con `ParamsEditorUI` (`schemas/items/instances.json`).
  - Seleccionar una instancia sincroniza la selección del Picker/Propiedades.
  - Press‑and‑hold sobre una instancia: centra temporalmente la cámara; al soltar, vuelve al jugador.

## Comportamientos clave

- **Toggle/visibilidad**: F7 abre/cierra; la Toolbar superior rige la visibilidad del Picker y del panel inferior.
- **Spawn en mapa**: en modo `add_item`, clic izquierdo sobre el mapa crea un drop en la zona/posición clicada. Se añade una marca para spawnear inmediatamente en ECS y refrescar la lista de instancias.
- **Añadir al inventario**: en `add_item`, si clicas dentro del panel de Inventario UI, se añade el ítem al inventario del jugador y se persiste.
- **Borrado**:
  - Sobre el mapa: elimina el drop bajo el cursor (hit‑test por sprite y z‑layer).
  - Desde el sistema: con `remove_item` activo, seleccionar/abrir un ítem en el Picker lo borra de `data/items/items.json` y refresca catálogos/caches.
- **Edición de propiedades**: las ediciones se reflejan inmediatamente en las vistas y en los drops existentes (iconos/escala) si el runtime lo soporta.
- **Rueda del ratón**: el scroll se enruta al panel bajo el cursor (Propiedades, Instancias o Picker) para una navegación natural.

## Integración con runtime

- El ECS se mantiene activo mientras el editor está abierto. El sistema de drag RMB y el hover de drops siguen funcionando; los paneles del editor se registran como "UI blockers" para evitar interacciones sobre la UI.
- Los spawns/borrados actúan tanto sobre disco (JSON) como sobre la escena en vivo para feedback inmediato.

## Datos y esquemas

- Catálogo: `data/items/items.json` (IDs de ítems → modelo de ítem).
- Instancias en mapa: `data/inventory/active/inventory_map.json`.
- Esquema de instancias: `schemas/items/instances.json`.

## Flujo de trabajo sugerido

1. Abrir con F7.
2. `items_on_map` para mostrar paneles.
3. Para colocar ítems: `add_item` → elegir en Picker → clic en mapa o en Inventario UI.
4. Para borrar: `remove_item` → clic en mapa o seleccionar en Picker para borrar del sistema.
5. Para crear ítems nuevos: `add_item_on_system` → completar en Propiedades → Confirmar (se persiste y vuelve el Picker).

## Módulos relevantes

- `items_editor_controller.py`, `items_editor_events.py`, `items_editor_view.py`, `items_editor_models.py`.
- `items_tool_bar_panel/`: modelo, vista, eventos y controlador de la toolbar.
- `items_add_remove_panel/`: sub‑toolbar de agregar/eliminar y alta en sistema.
- `items_picker_panel/`: catálogo y selección/preview.
- `items_properties_panel/`: edición y alta guiada por esquema.
- `items_instances_panel/`: lista de instancias y editor de parámetros.

## Notas

- IDs: se usan como claves en `items.json`; evita duplicados y renómbralos con cuidado.
- Algunos atajos/funciones (undo/redo) pueden no estar activos según la configuración del proyecto.
