# Plan de Mejora de Rendimiento

Este documento describe un conjunto de estrategias y herramientas para optimizar el rendimiento de la ECS de _RogueLike_. El objetivo es mantener **60 FPS** con decenas o cientos de NPCs, y escalar hasta **1 000 o más**.

---

## Tabla de Contenidos

1. [Perfilado y “Hot Spots”](#1-perfilado-y-hot-spots)
   1. [cProfile](#11-cprofile)
   2. [PyInstrument](#12-pyinstrument)
2. [Mejora de la Arquitectura ECS](#2-mejora-de-la-arquitectura-ecs)
   1. [Diseño Orientado a Datos](#21-diseño-orientado-a-datos-data-oriented-design)
   2. [Bitmasks y Consultas Rápidas](#22-bitmasks-y-consultas-rápidas)
3. [Batch-rendering y Uso de la GPU](#3-batch-rendering-y-uso-de-la-gpu)
   1. [Sprite Sheets e Instancing](#31-sprite-sheets-e-instancing)
   2. [Culling](#32-culling)
4. [Spatial Partitioning Avanzado](#4-spatial-partitioning-avanzado)
5. [Reducción de Sobrecarga en Python](#5-reducción-de-sobrecarga-en-python)
   1. [Cython / PyPy / Numba](#51-cython--pypy--numba)
6. [Paralelismo y Off-loading](#6-paralelismo-y-off-loading)
7. [Pathfinding e IA Masiva](#7-pathfinding-e-ia-masiva)
8. [Librerías y Motores Alternativos](#8-librerías-y-motores-alternativos)
9. [Otras Consideraciones](#9-otras-consideraciones)

---

## 1. Perfilado y “Hot Spots”

Antes de optimizar, identifica los **cuellos de botella** reales.

### 1.1 cProfile (integrado en Python)

- **Ventajas**: parte de la librería estándar, sin dependencias.
- **Uso**:
  ```bash
  python -m cProfile -o profile.prof launcher.py
  ```
- **Análisis**:
  ```python
  import pstats
  p = pstats.Stats("profile.prof")
  p.sort_stats("cumtime").print_stats(20)
  ```
- **Visualización**: con [snakeviz](https://jiffyclub.github.io/snakeviz/) o [gprof2dot](https://github.com/jrfonseca/gprof2dot).

### 1.2 PyInstrument (flame graph)

- **Ventajas**: flame graphs, interfaz interactiva.
- **Instalación**:
  ```bash
  pip install pyinstrument
  ```
- **Ejecución**:
  ```bash
  pyinstrument launcher.py --html
  ```
- **Salida**: HTML interactivo o reporte en consola.

---

## 2. Mejora de la Arquitectura ECS

### 2.1 Diseño Orientado a Datos (Data-Oriented Design)

- Sustituye diccionarios `{eid: Component}` por **arrays contiguos** (`list`, `numpy`, `array`).
- Mejora la _localidad de caché_ y reduce cache misses.
- Evalúa ECS nativos (e.g. **flecs**, **EnTT**) con bindings Python.

### 2.2 Bitmasks y Consultas Rápidas

- Usa **bitsets** (`int`, `bitarray`) para representar la presencia de componentes.
- Agrupa entidades en **archetypes** prefiltrados para consultas O(1).

---

## 3. Batch-rendering y Uso de la GPU

### 3.1 Sprite Sheets e Instancing

- Consolida sprites en **atlases** para reducir switches de textura.
- Con **PyOpenGL** o **moderngl**, envía todos los quads en un solo _draw call_.

### 3.2 Culling

- **View frustum culling**: descarta entidades fuera del campo de visión.
- **Screen culling**: no renderices objetos off-screen.

---

## 4. Spatial Partitioning Avanzado

- Sustituye el grid por **quadtrees** o **BVH** para colisiones y proximidad.
- Actualiza la estructura dinámicamente para mantener O(log n) en consultas.

---

## 5. Reducción de Sobrecarga en Python

### 5.1 Cython / PyPy / Numba

- **Cython**: compila bucles críticos a C y libera la GIL.
- **Numba**: JIT en funciones numéricas para vectorizar loops.
- **PyPy**: posible ganancia en objetos, cuidado con extensiones C.

---

## 6. Paralelismo y Off-loading

- Desacopla IA y lógica no-UI en hilos/procesos (`concurrent.futures`, `multiprocessing`).
- Implementa un _job system_: divide entidades en “chunks” y procesa con thread pool.
- Libera la GIL en código Cython/Numba para true concurrency.

---

## 7. Pathfinding e IA Masiva

- Evita A* completo cada _frame_: usa **flow fields** o **navigation meshes**.
- Calcula caminos incrementalmente o en segundo plano.
- Actualiza IA a 10–15 Hz y usa _interpolation_ para suavizar movimiento.

---

## 8. Librerías y Motores Alternativos

- **Panda3D** (C++ core, binding Python).
- **Godot Engine** (GDScript/C#, ECS en C++).
- **Bevy** (Rust, ECS moderno).
- **Unity ECS** (Entitas-CSharp).
- Núcleo en C++/Rust + scripting Python.

---

## 9. Otras Consideraciones

- **LOD**: simplifica lógica y animación de entidades lejanas.
- **Frame-skipping**: actualiza IA a menor frecuencia y suaviza _interpolation_.
- **Dirty Flags**: renderiza solo lo que cambia en UI/overlays.

---

**Con estas tácticas**, pasarás de decenas a cientos/miles de NPCs a 60 FPS sin sacrificar calidad ni jugabilidad.