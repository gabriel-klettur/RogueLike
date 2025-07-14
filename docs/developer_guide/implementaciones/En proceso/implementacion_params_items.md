# Especificación de parámetros de ítems

## Objetivo
Detallar formato, tipos, mapeo a código y guía de extensión de los campos `params` en las instancias de ítems.

## 1. Formato de datos
- Ubicación JSON de instancias: `data/map_items.json` o `data/levels/level_X_items.json` (planificado).
- Estructura típica:
```json
{
  "items": [
    {
      "instance_id": "portal_entrance_1",
      "definition_id": "portal",
      "x": 12,
      "y": 5,
      "params": { "dest_map": "dungeon_02", "dest_x": 3, "dest_y": 8 },
      "schema_version": "1.0.0"
    }
  ]
}
```
- Validación: validar parámetros e instancias usando los esquemas en `schemas/items/`.

### Esquemas JSON implementados

- `schemas/items/common.json`:
  - `#/definitions/params` define los campos de `params`.
  - `#/definitions/instance` define la estructura de instancias (incluye `params`).
- `schemas/items/definitions.json`: valida `data/items/items.json` (definiciones estáticas).
- `schemas/items/instances.json`: valida `data/inventory/inventory_map.json` (drops de ítems).

## 2. Tipos de parámetros
- **Portal**
  - `dest_map` (string)
  - `dest_x`, `dest_y` (int)

- **Consumibles**
  - `healing`, `mana` (int)

- **Buffs**
  - `buff_stat` (string)
  - `buff_value` (float)
  - `duration` (float)

- **Otros**
  - Llaves: `key_id` (string)
  - Triggers: `event_id` (string)

## 3. Mapeo a código
Para cada tipo, el componente ECS consume las keys de `params`.

```python
# TeleportComponent
class TeleportComponent:
    def __init__(self, params: Dict[str, Any]):
        self.dest_map = params["dest_map"]
        self.dest_x = params["dest_x"]
        self.dest_y = params["dest_y"]

# HealingComponent
class HealingComponent:
    def __init__(self, params: Dict[str, Any]):
        self.amount = params.get("healing", 0)
```

## 4. Guía de extensión
Para añadir nuevos parámetros y comportamientos:
1. Ampliar la definición `#/definitions/params` en `schemas/items/common.json` o crear nuevos esquemas en `schemas/items/`.
2. Definir nuevo ECS Component y System.
3. Actualizar `ItemFactory.create()` para incluir el nuevo componente.
4. Extender el editor de ítems (UI de formulario) para editar los nuevos campos.

## 5. Versionado y migraciones
- Incluir `schema_version` en cada instancia.
- Definir funciones de migración al cambiar estructura de `params`.
- Registrar migraciones en el loader antes de validar/parsear.
