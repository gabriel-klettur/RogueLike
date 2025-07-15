{{ ... }}
# Implementación de funcionalidades de ítems

## Objetivo
Plan detallado para dotar de lógica de comportamiento a las instancias de ítems en ECS y editor.

## 1. Carga de definiciones
- Usar `roguelike_game.managers.items.loader.ItemsLoader` para cargar y validar `data/items/items.json` utilizando el esquema `schemas/items/definitions.json`. [COMPLETADO]

## 2. Datos de instancias
- Usar `data/inventory/inventory_map.json` como diccionario de instancias, validado en `MapLoadDropsSystem` contra `schemas/items/instances.json`. [COMPLETADO]
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

## 3. Componentes ECS [COMPLETADO]
- `ItemComponent(definition_id: str)` para toda entidad-ítem.
- Componentes específicos según comportamiento:
  - `TeleportComponent(dest_map, dest_x, dest_y)`.
  - `HealingComponent(amount: int)`.
  - `BuffComponent(stat: str, value: float, duration: float)`.

## 4. Sistemas ECS [COMPLETADO]
- `TeleportSystem`:
  - Detecta colisión jugador↔portal y ejecuta teletransporte.
- `ConsumeSystem`:
  - Maneja uso de consumibles (curación, stat buffs).
- Otros sistemas según nuevos comportamientos.

## 5. Fábrica de entidades [COMPLETADO]
- `ecs/systems/items/item_factory.py` con `ItemFactory.create(instance_data)`:
  1. Recupera definición con `ItemDefinitions.get()`.
  2. Crea entidad, añade `ItemComponent` + componentes específicos según `params`.
  3. Posiciona la entidad en (x,y).

## 6. Integración en el editor de ítems (F7)
Para profesionalizar y reutilizar la UI actual de inventario, extraemos código de `roguelike_editors/inventory` a `roguelike_ui/widgets` y lo consumimos desde `roguelike_editors/items`:

### 6.1 Extracción a `roguelike_ui/widgets` [COMPLETADO]
- `ListPanelUI` (`src/roguelike_ui/widgets/list_panel_ui.py`)
  • Extraer de `roguelike_editors/inventory/view/editor_view.py`:
    - Uso de `ScrollPanel` y construcción de la lista (líneas ~64–85).
    - Método `set_items()`, `draw(surface, rect)` y nuevo `get_selected(mouse_pos)`.
- `TabPanelUI` (`src/roguelike_ui/widgets/tab_panel_ui.py`)
  • Extraer lógica de pestañas (líneas ~40–57).
  • API: `draw_tabs(surface, tabs: list[str], selected: str, rects_out: List[Rect])`, `handle_event(ev)->Optional[str]`.
- `IconCache` (`src/roguelike_ui/widgets/icon_cache.py`)
  • Extraer `_get_item_image()` (líneas ~104–118) como cache singleton.

### 6.2 Creación de `MapItemsUI` [COMPLETADO]
- `src/roguelike_ui/widgets/map_items_ui.py`:
  • Internamente usa `ListPanelUI` para listar instancias de `data/inventory/inventory_map.json`.
  • Emite `on_select(instance_id)` al hacer clic.
  • Permite refrescar con nuevos datos.

### 6.3 Reutilización en `roguelike_editors/items` [COMPLETADO]
- `src/roguelike_editors/items/items_editor_view.py`:
  • Layout de dos columnas: panel de Definiciones (reutilizar `ListPanelUI`) + `MapItemsUI`.
  • Manejar selección de instancia y desplegar `ParamsEditorUI` (punto 6.4).

### 6.4 `ParamsEditorUI` [COMPLETADO]
- `src/roguelike_ui/widgets/params_editor_ui.py`:
  • Formulario dinámico basado en schema JSON (`schemas/items/instances.json`).
  • Usa `text_input.TextInput` para campos de texto/número.
  • Métodos:
    - `load_values(data: dict)`
    - `get_values()->dict` (lanza `ValidationError` si falla `Draft7Validator`)
    - `draw(surface, rect)`
    - `handle_event(ev)->bool`

### 6.5 Flujo de interacción [COMPLETADO]
1. F7 abre `ItemsEditorController`.
2. `ItemsEditorView` instancia:
   - `DefinitionsPanelUI` (alias de `ListPanelUI`).
   - `MapItemsUI`.<br>
   - `ParamsEditorUI` oculto.
3. Al seleccionar instancia:
   - `ParamsEditorUI.load_values(instance.params)` + mostrar panel.
4. Edición → `ParamsEditorUI.handle_event` → `get_values()` + validación.
5. Guardar → `ItemsEditorController` actualiza `map_items.json`, marca dirty, refresca `MapItemsUI`.

**Beneficios**:
- DRY: un único widget de listado y formulario para todos los editores.
- Mantenible: cambiar schema actualiza ambos editores automáticamente.
- Profesional: UI consistente, validación robusta y separación clara de responsabilidades.

Beneficios:
- DRY: un solo widget de listado y formulario para ambos editores.
- Mantenible: cambiar schema actualiza ambos formularios automáticamente.
- Profesional: UI consistente, validación robusta y clara separación de responsabilidades.

## 7. Flujo de trabajo  [COMPLETADO]
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