# Sistema de Chat Inteligente para NPCs (gpt5-nano)

Este documento define la planificación de alto nivel para dotar a NPCs seleccionados de la capacidad de conversar en lenguaje natural, ejecutar acciones del juego mediante function-calling (herramientas), y mantener memorias persistentes. El diseño busca ser profesional, escalable y robusto, alineado con la arquitectura ECS, la política de UI blockers y la gestión centralizada de inputs.


## Objetivos
- Proveer conversaciones naturales Player ↔ NPC (y escalable a NPC ↔ NPC) con personalidad y rol configurables por datos.
- Integrar un proveedor LLM (gpt5-nano) a través de un adaptador, con fallback Dummy (sin red) para desarrollo y tests.
- Habilitar herramientas (function-calling) para que el NPC ejecute acciones del juego, p. ej. `vendor.buy`, `vendor.sell`, `vendor.stock`.
- Respetar proximidad y radio de conversación (`chat_range`) y lazos con el mundo (inventario, oro, tiempo, zona).
- Mantener memorias por NPC (corto y largo plazo) con políticas de retención, y aplicar guardrails de seguridad.
- UX sólida: overlay de chat en Pygame con historial, input, quick-replies, bloqueos adecuados de input de juego.


## Principios de diseño
- Configuración por datos (JSON) con validación por JSON Schema.
- Desacoplamiento: capa de servicios y adaptadores para LLM y tools; capa ECS para orquestación; capa UI independiente.
- Resiliencia: timeouts, reintentos, límites de tokens y fallback offline.
- Observabilidad: logs estructurados, trazas por conversación, métricas básicas.
- Testabilidad: DummyProvider, herramientas con side-effects bien encapsulados y baterías de tests.


## Arquitectura (alto nivel)
- Capa de Datos (JSON + Schemas): personas, roles, herramientas, asignaciones, prompts, memorias, configuración del proveedor.
- Capa de Servicios (Dominio): ChatService, LLMProviderAdapter, ToolRegistry/Executor, ContextBuilder, MemoryStore, Safety.
- Capa ECS: componentes (`ChatComponent`, `ConversationStateComponent`), colas de eventos, sistemas (`ChatSystem`, `ChatProcessingSystem`).
- Capa UI: `ChatOverlay` (view/controller/events) integrado con `managers/core/events.py` y UI blockers.
- Capa FSM (opcional): estado/flag “Talking” para inmovilizar y animar NPCs durante chat.

```
[Player Input] -> managers/core/events.py -> ChatEventQueue -> ChatSystem (proximidad) 
     -> ChatProcessingSystem (jobs) -> ChatService -> LLMProvider (gpt5-nano/Dummy)
     -> ToolRegistry (side-effects ECS) -> ChatResponseEvent -> ChatOverlay (UI)
```


## Estructura de datos y archivos
- `data/chat/`
  - `personas/` (definen personalidad):
    - `vendor_wood_gatita.json` ← AQUÍ se define su personalidad detallada (ver ejemplo más abajo).
    - `guard_default.json`
  - `roles/` (capacidades y límites por rol):
    - `vendor.json`
    - `villager.json`
  - `assignments.json` (qué entity_id habla y con qué persona/rol/radio):
    ```json
    {
      "npc_vendor_wood_gatita": {
        "persona_id": "vendor_wood_gatita",
        "role": "vendor",
        "chat_range": 140
      },
      "npc_guard_01": {
        "persona_id": "guard_default",
        "role": "villager",
        "chat_range": 100
      }
    }
    ```
  - `prompts/` (plantillas y políticas): `system.txt`, `safety.txt`, `style_vendor.txt`.
  - `tools/` (schemas de function-calling): `vendor_buy_sell.schema.json`.
  - `memories/` (persistencia por NPC): `{entity_id}.json`, `{entity_id}_ephemeral.json`.
- `schemas/chat/`
  - `persona.schema.json`, `role.schema.json`, `assignments.schema.json`, `tool.schema.json`.
  - Recomendado incluir en `persona.schema.json` campos opcionales: `origin`, `humor.enabled`, `humor.frequency`, `humor.topics`, `humor.style`, `humor.examples`.
- `data/config/chat.json` (config global del proveedor y políticas):
  ```json
  {
    "provider": "gpt5-nano",
    "api_url": "https://.../responses",
    "timeouts_s": 15,
    "retry": {"max": 2, "backoff_s": 1.0},
    "defaults": {"max_tokens": 384, "temperature": 0.2},
    "per_role_overrides": {"vendor": {"temperature": 0.1}},
    "streaming": false
  }
  ```

