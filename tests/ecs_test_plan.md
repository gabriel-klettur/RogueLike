# Plan de pruebas para ECS

Este documento describe el plan de alto nivel para implementar pruebas en el módulo `ecs`.

---

## Fase 1: Tests unitarios

**Objetivo:** comprobar la lógica aislada de componentes, manager y sistemas.

1. **ECSManager**
   - `create_entity()`, `add_component()`, `remove_component()`, `get_components()`
   - filtrado con `get_entities_with([A, B])`
2. **Componentes**
   - inicialización, valores por defecto, validaciones de datos (e.g. posición, estadísticas)
3. **Sistemas individuales**
   - *MeleeCombatSystem.update* → daño en rango, fuera de rango, valores límite
   - *HitboxDebugSystem.update* → genera debug solo si hay hitboxes
   - resto de sistemas (movimiento, IA, colisiones)

**Estrategia:** usar pytest con fixtures para un `ECSManager` limpio y entidades de prueba.

---

## Fase 2: Tests de integración

**Objetivo:** validar la interacción entre múltiples sistemas.

1. Montar un «mundo» con varios sistemas activos
2. Ejecutar un tick completo (`update_all`) y verificar efectos encadenados:
   - movimiento → colisión → combate → muerte
3. Uso de mocks para subsistemas externos (gráficos, I/O)

---

## Fase 3: Tests end-to-end / Smoke

**Objetivo:** asegurar que la aplicación arranca y responde correctamente.

1. Inicializar juego en modo prueba con un nivel simple
2. Simular inputs (movimiento, ataque)
3. Verificar estado final (posición, HP, existencia de entidades)

---

**Notas adicionales:**
- Definir convenciones de nombre para archivos de test.
- Integrar Hypothesis para tests basados en propiedades (opcional).
- Ejecutar `pytest --maxfail=1 --disable-warnings -q` como parte del CI.
