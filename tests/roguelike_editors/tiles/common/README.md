# Test Suite for Tiles Editor Common Module

Este directorio contiene tests de pytest para los componentes comunes del editor de tiles.

## test_controller.py
Pruebas para la función `flood_fill` del módulo `controller`:

- `test_fill_single_cell`: Verifica que solo la celda inicial con valor objetivo sea remplazada.
- `test_fill_connected_region`: Comprueba que se rellene toda la región conectada de celdas con el valor objetivo.
- `test_no_fill_if_start_not_target`: Asegura que no se modifique la matriz si la celda inicial no coincide con el valor objetivo.
- `test_fill_with_same_replacement`: Verifica que no haya cambios si el valor de reemplazo es igual al objetivo.
- `test_fill_entire_matrix`: Comprueba el relleno completo cuando toda la matriz coincide con el objetivo.

## test_events.py
Pruebas para la función `cycle_enum` del módulo `events`:

- `test_cycle_forward`: Ciclo hacia adelante a un siguiente miembro del Enum.
- `test_cycle_wrap_forward`: Ciclo que envuelve desde el último miembro al primero.
- `test_cycle_backward`: Ciclo hacia atrás a un miembro anterior.
- `test_cycle_with_large_delta`: Verifica comportamiento con deltas mayores al tamaño del Enum (modular).
- `test_invalid_current`: Asegura que lance `ValueError` si el miembro actual no pertenece al Enum.

## test_state.py
Pruebas para la función `deep_copy_state` del módulo `state`:

- `test_deep_copy_nested_structures`: Verifica copia profunda de estructuras anidadas (dict, list).
- `test_deep_copy_custom_object`: Comprueba copia profunda de instancias de objetos personalizados.
- `test_deep_copy_simple_types`: Asegura que tipos simples (int, str, float, None) se copien correctamente.

## test_view.py
Pruebas para las funciones de conversión de coordenadas en el módulo `view`:

- `test_screen_to_world_identity`: Validación sin transformación (zoom=1, offset=0).
- `test_screen_to_world_scale_and_offset`: Verifica aplicación de zoom y desplazamientos.
- `test_world_to_tile_basic`: Comprueba conversión de coordenadas exactas a índices de tile.
- `test_world_to_tile_flooring`: Asegura truncamiento de coordenadas no enteras.
- `test_screen_to_tile_integration`: Integración de `screen_to_world` y `world_to_tile`.

---

Para ejecutar todos los tests:

```bash
pytest tests/roguelike_editors/tiles/common -q
```
