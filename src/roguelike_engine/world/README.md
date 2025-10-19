# Paquete `world`

Responsable de la persistencia y orquestación de niveles (mundos) a un nivel superior que los mapas individuales.

## Objetivos

- Proveer un contrato estable y desacoplado para cargar/guardar el estado global del juego.
- Gestionar múltiples niveles simultáneamente con límite de memoria (LRU ligero).
- Ofrecer un repositorio JSON con guardado atómico e índice de slots.
- Integrarse con el resto del engine mediante un bus de eventos y un gateway de nivel.
- Soportar autosave configurable.

## Componentes

- `world.py`
  - `WorldManager`: punto de entrada. Orquesta niveles a través de una `LevelGatewayFactory`, persiste usando `IWorldRepository`, publica eventos por `EventBus` y ofrece `tick_autosave()`.
- `models.py`
  - `WorldSnapshot`: snapshot tipado y versionado del mundo (`version`, `player`, `npcs`, `levels`, ...). `meta` es un dict libre para metadatos.
  - `SaveSlot`: índice de slots.
- `repository.py`
  - `IWorldRepository`: contrato de persistencia.
  - `JSONWorldRepository`: implementación JSON con guardado atómico e `index.json`.
- `level_gateway.py`
  - `ILevelGateway`: protocolo mínimo para integrar un gestor de nivel.
  - `LevelGatewayFactory` y `DefaultLevelGatewayFactory` (adapter a MapManager del paquete del juego si está disponible).
- `events.py`
  - `EventBus`: suscripción y publicación de eventos (`on_before_save`, `on_after_save`, `on_level_loaded`, ...).
- `world_config.py`
  - `WorldConfig`: rutas, límites, autosave.

## Flujo típico

1. Crear `WorldManager()` (puedes inyectar `repository`, `event_bus`, `level_factory`).
2. Llamar a `load_world()` para cargar el slot actual o el más reciente.
3. Cambiar de nivel con `load_level(level_name)` (restaura posición del jugador y NPCs globales).
4. Guardar con `save_world()` manualmente, o invocar `tick_autosave()` periódicamente para autosave.

## Contratos

- `ILevelGateway` debe implementar:
  - `serialize_state() -> dict`
  - `deserialize_state(state: dict) -> None`
  - `spawn_player(pos) -> None`
  - `restore_npc_states(memory: dict) -> None`

- `WorldSnapshot` incluye `version` para permitir migraciones posteriores.

## Índice de slots

Se mantiene `index.json` en `WorldConfig.save_dir` con:

```json
{
  "current_path": ".../partida_2025-09-08_21-20-00.json",
  "slots": [
    {"slot_id": "partida_2025-09-08_21-20-00", "path": "...", "created_at": "2025-09-08T21:20:00", "size_bytes": 12345}
  ]
}
```

## Eventos disponibles

- `on_before_save(snapshot_dict)`
- `on_after_save(path, duration_ms)`
- `on_level_loaded(level_name)`
- `on_level_unloaded(level_name)`
- `on_slot_changed(path)`

## Integración con el game loop

Llama a `world_manager.tick_autosave()` desde el loop principal si `WorldConfig.autosave_enabled` es `True`.

## Compatibilidad

- Se mantiene compatibilidad con archivos `partida_*.json` legacy.
- Si existe el índice, se prioriza para descubrir slots; si no, se escanea el patrón legacy.

## Próximos pasos sugeridos

- Añadir migrador de versiones de snapshot.
- Tests de unidad y de integración (corrupción de archivo, índice, eventos, LRU).
