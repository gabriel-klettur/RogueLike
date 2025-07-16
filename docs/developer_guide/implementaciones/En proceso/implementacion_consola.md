# Plan de desarrollo de la consola Quake-like

Basado en `docs/developer_guide/ER/ER_consola.md`, proponemos este plan de 7 pasos:

1. **Configurar estructura y módulos**  [COMPLETADO]
   - Crear carpeta `src/roguelike_engine/console` con subcarpetas:  
     - `controller`  
     - `events`  
     - `model`  
     - `view`  
   - En cada subcarpeta, crear `__init__.py` y archivo base (`controller.py`, `events.py`, `model.py`, `view.py`).  
   - Añadir referencias y enlaces a utilidades de `src/roguelike_ui` para estilos y manejo de texto.

2. **Definir interfaces (Modelo)**  
   - Detallar `ConsoleState`: atributos como `history: List[str]`, `input_buffer: str`, `is_open: bool`, `cursor_pos: int`, `max_lines: int`.  
   - Detallar `CommandRegistry`: estructura interna `commands: Dict[str, Callable]` y métodos:  
     - `register(name: str, handler: Callable) -> None`  
     - `execute(command_line: str) -> Tuple[str, Optional[Exception]]`  
     - `autocomplete(prefix: str) -> List[str]`  
   - Especificar tipos de datos de entrada y manejo de excepciones en la ejecución.

3. **Implementar captura de eventos (Events)**  
   - Reutilizar listeners existentes de `src/roguelike_ui` para captura de teclas y eventos de input.  
   - Adaptar eventos (`backtick`, `Enter`, flechas, `Tab`, `Esc`) y mapearlos a métodos de `ConsoleController`.  
   - Gestionar la prioridad del input cuando la consola está abierta para evitar conflictos con el juego.

4. **Desarrollar lógica de controlador (Controller)**  
   - Implementar toggle de consola y gestión de `input_buffer`.
   - Navegación por historial de comandos.

5. **Desplegar vista inicial (View)**  
   - Dibujar overlay semitransparente, scrollback y prompt.
   - Integrar estilos y fuentes de `roguelike_ui`.

6. **Registrar y probar comandos básicos (Modelo)**  
   - Añadir handlers para `help`, `echo`, `spawn`, `setvar`, `getvar`, `pause`/`resume`.
   - Validar output y manejo de errores al ejecutar.

7. **Integrar y validar en el juego**  
   - Conectar consola al bucle principal Pygame/ECS.
   - Realizar pruebas de usabilidad y rendimiento.
   - Ajustar configuraciones (colores, opacidad, fuente).
