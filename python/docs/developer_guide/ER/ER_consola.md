# Especificación ER: Consola Quake-like

## 1. Ubicación del desarrollo

El módulo de la consola se implementará en:

```
src/roguelike_engine/console
```

## 2. Reutilización de componentes

Aprovecharemos componentes y utilidades de:

```
src/roguelike_ui
```

(p. ej. estilos, manejo de texto, fuentes)

## 3. Requerimientos Funcionales

1. **Apertura/Cierre**
   - Tecla `` ` `` (backtick) para toggle de la consola.
   - Al abrir, captura todo el input; al cerrar, devuelve control al juego.

2. **UI (Vista)**
   - Overlay semi-transparente (top o bottom).
   - Área de scrollback con historial de líneas.
   - Línea de entrada (prompt) con cursor parpadeante.
   - Scroll con rueda o `PgUp`/`PgDn`.

3. **Input (Controlador)**
   - Captura caracteres, `Enter` para ejecutar, `Esc` o `` ` `` para cerrar.
   - Flechas ↑/↓ para navegar historial de comandos.
   - `Tab` para autocompletar comandos registrados.

4. **Ejecución de comandos (Modelo/Controlador)**
   - Parser sencillo: tokens separados por espacios.
   - Registro dinámico de handlers: `register(name, func)`.
   - La ejecución devuelve texto de salida o excepción.
   - Mostrar salida o errores en el scrollback.

5. **Comandos básicos iniciales**
   - `help`, `echo`, `spawn`, `setvar`, `getvar`, `pause`/`resume`.

6. **Historial de sesión**
   - En memoria, máximo ~200 líneas; rotatorio.

7. **Debug integrado**
   - Inspección de entidades, dumps de estado, logs, etc.

## 4. Requerimientos No Funcionales

- **Rendimiento**: mínimo overhead, sin caída notable de FPS.
- **Configurabilidad**: colores, fuentes, opacidad (parametrizables).
- **Extensibilidad**: registro de nuevos comandos desde otros módulos.

## 5. Arquitectura MVC + Events

Además de las carpetas tradicionales **Model**, **View**, **Controller**, se añade **Events** para centralizar captura de teclas y eventos de input.

- **Model** (`model.py`)
  - `ConsoleState`: historial, buffer de entrada.
  - `CommandRegistry`: registro y ejecución de comandos.

- **View** (`view.py`)
  - `ConsoleView`: dibuja overlay, scrollback y prompt según `ConsoleState`.

- **Controller** (`controller.py`)
  - `ConsoleController`: procesa input de caracteres y gestión de prompt.
  - Invoca ejecución y actualiza estado.

- **Events** (`events.py`)
  - Escucha teclas (`backtick`, flechas, `Tab`, `Enter`, `Esc`).
  - Mapea eventos a métodos de `ConsoleController`.

## 6. Estructura de carpetas

```
src/roguelike_engine/console/
├── model.py
├── view.py
├── controller.py
└── events.py
```

## 7. Flujo de datos

1. `Events` detecta `` ` `` → notifica a `ConsoleController`.
2. `ConsoleController` alterna `ConsoleState.open`.
3. Si abierta:
   - Teclas → `Events` → `Controller` → actualiza `ConsoleState.input_buffer`.
   - `Enter` → `CommandRegistry.execute(buffer)` → salida a historial.
   - `Tab` → `CommandRegistry.autocomplete(buffer)`.
   - Flechas → navegación de historial.
4. En cada frame, si `open`, `ConsoleView.render(ConsoleState)`.

## 8. Próximos pasos

1. Definir interfaces (clases y métodos) en cada módulo.
2. Primer prototipo: abrir/cerrar consola sin comandos.
3. Integrar en el game loop principal de Pygame/ECS.