Recomendaciones:
- Validar JSONs en guardado (al estilo `data/fsm/schema.json`).
- Mantener ejemplos y plantillas para facilitar authoring.


## LLM y Adaptadores
- `src/roguelike_game/chat/providers/base.py`
  - Interfaz `LLMProvider`: `generate(messages, tools=None, tool_choice="auto", stream=False)` devuelve `LLMResult` (texto, tool_calls, usage, finish_reason).
- `src/roguelike_game/chat/providers/gpt5_nano.py`
  - Implementa timeouts, reintentos, límites de tokens, cabeceras, y manejo de tool-calls. No hardcodear API key: cargar de entorno/secret seguro (`OPENAI_API_KEY`).
- `src/roguelike_game/chat/providers/dummy.py`
  - Sin red. Regla simple: si input contiene `buy/sell/stock`, simula tool-calls válidos; responde texto breve. Útil para tests y dev offline.


## Agentes (OpenAI Agents SDK): ¿cuándo conviene y cómo integrarlo?
Los “agentes” añaden una orquestación de mayor nivel (handoffs entre agentes, trazas, guardrails, herramientas integradas como web/file search, y jerarquía de instrucciones). 

- Ventajas
  - Orquestación avanzada (handoff entre agentes por idioma/rol/objetivo).
  - Telemetría/tracing y evaluaciones integradas en la plataforma.
  - Guardrails adicionales (moderation API, instruction hierarchy) sin re-implementar.
- Desventajas
  - Complejidad extra y dependencia de red/SDK.
  - Menor control fino que un orquestador propio en ciertas rutas de juego.
  - Latencia adicional frente a un flujo local con DummyProvider.

Estrategia recomendada (faseada)
- Fase 0–1: Mantener nuestro orquestador (ChatService + LLMProvider + ToolRegistry). Es lo más simple y controlable para integrar con ECS/UI y con tus tools in‑game.
- Fase 2+: Añadir `AgentProviderAdapter` (opcional) que implemente la misma interfaz que `LLMProvider`, pero delegue en el Agents SDK. 
  - Casos ideales: triage de idioma/rol, misiones complejas con sub‑tareas, o agentes especializados (comercio, quest, guía).
  - Respetar el contrato de tools: mapear los schemas locales a “function calling” del agente.

Esbozo de integración (Python/TypeScript)
- Python: usar `openai-agents` para definir un agente por rol (p. ej., `VendorAgent`) y uno de triage opcional; el adapter traduce `messages/tools` → llamada al agente y retorna `LLMResult` compatible.
- TypeScript: similar con `@openai/agents` (si un backend TS te resulta más conveniente). 

Decisión actual
- Avanzar con orquestador propio (más predecible y ligero). Mantener la puerta abierta a Agents con un `AgentProviderAdapter` intercambiable.


## Tools (function-calling) y side-effects
- `data/chat/tools/vendor_buy_sell.schema.json` (ejemplo):
  ```json
  {
    "functions": [
      {
        "name": "vendor.buy",
        "description": "Vender madera al jugador por 1 oro c/u",
        "parameters": {
          "type": "object",
          "properties": {
            "item": {"type": "string", "enum": ["wooden"]},
            "quantity": {"type": "integer", "minimum": 1}
          },
          "required": ["item", "quantity"]
        }
      },
      {
        "name": "vendor.sell",
        "description": "Comprar madera del jugador por 1 oro c/u",
        "parameters": {
          "type": "object",
          "properties": {
            "item": {"type": "string", "enum": ["wooden"]},
            "quantity": {"type": "integer", "minimum": 1}
          },
          "required": ["item", "quantity"]
        }
      },
      {
        "name": "vendor.stock",
        "description": "Consultar stock disponible y precio",
        "parameters": {"type": "object", "properties": {}}
      }
    ]
  }
  ```
- `src/roguelike_game/chat/service/tool_registry.py`
  - Registra funciones disponibles por rol y ejecuta side-effects con validación de schema.
  - Integra con sistemas/gestores existentes (inventario, oro, drops). Publica eventos de UI (p. ej., “+5 madera”, “-5 oro”).


## ChatService y orquestación
- `src/roguelike_game/chat/service/chat_service.py`
  - Recibe `ChatJob` (player_id, npc_id, mensajes previos, contexto actual).
  - Usa `ContextBuilder` para prompt: persona + rol + mundo (posición, hora, inventarios) + memorias + políticas de seguridad.
  - Invoca `LLMProvider` (tools activas según rol) y procesa tool-calls mediante `ToolRegistry`.
  - Actualiza memorias (ephemeral/long-term) y retorna `ChatResult` listo para UI.
