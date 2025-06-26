# 🌍 24. World Events – Eventos Globales, Bosses y Dinámica del Entorno

Este documento describe la planificación de los eventos globales del mundo del juego. Estos eventos tienen como objetivo alterar temporal o permanentemente el estado del entorno, presentar desafíos únicos y enriquecer la experiencia narrativa y cooperativa.

---

## 🎯 Objetivo

- Introducir eventos que **afecten a todos los jugadores** o al mundo de forma global.
- Generar **dinámicas emergentes** (cooperación, caos, exploración).
- Conectar el ecosistema del juego con **recompensas únicas, bosses y narrativa**.

---

## 🧩 Tipos de Eventos Globales

| Evento                     | Efecto Principal                                                                 |
|----------------------------|----------------------------------------------------------------------------------|
| 🌋 Erupción volcánica       | Aparecen zonas de lava que impiden el paso. Spawnea enemigos resistentes al fuego. |
| ❄️ Invasión de escarcha     | Ralentiza el movimiento. Congela estructuras. Cambia tiles visuales y mecánicas.  |
| 🧟 Ola de No-Muertos        | Spawn masivo de zombis en regiones específicas.                                |
| 💀 Jefe mundial             | Aparece un boss visible para todos los jugadores online.                       |
| 🌀 Distorsión mágica        | Cambia reglas del juego temporalmente: hechizos más fuertes, sin colisiones, etc.|
| 🌕 Luna roja               | Incrementa agresividad de los enemigos. Mejores drops.                          |

---

## ⏳ Frecuencia y Activación

- **Aleatoria**: Basada en tiempo jugado, regiones visitadas, o progresión.
- **Determinada**: Tras completar misiones clave o ciertos hitos de ciudad.
- **Programada** (modo online): Eventos sincronizados entre jugadores.

---

## 🗺️ Localización

- Pueden ser **globales** (afectan todo el mapa) o **zonales** (ciertas regiones).
- Al acercarse a una zona afectada, aparece una **advertencia visual y sonora**.

---

## 🦴 Eventos de Boss Global

- Requiere colaboración de múltiples jugadores.
- Anunciado globalmente con temporizador.
- Recompensas escaladas por contribución (loot, logros, upgrades).
- Spawn del boss tiene **tiles únicos**, decoraciones y música especial.

---

## 📦 Recompensas por Evento

- Ítems cosméticos
- Recurso de mejora raro
- Nuevos NPCs desbloqueables
- Acceso a nuevas zonas
- Trofeos visuales en la ciudad del jugador

---

## 📜 Integración con la Narrativa

- Algunos eventos están atados al lore (rituales, maldiciones, profecías).
- Eventos recurrentes pueden tener múltiples fases (ej. Ola 1, Ola 2...).

---

## ⚙️ Técnicamente

- Se activa una **flag global** en el GameState.
- Se notifican subsistemas (tiles, enemigos, música, interfaz).
- En modo online: sincronización vía WebSocket → evento único compartido.

---

## 🏆 Eventos Competitivos y Sociales (Futuro)

Se planea incorporar eventos semanales que fomenten la competencia, la cooperación o la estrategia entre jugadores y/o NPCs:

| Tipo de Evento             | Descripción |
|---------------------------|-------------|
| ⚔️ Torneos PvP            | Duelo o batalla entre jugadores en arenas controladas. |
| 🏃‍♂️ Carreras              | Competencias de velocidad, con obstáculos y rutas múltiples. |
| 🎯 Captura la bandera     | Dos bandos compiten por capturar el estandarte enemigo. |
| 👑 Rey de la colina       | Controlar una zona durante un tiempo específico contra oleadas. |
| ⚔️ Facciones/Clanes       | Lucha entre gremios o clases por zonas del mapa o recursos. |

### Modos disponibles:
- Solo NPCs (modo local): El jugador compite contra enemigos IA.
- Híbrido (jugador + aliados IA): Enfrentamientos con equipo de acompañantes.
- Online (jugador vs jugador): Sincronizado entre varios jugadores reales.

Esto generará desafíos semanales únicos, clasificaciones, logros especiales y economía basada en rendimiento dentro del evento.

---

## 🔮 Futuras Extensiones

| Extensión                        | Descripción |
|----------------------------------|-------------|
| 🧠 Eventos adaptativos           | Reaccionan al comportamiento del jugador. |
| 🏙️ Impacto en ciudades           | Cambian precios, NPCs desaparecen o mueren. |
| 🕯️ Eventos con consecuencias      | Permanecen en el mundo (mapas quemados, climas alterados). |
| 🔗 Encadenamiento narrativo      | Un evento lleva a otro, cambiando el rumbo global. |
| 🕳️ Portales temporales           | Zonas alternativas accesibles solo durante eventos. |

---

Este sistema será clave para ofrecer **rejugabilidad infinita**, dar sentido al mundo vivo, y conectar el gameplay con la historia de forma dinámica y coherente.
