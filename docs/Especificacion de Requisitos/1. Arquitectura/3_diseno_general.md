# 🧠 Diseño General – Proyecto RogueLike

Este documento describe los **principales sistemas y subsistemas** que integran la arquitectura del juego **RogueLike Top-down**, así como su funcionamiento en modo local y multijugador. Sirve como mapa general para entender cómo se conectan los módulos, archivos y conceptos clave.

---

## 🌐 1. Modos de Juego

### 1.1 Modo Local
- Todo el procesamiento ocurre en el cliente.
- Entidades y lógica se manejan de forma autónoma.
- No requiere conexión a Internet.

### 1.2 Modo Multijugador
- Cliente se conecta mediante WebSocket.
- Las entidades remotas se sincronizan desde un servidor (actual o futuro).
- Alternancia de modo en tiempo real desde el menú.
- Sistema de "host local" planificado para MVP.

---

## 🧍 2. Jugador

- Control directo por teclado.
- Movimiento en 360 grados, pero animado solo con **sprites en 4 direcciones cardinales**.
- Usa sprites por dirección (96x128 px por defecto).
- Compuesto por:
  - `PlayerStats`
  - `PlayerMovement`
  - `PlayerRenderer`
- Tiene cooldown de restauración (tecla `Q`).
- HUD ubicado en la parte inferior de la pantalla mostrará:
  - Icono de restauración (cooldown)
  - Espacios para hechizos, habilidades y objetos utilizables (futuro HUD completo).

---

## 👾 3. NPCs y Entidades Remotas

- Compartirán lógica con el jugador (colisiones, stats).
- Movimiento en 360 grados como el jugador, con sprites de 4 direcciones.
- Soportan IA simple o control remoto (jugadores online).
- `RemotePlayer` sincroniza: posición, dirección, PID, stats.

---

## 🗺️ 4. Mapas y Tiles

- Mapa generado proceduralmente con habitaciones conectadas.
- Integración con zonas hechas a mano (lobby).
- Cada tile tiene:
  - Tipo (`#` sólido, `.` transitable)
  - Sprite correspondiente
  - Hitbox si aplica
- Los mapas se cargan desde texto (`map_loader.py`).

### 🧬 Sistema de Capas para Variantes de Tiles (planeado)

Para permitir variantes visuales más complejas (como 100 tipos distintos de muros de piedra), se implementará un sistema en dos capas:

1. **Capa lógica (actual):**
   - `#` = tile sólido
   - `.` = tile transitable
   - Se mantiene para colisiones y lógica base.

2. **Capa visual extendida (futura):**
   - Mapa adicional que define variantes: `W1`, `G2`, `S3`, etc.
   - Se asocia dinámicamente con sprites específicos desde el motor.

**Ejemplo:**

```text
Mapa base:       Mapa extendido:
# . # #          W1 G1 W3 W4
. . # .          G1 G2 W2 G1
```

Esto permite ampliar el detalle visual sin comprometer la lógica del mapa base. Es compatible hacia atrás y escalable a futuro.

---

## 🎥 5. Cámara y Zoom

- Sistema de seguimiento suave centrado en el jugador.
- Permite zoom entre 0.5x y 2.0x.
- `Camera.apply()` y `Camera.scale()` transforman coordenadas y dimensiones.

---

## 🧱 6. Obstáculos y Colisiones

- `Obstacle` tiene sprite, hitbox y posición.
- Sistema unificado de colisiones:
  - Con tiles sólidos (`tile.solid`)
  - Con entidades (jugadores, NPCs, objetos)
- Hitboxes visibles si `DEBUG = True`

---

## 🧭 7. GameState

- Objeto central que contiene todo el estado actual:
  - Jugador
  - Entidades
  - Cámara
  - Tiles
  - Clock
  - Modo de juego (local/online)
  - Conexión WebSocket (si aplica)
- Se pasa entre `handle_events()`, `update()`, `render()`

---

## 🎮 8. Interfaz y Menú

- Menú accesible con `ESC`.
- Opciones:
  - Cambiar personaje
  - Cambiar modo de juego
  - Salir
- Se renderiza como superposición semitransparente.
- Navegación con flechas + `Enter`

---

## 🗺️ 9. Minimap

- Muestra en pequeño los tiles cercanos al jugador.
- Centrado en el jugador.
- Solo renderiza lo visible en la región.
- Ubicado arriba a la derecha.

---

## 🔌 10. Red y Sincronización

- Uso de WebSocket (`websocket-client`)
- Envío de datos cada 0.05s (posición, stats, dirección).
- Recepción de info de otros jugadores (ID único).
- Futuro modo "host-local" actuará como mini-servidor.

---

## 🔁 11. Ciclo Principal del Juego

```python
while game.state.running:
    game.handle_events()    # Entradas del usuario
    game.update()           # Cámara y lógica
    game.render()           # Sprites, UI, HUD, minimapa
    game.state.clock.tick(60)  # Límite de FPS
```

Este bucle central gestiona el flujo del juego, procesando entradas, actualizando el estado y renderizando cada cuadro.

---

## 🎨 12. Detalles del Renderizado

- **Adaptado al zoom:** los elementos gráficos escalan dinámicamente.
- **HUD flotante:** barras de vida, maná y energía se dibujan sobre el personaje.

---

## 🧪 13. Consola de Debug (Planeada)

- Mostrará información útil:
  - FPS
  - Posición jugador
  - Estado de conexión
  - Dirección actual
  - Inputs activos

---

## 🚀 14. Extensiones Futuras del Sistema

- Soporte PvE (enemigos con IA)
- Soporte PvP (combate entre jugadores)
- Progresión y stats avanzados
- Guardado y carga de partidas
- Edición de mapas en tiempo real

---