- `src/roguelike_game/chat/service/context_builder.py`
  - Obtiene datos del mundo (ECS) y de archivos (personas/roles/asignaciones).
- `src/roguelike_game/chat/service/memory_store.py`
  - Lee/escribe `data/chat/memories/`. Implementa resúmenes y retención (por tamaño y antigüedad).
- `src/roguelike_game/chat/service/safety.py`
  - Filtros de entrada/salida, PII básica, palabra malsonante, truncado y sanitización.

### Modo offline y fallback (sin internet)
- Detección: si el proveedor real falla por timeout/conectividad o si `provider = "dummy"` en `data/config/chat.json`, el `ChatService` cambia a ruta offline.
- Comportamiento offline:
  - Parser de comandos (buy/sell/stock) por regex simple: `^\s*(buy|sell)\s+(\d+)\s+(wood|wooden)\s*$` (tolerante a mayúsculas/minúsculas y espacios).
  - Ejecuta tools directamente vía `ToolRegistry` y genera respuestas cortas y funcionales (sin “decoración humana”).
  - Mantiene HUD de feedback (“+X madera”, “-X oro”).
- Comportamiento online (API disponible):
  - Estilo humano configurable por persona/rol: saludos al abrir, despedidas al cerrar, y tono decorado (coqueto/travieso para Gatita), además de realizar tool-calls.
  - Plantillas sugeridas en `roles/vendor.json`:
    ```json
    {
      "dialogue": {
        "greet_on_open": true,
        "goodbye_on_close": true,
        "greet_lines": ["¡Hola, corazón! ¿Buscas madera fresca?"],
        "goodbye_lines": ["Vuelve pronto, tesoro."]
      },
      "online_style": {"humanize": true, "decorate_sales_pitch": true}
    }
    ```


## Parser de comandos y detección semántica
- Reglas mínimas (offline y como atajo online):
  - `buy <cantidad> <wood|wooden>`
  - `sell <cantidad> <wood|wooden>`
  - `stock` (sin parámetros)
- Implementación:
  - Regex tolerantes a espacios y pluralización ligera (opcional): `woods?`.
  - Normalización de tokens y fallback a LLM cuando esté online y no coincida el patrón exacto (para soportar lenguaje natural).
  - Respuestas por defecto ante comandos desconocidos.


## Integración ECS
- Componentes (`src/roguelike_game/chat/ecs/components.py`):
  - `ChatComponent`: `persona_id`, `role`, `chat_range`, `cooldown_s`, `last_chat_ts`, `allow_tool_calls`, `max_turns`.
  - `ConversationStateComponent`: `conversation_id`, `status` (`idle|opening|awaiting_llm|speaking|closed`), `history_window`, `pending_player_text`.
- Eventos (`src/roguelike_game/chat/ecs/events.py`):
  - `ChatOpenRequest(player_id, npc_id)`
  - `ChatPlayerMessage(player_id, npc_id, text)`
  - `ChatCloseRequest(player_id, npc_id, reason)`
  - `ChatServiceJob(job)` (cola interna de procesamiento)
  - `ChatResponseEvent(player_id, npc_id, text, tool_effects, metadata)`
- Sistemas (`src/roguelike_game/chat/ecs/systems.py`):
  - `ChatSystem`: detección de proximidad usando el índice espacial y `chat_range`, apertura/cierre, rate-limit, encolado de `ChatOpenRequest` y `ChatPlayerMessage`.
  - `ChatProcessingSystem`: saca jobs de la cola, llama a `ChatService` en background (thread pool), y publica `ChatResponseEvent`.

Referencias útiles:
- Índice espacial actual: `src/roguelike_game/ecs/core/spatial_index.py` (p. ej., `SpatialIndex.get_solid_tiles_for_rect`, y utilidades para consultas en rango). El `ChatSystem` consultará entidades con `ChatComponent` dentro de `chat_range` del jugador.
- Enrutado de inputs globales: `src/roguelike_game/managers/core/events.py`.


## UI de Chat (Pygame)
- Módulos (`src/roguelike_game/chat/ui/`): `chat_overlay_view.py`, `chat_overlay_controller.py`, `chat_overlay_events.py`.
- Comportamiento:
  - Panel flotante con historial (burbujas de NPC/jugador), caja de texto, “Enviar”, “Cerrar”, y quick-replies personalizables por rol.
  - Respeta UI blockers (similar a editores). No deja pasar inputs al juego mientras visible.
  - Soporte de streaming opcional: render incremental si el proveedor lo soporta.
