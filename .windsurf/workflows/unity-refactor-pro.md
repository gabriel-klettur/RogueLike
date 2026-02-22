---
description: principal unity architectural refactor workflow
---
# Objetivo
Actuar como arquitecto principal para diagnosticar y refactorizar el proyecto Unity (roguelike) garantizando legibilidad, escalabilidad y modularidad de nivel AAA.

## 0) Preparación obligatoria
1. Leer `.windsurf/rules/workspace-engineering-rules.md` y `.windsurf/rules/unity-workspace-rules.md`.
2. Confirmar rama limpia o crear rama `feature/refactor/<topic>`.
3. Registrar métricas iniciales: clases/archivos >300 líneas, métodos >30 líneas, singletons y MonoBehaviours críticos.

## 1) Alcance y criterios de éxito
1. Definir alcance concreto de la refactorización (subsistema o feature).
2. Establecer criterios de aceptación técnicos: reducción de líneas, separación de capas, ausencia de lógica de dominio en MonoBehaviours.
3. Identificar riesgos/regresiones potenciales (combate, IA, loot, UI, generación procedural).

## 2) Auditoría arquitectónica
1. Mapear dependencias actuales entre Gameplay, UI, Systems, Data.
2. Detectar God Objects, Update abuse, lógica UI mezclada con dominio, dependencias circulares.
3. Medir complejidad ciclomática en scripts clave (combate, habilidades, IA, dungeon generation).
4. Catalogar violaciones de reglas de tamaño (archivos/métodos) y priorizarlas.

## 3) Diseño objetivo
1. Trazar diagrama de capas propuesto (Core, Features, Systems, Presentation, Infrastructure, Data, Bootstrap) y justificar cada carpeta.
2. Asignar responsabilidades únicas por clase; definir nuevas clases si es necesario.
3. Seleccionar patrones (State para IA/jugador, Strategy para habilidades, Observer/EventBus para daño/muerte, Factory para enemigos/loot, Command para input, Object Pooling para proyectiles, DI para wiring).
4. Documentar flujo de datos desde entrada hasta rendering para el subsistema.

## 4) Plan de refactorización incremental
1. Orden recomendado:
   1. Extraer lógica de dominio a clases puras en `Scripts/Core`.
   2. Introducir interfaces y eventos desacoplados.
   3. Reorganizar features en `Scripts/Features/<Feature>` respetando composición.
   4. Ajustar Systems (Save, EventBus, Pooling) y Infrastructure (factories, DI).
   5. Alinear Presentation/UI como adaptadores del dominio.
2. Dividir archivos grandes >400 líneas en módulos pequeños; documentar nuevos nombres.
3. Limitar métodos a 20 líneas (30 máx) reubicando lógica en helpers o servicios.

