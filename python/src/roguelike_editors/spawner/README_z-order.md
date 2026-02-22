# Spawner Editor Z-Order Guide

This document explains how visual layering (what renders above/below) is controlled for the Spawner Editor via `z-order.json`.

## Scope

- Covered by `z-order.json`:
  - Spawner Editor panels (title, toolbars, lists, properties).
  - Spawner Editor overlays (hints, confirmations, visuals picker).
- Not controlled by `z-order.json`:
  - World/ECS overlays (e.g., cyan spawner center/radius). Estos se renderizan antes de la UI del editor por el pipeline global.
  - Nota: `buildings_overlays` del editor se dibuja explícitamente antes de los paneles para garantizar que nunca tape la UI (su `z` en el JSON es informativo).

## Archivo de configuración

Ubicación: `src/roguelike_editors/spawner/z-order.json`

Campos por entrada:
- `z` (entero): prioridad de apilado. Más alto = se dibuja después = más arriba.
- `before` (lista opcional): este elemento debe dibujarse antes de los listados.
- `after` (lista opcional): este elemento debe dibujarse después de los listados.

Reglas:
- Se calcula un orden topológico que respeta `z` y las relaciones `before/after`.
- Si hay ciclos o referencias desconocidas, se registran en log y se cae a un orden estable por `z` (empates conservan el orden actual).

## Elementos del JSON

- `buildings_overlays`
  - Overlays del editor sobre edificios (hover/selección, z-tools, split bar).
  - Siempre se dibuja antes de los paneles para no taparlos. El `z` es informativo.

- `spawner_title`
  - Barra de título del Spawner Editor, anclada arriba a la izquierda.

- `spawner_toolbar`
  - Toolbar principal, renderizada debajo del título (depende de `spawner_title`).

- `spawner_instance_toolbar`
  - Toolbar de instancias (añadir/eliminar), a la derecha de la toolbar principal cuando corresponde.

- `spawner_manager`
  - Panel de templates (manager). Se posiciona a la derecha de la(s) toolbar(s) si está activo.

- `spawner_instances_panel`
  - Panel de lista de instancias. Exclusivo con `spawner_manager` (no se dibujan juntos).

- `spawner_instance_properties_panel`
  - Panel de propiedades de la instancia. Normalmente a la derecha de la lista de instancias.

- `hints_overlay`
  - Overlay de hints/ayudas del editor.

- `zone_change_confirmation`
  - Confirmación de cambio de zona.

- `visuals_picker`
  - Selector de “visuals” (usa el Buildings Picker). Normalmente debe quedar por encima de los paneles relacionados.

- `delete_instance_confirmation`
  - Confirmación de borrado de instancia; típicamente por encima de la lista de instancias.

## Ejemplos

- Subir la confirmación de borrado por encima de todo:
```json
{
  "delete_instance_confirmation": { "z": 999 }
}
```

- Asegurar que el `visuals_picker` nunca tape la confirmación de borrado:
```json
{
  "visuals_picker": { "z": 360, "before": ["delete_instance_confirmation"] }
}
```

- Forzar que la lista de instancias quede debajo del manager si ambos se activaran por error:
```json
{
  "spawner_instances_panel": { "z": 210, "before": ["spawner_manager"] }
}
```

## Consejos de uso

- Preferir ajustar `z` para la mayoría de los casos. Usar `before/after` cuando existe una dependencia fuerte entre dos elementos.
- Evitar ciclos (A after B y B after A). El sistema hará fallback y lo registrará en los logs.
- Los paneles son opacos para ocluir overlays del mundo (cyan). Si algún panel personalizado es translúcido, el cyan podría verse a través.

## Implementación (resumen)

- El orquestador del editor (`views/orchestrator.py`) carga `z-order.json` al iniciar.
- Paneles y overlays del editor se agregan a una lista de "tareas de pintado" con su `z` y se ordenan por topological sort.
- Se preserva la geometría con `last_*_rect` para calcular anclas (la configuración controla apilado, no layout).

## Troubleshooting

- No se aplican mis cambios: valida que el JSON sea correcto (sintaxis) y que los nombres coincidan con los listados arriba.
- Conflictos en `before/after`: revisa los logs; elimina ciclos o referencias desconocidas.
- Cyan por encima de paneles: asegúrate de que el panel afectado use `fill(..., 255)` (opaco) o sube su `z` si fuera overlay.

