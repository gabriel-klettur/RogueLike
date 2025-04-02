# 🌐 8. Networking y Sincronización Multijugador – RogueLike Top-down

Este documento describe la arquitectura de red utilizada para el modo multijugador, basada en **WebSockets**. Se detallan los componentes del cliente, la estructura de datos, el flujo de sincronización, y los planes a futuro para migrar a una arquitectura más robusta.

---

## 📡 1. Arquitectura Cliente-Servidor

- Modelo actual: **Cliente-Servidor temporal**, donde el servidor puede ser otra instancia del cliente (modo host).
- Cada jugador se conecta a través de **WebSocket** a un host remoto o local.
- El juego cliente envía su estado al servidor, y este reenvía el estado de todos los jugadores conectados.

### Ventajas actuales

✅ Simplicidad para pruebas locales y en LAN.  
✅ Escalabilidad futura sin reescribir la lógica del cliente.  
✅ Permite múltiples estilos: cooperativo, PvP, PvE, y modos asíncronos en el futuro.

---

## 🔄 2. Ciclo de Sincronización

### 📤 Cliente envía (cada 0.05 segundos si hay cambios):

```json
{
  "id": "abc123",
  "x": 600,
  "y": 700,
  "character": "first_hero",
  "direction": "up",
  "health": 88,
  "mana": 40,
  "energy": 85
}
```

### 📥 Servidor reenvía a todos los clientes:

```json
{
  "uuid1": {
    "x": 600,
    "y": 700,
    "character": "first_hero",
    "direction": "up",
    "health": 88,
    "mana": 40,
    "energy": 85
  },
  "uuid2": {
    "x": 800,
    "y": 600,
    "character": "valkyria",
    "direction": "down",
    "health": 90,
    "mana": 50,
    "energy": 100
  }
}
```

---

## ⚙️ 3. Componentes del Módulo de Red

| Archivo                             | Propósito                                          |
|------------------------------------|----------------------------------------------------|
| `network/client.py`                | Lógica de conexión, envío y recepción por socket. |
| `entities/remote_player/base.py`   | Entidad visual para representar a otros jugadores.|
| `core/game/game.py`                | Invoca renderizado y acceso a jugadores remotos.  |

---

## 🧠 4. Identificación de Jugadores

- Cada cliente genera un `UUID` al iniciar sesión (ej. `"abc123"`).
- Se incluye en todos los paquetes enviados.
- En el render, se muestra sobre el personaje como `abc123[:6]`.

---

## 📥 5. Recepción de Datos

- Se reciben todos los jugadores conectados.
- Se eliminan jugadores no presentes.
- Se actualiza la lista `remote_players` en tiempo real.
- Se ignora el propio `UUID` para no sobrescribirse.

---

## 🧵 6. Multithreading en Cliente

- Se usan **dos hilos**:
  - Uno para `send_loop`.
  - Otro para `receive_loop`.
- Ambos son `daemon` para evitar bloqueo del juego.
- La conexión se mantiene asincrónica, fluida y separada del render principal.

---

## 🔁 7. Flujo del modo online

```text
Jugador inicia juego
 ⮕ Detecta modo online
     ⮕ Conecta vía WebSocket
         ⮕ Envía estado actual
             ⮕ Recibe estados de otros jugadores
                 ⮕ Instancia o actualiza RemotePlayer
                     ⮕ Renderiza en pantalla
```

---

## 🧪 8. Uso en el Game Loop

Desde `game.py`:

```python
for pid, data in client.remote_players.items():
    if pid != client.id:
        remote_player = RemotePlayer.from_data(data)
        remote_player.render(screen, camera)
```

---

## 🛡️ 9. Consideraciones de Seguridad y Validación (Futuro)

| Mejora | Motivo |
|--------|--------|
| 🔁 Reconexión automática | Para evitar desconexión permanente en partidas largas. |
| 🔑 Autenticación básica | Evitar suplantaciones si se hace público. |
| 🛡️ Ignorar `self.id` al reconstruir | Evitar sobrescribir el jugador local. |
| 🚫 Validación de datos | No permitir stats inválidos, coordenadas absurdas, etc. |
| 🧾 Log de eventos | Para debug, repetición, o historial de eventos en red. |

---

## 🌐 10. Migración a Servidor Dedicado

La arquitectura está preparada para:

- Reemplazar al cliente host por un **servidor WebSocket separado**.
- Ejecutarlo en VPS o servicios como AWS.
- Soportar múltiples salas, persistencia o login.

---

## 🧩 11. Debug y Visualización (modo `DEBUG = True`)

- Mostrar `uuid[:6]` sobre los jugadores.
- Renderizar stats flotantes (vida, maná, energía).
- Imprimir en consola paquetes enviados/recibidos.
- Mostrar errores de conexión.

---

