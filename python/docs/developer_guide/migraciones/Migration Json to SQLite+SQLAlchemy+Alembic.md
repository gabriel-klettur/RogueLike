 # Migración de JSON a SQLite + SQLAlchemy + Alembic

 Documento de alto nivel y guía paso a paso para migrar los datos del juego desde archivos JSON en `data/` a una base de datos local SQLite, usando SQLAlchemy (ORM) y Alembic (migraciones). Deja listo el camino para PostgreSQL en modo servidor/multiplayer sin reescribir la lógica del juego.

 ---

 ## 1) Objetivo y alcance

 - **Objetivo**: Mejorar tiempos de carga, consultas, integridad y escalabilidad manteniendo los JSON como fuente de verdad durante el desarrollo.
 - **Alcance MVP**: migrar datos usados por el runtime actual del juego: `spells`, `entities/hostiles`, `spawners` y colisiones de `buildings`.
 - **Fuera de alcance (por ahora)**: analítica avanzada, PostGIS, normalización completa de loot y variantes complejas.

 ---

 ## 2) Decisiones de arquitectura

 - **Local**: SQLite (archivo único, rápido, sin servidor) + SQLAlchemy 2.0 + Alembic.
 - **Futuro**: PostgreSQL para servidor/multiplayer. Se mantiene el ORM y migraciones; solo cambia `DATABASE_URL`.
 - **Patrón de acceso**: capa de repositorios (Repository Pattern) para desacoplar lógica de juego del almacenamiento.
 - **Importación**: incremental por hash de archivo; upsert por `id` dentro de transacciones.
 - **Flexibilidad**: campo `extra_json` para atributos aún no estabilizados, evitando sobre-modelado temprano.

 ---

 ## 3) Requisitos y rutas

 - Python 3.10+ (proyecto actual).
 - Dependencias a añadir: `SQLAlchemy>=2.0`, `alembic`. Para PostgreSQL: `psycopg2-binary` (solo servidor).
 - **Rutas de proyecto** (propuesta):
   - `src/roguelike_engine/db/engine.py` — creación del engine, sesión y `DATABASE_URL`.
   - `src/roguelike_engine/db/models.py` — modelos ORM.
   - `alembic/` — directorio de migraciones.
   - `scripts/import_spells.py` (y otros importadores).
   - `src/roguelike_game/ecs/repositories/` — interfaces e implementaciones (SQLite/JSON).
 - **Base de datos local**: `sqlite:///data/cache/roguelike.sqlite3` (activando WAL en init).

 ---

 ## 4) Esquema de datos (MVP)

 Claves naturales reutilizan slugs/ids de los JSON.

 - `spells`
   - `id` (TEXT PK), `name`, `mana_cost`, `cooldown_ms`, `element`, `damage_min`, `damage_max`, `range`, `area`, `duration_ms`, `cast_time_ms`, `tags` (TEXT CSV en SQLite), `extra_json` (JSON TEXT).
   - Índices sugeridos: `element`, (opcional) `tags` si se consulta por tag.
 - `entities`
   - `id` (TEXT PK), `kind` (hostile/npc), `name`, `level`, `hp`, `atk`, `def`, `speed`, `ai_behavior`, `loot_table_id` (nullable), `extra_json`.
   - Índices: `kind`, `ai_behavior`.
 - `spawners`
   - `id` (TEXT PK), `map_id`, `x`, `y`, `radius`, `max_count`, `respawn_seconds`, `conditions_json`, `spawn_table_id`.
   - Índices: `(map_id, x, y)`, `spawn_table_id`.
 - `spawn_table_entries`
   - `spawn_table_id` (TEXT), `entity_id` (TEXT FK a `entities.id`), `weight`, `min_qty`, `max_qty`.
   - Índices: `spawn_table_id`.
 - `building_instances`
   - `instance_id` (TEXT PK), `image_id` (TEXT), `spawn_id` (TEXT), `zone_id` (TEXT).
   - Índices: `image_id`, `spawn_id`.
 - `building_collisions`
   - `id` (INTEGER PK), `instance_id` (FK a `building_instances.instance_id`), `kind` (tile/polygon), `shape_wkt` (TEXT), `extra_json`.
   - Índices: `instance_id`, `kind`.
 - `import_log`
   - `source_path`, `content_hash`, `imported_at`, `row_count`, `version`.

 Nota: En PostgreSQL futuro, `extra_json` puede ser `JSONB` con índice GIN; colisiones podrían migrar a PostGIS.

 ---

 ## 5) Pasos detallados

 ### 5.1 Preparar entorno

 - Crear y activar entorno (Windows PowerShell):
