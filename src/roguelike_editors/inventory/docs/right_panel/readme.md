# Panel Derecho (Right Panel)

El panel derecho gestiona la visualización y edición del inventario de la entidad seleccionada.

## Componentes

1. **Grid de Inventario**:
   - Muestra los slots de inventario para la entidad actual (player o monster).
   - **Add Item**: botón para abrir el selector de ítems.
   - **Delete Item**: botón para eliminar el ítem seleccionado (por implementar).
   - **Show Default / Show Active**: alterna entre plantilla por defecto y datos activos.
   - **Save**: guarda la plantilla o inventario activo en su archivo JSON correspondiente.
   - **Drag & Drop**: arrastra con clic izquierdo para mover ítems entre slots.
   - **Hover**: resalta slot con borde amarillo al pasar el cursor.

2. **Item Selection Panel**:
   - Se abre al pulsar **Add Item**.
   - **Pestañas**:
     - **Default**: lista de ítems disponibles según plantilla.
     - **Ground**: ítems en el suelo (ground_items) con formato `item_id xcantidad`.
   - **Lista Scrollable**: scroll con rueda o arrastre.
   - **Selección**: click en ítem marca la selección.
   - **Cantidad**: tras seleccionar, aparece input numérico para definir cantidad.
   - **Confirm**: añade el ítem al grid; si en pestaña Ground, también elimina del suelo y guarda JSON.

## Persistencia

- Los cambios se guardan en:
  - `data/defaults/inventory_<categoria>.json` para plantillas.
  - `data/inventory/inventory_<categoria>.json` para inventario activo.
- La acción **Save** persiste siempre la vista actual (default o active).
