# Guía de desarrollo: Inventario Online

## 1. Introducción
Este documento describe la arquitectura y flujos del sistema de inventario en modo multijugador online. Cubre sincronización en red, protocolos cliente-servidor y manejo de estado en servidor.

## 2. Arquitectura Cliente-Servidor
- **Cliente**: UI, input y renderizado, aplica diffs locales.
- **Servidor**: autoridad única, estado global, validación y persistencia.
- Lógica de reconciliación y rollbacks.

## 3. Mensajes y formatos JSON
- **SyncInventory** (estado completo):
  ```json
  { "player_id": "...", "slots": [ ... ], "version": N }
  ```
- **Diffs**:
  - `AddItemOnline`: `{ "player_id":"...", "item_id":"...", "quantity":N }`
  - `RemoveItemOnline`: `{ "player_id":"...", "item_id":"...", "quantity":N }`
  - `DropItemOnline`: `{ "drop_id":"...", "player_id":"...", "item_id":"...", "quantity":N, "position":{ "x":X, "y":Y } }`
  - `PickUpOnline`: `{ "player_id":"...", "drop_id":"..." }`

> Incluir JSON Schema en `schemas/InventoryOnlineSchema.json`.

## 4. ECS: InventoryOnlineSystem
- Archivo: `src/roguelike_game/ecs/systems/inventory_online_system.py`.
- Clases y métodos principales:
  - `handle_sync(data: dict)`
  - `handle_add(msg: AddItemOnline)`
  - `handle_remove(msg: RemoveItemOnline)`
  - `handle_drop(msg: DropItemOnline)`
  - `handle_pickup(msg: PickUpOnline)`

## 5. Persistencia en Servidor
- Base de datos relacional o NoSQL.
- Tablas/colecciones: `player_inventories`, `map_drops`.
- Migraciones y versionado de esquema.

## 6. Gestión de Conflictos
- Concurrency control (optimistic locking, versionamiento).
- Reenvío de comandos fallidos.
- Estrategia de merge o rechazo.

## 7. UI/UX Online
- Indicadores de estado (sincronizando, desconectado).
- Visualizar conflictos y opciones de resolución.

## 8. Tests y CI
- Tests unitarios de parsers y validadores de mensajes.
- Pruebas de integración con servidor simulado.
- Pipeline CI valida `InventoryOnlineSchema.json` y corre `pytest`.

## 9. Diagrama de Secuencia Online
```mermaid
sequenceDiagram
  Client->>Server: SyncInventory
  Server-->>Client: Inventory state
  Client->>Server: AddItemOnline
  Server-->>Client: Update diff
  ...
```
