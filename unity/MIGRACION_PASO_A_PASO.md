# Migracion Python -> Unity: plan operativo en 50 pasos

Este documento es una guia accionable basada en `unity/README.md`.
Se eligieron **50 pasos** (en lugar de 20) porque buscas una migracion total: codigo, datos, herramientas y **todos los assets**.

Objetivo:

- mantener paridad funcional,
- migrar datos y assets sin perdida,
- preparar una base robusta y escalable para evolucion futura.

---

## Entregables obligatorios

1. Proyecto Unity 2D URP estable.
2. Vertical slice jugable.
3. Port completo de sistemas core.
4. Migracion de assets con trazabilidad (Asset Map).
5. Compatibilidad de guardados o migrador de saves.
6. Pipeline de pruebas y build.

---

## Fase 0 - Preparacion y baseline (Pasos 1-6)

1. Congela un commit baseline del proyecto Python (`python/`) para tener referencia estable.
2. Define KPI base de comparacion: FPS medio, frame time p95, tiempo de carga y uso de RAM.
3. Crea una lista de flujos criticos jugables: mover, atacar, castear, lootear, morir, respawn, guardar/cargar.
4. Registra evidencias del baseline (video corto + capturas + logs de perfil).
5. Crea una matriz de paridad funcional (feature Python -> feature Unity).
6. Firma criterios de aceptacion de migracion completa con el equipo.

## Fase 1 - Bootstrap tecnico Unity (Pasos 7-12)

7. Crea proyecto nuevo usando template **Universal 2D** (URP con renderer 2D).
8. Define estructura de carpetas alineada con `unity/README.md` (`Assets/_Project/...`).
9. Instala y fija paquetes: Input System, Cinemachine, 2D Tilemap, TextMeshPro, Addressables, Test Framework.
10. Configura Project Settings: Input System nuevo, Time, Physics2D layers, URP 2D Renderer, Quality tiers.
11. Crea escenas `Bootstrap` y `MainGameplay`, y configura `Bootstrap` como escena inicial.
12. Crea asmdefs por capas: Core, Gameplay, Data, Infrastructure, UI, Tests.

## Fase 2 - Mapa de assets y pipeline de importacion (Pasos 13-22)

13. Inventaria `python/assets` y clasifica por tipo: sprites, tiles, UI, VFX, audio, fuentes.
14. Crea el archivo maestro `asset_map.csv` (o `asset_map.json`) con columnas minimas:
    - asset_id
    - source_path_python
    - target_path_unity
    - asset_type
    - pixels_per_unit
    - pivot
    - filter_mode
    - compression
    - atlas_group
    - addressable_key
    - owner_system
    - migration_status
15. Define convencion de nombres unica para assets y prefabs (sin duplicados ambiguos).
16. Define politica de pivots por categoria (personajes, tiles, props, UI).
17. Define politica PPU por categoria para evitar escalas inconsistentes.
18. Define politica de SpriteAtlas (grupos por dominio: player, npc, environment, ui).
19. Implementa `AssetPostprocessor` en Unity para aplicar reglas de importacion automaticamente.
20. Migra un lote pequeno (5-10%) y valida visualmente pivots, sorting y calidad.
21. Ajusta reglas de importacion segun hallazgos y vuelve a correr el lote.
22. Ejecuta migracion completa de assets usando el `asset_map` como fuente de verdad.

## Fase 3 - Contratos de datos y migradores (Pasos 23-30)

23. Inventaria JSONs de `python/data` y etiqueta cada archivo con `schema_version`.
24. Define DTOs C# para cada dominio: buildings, particles, lights, inventory, drops, save metadata, npc memory.
25. Implementa validadores de schema (fallar rapido en caso de incompatibilidad).
26. Implementa mappers DTO -> modelos runtime internos (sin acoplar gameplay a JSON crudo).
27. Implementa migradores versionados (v1 Python -> v2 Unity) con logs de conversion.
28. Construye pruebas golden de datos: mismo input produce mismo estado esperado.
29. Implementa reporte de conversion con conteos (ok, warning, error) por archivo.
30. Crea modo `dry-run` de migracion de datos para validar sin tocar estado final.

## Fase 4 - Vertical slice minimo (Pasos 31-36)

31. Implementa player movement + colision contra mundo.
32. Implementa camara de seguimiento con Cinemachine.
33. Implementa tilemap base y orden de render Y/Z equivalente.
34. Implementa 1 NPC con FSM minima (Idle, Chase, Attack).
35. Implementa 1 habilidad/proyectil de punta a punta.
36. Implementa save/load minimo (posicion, HP, inventario basico) y valida 10 minutos jugables.

## Fase 5 - Port completo de gameplay y ECS (Pasos 37-44)

37. Define estrategia final de simulacion: DOTS o ECS custom C# (decidir una sola y documentar).
38. Porta sistemas de input y movimiento en el mismo orden de actualizacion del origen.
39. Porta combate base: melee, cooldowns, damage, death.
40. Porta sistemas de spells y VFX asociados con pooling.
41. Porta IA/FSM y comportamiento de spawner runtime.
42. Porta inventario, pickups, drops y reglas de consumo/transferencia.
43. Porta overlays/HUD esenciales para gameplay (barras, target, mensajes).
44. Cierra brechas de paridad funcional detectadas en la matriz de paridad.

## Fase 6 - Herramientas, editores y flujo de contenido (Pasos 45-47)

45. Define que herramientas seran runtime UI y cuales seran EditorWindow (Unity Editor).
46. Implementa tools de autoria prioritarias: spawner placement, tuning NPC/spells, validacion de mapa.
47. Implementa validadores de contenido previos a build (assets faltantes, referencias rotas, addressables invalidos).

## Fase 7 - Persistencia final, rendimiento y release (Pasos 48-50)

48. Implementa save system final con backups rotativos y recuperacion ante corrupcion.
49. Ejecuta hardening: profiling CPU/GPU/GC, soak tests 30-60 min, optimizaciones de pooling/culling.
50. Configura pipeline de CI/CD (build + EditMode + PlayMode smoke), genera build release y checklist final de salida.

---

## Checklist de control rapido (debe quedar en verde)

- [ ] Paridad funcional core validada.
- [ ] Asset map completo y actualizado.
- [ ] 100% assets migrados o reemplazos documentados.
- [ ] Migradores de datos versionados y probados.
- [ ] Save/load estable con backups.
- [ ] Sin errores bloqueantes en smoke tests.
- [ ] Rendimiento aceptable frente al baseline.
- [ ] Build reproducible desde CI.

---

## Recomendaciones de gobernanza tecnica

1. No mezclar UI con logica de dominio.
2. No leer JSON directamente desde sistemas de gameplay.
3. No permitir imports de capa Infrastructure hacia Core.
4. No migrar por archivos sueltos: migrar por capacidades completas.
5. Cada paso cerrado debe dejar evidencia (PR, test, perfil, video corto).

Con esta guia puedes ejecutar la migracion de forma controlada, profesional y escalable, alineada al marco definido en `unity/README.md`.
