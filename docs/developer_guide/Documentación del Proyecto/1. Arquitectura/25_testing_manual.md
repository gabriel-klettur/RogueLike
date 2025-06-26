# 🧪 25. Testing Manual – Casos de Prueba por Sistema

Este documento proporciona una guía organizada para validar manualmente las funcionalidades principales del juego, asegurando estabilidad y detección temprana de errores.

---

## 🎯 Objetivo

- Probar funcionalidades clave del juego.
- Identificar errores visuales, lógicos y de rendimiento.
- Validar nuevos sistemas antes de integración completa.

---

## ✅ Checklist Global

| Área                         | Ítem a probar                                                                 |
|-----------------------------|-------------------------------------------------------------------------------|
| 🎮 Control del jugador       | Movimiento, animaciones, cambio de personaje                                 |
| 🗺️ Mapas                    | Carga, generación procedural, fusión con mapas hechos a mano                 |
| 🧱 Colisiones                | Con obstáculos, límites del mapa y otros jugadores                           |
| 🎥 Cámara                   | Seguimiento suave, zoom, transformaciones                                   |
| 🧍‍♂️ Jugadores remotos       | Renderizado, sincronización de dirección y stats                            |
| 🧠 IA básica NPCs            | Movimiento, detección del jugador, reacciones                              |
| 🔁 Game Loop                | Fluidez de frames, sin congelamientos                                       |
| 💬 Menú y HUD               | Accesibilidad, cambios visuales, interacciones                             |
| 🌐 Networking               | Conexión, desconexión, actualización de datos                              |
| 🔄 Recarga                  | Cambiar de personaje, volver al menú, reiniciar                             |

---

## 🧪 Casos por Sistema

### 🧍 Jugador

- Mover en las 8 direcciones.
- Ver que el sprite cambia correctamente.
- Usar tecla Q y comprobar restauración.
- Recibir daño (`.take_damage()`).

### 🎮 Input y Eventos

- ESC abre/cierra el menú.
- Enter cambia personaje.
- Flechas seleccionan opción correctamente.

### 🧱 Obstáculos y Colisiones

- Chocar contra objetos (`Obstacle`).
- Intentar caminar sobre tile sólido (`#`) y comprobar bloqueo.
- Deslizar cerca de un borde para comprobar límites precisos.

### 🗺️ Mapa

- Generación aleatoria sin errores.
- Carga del mapa lobby manual correctamente.
- Inserción visual y lógica coherente.

### 🎥 Cámara

- Sigue al jugador fluidamente.
- Zoom mínimo y máximo con mouse.
- Elementos se escalan y posicionan correctamente con zoom.

### 🌐 Multijugador (si está activo)

- Conexión estable.
- Render de jugadores remotos.
- Ver stats remotos actualizados (vida, maná, energía).

### 🧠 NPCs

- Detectan jugador si se acerca.
- Reaccionan con animación o movimiento.
- Responden a ataques si están habilitados.

### 🧭 HUD & UI

- Barras del HUD visibles y correctas.
- Menú funcional y render correcto.
- Minimapa visible, representando entorno correctamente.

---

## 🧪 Testing de Rendimiento

| Acción                          | Resultado esperado                                  |
|--------------------------------|-----------------------------------------------------|
| Correr durante 5 minutos       | Sin caída de FPS, sin freezing                     |
| Crear 50 NPCs                  | FPS aceptable, sin errores                         |
| Activar evento global          | Todos los elementos reaccionan sin delay           |

---

## 📋 Registro de Pruebas

Se recomienda anotar en cada iteración:

- Fecha
- Qué se probó
- Resultado (✅ / ⚠️ / ❌)
- Bugs detectados
- Comportamiento inesperado

Formato sugerido:

```text
🗓️ 2025-04-01
🧪 Probado: Movimiento jugador, cambio de personaje
✅ Resultado: Funciona correctamente
❌ Bug: La animación hacia arriba se ve congelada (sprites)
```

---

## 🧪 Checklist Pre-release

Antes de cada entrega o demo:

- [ ] Todos los sistemas base funcionales.
- [ ] No hay errores críticos al iniciar o jugar.
- [ ] Modo local y multijugador probados.
- [ ] HUD y minimapa operativos.
- [ ] Eventos no bloquean el flujo normal del juego.

---

## 📌 Plantilla para registrar pruebas como Issue en GitHub

Se recomienda abrir un **Issue** en GitHub Projects con el siguiente formato:

```markdown
### 🧪 Testing Manual – [Nombre del Sistema]
🗓️ Fecha: 2025-04-01

**✅ Objetivo:**
Validar que [funcionalidad] funcione correctamente en [modo local / online].

**🎮 Escenario de prueba:**
1. Iniciar el juego.
2. [Describir pasos].
3. Observar el comportamiento del sistema.

**✔️ Resultado esperado:**
[Describir comportamiento correcto]

**❌ Resultado obtenido (si aplica):**
[Descripción del bug]

**📎 Capturas / logs (opcional):**
[Adjuntar imagen o error]

---

**Estado:**  
☑️ Pasó la prueba  
⛔ Falló la prueba  
⚠️ Funciona parcialmente
```
