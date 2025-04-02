# 🖼️ 13. UI Architecture – Interfaz de Usuario, HUD y Navegación

Este documento describe la estructura de la interfaz de usuario del juego, incluyendo menús, HUDs, navegación y cómo se integran con el ciclo principal del juego.

---

## 🎯 Objetivo

- Proporcionar interfaces visuales claras e intuitivas.
- Separar el HUD principal del HUD flotante.
- Organizar el menú de pausa y la navegación.
- Preparar el sistema para expandirse con más elementos visuales (hechizos, items, inventario, etc.).

---

## 📋 1. Menú Principal (Pausa)

- Acceso con tecla `ESC`.
- Opciones actuales:
  - Cambiar personaje (alternar entre `first_hero` y `valkyria`)
  - Salir del juego
- Interacción por teclado (`↑ ↓ Enter`)
- Renderizado condicional según `self.show_menu`
- Ubicado en `ui/menu.py`, integrado desde `Game.handle_events()` y `Game.render()`

---

## ❤️ 2. HUD del Jugador (Inferior de pantalla)

🗂️ **Nombre interno recomendado:** `Main HUD`

Este HUD es exclusivo del **jugador local** y se muestra siempre en pantalla, generalmente en la parte inferior.

### Contenido actual y futuro:
- Barras grandes de:
  - ✅ Vida
  - ✅ Maná
  - ✅ Energía
- Iconos de habilidades:
  - Cooldowns (actualmente: `Q` restauración)
  - Teclas asignadas (futuro: `1`, `2`, `E`, `R`...)
- Espacio para:
  - Objeto consumible rápido
  - Buffs / estados alterados (con iconos)
  - Experiencia y nivel (futuro)

### Renderizado actual:
- Función `player.render_hud(screen, camera)`
- Dentro de `PlayerRenderer` (modular)

---

## 🧍 3. HUD Flotante sobre Personaje

🗂️ **Nombre interno recomendado:** `Floating HUD`

Este HUD aparece **sobre cada personaje en el mundo**, incluyendo el jugador local y los jugadores remotos.

### Contenido:
- ✅ Barra de vida
- ✅ Barra de maná
- ✅ Barra de energía
- ✅ ID visible (`pid[:6]` para jugadores online)

### Renderizado actual:
- Dentro de `RemotePlayer.render()` y `PlayerRenderer.render()`
- Calcula posición en pantalla según zoom y cámara
- Usa escalado dinámico para adaptarse a la vista

### Futuro:
- Nombre personalizado
- Clanes o gremios
- Rango / nivel visual
- Íconos especiales según estado (veneno, buff, etc.)

---

## 🧭 4. Navegación UI

- Control exclusivamente con teclado.
- Flujo en `menu.handle_input(event)`.
- Se planea un sistema de UI más avanzado con:
  - Navegación por mouse
  - Tooltips
  - Feedback visual (hover, selección, disabled)

---

## 🔄 5. Integración con Game Loop

- `Game.handle_events()` controla inputs para abrir/cerrar el menú.
- `Game.render()` se encarga de dibujar el menú, HUDs y todos los overlays de UI.
- HUD e interfaces deben ser renderizados **después de** entidades y mapa.

---

## 🔮 Consideraciones Futuras

- Sistema de inventario con su propia ventana.
- Árbol de habilidades navegable con mouse/teclado.
- Overlays contextuales (p. ej. nombre al pasar el mouse sobre NPC).
- Transiciones suaves (fade, animaciones) al abrir/cerrar UI.
- UI escalable y adaptable a resoluciones.

---

