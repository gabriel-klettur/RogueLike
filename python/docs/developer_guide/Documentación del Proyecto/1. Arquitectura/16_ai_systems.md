# 🧠 16. AI Systems – Árboles de Decisión, FSM y Comportamiento Enemigo Complejo

Este documento define los sistemas de inteligencia artificial utilizados en el juego, tanto para NPCs hostiles como no hostiles, y su evolución planeada hacia comportamientos realistas y humanos.

---

## 🎯 Objetivo

- Proveer un sistema **modular, escalable y eficiente** para controlar el comportamiento de NPCs.
- Permitir una evolución desde IA básica hacia **IA avanzada con personalidad y aprendizaje**.
- Aplicar múltiples enfoques (FSM, árboles, machine learning) según el tipo de NPC.

---

## 🧩 1. Estructura Modular

Ubicación estimada:

```
entities/npc/ai/
├── base_ai.py              # Interfaz o clase base común
├── fsm.py                  # Máquina de estados finitos
├── decision_tree.py        # Árboles de decisión
├── behavior_tree.py        # Árboles de comportamiento (futuro)
├── learning.py             # IA con aprendizaje (futuro)
```

---

## 🔁 2. Máquina de Estados Finitos (FSM)

Ideal para NPCs simples (enemigos básicos, civiles).

### 📌 Estados posibles:
- `Idle`: no hace nada.
- `Patrolling`: se mueve entre puntos.
- `Chasing`: persigue al jugador.
- `Attacking`: entra en modo ataque.
- `Fleeing`: huye si la vida es baja.
- `Dead`: no realiza acciones.

### 🧠 Ejemplo FSM:
```text
[Idle] → ve al jugador → [Chasing] → cerca → [Attacking] → recibe daño → vida < 20% → [Fleeing]
```

---

## 🌳 3. Árboles de Decisión

Usados para lógica más flexible o con múltiples condiciones:

```text
¿Veo al jugador?
├── No → Patrullar
└── Sí
    ├── ¿Estoy cerca?
    │   ├── Sí → Atacar
    │   └── No → Perseguir
```

Beneficios:
- Fáciles de extender.
- Visuales y comprensibles.
- Perfectos para IA de combate, vendedores, quest givers.

---

## 🧬 4. Árboles de Comportamiento (Futuro)

Pensados para NPCs complejos:

- Permite tareas jerárquicas.
- Controlan múltiples ramas en paralelo (ej. atacar mientras grita).
- Útiles para jefes o NPCs de historia.

---

## 📈 5. Aprendizaje y Adaptación (Futuro largo plazo)

### ✅ Enfoques posibles:

| Tipo                     | Descripción                                                |
|--------------------------|------------------------------------------------------------|
| Estadístico              | Recordar eventos y cambiar comportamiento (por SQLite).    |
| ML Supervisado           | Entrenar bots fuera del juego con logs reales.             |
| ML No Supervisado        | Aprender patrones sin guía directa.                        |
| Reinforcement Learning   | Aprender a maximizar recompensas a través de prueba-error. |

### Ejemplo simple:
> Un NPC recuerda que lo atacaron por la espalda → empieza a patrullar más frecuentemente → pone trampas.

---

## 🧠 6. Simulación de Personalidad

Objetivo: que cada NPC actúe como un **individuo único**.

| Atributo     | Descripción                                      |
|--------------|--------------------------------------------------|
| Agresividad  | Qué tan rápido decide atacar.                    |
| Curiosidad   | ¿Se acerca a investigar sonidos?                 |
| Sociabilidad | ¿Habla con otros NPCs o los ignora?             |
| Memoria      | ¿Recuerda acciones del jugador?                 |
| Fobia        | ¿Evita monstruos, oscuridad, multitudes?        |

Estos rasgos podrían estar almacenados en una tabla SQL o en archivos de configuración por NPC.

---

## 🧪 7. Debug y Visualización de IA

- Modo `DEBUG = True` activa:
  - Líneas de visión.
  - Estado actual del NPC.
  - Árbol de decisión renderizado en consola.
  - Registro de decisiones recientes.

---

## 🛠️ 8. Editor de IA (opcional a futuro)

- Herramienta visual para construir árboles de decisión o FSMs.
- Guardado en JSON/SQL.
- Integración con el editor de mapas o entorno de desarrollo.

---

## 🚧 9. Consideraciones de rendimiento

- Los árboles o FSMs deben evaluarse **cada X ticks**, no cada frame.
- Utilizar **entidades activas** (solo IA de NPCs visibles).
- Priorizar **detección simplificada** si no hay interacción directa.

---