```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
```

 - Instalar dependencias (local):
```powershell
pip install SQLAlchemy alembic
```

 - (Opcional, servidor) PostgreSQL:
```powershell
pip install psycopg2-binary
```

 ### 5.2 Configurar `DATABASE_URL` y engine

 Crear `src/roguelike_engine/db/engine.py`:
```python
from __future__ import annotations

from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

from sqlalchemy import create_engine, event, text
from sqlalchemy.orm import sessionmaker


DB_PATH = Path("data/cache/roguelike.sqlite3")
DB_PATH.parent.mkdir(parents=True, exist_ok=True)

DATABASE_URL = f"sqlite:///{DB_PATH.as_posix()}"

engine = create_engine(
    DATABASE_URL,
    future=True,
)


@event.listens_for(engine, "connect")
def _set_sqlite_pragma(dbapi_connection, connection_record) -> None:  # type: ignore[no-untyped-def]
    try:
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA journal_mode=WAL;")
        cursor.execute("PRAGMA synchronous=NORMAL;")
        cursor.close()
    except Exception:
        pass


SessionLocal = sessionmaker(bind=engine, autoflush=False, autocommit=False, future=True)


@contextmanager
def session_scope() -> Iterator:
    session = SessionLocal()
    try:
        yield session
        session.commit()
    except Exception:
        session.rollback()
        raise
    finally:
        session.close()
```

 ### 5.3 Inicializar Alembic

 - Inicializar proyecto Alembic (desde raíz):
```powershell
alembic init alembic
```

 - Editar `alembic.ini`:
  - Establecer `script_location = alembic`.
  - Dejar `sqlalchemy.url` vacío; Alembic leerá el engine desde `env.py`.

 - Editar `alembic/env.py` para usar nuestro engine:
```python
from logging.config import fileConfig
from sqlalchemy import engine_from_config, pool
from alembic import context

from src.roguelike_engine.db.engine import engine
from src.roguelike_engine.db.models import Base

config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

target_metadata = Base.metadata

def run_migrations_offline() -> None:
    url = str(engine.url)
    context.configure(url=url, target_metadata=target_metadata, literal_binds=True)
    with context.begin_transaction():
        context.run_migrations()

def run_migrations_online() -> None:
    connectable = engine
    with connectable.connect() as connection:
        context.configure(connection=connection, target_metadata=target_metadata)
        with context.begin_transaction():
            context.run_migrations()

if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
```

 ### 5.4 Modelos ORM y migración inicial

 Crear `src/roguelike_engine/db/models.py` (esqueleto):
