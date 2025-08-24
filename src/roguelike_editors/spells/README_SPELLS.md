# Editor de Hechizos (Spells Editor)

Este módulo provee un editor visual para definir, previsualizar y modificar hechizos del proyecto. Está diseñado con un enfoque modular (MVC por panel) y se integra con el sistema de assets para ofrecer previsualizaciones de sprites y partículas, edición de propiedades en JSON anidado y hot‑reload de cambios.

## Objetivos

- Permitir gestionar el catálogo de hechizos: selección, alta/baja y edición.
- Ofrecer previsualizaciones rápidas (sprites/partículas) para validar el aspecto del hechizo.
- Editar propiedades complejas (JSON anidado) de forma segura y cómoda.
- Integrarse con el sistema de archivos para recargar cambios sin reiniciar.

## Arquitectura general

El editor sigue un patrón MVC por panel y se orquesta desde un controlador de alto nivel:

- Controlador principal: `src/roguelike_editors/spells/spells_editor_controller.py`.
  - Compone un controlador interno del Picker: `src/roguelike_editors/spells/spells_picker_panel/spells_editor_controller.py`.
  - Sincroniza estado global (visibilidad, selección, modos) con los subpaneles.
- Eventos globales: `src/roguelike_editors/spells/spells_editor_events.py`.
  - Atajo principal: F4 para mostrar/ocultar el editor.
- Vista contenedora: `src/roguelike_editors/spells/spells_editor_view.py`.
  - Gestiona rects/anchors compartidos para el layout.
- Modelo global: `src/roguelike_editors/spells/spells_editor_models.py`.
  - Fuente de verdad de visibilidad, selección, modos, y cachés compartidos.

## Componentes y paneles

- Barra de herramientas: `src/roguelike_editors/spells/spells_tool_bar_panel/`
  - Orden vertical preferido: `['spells_on_map', 'undo', 'redo']`.
  - El botón `spells_on_map` alterna visibilidad del Picker y Add/Remove, y gestiona el "modo borrar".
- Picker de hechizos (controlador interno): `src/roguelike_editors/spells/spells_picker_panel/`
  - Controlador: `spells_editor_controller.py` (maneja modelo, vista, input, eventos y caché de previews).
  - Integra Toolbar, Add/Remove y Properties.
  - Soporta hot‑reload de cambios de assets/hechizos y renombrado de IDs con persistencia.
- Add/Remove: `src/roguelike_editors/spells/spells_add_remove_panel/`
  - Altas y bajas de entradas del catálogo.
- Propiedades: `src/roguelike_editors/spells/spells_properties_panel/`
  - Controlador: `spells_properties_panel_controller.py`.
  - Edición inline de JSON anidado (helpers de get/set por rutas).
  - Doble clic en campos de asset abre el selector (ej.: `vfx.sprite.path`).
  - Previsualizaciones embebidas (sprite/partículas), tabs, scroll y tooltips.
- Título: `src/roguelike_editors/spells/spells_title_panel/` (vista de cabecera).
- Servicios de preview: `src/roguelike_editors/spells/services/particle_preview.py`.

## Sistema de previsualización de partículas

Las previsualizaciones de partículas son superficies simuladas/animadas en miniatura que se renderizan en el UI (Picker y Propiedades). El proveedor de previews infiere el tipo de efecto a partir de los datos del hechizo (ej.: `type`, `vfx`, `effect`).

- Efectos soportados (principales ejemplos): humo y ráfagas, aura/curación, fuegos artificiales, rayo, dash, slash, láser, explosión, teletransporte, llama arcana, entre otros.
- Parámetros comunes: color, radio/tamaño, duración, densidad.
- Caché de previews por hechizo para reducir coste de render.
- Nota: actualmente se infiere el preview de algunos tipos especiales. Por ejemplo, `sphere_magic_shield` se representa como un aura azul por defecto y el substring "shield" también activa esta heurística (vista previa en bucle del tamaño de la celda, usando `effect.radius` si está disponible).

> Implementación: ver `src/roguelike_editors/spells/services/particle_preview.py` y el registro/uso dentro de `spells_picker_panel/spells_editor_controller.py`.

## Flujo de trabajo y uso

- Abrir/Cerrar el editor: tecla F4.
- Seleccionar un hechizo en el Picker para ver su preview.
- Usar la barra de herramientas:
  - `spells_on_map`: alterna Picker y Add/Remove, y controla el modo borrar.
  - `undo` / `redo`: deshacer/rehacer acciones soportadas por el editor.
- Editar propiedades en el panel derecho:
  - Click para foco y edición inline de valores.
  - Doble clic en rutas de assets abre el selector correspondiente.
  - Los cambios se confirman y se persisten en el JSON de hechizos; el hot‑reload actualiza previews y datos.

## Gestión de assets y hot‑reload

- Cambios en imágenes o datos de hechizos se reflejan automáticamente en el editor (sin reiniciar).
- Renombrar IDs sincroniza modelos, vistas y persistencia del JSON.
- Las vistas recalculan y vuelven a renderizar previews cuando se detectan cambios.

## Debug y variables de entorno

Activar logs de diagnóstico estableciendo estas variables:

- `RL_SPELLS_PREVIEW_DEBUG`
- `RL_SPELLS_EDITOR_DEBUG`
- `RL_SPELLS_PROPS_DEBUG`
- `RL_SPELLS_VIEW_DEBUG`

## Extensión y convenciones

- Mantener el patrón MVC por panel y la composición desde el controlador principal.
- Añadir nuevos efectos de partículas implementando clases en `services/particle_preview.py` y registrándolos en el controlador del Picker.
- Respetar el orden vertical de la Toolbar: `['spells_on_map', 'undo', 'redo']`.
- Reutilizar los helpers de edición de propiedades para manipulación de JSON anidado.

## Referencias de código

- `src/roguelike_editors/spells/spells_editor_controller.py`
- `src/roguelike_editors/spells/spells_editor_events.py`
- `src/roguelike_editors/spells/spells_editor_models.py`
- `src/roguelike_editors/spells/spells_editor_view.py`
- `src/roguelike_editors/spells/spells_tool_bar_panel/`
- `src/roguelike_editors/spells/spells_picker_panel/`
- `src/roguelike_editors/spells/spells_add_remove_panel/`
- `src/roguelike_editors/spells/spells_properties_panel/`
- `src/roguelike_editors/spells/spells_title_panel/`
- `src/roguelike_editors/spells/services/particle_preview.py`