## 5) Implementación
1. Trabajar por feature: combate, habilidades, IA, loot, progresión, generación procedural.
2. Para cada feature:
   - Crear servicios de dominio (pure C#) y adapters MonoBehaviour.
   - Eliminar `Update` innecesarios; usar eventos/coroutines.
   - Sustituir `Find()/GetComponent` repetitivos por referencias inyectadas.
   - Implementar pooling para objetos temporales.
3. Mantener pruebas/manual testing: combate básico, habilidades, IA, loot drop, dungeon run.

## 6) Validación
1. Ejecutar matriz de verificación:
   - Combate (OnHit/OnDeath events disparan correctamente).
   - Habilidades (Strategy + efectos temporales).
   - IA (State Machine respondiendo a eventos).
   - Generación procedural (modular, sin dependencias UI).
   - Loot/progresión (data vs ejecución separadas).
2. Revisar GC allocations y LINQ en runtime crítico; usar perfiles de Unity.
3. Confirmar que MonoBehaviours solo coordinan y no contienen lógica de dominio.

## 7) Documentación y handoff
1. Actualizar README o docs con nueva estructura y patrones aplicados.
2. Registrar lista de problemas detectados, justificación técnica y refactors realizados (incluye before/after snippets).
3. Entregar plan paso a paso para siguientes refactors pendientes y nivel de madurez (Junior/Mid/Senior/Production-ready).
4. Crear backlog de tareas futuras con orden óptimo y riesgos.

## Anexo A - Prompt completo de referencia
Usar este prompt literal cuando se necesite una evaluación arquitectónica completa:

```
Actúa como un Principal Unity Software Architect con experiencia en estudios AAA y en desarrollo de roguelikes complejos.

Quiero que analices mi proyecto de Unity y propongas una refactorización completa enfocada en:

- Legibilidad extrema
- Escalabilidad real a largo plazo
- Robustez
- Modularización estricta
- Arquitectura profesional
- Separación clara de responsabilidades
- Preparación para crecimiento del proyecto

El proyecto es un roguelike con:
- Generación procedural
- Sistema de combate
- IA de enemigos
- Sistema de habilidades
- Loot
- Progresión
- Posibles expansiones futuras

-------------------------------------
REGLAS ESTRICTAS DE CALIDAD DE CÓDIGO
-------------------------------------

Evalúa y fuerza estas reglas:

1. Ningún archivo debe superar:
   - Ideal: 200–300 líneas
   - Máximo absoluto: 400 líneas

2. Ningún método debería superar:
   - Ideal: 10–20 líneas
   - Máximo absoluto: 30 líneas

3. Una clase = una única responsabilidad clara.

4. Si una clase hace más de una cosa:
   - Divídela en múltiples archivos.
   - Propón nombres coherentes y profesionales.

5. MonoBehaviours deben:
   - Ser lo más delgados posible.
   - Actuar como adaptadores entre Unity y el dominio.
   - No contener lógica compleja de negocio.

6. La lógica de juego debe vivir en clases puras de C# cuando sea posible.

-------------------------------------
ANÁLISIS ARQUITECTÓNICO
-------------------------------------

1. Detecta:
   - God Objects
   - Alto acoplamiento
   - Dependencias circulares
   - Violaciones SOLID
   - Lógica de dominio dentro de MonoBehaviours
   - Lógica mezclada con UI
   - Uso excesivo de Update
   - Código duplicado
   - Responsabilidades mezcladas

2. Evalúa complejidad ciclomática.
3. Señala archivos que deberían dividirse por tamaño o responsabilidad.

-------------------------------------
ARQUITECTURA OBJETIVO
-------------------------------------

Propón una arquitectura profesional usando:

- SOLID
- Clean Architecture adaptada a videojuegos
- Separación por capas
- Arquitectura orientada a features (Feature-based)
- Event-driven design cuando sea apropiado
- Composición sobre herencia

Estructura recomendada de alto nivel:

Scripts/
│
├── Core/                (Dominio puro, sin Unity)
│   ├── Combat/
│   ├── Characters/
│   ├── Abilities/
│   ├── Stats/
│   ├── Progression/
│
├── Features/
│   ├── Combat/
│   ├── Abilities/
│   ├── Enemies/
│   ├── Loot/
│   ├── DungeonGeneration/
│
├── Systems/
│   ├── SaveSystem/
│   ├── EventBus/
│   ├── ObjectPooling/
│
├── Presentation/
│   ├── UI/
│   ├── VFX/
│   ├── Animation/
│
├── Infrastructure/
│   ├── DependencyInjection/
│   ├── Factories/
│
├── Data/
│   ├── ScriptableObjects/
│   ├── Configs/
│
└── Bootstrap/

Explica por qué cada parte vive donde vive.

-------------------------------------
PATRONES A EVALUAR
-------------------------------------

Indica dónde aplicar correctamente:

- State Pattern (IA y estados del jugador)
- Strategy Pattern (habilidades)
- Observer / Event Bus (daño, muerte, eventos globales)
- Factory Pattern (enemigos, loot)
- Command Pattern (input desacoplado)
- Object Pooling (proyectiles, enemigos)
- Dependency Injection (manual o Zenject si aplica)

-------------------------------------
ROGUELIKE - CONSIDERACIONES ESPECÍFICAS
-------------------------------------

Evalúa especialmente:

1. Sistema de combate desacoplado.
2. Sistema de habilidades basado en composición.
3. Sistema de efectos temporales (buffs/debuffs).
4. Generación procedural modular.
5. Separación entre definición de datos (ScriptableObject) y ejecución.
6. Event system para:
   - OnHit
   - OnDeath
   - OnAbilityCast
   - OnRoomCleared

-------------------------------------
OPTIMIZACIÓN Y ROBUSTEZ
-------------------------------------

Evalúa:

- Uso innecesario de GC allocations.
- Uso indebido de LINQ en runtime crítico.
- Find() o GetComponent repetidos.
- Lógica pesada en Update.
- Falta de pooling.
- Falta de separación entre lógica determinista y visual.

-------------------------------------
QUÉ ESPERO EN TU RESPUESTA
-------------------------------------

1. Lista de problemas detectados.
2. Justificación técnica.
3. Propuesta de refactorización.
4. Ejemplos de código antes/después.
5. División recomendada de archivos grandes.
6. Plan paso a paso para refactorizar sin romper el proyecto.
7. Orden óptimo de refactorización.
8. Nivel de madurez arquitectónica actual del proyecto (Junior / Mid / Senior / Production-ready).

Responde como un arquitecto senior real, no como un tutorial básico.
```
