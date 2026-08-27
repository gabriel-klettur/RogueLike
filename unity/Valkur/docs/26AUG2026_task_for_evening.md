Perfecto. Voy dejando estas incidencias organizadas para que podamos seguir añadiendo problemas del **Building Editor** y después convertirlas en una lista de bugs/prioridades.

# Building Editor — Bugs y problemas detectados

## Colliders

### 1. Barra de separación de Z-Layer

**Estado:** ❌ No funciona correctamente

La barra/separador utilizada para cambiar o interactuar con el **Z-Layer** no se engancha correctamente al hacer **hover** con el ratón.

**Comportamiento esperado:**

* El área interactiva debería detectarse fácilmente al acercar el cursor.
* El hover debería activarse de forma consistente.
* No debería ser necesario posicionar el cursor de manera excesivamente precisa.

---

### 2. Herramienta Erase extremadamente lenta

**Estado:** ❌ Problema grave de rendimiento

La herramienta **Erase** dentro del sistema de colliders funciona de forma absurdamente lenta y produce bastante **lag**.

**Comportamiento esperado:**

* El borrado debería sentirse prácticamente instantáneo.
* Mantener pulsado y arrastrar debería permitir eliminar colliders de forma fluida.
* No debería producir bloqueos, tirones ni retrasos perceptibles en el editor.

**Prioridad sugerida:** 🔴 Alta

---

### 3. Selección de Buildings utiliza el botón incorrecto

**Estado:** ❌ Input incorrecto

Actualmente un **Building** se selecciona mediante **click derecho**.

Esto debería realizarse mediante:

**Click izquierdo → seleccionar Building**

El click derecho debería quedar reservado para otras acciones/context menus si se utilizan posteriormente.

**Comportamiento esperado:**

* `Left Click` → Seleccionar Building.
* La selección debería ser inmediata y consistente.

---

### 4. Show Colliders permanece activo al cerrar Building Editor

**Estado:** ❌ Problema de estado/UI

Si tenemos activada la opción **Show Colliders** y cerramos el **Building Editor**, los colliders continúan mostrándose en el juego/editor.

**Comportamiento esperado:**

Al cerrar el Building Editor:

* Los colliders visualizados por `Show Colliders` deberían ocultarse automáticamente.
* El estado interno de la opción puede conservarse si interesa, pero su representación visual no debería permanecer fuera del Building Editor.

Ejemplo:

`Building Editor abierto + Show Colliders ON`
→ Colliders visibles.

`Building Editor cerrado`
→ Colliders ocultos.

`Building Editor abierto nuevamente`
→ Se puede decidir si restaurar el estado anterior de Show Colliders o iniciarlo desactivado.

A partir de ahora podemos ir añadiendo aquí **cada herramienta del Building Editor**, separando además los problemas entre **bugs funcionales, UX, rendimiento y mejoras deseadas**.
