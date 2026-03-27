# Architecture — RogueLike Valkur (Python)

**Version:** 1.0  
**Stack:** Python 3.8+ · Pygame CE · SQLAlchemy · SQLite · OpenAI API  
**Type:** Single-player top-down roguelike — desktop application

---

## 1. System Overview

RogueLike Valkur is a **real-time top-down roguelike game** built entirely in Python with Pygame CE. It combines:

- A **custom ECS (Entity Component System)** engine with ~70 ordered systems
- **Procedural dungeon generation** and a multi-zone world
- An **embedded in-game tooling suite** (map, tile, entity, spell, spawner, particle, and lighting editors)
- **LLM-powered NPC dialogue** via the OpenAI Responses API (GPT-5 Nano)
- A **relational data layer** (SQLite + Alembic migrations) alongside JSON content files

The project is organized as a monorepo with five Python packages under `src/`, a local SQLite database, and a `data/` tree of JSON files for content and configuration.

---

## 2. Package Architecture

| Package | Responsibility |
|---------|---------------|
| `roguelike_engine` | Low-level engine layer — no game logic. Camera, map/tile/zone/buildings, audio, DB ORM, cache, console, diagnostics, input normalization |
| `roguelike_game` | Game logic layer — ECS world, ~70 systems, entity factories, game-loop managers, staged initialization |
| `roguelike_editors` | In-game overlay editors — map, tiles, buildings, entities, FSM, spells, spawner, particles, lighting |
| `roguelike_ui` | Reusable UI primitives — panels, widgets (button, grid, text input, list), HUD, formatting services |
| `minigames` | Self-contained minigames (Pylos, Soluna) launched from the main game |

**Dependency direction (strictly one-way):**

```
roguelike_game  ──►  roguelike_engine
roguelike_editors  ──►  roguelike_game, roguelike_engine, roguelike_ui
minigames  ──►  roguelike_engine, roguelike_ui
```

---

## 3. Key Architectural Patterns

### 3.1 Entity Component System (ECS)
The dominant runtime pattern. `ECSWorld` holds:
- **`ComponentRegistry`** — `Dict[ComponentType, Dict[EntityId, Component]]` for O(1) lookup
- **`SpatialIndex`** — broadphase spatial hashing over map tiles and building colliders, rebuilt lazily when invalidated
- **~50 update systems + ~30 render systems** — pre-ordered, benchmarked per frame, configurable z-order via `ecs-z-order.json`

All game logic (movement, combat, spells, AI, inventory, audio, chat) lives in independent, composable systems.

### 3.2 MVC per Domain (Engine Layer)
Each engine domain (`map/`, `tile/`, `buildings/`, `zone/`, `console/`, `diagnostics/`) follows a strict **Model–View–Controller** split with a stable public API exposed through `__init__.py`.

### 3.3 Builder + Facade (Factories)
`PlayerFactory` and `MonsterFactory` use a **Builder** to assemble complete component sets from data definitions and a **Facade** as a single entry point for game managers.

### 3.4 Staged Initialization Pipeline
Game startup executes a **22-stage sequential pipeline** (`GameInitializer`) with visual loading-screen feedback. Stages are injectable for extension and test isolation.

### 3.5 Strategy Pattern (LLM Providers)
The chat subsystem defines a `LLMProvider` abstract interface with pluggable backends:
- `Gpt5NanoProvider` — OpenAI Responses API (production)
- `DummyProvider` — no-op for headless testing

### 3.6 Benchmark Decorator
A `BenchmarkGroup` decorator wraps every hot path (events, update, render, and every individual ECS system) to collect per-frame timings into `perf_log` without changing call signatures.

### 3.7 Repository / Unit-of-Work (Data Layer)
`session_scope()` context manager wraps all SQLAlchemy operations in explicit transactions with rollback on error. `ImportLog` tracks content hashes for idempotent JSON→DB syncing.

---

## 4. Data Layer

| Store | Technology | Contents |
|-------|-----------|---------|
| `data/roguelike.sqlite3` | SQLite (WAL + NORMAL sync) via SQLAlchemy ORM | Entities, Items, Spells, Spawners (instances + templates + waves), Buildings, ItemPrices, entity asset sets |
| `data/**/*.json` | JSON + jsonschema validation | Map overlays, buildings, particles, lights, inventory drops, FSM config, world saves, input bindings |
| `logs/` | Python `RotatingFileHandler` | Engine log, init stage timings, per-session profiling |

**Migration strategy:** Alembic (10 migration files) with a `content_hash` idempotency guard in `import_log`.

---

## 5. External Integrations

| Service | Purpose | Protocol | Auth |
|---------|---------|---------|------|
| **OpenAI Responses API** | NPC dialogue via GPT-5 Nano | HTTPS POST `/v1/responses` | `OPENAI_API_KEY` env var or `.env` file |
| **WebSocket Server** | Planned optional multiplayer | WebSocket `ws://localhost:8000/ws` | — *(SUPUESTO: not yet implemented)* |

---

## 6. Deployment

| Mode | Command | Notes |
|------|---------|-------|
| Development | `python launcher.py` | `src/`-layout editable install via `pip install -e .` |
| Installed entry-point | `roguelike` | Registered in `setup.py` console_scripts |
| Distribution bundle | `pyinstaller roguelike.spec --onefile` | Bundles `assets/` and `data/`; Windows primary target |

---

## 7. Diagrams

| File | Type | Scope |
|------|------|-------|
| [`context.mmd`](context.mmd) | C4 Context | System boundaries + external actors |
| [`containers.mmd`](containers.mmd) | C4 Containers | Python packages + datastores |
| [`components.mmd`](components.mmd) | C4 Components | ECS core architecture |
| [`sequence-game-loop.mmd`](sequence-game-loop.mmd) | Sequence | 60 FPS frame pipeline |
| [`sequence-npc-chat.mmd`](sequence-npc-chat.mmd) | Sequence | LLM-powered NPC dialogue flow |
| [`deployment.mmd`](deployment.mmd) | Deployment | Local dev + PyInstaller distribution |

---

## 8. Assumptions & Uncertainties

| # | Item | Status |
|---|------|--------|
| 1 | WebSocket server at `ws://localhost:8000/ws` is configured but no server-side code found in this repo | **SUPUESTO:** planned/future feature |
| 2 | `aiortc` + `miniupnpc` dependencies suggest P2P/WebRTC multiplayer exploration | **SUPUESTO:** early exploration, not active in current gameplay |
| 3 | `OPENAI_API_KEY` required for NPC chat; falls back to `DummyProvider` if absent | **CONFIRMADO** |
| 4 | `pygame-menu` dependency present alongside `pygame-ce` | **SUPUESTO:** legacy or optional menu system; main game uses custom `roguelike_ui` |
| 5 | No authentication or authorization system found | **CONFIRMADO:** not applicable for single-player desktop |
| 6 | `tcod` (libtcod) dependency present; no direct usage found in explored paths | **SUPUESTO:** used for FOV or pathfinding algorithms in unexplored modules |
| 7 | `launcher.py` calls `os.system('cls')` → Windows primary target, but SDL2 makes cross-platform feasible | **SUPUESTO** |
| 8 | No CI/CD pipeline or containerization found | **CONFIRMADO:** pure local development workflow |
