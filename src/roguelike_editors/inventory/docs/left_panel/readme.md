# Panel Izquierdo (Left Panel)

El panel izquierdo muestra la lista de entidades o elementos según la categoría seleccionada y permite:

## Categorías

- **Player**: muestra los ítems en el inventario del jugador.
- **Monsters**: lista de monstruos instanciados en el mundo, agrupados por entidad:
  - Línea principal: `<EntityID> | Template: <template_id>`
  - Sub-líneas: 
    - `Name: <nombre de plantilla>`
    - `Pos: (x, y)` (clickable para centrar cámara en la posición)
    - `Items: <item> x<cantidad>, ...`
- **Map**: elementos en el suelo del mapa, mostrados como `<item_id> x<cantidad> @(x,y)`.

## Funcionalidades

- **Pestañas**: cambiar categoría con un click.
- **Listado scrollable**: scroll con rueda o arrastre.
- **Selección**: click en un elemento selecciona la entidad o elemento.
- **Resaltado**:
  - Permanente: muestra un borde amarillo en el grupo del monstruo seleccionado.
  - Hover: resalta el grupo completo y la línea `Pos:` en naranja al pasar el cursor.
- **Doble-click** (en línea `Pos:`):
  - Centra la cámara en las coordenadas del monstruo.
  - Selecciona automáticamente el monstruo en el panel.
