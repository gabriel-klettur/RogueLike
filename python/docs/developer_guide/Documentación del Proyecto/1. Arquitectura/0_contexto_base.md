# 🎮 Contexto Base del Proyecto – RogueLike Top-Down

## 🧭 Visión General

Este proyecto es un **videojuego Roguelike en 2D con vista Top-Down**, inspirado en títulos como *The Binding of Isaac*, *Enter the Gungeon* o *Heroes of Hammerwatch*, pero con un enfoque técnico propio, modular y abierto a la expansión.

Queremos construir un sistema robusto y flexible que permita:
- Multijugador online mediante WebSocket.
- Generación procedural de mapas.
- Combate fluido con múltiples estilos (melee, magia, proyectiles).
- Progresión del jugador y de la ciudad.
- Soporte futuro para mods, quests y contenido dinámico.

---

## 💻 Tecnologías principales

| Tecnología        | Propósito                                    |
|------------------|-----------------------------------------------|
| **Python 3**      | Lenguaje principal del proyecto.              |
| **Pygame**        | Motor de renderizado y entrada.               |
| **WebSocket**     | Comunicación entre clientes (modo online).    |
| **SQLite / JSON** | Guardado local y temporal.                    |
| **PyInstaller**   | Empaquetado para distribución en Windows.     |
| **Git + GitHub**  | Control de versiones, issues y documentación. |

---

## 🔥 Metas del Proyecto

1. Crear un MVP jugable con:
   - Movimiento libre y combate funcional.
   - Interfaz completa con HUD y menú.
   - Mapas generados + zona estática de lobby.
   - Modo local + multijugador simple.

2. Iterar hasta construir una **matrix jugable**:
   - Sistema flexible de entidades.
   - IA y NPCs personalizables.
   - Misiones, progresión, economía.
   - Escalabilidad modular.

---

## 🧠 Enfoque de desarrollo

Desarrollo libre y creativo, pero con estructura profesional:
- Ciclos de desarrollo iterativos por funcionalidad.
- Uso de GitHub Projects (Kanban sin fechas).
- Documentación modular y ampliable.
- Testing gradual (manual y automatizado).

---

## 🤝 Equipo y roles

| Nombre        | Rol                             |
|---------------|----------------------------------|
| Gaby          | Desarrollo principal, diseño, arquitectura, documentación. |
| Mistico       | Desarrollo técnico, lógica, investigación, gameplay.       |

Ambos colaboran en decisiones de diseño, testing, commits y documentación.

---

## 📅 Estado actual

✅ Movimiento, cámara y colisiones  
✅ Sprites dinámicos y HUD  
✅ Sistema de cooldowns  
✅ Lobby + generación procedural  
✅ Cliente WebSocket básico  
✅ Multijugador funcional (test)  
🔄 En desarrollo: sistema de combate avanzado, NPCs y progreso

---

## 🔮 Visión futura (ejemplos)

- Modo cooperativo PvE persistente.
- Sistema de habilidades y clases personalizadas.
- Base central (pueblo) que evoluciona con el progreso.
- IA basada en árboles de decisión.
- Misiones generadas o diseñadas por los usuarios.
- Posibilidad de mods (tiles, quests, mapas).

---
