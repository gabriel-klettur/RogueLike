# 📜 19. Quest System – Diseño de Misiones y Flujo de Tareas

## 🎯 Objetivo

- Proveer un sistema **modular y escalable** para misiones.
- Permitir **misiones únicas, repetibles y ramificadas**.
- Integrarse con NPCs, el mundo, la progresión del jugador y la narrativa.

---

## 🧱 1. Tipos de Misiones

| Tipo              | Descripción                                                   |
|-------------------|---------------------------------------------------------------|
| Historia principal | Misiones que avanzan el arco principal del juego.           |
| Secundarias        | Misiones optativas que profundizan el lore o dan recompensas.|
| Diarias / Repetibles| Tareas que pueden completarse múltiples veces.              |
| Por facción / gremio| Asociadas a grupos dentro del mundo.                       |
| Eventos globales    | Misiones activas en todo el servidor o campaña.             |

---

## 🔄 2. Flujo de una Misión

1. **Inicio**: el jugador interactúa con un NPC o evento que inicia la misión.
2. **Condiciones**: se registran los objetivos a cumplir.
3. **Seguimiento**: el juego verifica continuamente el progreso.
4. **Entrega**: una vez cumplidas las condiciones, se otorga la recompensa.
5. **Registro**: se actualiza el historial y desbloquea nuevas posibilidades.

---

## 🗂️ 3. Estructura de Datos (ejemplo JSON)

```json
{
  "id": "quest_001",
  "name": "Recolectar hongos mágicos",
  "description": "Recolecta 10 hongos mágicos del Bosque Sombrío.",
  "objectives": [
    {
      "type": "collect",
      "item": "mushroom_magic",
      "amount": 10
    }
  ],
  "rewards": {
    "xp": 100,
    "gold": 50,
    "items": ["elixir_small"]
  },
  "start_npc": "npc_herbalist",
  "end_npc": "npc_herbalist",
  "repeatable": false
}
```

---

## 🧠 4. Implementación Técnica

| Componente         | Función                                                            |
|--------------------|--------------------------------------------------------------------|
| `quests/`          | Carpeta para lógica, clases y plantillas de misiones.              |
| `data/quests.json` | Archivo con todas las misiones definidas.                          |
| `PlayerQuestLog`   | Clase que gestiona las misiones activas y completadas del jugador. |
| `UIQuestLog`       | Interfaz para ver el progreso de misiones.                         |
| `DialogueEngine`   | Activa misiones desde NPCs y responde según progreso.              |

---

## 🎁 5. Recompensas

| Tipo      | Detalle                                                                 |
|-----------|-------------------------------------------------------------------------|
| XP        | Incremento en el sistema de nivel.                                      |
| Oro       | Se suma al inventario o economía global.                                |
| Objetos   | Armas, pociones, llaves u objetos únicos.                               |
| Acceso    | Desbloquea zonas, NPCs, funciones del pueblo o mejoras.                 |

---

## 📜 6. Diseño Narrativo

- Las misiones deben tener **contexto narrativo y coherencia con el mundo**.
- El jugador debe sentir que sus acciones tienen un **impacto**.
- Se recomienda un documento paralelo: `quests_lore_map.md`.

---

## 🧩 7. Extensiones Futuras

| Sistema                        | Descripción                                                      |
|-------------------------------|------------------------------------------------------------------|
| Árbol de decisiones            | Misiones con múltiples finales o caminos ramificados.           |
| Sistema de reputación          | Acciones que afectan cómo los NPCs ven al jugador.              |
| Elecciones morales             | Alineamientos que cambian recompensas o consecuencias.          |
| Seguimiento histórico global   | Eventos que afectan el mundo persistente.                       |
| Misiones cooperativas          | Misiones en modo multijugador con roles o coordinación.         |

---

## 🤖 8. Generación Procedural e Inteligencia Artificial

En el futuro, se incorporará un sistema avanzado de **generación de misiones automáticas** con capacidad de:

- **Autogenerar misiones** basadas en el progreso del jugador, eventos del mundo y estado global del entorno.
- Utilizar modelos de IA (como LLMs o generadores de texto) para **crear descripciones, ramificaciones narrativas y decisiones** únicas.
- Crear **impacto permanente**: misiones que cambian el mundo, desbloquean regiones o alteran el comportamiento de los NPCs.

### 🔄 Flujo propuesto para misiones generadas dinámicamente:

1. El motor de generación analiza:
   - Nivel del jugador, facción, clase, raza.
   - Eventos recientes (batallas, descubrimientos, elecciones).
   - Estado global del juego (ciudad destruida, invasión en curso, etc.).

2. Se genera una misión como:
   ```json
   {
     "name": "El juramento de las Sombras",
     "objectives": ["Localiza al traidor", "Decide su destino"],
     "rewards": ["Acceso a la Sombra Oculta", "Cambio en reputación con facción X"]
   }
   ```

3. Las decisiones del jugador se almacenan y afectan:
   - La **narrativa** del mundo y su curso.
   - El **desarrollo futuro de otras misiones**.
   - La **disponibilidad de personajes** y zonas.

### 📌 Esto permitirá:

- Juegos altamente rejugables.
- Caminos narrativos únicos por jugador.
- Uso de una **base de datos dinámica (SQLite/MySQL)** para registrar cada decisión e impacto.

