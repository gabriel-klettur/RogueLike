# Plan de implementación: Especificación de Ítems

Este documento describe el roadmap de alto nivel para llevar a producción la guía de ítems (`items.md`). Tras cada paso, el proyecto debe compilar y pasar tests sin errores.

## Requisitos previos
- Dependencias:
  - `jsonschema`, `pydantic`, `pytest`
- Código base debe compilar y los tests actuales pasar.

## 1. JSON Schema y datos de ejemplo
1. Crear `schemas/ItemSchema.json` con la definición completa de campos y validaciones, incluyendo un campo opcional `z_layer` (int) para definir la capa de renderizado de cada ítem.
2. Generar ejemplos mínimos en `data/items.json` que cumplan el esquema.
3. Validar con:
   ```bash
   check-jsonschema --schemafile schemas/ItemSchema.json data/items.json
   ```

> Estado tras paso 1: Validación JSON completada sin errores.

## 2. Modelos de datos
1. Implementar `ItemModel` (Pydantic) y `ItemStack` en `src/roguelike_game/ecs/components/item_models.py`.
2. Escribir tests en `tests/test_items.py` para:
   - Validar instanciación de `ItemModel` (import desde `roguelike_game.ecs.components.item_models`)
   - Reglas de `stackable`, `max_stack`, `threshold`.

> Estado tras paso 2: Tests de modelos pasan correctamente.

## 3. Integración de carga de Ítems
1. Escribir función de carga en `src/roguelike_game/ecs/components/item_models.py`: 
   ```python
   def load_items(path: str) -> Dict[str, ItemModel]:
       ...
   ```
2. Consumir en inicialización del juego y exponer `items` global.
3. Añadir test de carga completa y acceso por ID.

> Estado tras paso 3: Juego arranca con catálogo de ítems cargado.

## 4. Extensiones de Tipos y Validaciones
1. Agregar lógica para:`Consumibles`, `Equipables`, `Quest Items`.
2. Definir clases derivadas o campos opcionales en `ItemModel`.
3. Tests de comportamiento (e.g., campo `effect`, `durability`).

> Estado tras paso 4: Nuevos tipos correctos y tests pasan.

## 5. Activos y UI de Ítems
1. Verificar rutas de iconos en `assets/items/`.
2. Cargar assets de iconos en `GameInitializer._load_items`, almacenándolos en `game.item_assets: Dict[str, Surface]`.
   
   2.a En runtime, `MapLoadDropsSystem` utiliza estos assets para asignar componentes `Sprite` y `Scale` a las entidades de drops, además de `ZLayer`, y el sistema de renderizado principal (`RendererManager`) las dibuja ordenadas por `ZLayer` y posición Y.
3. Crear paquete MVC `item_editor` en `src/roguelike_editors/items`:
   - Directorios:
     - `model/`: definir `ItemEditorModel` (estado: lista de ítems, posición de scroll, visibilidad).
     - `view/`: definir `ItemEditorView` (renderiza panel semi-transparente, iconos y datos de cada ítem).
     - `controller/`: definir `ItemEditorController` (maneja input: F7 para togglear, flechas para navegar).
   - `__init__.py`: exponer `ItemEditor = ItemEditorController(ItemEditorModel, ItemEditorView)`.
4. Extender `InputConfig` en `roguelike_game.config.input_config` para mapear `pygame.K_F7` a `toggle_item_editor`.
5. Integrar editor en la inicialización:
   - En `GameInitializer._load_items`, instanciar `self.item_editor = ItemEditor(game.items, game.item_assets)` y añadir `game.show_item_editor=False`.
   - En el bucle de eventos (en `RendererManager` o en el loop principal): llamar `game.item_editor.handle_event(event)`.
   - En la fase de render: si `game.show_item_editor`, llamar `game.item_editor.draw(screen)`.
6. Pruebas manuales: presionar F7 para abrir/cerrar el editor, verificar grid de iconos, scroll, hover y panel de detalles emergente.

> Estado tras paso 5: Editor de ítems invocable con F7 y muestra datos e imágenes correctamente.

## 6. Testing & CI
1. Añadir en CI:
   - Validación de `ItemSchema.json`.
   - Ejecución de `pytest` para `test_items.py` y `test_item_editor_ui.py`.
2. Pipeline verde garantiza calidad.

> Estado tras paso 6: CI incorpora esquemas, tests de ítems y UI del editor de ítems.

## 7. Revisión y Documentación Final
1. Validar alineación de `items.md` y `implementacion_items.md` con el grid UI y tests del editor de ítems.
2. Ajustar ejemplos y diagramas si hay cambios.
3. Aprobar PR y merge.

> Estado tras paso 7: Documentación de ítems y código en producción.

---
**Fin del plan de implementación**
