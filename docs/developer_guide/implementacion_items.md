# Plan de implementación: Especificación de Ítems

Este documento describe el roadmap de alto nivel para llevar a producción la guía de ítems (`items.md`). Tras cada paso, el proyecto debe compilar y pasar tests sin errores.

## Requisitos previos
- Dependencias:
  - `jsonschema`, `pydantic`, `pytest`
- Código base debe compilar y los tests actuales pasar.

## 1. JSON Schema y datos de ejemplo
1. Crear `schemas/ItemSchema.json` con la definición completa de campos y validaciones.
2. Generar ejemplos mínimos en `data/items.json` que cumplan el esquema.
3. Validar con:
   ```bash
   jsonschema -i data/items.json schemas/ItemSchema.json
   ```

> Estado tras paso 1: Validación JSON completada sin errores.

## 2. Modelos de datos
1. Implementar `ItemModel` (Pydantic) y `ItemStack` en `components/models.py`.
2. Escribir tests en `tests/test_items.py` para:
   - Validar instanciación de `ItemModel`
   - Reglas de `stackable`, `max_stack`, `threshold`.

> Estado tras paso 2: Tests de modelos pasan correctamente.

## 3. Integración de carga de Ítems
1. Escribir función de carga en `components/models.py`:
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
2. Conectar `icon` o `icon_small`/`icon_large` con el renderer.
3. Pruebas manuales de visualización en el juego.

> Estado tras paso 5: Ítems se muestran correctamente en UI.

## 6. Testing & CI
1. Añadir en CI:
   - Validación de `ItemSchema.json`.
   - Ejecución de `pytest` para `test_items.py`.
2. Pipeline verde garantiza calidad.

> Estado tras paso 6: CI incorpora esquemas y tests de ítems.

## 7. Revisión y Documentación Final
1. Validar alineación de `items.md` con código generado.
2. Ajustar ejemplos y diagramas si hay cambios.
3. Aprobar PR y merge.

> Estado tras paso 7: Documentación de ítems y código en producción.

---
**Fin del plan de implementación**