- Atajos/Bindings:
  - Añadir en `data/config/input_bindings.json`: `chat_interact` (p. ej., `K_e`), `chat_send` (`K_RETURN`), `chat_close` (`K_ESCAPE`).
  - Integrar en `src/roguelike_game/managers/core/events.py` (centralizado), siguiendo tu política actual.

### Indicador de escritura (typing indicator)
- Objetivo: dar sensación de “está pensando/escribiendo”.
- Diseño:
  - Estado `is_typing` en `ConversationStateComponent` y/o en el controlador UI.
  - Animación cíclica `'.' -> '..' -> '...' -> '.'` cada 300 ms.
  - Inicio: al encolar un `ChatServiceJob` (antes de llamar al proveedor) o al entrar en `awaiting_llm`.
  - Fin: al recibir `ChatResponseEvent` o al producirse un timeout/offline fallback.
- Latencia/ritmo según tamaño de respuesta:
  - Heurística: `typing_delay_ms = clamp( min_delay, max_delay, base + k * estimated_tokens )`.
  - Estimación de tokens por longitud: `estimated_tokens ≈ len(text)/4` (si `usage` del proveedor no está disponible).
  - Mostrar la animación durante `typing_delay_ms` o hasta que llegue streaming/resultado.


## Seguridad y robustez
- Límites de tokens y longitud de mensajes, truncado de historial.
- Timeouts de red, reintentos exponenciales (pocos), y fallback automático al `DummyProvider` si falla el real.
- Validación estricta de tool-calls contra schemas JSON; sandbox de side-effects.
- Filtros básicos (malas palabras, PII) y estilo controlado (p. ej., número de frases) desde datos `persona/rol`.
- Logs con `conversation_id`, provider, latencias, tokens, tool-calls invocados y resultados.


## Memoria y contexto
- Corto plazo (ephemeral): ventana de N turnos por conversación. Persistido en `{entity_id}_ephemeral.json` para continuidad rápida.
- Largo plazo (long-term): resúmenes periódicos en `{entity_id}.json` (preferencias del jugador, hitos). Políticas de rotación/tamaño.
- Selección de memoria relevante por similitud simple (palabras clave) o heurística (por rol), sin dependencias externas complejas.

### Amistad (friendship) y tono adaptativo
- Objetivo: que el vendor busque entablar amistad con el jugador.
- Implementación:
  - `MemoryStore` mantiene `friendship_score` por NPC↔jugador (p. ej., -100..+100).
  - Incrementar con transacciones justas, cumplidos, ayudas; decrementar con regateos agresivos o cancelaciones.
  - `ContextBuilder` inserta una etiqueta de cercanía (`friendship_level`: mini/mid/alto) para modular tono.
  - En `personas/vendor_wood_gatita.json`, usar `style`/`tone` y `humor` más juguetón cuando `friendship_level` crece.


## Flujo de interacción (ejemplo Vendor)
1) Jugador pulsa `E` cerca de `npc_vendor_wood_gatita` (dentro de `chat_range`).
2) `ChatSystem` encola `ChatOpenRequest`; UI abre overlay y activa `is_typing` si procede.
3) Jugador escribe “buy 3 wood” o lenguaje natural. Se encola `ChatPlayerMessage`.
4) `ChatProcessingSystem` crea `ChatServiceJob`. `ChatService` construye contexto y llama al LLM con tools del rol `vendor` (online) o ejecuta parser+tools directamente (offline).
5) Online: el LLM puede emitir tool-call `vendor.buy` y producir respuesta de estilo humano (saludo, tono coqueto/travieso). Offline: ejecución directa de `vendor.buy` y respuesta corta.
6) UI muestra resultado, detiene `is_typing`, y HUD refleja cambios (“+3 madera”, “-3 oro”).
7) Cierre: `ESC` o alejamiento del rango con gracia (si operación en curso, confirmar). Si `goodbye_on_close = true`, mostrar despedida.


## Roadmap por fases
- Fase 0: Infra mínima con DummyProvider
  - Datos básicos (`personas`, `roles`, `assignments`, `prompts`, `tools`).
  - ECS (componentes, eventos, sistemas) + UI overlay básico.
  - ToolRegistry con `vendor.stock/buy/sell` y conexión a inventario/oro del juego.
  - Indicador de escritura básico (animación cíclica) vinculado a `awaiting_llm`.
  - Parser simple buy/sell/stock y respuestas por defecto.
