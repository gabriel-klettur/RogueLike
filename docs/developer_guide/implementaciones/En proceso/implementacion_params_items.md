# Especificación de parámetros de ítems

## Objetivo
Detallar formato, tipos, mapeo a código y guía de extensión de los campos `params` en las instancias de ítems.

## 1. Formato de datos
- Ubicación JSON de instancias: `data/map_items.json` o `data/levels/level_X_items.json`.
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
- Validación: crear `data/items/item_params_schema.json` (JSON Schema) y validar al cargar.

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
1. Ampliar JSON Schema (`item_params_schema.json`).
2. Definir nuevo ECS Component y System.
3. Actualizar `ItemFactory.create()` para incluir el nuevo componente.
4. Extender el editor de ítems (UI de formulario) para editar los nuevos campos.

## 5. Versionado y migraciones
- Incluir `schema_version` en cada instancia.
- Definir funciones de migración al cambiar estructura de `params`.
- Registrar migraciones en el loader antes de validar/parsear.
