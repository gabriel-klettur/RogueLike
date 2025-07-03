# Plan de implementación: Sistema de Inventario

Este documento describe el roadmap de alto nivel para llevar a producción la guía de inventario (`Inventario.md`). Tras cada sección, el proyecto deberá compilarse y funcionar correctamente.

## Requisitos previos
- Dependencias:
  - jsonschema
  - pydantic
  - pytest
  - mermaid-cli (opcional para diagramas)
- Asegurarse de que el juego arranca sin errores actuales.

## 1. JSON Schemas y datos de ejemplo
1. Crear esquemas JSON en `schemas/` para:
   - `ItemsSchema.json`
   - `InventoryMonstersSchema.json`
   - `InventoryPlayerSchema.json`
   - `InventoryMapSchema.json`
2. Definir ejemplos mínimos en `data/` que cumplan cada esquema.
3. Validar con `jsonschema -i data/*.json schemas/*.json`.

> Estado tras paso 1: Validación JSON completada sin errores.

## 2. Modelos de datos
1. Implementar `ItemModel` (pydantic) y `ItemStack` en `components/models.py`.
2. Escribir tests para carga de `items.json` y validación de atributos.

> Estado tras paso 2: Tests de modelos pasan.

## 3. Componente de Inventario
1. Desarrollar `InventoryComponent` con métodos:
   - `add(item_id, qty)`
   - `remove(item_id, qty)`
   - `has(item_id, qty)`
   - `serialize()`
2. Añadir tests unitarios en `tests/test_inventory.py`.

> Estado tras paso 3: Inventario base funciona y tests pasan.

## 4. Mapa de drops
1. Crear `MapManager` en `components/map_manager.py` con:
   - `create_drop(drop_id, item_id, qty, position)`
   - `pick_up(drop_id)`
   - `load_drops()`
2. Definir `inventory_map.json` y tests de flujo drop→pickup.

> Estado tras paso 4: Drops en mapa funcionan y no rompen inventario.

## 5. Integración de NPCs
1. Actualizar función `on_npc_death` para usar `inventory_monsters.json`.
2. Probar dropeo de NPCs en entorno de juego.

> Estado tras paso 5: NPCs dropean items según plantilla.

## 6. UI e interacción
1. Implementar ventana de inventario y grid de slots.
2. Conectar eventos de teclado (`I`) y drag & drop.
3. Validar tooltips e indicadores de estado.

> Estado tras paso 6: UI de inventario operativa sin errores.

## 7. Persistencia y versionado
1. Serializar/deserializar inventario de jugador en `data/`.
2. Incluir `schema_version` en JSON y crear script de migración simple.

> Estado tras paso 7: Guardado/carga de partidas estable.

## 8. Multijugador y sincronización
1. Transmitir sólo operaciones (diffs) de inventario y drops.
2. Gestionar conflictos con locks optimistas o validación en servidor.

> Estado tras paso 8: Inventarios y drops sincronizados en red.

## 9. Documentación y CI
1. Confirmar que `docs/developer_guide` está alineado con código.
2. Configurar GitHub Actions para:
   - Validar JSON con `jsonschema`.
   - Ejecutar tests con `pytest`.

> Estado tras paso 9: Pipeline verde y documentación confiable.

---

**Fin del plan de implementación**