- Fase 1: Adaptador gpt5-nano + safety
  - Integración real, timeouts, reintentos, límites, logs, tool-calling.
  - Estilo humano online (saludos/despedidas, decoración) por persona/rol.
  - Ajuste de `typing_delay_ms` usando `usage` del proveedor si está disponible.
- Fase 2: UX avanzada
  - Streaming, quick-replies por rol, estilos por persona (emojis, longitud), accesibilidad.
  - Amistad: ajustar tono y líneas de diálogo por `friendship_level`.
- Fase 3: FSM/AI
  - Estado/flag “Talking”, animaciones, cámara, gestos, y posible impacto en aggro/alert.


## Pruebas
- Unitarias (`tests/roguelike_game/chat/`):
  - Parser de comandos, validación de tool-calls, ToolRegistry con inventarios dummy.
  - ChatService con DummyProvider; MemoryStore (rotación, resúmenes, friendship_score).
  - UI: animación del indicador de escritura (timers) y estados `is_typing`.
- Integración:
  - Flujo vendor completo: stock → buy 2 → sell 1; efectos en ECS, UI y logs; offline/online.
  - Timeouts/reintentos + fallback; saludos/despedidas en online.
- E2E básico: interacción dentro/fuera de `chat_range`, apertura/cierre UI, bloqueos correctos de input.


## Configuración y despliegue
- Clave de API: utilizar `OPENAI_API_KEY` cargada desde el archivo de entorno del proyecto: `/.env` (no versionar). 
  - Ejemplo `.env`:
    ```dotenv
    OPENAI_API_KEY=sk-xxxx
    ```
  - El proveedor `gpt5_nano.py` debe leerla con `os.getenv("OPENAI_API_KEY")`. 
  - Mantener `.env` en `.gitignore`.
- Config general en `data/config/chat.json`: controla provider y políticas (timeouts, límites, streaming, etc.).
- Separar límites por entorno (dev/test/prod) y habilitar `DummyProvider` sin red para CI.
- Telemetría opcional: archivo de logs o consola con prefijo `chat.*`.


## Extensiones futuras
- Editor in-game para authoring de personas/roles/asignaciones.
- Chat NPC ↔ NPC accionado por eventos del mundo (patrullas, clima, spawners).
- Soporte multi-idioma y localización del estilo/respuestas.
- Herramientas adicionales (misiones, guía, crafting) con schemas dedicados.


## Checklist de implementación
- Datos y Schemas creados y validados.
- Componentes/Events/Sistemas ECS integrados.
- UI Overlay operativo con bloqueos correctos.
- ToolRegistry conectado a inventario/oro y probado.
- DummyProvider funcionando; gpt5-nano integrado con seguridad básica.
- Parser simple + modo offline documentado y probado.
- Indicador de escritura implementado.
- Migración inicial: `vendor_wood_gatita` activo con `chat_range` configurado.


## Anexos: Ejemplos
- `data/chat/personas/vendor_wood_gatita.json` (personalidad coqueta, traviesa, origen ucraniana y humor sobre comida tradicional)
```json
{
  "name": "Gatita",
  "origin": "ucraniana",
  "tone": "coqueta, traviesa, amable",
  "background": "Vendedora de madera local; ama la artesanía.",
  "goals": ["comerciar justo", "cuidar el bosque", "ayudar a viajeros"],
  "humor": {
    "enabled": true,
    "frequency": "sometimes",
    "topics": ["comida tradicional ucraniana"],
    "style": "ligero y juguetón",
    "examples": [
      "Dicen que mis varenyky son tan buenos que hasta los árboles vienen a negociar.",
      "Si traes borsch calentito, quizá te haga un descuentito… quizá. 😉"
    ]
  },
  "style": {"emoji": true, "verbosity": "medium", "sentences_max": 3}
}
```
- `data/chat/roles/vendor.json`
```json
{
  "tools": ["vendor.buy", "vendor.sell", "vendor.stock"],
  "limits": {"max_tokens": 384, "temperature": 0.2},
  "guardrails": {"no_insults": true, "no_private_info": true},
  "dialogue": {
    "greet_on_open": true,
    "goodbye_on_close": true,
    "greet_lines": ["¡Hola, corazón! ¿Buscas madera fresca?"],
    "goodbye_lines": ["Vuelve pronto, tesoro."]
  },
  "online_style": {"humanize": true, "decorate_sales_pitch": true}
}
```
