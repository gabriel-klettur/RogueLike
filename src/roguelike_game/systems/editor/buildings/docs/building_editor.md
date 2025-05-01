# 🛠️ Editor Visual de Edificios

Sistema interactivo para crear, mover, escalar y eliminar edificios directamente desde el juego. Diseñado para facilitar el diseño visual del mapa.

---

## ✨ Características principales en desarrollo.

- Activación rápida con `F10`.
- Herramientas aplicadas sobre 'buildings' en el mapa.:
  - **Placer** (`boton derecho del raton desplaza edificios`)
  - **Borrado** (`Delete`) ---> Mediante cuadrado rojo a la izquierda de nuestro cuadrado para modificar el size de un buildings el cual funcionara como boton.
  - **Guardar** (`Cuando el editor se cierra`)
- Guardado en archivo JSON.

---

## ⚙️ Flujo de trabajo esperado

1. Presionar `F10` para activar el editor.
2. Click derecho para seleccionar un edificio.
3. Arrastrar o escalar si es necesario.
4. Sistema para cambiar el size de un edificio mediante un cuadrado azul y arrastrar con click derecho.
5. Presionar `Delete` para eliminar el edificio seleccionado.

---

## 🔁 Sistema de eventos

```python
if event.key == pygame.K_F10:
    editor_state.active = not editor_state.active

elif keys[pygame.K_LCTRL] and keys[pygame.K_s]:
    save_buildings_to_json(...)
