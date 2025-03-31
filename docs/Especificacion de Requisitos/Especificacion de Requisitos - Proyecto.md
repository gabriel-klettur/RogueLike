# Especificación de Requisitos - Proyecto RogueLike

## 1. Contexto del Proyecto

Estamos desarrollando un videojuego tipo **Roguelike Top-down** utilizando **Python y Pygame**.
El proyecto se basa en una estructura modular y profesional, y actualmente incluye:

- Movimiento y colisiones del jugador
- Carga y fusión de mapas generados/proporcionados
- Sistema de cámara y zoom
- Soporte multijugador básico (WebSocket)
- Menú interactivo y HUD con indicadores de estado (vida, maná, energía)
- Minimapa
- Cambio de personaje y uso de distintos assets visuales

## 2. Requisitos Generales

- El proyecto debe ser mantenible y modular.
- Se utilizará control de versiones (GitHub) y se fomentará la colaboración profesional.
- Se desarrollará en fases o sprints, definiendo MVPs (productos mínimos viables).

## 3. Requisitos Funcionales Iniciales

### 3.1 Movimiento de Personajes

- Todos los personajes (jugador y NPCs) deben poder moverse inicialmente en **4 direcciones**:

  - Arriba (↑)
  - Abajo (↓)
  - Izquierda (←)
  - Derecha (→)

- Cada dirección tendrá su propio sprite asociado.

- En el futuro, se evaluará la posibilidad de extender a **8** o incluso **16 direcciones** para un movimiento más fluido si la complejidad no afecta el rendimiento o el pipeline de assets.

- Las **interacciones** (ataques, habilidades, detección de objetos o enemigos) serán en **360 grados**, permitiendo un control más preciso y natural del entorno del jugador.

- Para representar la dirección en la que apunta o interactúa cada personaje o NPC, se dibujará un **círculo con una flecha** sobre cada uno, indicando visualmente la orientación actual.

### 3.2 Movimiento de NPCs

- Los NPCs deben tener también movimiento en 4 direcciones.
- Inicialmente se moverán según patrones simples (aleatorio, patrulla, seguir jugador).
- El sistema debe ser extensible para permitir:
  - Movimiento basado en IA.
  - Reacciones ante eventos o colisiones.
  - Interacción con el jugador.

### 3.3 Consola del Cliente

Se implementará una consola en pantalla para mostrar información relevante durante el desarrollo y el juego. Esta consola incluirá:

#### Información del personaje:

- Coordenadas actuales (X, Y)
- Dirección a la que apunta (en grados o cardinal)
- Estado actual (vida, maná, energía)
- Enfriamiento restante para habilidades/restauración
- Velocidad de movimiento
- Sprite o personaje activo
- ID de jugador (en modo multijugador)
- Última acción ejecutada

#### Información del entorno:

- Tile actual bajo el jugador (tipo: piso, muro, etc.)
- Obstáculos cercanos (cantidad o coordenadas)
- Cantidad de NPCs en pantalla
- Entidades remotas visibles (otros jugadores)
- Coordenadas de la cámara (offset_x, offset_y, zoom)

#### Información técnica:

- FPS actual
- Modo de juego (local u online)
- Estado de conexión WebSocket
- ID de conexión y cantidad de entidades sincronizadas
- Logs de eventos recientes (inputs, errores, colisiones, etc.)
- Estado del menú (activo/inactivo)
- Teclas activas en el momento

#### Interacción y feedback:

- Mensajes del servidor (si aplica)
- Confirmaciones de acciones (ataques, restauraciones, etc.)
- Errores de carga
- Cambios de estado importantes (personaje, conexión, etc.)

### 3.4 Multijugador y Topología de Red

- Actualmente el sistema multijugador se basa en una arquitectura **cliente-servidor**, donde el servidor es una instancia externa y los clientes se conectan a él vía WebSocket.

- Para facilitar el desarrollo y pruebas sin necesidad de un servidor dedicado, se implementará un modo en el que **un cliente pueda actuar como servidor ('host')**.

  - Este cliente actuará como punto central de conexión y sincronización.
  - Otros clientes podrán conectarse a este host a través de la red local o mediante IP pública (según configuración).
  - El **cliente que cree la partida o presione el botón de "Hostear" será automáticamente designado como host.**

- En el futuro se migrará a un **servidor dedicado independiente**, pero esta solución inicial permitirá funcionalidad multijugador sin infraestructura adicional.

- La arquitectura debe permitir una transición limpia del modelo host-local a servidor dedicado.

---

## 4. Requisitos No Funcionales

- El juego debe correr a 60 FPS en equipos de gama media.
- El sistema de assets debe ser fácil de mantener y escalar (cambio de personajes, sprites, etc).
- Los sprites deben estar normalizados en tamaño (actualmente 96x128 px).

---

## 5. Preguntas Abiertas para Definir con Mistico

- ✅ Los NPCs deben tener colisiones igual que el jugador. Todos los NPCs deben usar la misma lógica de colisión.

- ✅ Deben tener un sistema de estadísticas similar al jugador:
  - Vida: sí, todos los NPCs deben tenerla.
  - Maná: depende del tipo de NPC.
  - Energía: depende del tipo de NPC.

- Existen diferentes tipos de NPCs, como monstruos, vendedores o NPCs de misiones, por lo que su comportamiento y estadísticas pueden variar.

- 🔄 ¿Se moverán por tiles o libremente como el jugador?
  - A definir, pero se contempla que los NPCs tengan movimiento libre similar al jugador.

- ✅ Queremos animaciones para caminar y para diferentes acciones. Inicialmente se usará una cantidad mínima de animaciones, pero el sistema debe permitir agregar más con el tiempo para lograr un estilo más detallado y dinámico.

- ✅ El jugador puede empujar/interactuar físicamente con NPCs.

- ✅ Los NPCs pueden reaccionar a la presencia del jugador.

- ⏳ ¿Cómo gestionamos la sincronización de entidades cuando un cliente actúa como host?
  - Se definirá cuando se implemente el modo multijugador con cliente como host.

- ✅ ¿Qué cliente puede ser designado como host y cómo se comunica esta decisión?
  - El cliente que cree la partida o presione el botón de "Hostear" será el host.

---

Este documento irá evolucionando a medida que se definan nuevas funcionalidades y detalles técnicos.

