{{ ... }}
# Implementación de funcionalidades de ítems

## Objetivo
Plan detallado para dotar de lógica de comportamiento a las instancias de ítems en ECS y editor.

## 1. Carga de definiciones
- Usar `roguelike_game.managers.items.loader.ItemsLoader` para cargar y validar `data/items/items.json` utilizando el esquema `schemas/items/definitions.json`.

## 2. Datos de instancias
- Usar `data/inventory/inventory_map.json` como diccionario de instancias, validado en `MapLoadDropsSystem` contra `schemas/items/instances.json`.
- Estructura típica (clave dinámica por `instance_id`):
```json
{
  "portal_entrance_1": {
    "item_id": "portal",
    "quantity": 1,
    "schema_version": "1.0.0",
    "position": { "x": 12, "y": 5 },
    "params": { "dest_map": "dungeon_02", "dest_x": 3, "dest_y": 8 }
  }
}
```

## 3. Componentes ECS
- `ItemComponent(definition_id: str)` para toda entidad-ítem.
- Componentes específicos según comportamiento:
  - `TeleportComponent(dest_map, dest_x, dest_y)`.
  - `HealingComponent(amount: int)`.
  - `BuffComponent(stat: str, value: float, duration: float)`.

## 4. Sistemas ECS
- `TeleportSystem`:
  - Detecta colisión jugador↔portal y ejecuta teletransporte.
- `ConsumeSystem`:
  - Maneja uso de consumibles (curación, stat buffs).
- Otros sistemas según nuevos comportamientos.

## 5. Fábrica de entidades
- `ecs/systems/items/item_factory.py` con `ItemFactory.create(instance_data)`:
  1. Recupera definición con `ItemDefinitions.get()`.
  2. Crea entidad, añade `ItemComponent` + componentes específicos según `params`.
  3. Posiciona la entidad en (x,y).

## 6. Integración en el editor de ítems (F7)
- En `src/roguelike_editors/items`:
  - Panel lateral que lista definiciones (`definition_id`, nombre).
  - Botón “Añadir al mapa”: al seleccionar, clic en tile crea nueva entrada en `map_items.json`.
  - UI para editar parámetros de `params` en el momento de colocación.

## 7. Flujo de trabajo
1. Definir propiedades estáticas en `data/items/items.json`.
2. Instanciar objetos en el mapa vía editor.
3. Al cargar nivel, el loader instancia entidades con lógica.
4. Probar y ajustar comportamientos.

## 8. Próximos pasos
1. Integrar `ItemsLoader` con validación de esquemas en el initializer del juego.
2. Ajustar `ecs/systems/items/item_factory.py` para consumir `params` y añadir componentes.
3. Implementar componentes ECS para ítems (`TeleportComponent`, `HealingComponent`, `BuffComponent`).
4. Desarrollar sistemas ECS específicos (`TeleportSystem`, `ConsumeSystem`).
5. Probar flujo completo en juego y editor, ajustando validaciones y UI según sea necesario.