```python
from __future__ import annotations

from sqlalchemy import Integer, String, Text, Float, ForeignKey
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class Spell(Base):
    __tablename__ = "spells"
    id: Mapped[str] = mapped_column(String, primary_key=True)
    name: Mapped[str] = mapped_column(String)
    element: Mapped[str | None] = mapped_column(String, nullable=True)
    mana_cost: Mapped[float | None] = mapped_column(Float, nullable=True)
    cooldown_ms: Mapped[int | None] = mapped_column(Integer, nullable=True)
    tags: Mapped[str | None] = mapped_column(String, nullable=True)  # CSV en SQLite
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class Entity(Base):
    __tablename__ = "entities"
    id: Mapped[str] = mapped_column(String, primary_key=True)
    kind: Mapped[str] = mapped_column(String)
    name: Mapped[str] = mapped_column(String)
    level: Mapped[int | None] = mapped_column(Integer, nullable=True)
    hp: Mapped[int | None] = mapped_column(Integer, nullable=True)
    atk: Mapped[int | None] = mapped_column(Integer, nullable=True)
    def_: Mapped[int | None] = mapped_column("def", Integer, nullable=True)
    speed: Mapped[float | None] = mapped_column(Float, nullable=True)
    ai_behavior: Mapped[str | None] = mapped_column(String, nullable=True)
    loot_table_id: Mapped[str | None] = mapped_column(String, nullable=True)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)


class Spawner(Base):
    __tablename__ = "spawners"
    id: Mapped[str] = mapped_column(String, primary_key=True)
    map_id: Mapped[str | None] = mapped_column(String, nullable=True)
    x: Mapped[int | None] = mapped_column(Integer, nullable=True)
    y: Mapped[int | None] = mapped_column(Integer, nullable=True)
    radius: Mapped[int | None] = mapped_column(Integer, nullable=True)
    max_count: Mapped[int | None] = mapped_column(Integer, nullable=True)
    respawn_seconds: Mapped[int | None] = mapped_column(Integer, nullable=True)
    conditions_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    spawn_table_id: Mapped[str | None] = mapped_column(String, nullable=True)


class SpawnTableEntry(Base):
    __tablename__ = "spawn_table_entries"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    spawn_table_id: Mapped[str] = mapped_column(String)
    entity_id: Mapped[str] = mapped_column(String, ForeignKey("entities.id"))
    weight: Mapped[int | None] = mapped_column(Integer, nullable=True)
    min_qty: Mapped[int | None] = mapped_column(Integer, nullable=True)
    max_qty: Mapped[int | None] = mapped_column(Integer, nullable=True)


class BuildingInstance(Base):
    __tablename__ = "building_instances"
    instance_id: Mapped[str] = mapped_column(String, primary_key=True)
    image_id: Mapped[str | None] = mapped_column(String, nullable=True)
    spawn_id: Mapped[str | None] = mapped_column(String, nullable=True)
    zone_id: Mapped[str | None] = mapped_column(String, nullable=True)


class BuildingCollision(Base):
    __tablename__ = "building_collisions"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    instance_id: Mapped[str] = mapped_column(String, ForeignKey("building_instances.instance_id"))
    kind: Mapped[str | None] = mapped_column(String, nullable=True)
    shape_wkt: Mapped[str] = mapped_column(Text)
    extra_json: Mapped[str | None] = mapped_column(Text, nullable=True)
```

 - Generar y aplicar migración inicial:
```powershell
alembic revision -m "initial schema" --autogenerate
alembic upgrade head
```

 ### 5.5 Importadores desde JSON (incremental por hash)

 - Estrategia: calcular SHA256 del archivo; si cambia respecto a `import_log`, reimportar con `ON CONFLICT`/upsert.
 - Ejemplo `scripts/import_spells.py` (pseudocódigo):
