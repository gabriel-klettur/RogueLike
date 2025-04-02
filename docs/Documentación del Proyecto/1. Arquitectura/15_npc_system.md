
# 👥 15. NPC System – Tipos, IA, Diálogos y Comportamiento Humano

Este documento describe el sistema actual y futuro de los NPCs (personajes no jugables), su estructura modular, lógica de IA, interacción y posibles extensiones hacia NPCs con **comportamientos humanos avanzados**.

---

## 🎯 Objetivo

- Crear NPCs versátiles: **hostiles, neutrales y aliados**.
- Permitir IA con comportamientos realistas.
- Integrarlos en la historia, mundo y economía del juego.

---

## 🔰 1. Estado Actual

- Actualmente no hay NPCs implementados.
- El sistema está preparado para integrarlos fácilmente.
- Compartirán sistemas de colisión, renderizado y estados con el jugador.

---

## 🧠 2. Tipos de NPCs Planeados

| Tipo            | Rol Esperado                                              |
|-----------------|-----------------------------------------------------------|
| 🧟 Enemigos      | Hostiles con IA de combate                               |
| 🧙‍♂️ Vendedores    | Comerciar objetos, economía ligada al mundo             |
| 📜 Quest Givers  | Ofrecer misiones y narrativa                             |
| 🧍 Civiles       | Ambiente y respuestas contextuales                        |
| 🛡️ Aliados        | Asisten en combate o tareas                             |

---

## 🧱 3. Arquitectura Modular

Ubicación estimada:
```
entities/npc/
├── base.py               # Clase común para todos los NPCs
├── enemy.py              # IA hostil
├── vendor.py             # Comercio
├── quest_giver.py        # Diálogos y misiones
└── ai/                   # Lógica avanzada de comportamiento
    ├── state_machine.py
    └── decision_tree.py
```

---

## 🧭 4. Movimiento y Renderizado

- El movimiento será **en 360 grados**.
- Los NPCs tendrán sprites de **4 direcciones** (`up`, `down`, `left`, `right`).
- Se utilizará interpolación de dirección para seleccionar el sprite más cercano.
- El sistema de render respetará el zoom y la escala dinámica.

---

## 🤖 5. IA Hostil – Plan Inicial y Futuro

### 🎮 Fase 1: IA Básica
- Patrullaje aleatorio o estático.
- Detección del jugador por proximidad.
- Ataque simple al contacto.

### 🎮 Fase 2: IA Media
- Huida si la vida < X%.
- Selección de objetivos.
- Uso de habilidades o rangos.

### 🤯 Fase 3: IA Avanzada
- Árboles de decisión (`DecisionTree`) o `FSM`.
- Reacciones al entorno: ruido, luz, enemigos, aliados.
- Cobertura, emboscadas, formaciones.

### 🤖 Fase 4: IA "Humana" (larga escala)
- **Aprendizaje local** (machine learning simple).
- Comportamiento adaptativo basado en historial del jugador.
- Comunicación entre NPCs.
- Personalidad individual (temeroso, agresivo, social, curioso).
- Simulación de recuerdos o emociones (usando SQLite o Redis como respaldo).

---

## 💬 6. Sistema de Diálogos

- Cada NPC puede iniciar un diálogo con opciones múltiples.
- Se utilizará SQLite en modo local y MySQL en modo multijugador.
- Los diálogos tendrán estructura jerárquica:
  - Entrada → Pregunta → Opciones → Respuestas → Consecuencias
- Ejemplo de estructura:

```json
{
  "npc_id": "vendor_001",
  "dialogue": [
    {
      "question": "¿Qué deseas comprar?",
      "options": ["Espadas", "Pociones", "Salir"]
    }
  ]
}
```

---

## 🛒 7. Economía y Vendedores

- Cada vendedor tendrá un **inventario limitado** y un fondo de **oro personal**.
- En el futuro se implementará una **economía global dinámica**:

### 💱 Economía Dinámica (Futuro)
- Los precios fluctúan según la **cantidad de oro circulante en el mundo**.
- Si los jugadores acumulan mucho oro:
  - Suben los precios (inflación).
- Si hay poco oro:
  - Los vendedores ofrecen mejores precios (deflación).
- Esta economía se gestionará en **SQLite (local)** o **MySQL (online)**.
- Posibilidad de implementar una **reserva central de economía** en el servidor.

---

## 🧪 8. Estado y Reacción del NPC

| Estado         | Reacción                                                    |
|----------------|-------------------------------------------------------------|
| Neutral        | Solo responde si se interactúa                              |
| Hostil         | Ataca al jugador al verlo                                   |
| Temeroso       | Huye si está herido o rodeado                               |
| Aliado         | Protege al jugador y se mueve en grupo                      |

---

## 🔮 9. Extensiones Futuras del Sistema de NPCs

- ✅ Interacciones emocionales (amistad, rivalidad, miedo)
- ✅ Eventos dinámicos (NPC secuestrado, atacado, asesinado)
- ✅ Reputación del jugador impacta cómo te hablan
- ✅ Participación en la economía del juego
- ✅ Posibilidad de formar gremios con NPCs
- ✅ Misiones específicas de cada NPC
- ✅ Personalidades únicas persistentes
- ✅ Guardado de historial de conversaciones

---