```python
import json, hashlib
from pathlib import Path
from sqlalchemy.dialects.sqlite import insert
from src.roguelike_engine.db.engine import session_scope
from src.roguelike_engine.db.models import Spell

def content_hash(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()

def run() -> None:
    src = Path("data/spells/spells.json")
    if not src.exists():
        raise SystemExit(f"Missing {src}")
    h = content_hash(src)
    # TODO: leer import_log y comparar; si igual, salir
    data = json.loads(src.read_text(encoding="utf-8"))
    with session_scope() as s:
        for item in data:
            stmt = insert(Spell).values(
                id=item["id"],
                name=item.get("name", item["id"]),
                element=item.get("element"),
                mana_cost=item.get("mana_cost"),
                cooldown_ms=item.get("cooldown_ms"),
                tags=",".join(item.get("tags", [])) if item.get("tags") else None,
                extra_json=json.dumps(item, ensure_ascii=False),
            )
            stmt = stmt.on_conflict_do_update(
                index_elements=[Spell.id],
                set_={
                    "name": stmt.excluded.name,
                    "element": stmt.excluded.element,
                    "mana_cost": stmt.excluded.mana_cost,
                    "cooldown_ms": stmt.excluded.cooldown_ms,
                    "tags": stmt.excluded.tags,
                    "extra_json": stmt.excluded.extra_json,
                },
            )
            s.execute(stmt)
        # TODO: registrar en import_log

if __name__ == "__main__":
    run()
```

 Repetir patrón para `entities`, `spawners` (+ `spawn_table_entries`), `building_instances` y `building_collisions` (con `shape_wkt`).

 ### 5.6 Capa de repositorios e integración

 - Definir interfaces en `src/roguelike_game/ecs/repositories/interfaces.py` (p. ej., `SpellsRepository`, `EntitiesRepository`).
 - Implementación SQLite en `sql_repo.py` y legado JSON en `json_repo.py` (para fallback/A-B).
 - Sustituir cargas directas de JSON en factories/managers por llamadas al repositorio. La lógica de FSM (`chase_state.py`, `patrol_state.py`) no debería cambiar.

 ### 5.7 Pruebas y verificación

 - Unit tests de importadores (validación mínima de filas) y repos.
 - Smoke test: arranque del juego leyendo desde SQLite, logs de tiempos de carga.
 - Validación con fixtures existentes en `tests/` cuando sea posible.

 ### 5.8 Rendimiento y ajustes

 - Activar WAL y `synchronous=NORMAL` (ya incluido en `engine.py`).
 - Índices en rutas calientes: búsquedas por `id`, `(map_id, x, y)`, `ai_behavior`.
 - Medir antes/después con un pequeño benchmark de arranque.

 ### 5.9 PostgreSQL (validación futura)

 - Cambiar `DATABASE_URL` a `postgresql+psycopg2://user:pass@host:5432/dbname`.
 - Ejecutar `alembic upgrade head`.
 - Probar importadores y smoke test. (Opcional: `JSONB` y PostGIS más adelante.)

 ---

 ## 6) Plan de ejecución recomendado

 1. Setup DB + Alembic + modelos y migración inicial (0.5–1 día).
 2. Importadores `spells` y `entities` + repositorios (0.5–1 día).
 3. Importadores `spawners` y colisiones + integración (0.5–1 día).
 4. Pruebas, tuning e índices + documentación final (0.5 día).

 Total MVP: 2–4 días, según complejidad real de JSON y validaciones.

 ---

 ## 7) Aceptación, rollback y riesgos

 - **Criterios de aceptación**
   - El juego arranca leyendo desde SQLite sin cambios en la jugabilidad.
   - Importación es idempotente; si los JSON no cambian, no hay reimport innecesaria.
   - Consultas clave usan índices y cumplen tiempos objetivos (arranque < X s).
 - **Rollback**
   - Conmutar repositorio a implementación JSON (flag de config) y borrar `data/cache/roguelike.sqlite3`.
 - **Riesgos y mitigación**
   - Inconsistencias en JSON → Validar contra `schemas/` existentes; usar `extra_json`.
   - Geometrías complejas → Guardar WKT ahora; PostGIS después.
   - Bloqueos en escritura SQLite → Transacciones cortas; escrituras en momentos seguros.

 ---

 ## 8) Terminología (glosario rápido)

 - **ORM (Object-Relational Mapper)** — Mapea clases a tablas — Evita SQL repetitivo — `session.get(Spell, "fireball")`.
 - **Migración** — Cambio versionado de esquema — Trazabilidad del DB — `alembic upgrade head`.
 - **Repository (patrón)** — Capa que encapsula acceso a datos — Desacopla juego/DB — `repo.get_spell(id)`.
 - **Upsert** — Insertar o actualizar si existe — Ideal para importación — `ON CONFLICT(id) DO UPDATE`.
 - **WAL** — Modo de journal en SQLite — Mejora concurrencia de lectura — `PRAGMA journal_mode=WAL`.
 - **Hash de contenido** — Huella del archivo — Importación incremental — SHA256 de `spells.json`.
 - **WKT** — Texto para geometrías — Simple en SQLite — `POLYGON((x y, ...))`.

 ---

 ## 9) Checklist de calidad

 - [ ] `DATABASE_URL` configurable y por defecto en `data/cache/roguelike.sqlite3`.
 - [ ] Alembic inicializado y migración inicial aplicada.
 - [ ] Modelos creados con claves naturales y `extra_json`.
 - [ ] Importadores idempotentes con hash y upsert.
  - [ ] Repositorios (SQLite/JSON) intercambiables por config.
  - [ ] WAL activado y índices en consultas calientes.
  - [ ] Smoke test de arranque pasando.
  - [ ] Documentación actualizada (este archivo) y comandos verificados en Windows.

 ---

 ## 10) Integración con MCP (Windsurf)

 Para facilitar la interacción de Cascade con la base de datos directamente desde el IDE, el marketplace de Windsurf ofrece MCPs específicos:

 - **PostgreSQL MCP**: permite acceso de solo lectura/inspección a bases PostgreSQL desde Cascade, útil para validar esquema y consultar datos en entorno servidor.
 - **SQLite MCP**: permite inspección y consultas a archivos `.sqlite3` locales, ideal para el flujo de desarrollo con SQLite.

 Pasos sugeridos:
 1. Abrir el **MCP Marketplace** en Windsurf y buscar "PostgreSQL" o "SQLite".
 2. Instalar el MCP correspondiente.
 3. Configurar la conexión (ruta del `.sqlite3` o `DATABASE_URL` de PostgreSQL) según la guía del MCP.
 4. Usar los comandos del MCP desde Cascade para consultas de verificación, inspección de tablas e índices.
