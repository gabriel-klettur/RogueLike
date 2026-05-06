"""
Generates the complete Fase 2_v1_Atlas.md document from unity_asset_actions.json.
Outputs to unity/Fase 2_v1_Atlas.md with every file listed per action group.
"""
import json
from pathlib import Path

ACTIONS_PATH = Path(__file__).resolve().parents[1] / "cache" / "atlas" / "unity_asset_actions.json"
AUDIT_PATH = Path(__file__).resolve().parents[1] / "cache" / "atlas" / "unity_asset_audit.json"
OUTPUT_PATH = Path(__file__).resolve().parents[2] / "unity" / "Fase 2_v1_Atlas.md"

with open(ACTIONS_PATH, "r", encoding="utf-8") as f:
    actions = json.load(f)

with open(AUDIT_PATH, "r", encoding="utf-8") as f:
    audit = json.load(f)

# Compute stats
from collections import defaultdict
by_cat = defaultdict(lambda: {"count": 0, "size_mb": 0.0, "dims": defaultdict(int), "modes": defaultdict(int)})
for img in audit:
    cat = img["category"]
    by_cat[cat]["count"] += 1
    by_cat[cat]["size_mb"] += img["size_kb"] / 1024
    by_cat[cat]["dims"][f"{img['w']}x{img['h']}"] += 1
    by_cat[cat]["modes"][img["mode"]] += 1

total_files = len(audit)
total_mb = round(sum(i["size_kb"] for i in audit) / 1024, 1)

need_change = sum(len(v) for k, v in actions.items() if not k.endswith("_ok"))
already_ok = sum(len(v) for k, v in actions.items() if k.endswith("_ok"))

lines = []
def w(text=""):
    lines.append(text)

# ============================================================
# HEADER
# ============================================================
w("# Fase 2 — Atlas de Sprites: Analisis y Plan de Normalizacion")
w()
w("> **IMPORTANTE:** Este analisis se realiza sobre los assets de **Unity**")
w("> Operates on the Unity Art folder (`unity/Valkur/Assets/_Project/Art/`).")
w("> Los archivos originales de Python **no se tocan**. Toda normalizacion")
w("> se aplica exclusivamente sobre los assets ya migrados en el proyecto Unity.")
w()
w("> Datos generados automaticamente por `tools/atlas/unity_asset_audit.py`.")
w()
w("---")
w()

# ============================================================
# 1. RESUMEN EJECUTIVO
# ============================================================
w("## 1. Resumen Ejecutivo")
w()
w(f"- **Total archivos de imagen:** {total_files}")
w(f"- **Tamano total:** {total_mb} MB")
w(f"- **Archivos que necesitan cambios:** {need_change} (76%)")
w(f"- **Archivos ya correctos:** {already_ok} (24%)")
w()
w("**Respuesta: NO se pueden crear atlas directamente.** Los problemas criticos:")
w()
w("| Problema | Archivos afectados | Severidad |")
w("|----------|-------------------|-----------|")
w(f"| Tiles a 48x48 (deben ser 32x32) | {len(actions.get('TILES_resize_48_to_32', []))} | CRITICO |")
w(f"| Tiles a 64x64 (deben ser 32x32) | {len(actions.get('TILES_resize_64_to_32', []))} | CRITICO |")
w(f"| Tilesets sin cortar (>64px) | {len(actions.get('TILES_slice_tileset', []))} | CRITICO |")
w(f"| Tiles a 16x16 (deben ser 32x32) | {len(actions.get('TILES_upscale_16_to_32', []))} | MEDIO |")
w(f"| NPCs sobredimensionados (>256px) | {len(actions.get('NPC_autocrop_resize', [])) + len(actions.get('NPC_autocrop_resize_rgba', []))} | CRITICO |")
w(f"| Buildings sobredimensionados | {len(actions.get('BUILDINGS_autocrop', [])) + len(actions.get('BUILDINGS_autocrop_rgba', []))} | CRITICO |")
w(f"| Items sobredimensionados (>64px) | {len(actions.get('ITEMS_resize_to_64', [])) + len(actions.get('ITEMS_resize64_rgba', []))} | CRITICO |")
w(f"| UI sobredimensionada (>128px) | {len(actions.get('UI_resize', [])) + len(actions.get('UI_resize_rgba', []))} | ALTO |")
w(f"| Spells sobredimensionados (>128px) | {len(actions.get('SPELLS_resize_to_128', [])) + len(actions.get('SPELLS_resize128_rgba', []))} | ALTO |")
w(f"| VFX sobredimensionados | {len(actions.get('VFX_resize', []))} | MEDIO |")
w()
w("---")
w()

# ============================================================
# 2. INVENTARIO POR CATEGORIA
# ============================================================
w("## 2. Inventario por Categoria")
w()
w("| Categoria | Archivos | Tamano | Dimensiones dominantes | Modos color |")
w("|-----------|----------|--------|------------------------|-------------|")
for cat in sorted(by_cat.keys()):
    d = by_cat[cat]
    top_dims = sorted(d["dims"].items(), key=lambda x: -x[1])[:3]
    dims_str = ", ".join(f"{k}({v})" for k, v in top_dims)
    modes_str = ", ".join(f"{k}:{v}" for k, v in d["modes"].items())
    w(f"| **{cat}** | {d['count']} | {round(d['size_mb'], 1)} MB | {dims_str} | {modes_str} |")
w(f"| **TOTAL** | **{total_files}** | **{total_mb} MB** | | |")
w()
w("---")
w()

# ============================================================
# 3-12. PER-SECTION FILE LISTS
# ============================================================

# Helper to write a section
def write_action_section(section_num, title, description, action_keys, target_size):
    w(f"## {section_num}. {title}")
    w()
    w(description)
    w()
    total_in_section = sum(len(actions.get(k, [])) for k in action_keys)
    w(f"**Total archivos a modificar: {total_in_section}**")
    w(f"**Tamano objetivo: {target_size}**")
    w()
    for key in action_keys:
        items = actions.get(key, [])
        if not items:
            continue
        # Derive human-readable action name
        action_label = key.split("_", 1)[1] if "_" in key else key
        w(f"### {key} ({len(items)} archivos)")
        w()
        w("| # | Archivo | Dimensiones | Modo | Tamano |")
        w("|---|---------|-------------|------|--------|")
        for i, item in enumerate(sorted(items, key=lambda x: x["path"]), 1):
            parts = item["tag"].split(" ")
            dim = parts[0] if len(parts) > 0 else "?"
            mode = parts[1] if len(parts) > 1 else "?"
            size = parts[2] if len(parts) > 2 else "?"
            w(f"| {i} | `{item['path']}` | {dim} | {mode} | {size} |")
        w()

# --- TILES ---
write_action_section(
    "3", "TILES — Archivos a modificar",
    "Todos los tiles deben ser **32x32 RGBA** para funcionar con Unity Tilemap.\n"
    "Ruta base: `_Project/Art/Tiles/`",
    ["TILES_resize_48_to_32", "TILES_resize_64_to_32", "TILES_slice_tileset", "TILES_upscale_16_to_32"],
    "32x32 RGBA"
)

# Tiles OK
tiles_ok = actions.get("TILES_ok", [])
w(f"### TILES ya correctos ({len(tiles_ok)} archivos — sin cambios)")
w()
w("Estos tiles ya son 32x32 RGBA y no necesitan modificacion.")
w()
w("---")
w()

# --- CHARACTERS ---
chars_ok = actions.get("CHARACTERS_ok", [])
w("## 4. CHARACTERS — Sin cambios necesarios")
w()
w(f"Los {len(chars_ok)} sprite sheets de personajes estan correctamente estructurados.")
w("Unity los importa como Sprite Mode=Multiple y los corta con Sprite Editor (grid 128x128).")
w()
w("| # | Archivo | Dimensiones | Modo |")
w("|---|---------|-------------|------|")
for i, item in enumerate(sorted(chars_ok, key=lambda x: x["path"]), 1):
    parts = item["tag"].split(" ")
    w(f"| {i} | `{item['path']}` | {parts[0]} | {parts[1]} |")
w()
w("---")
w()

# --- NPC ---
write_action_section(
    "5", "NPC — Archivos a modificar",
    "Los NPCs son sprites generados por IA a 1024x1024 que necesitan:\n"
    "1. **Auto-crop** (recortar transparencia/fondo)\n"
    "2. **Resize** a 128x128 (consistente con player frames)\n"
    "3. **Ensure RGBA** (los RGB necesitan deteccion de fondo + transparencia)\n\n"
    "Ruta base: `_Project/Art/NPC/`",
    ["NPC_autocrop_resize", "NPC_autocrop_resize_rgba"],
    "128x128 RGBA"
)

npc_ok = actions.get("NPC_ok", [])
w(f"### NPC ya correctos ({len(npc_ok)} archivos — sin cambios)")
w()
w("| # | Archivo | Dimensiones | Modo |")
w("|---|---------|-------------|------|")
for i, item in enumerate(sorted(npc_ok, key=lambda x: x["path"]), 1):
    parts = item["tag"].split(" ")
    w(f"| {i} | `{item['path']}` | {parts[0]} | {parts[1]} |")
w()
w("---")
w()

# --- BUILDINGS ---
write_action_section(
    "6", "BUILDINGS — Archivos a modificar",
    "Buildings/props generados por IA a 1024x1024. Necesitan:\n"
    "1. **Auto-crop** (recortar transparencia)\n"
    "2. **Mantener aspect ratio** (no forzar cuadrado)\n"
    "3. **Ensure RGBA** donde sea RGB\n\n"
    "Ruta base: `_Project/Art/Buildings/`",
    ["BUILDINGS_autocrop", "BUILDINGS_autocrop_rgba"],
    "Auto-crop + max 256px lado mayor"
)

buildings_ok = actions.get("BUILDINGS_ok", [])
w(f"### BUILDINGS ya correctos ({len(buildings_ok)} archivos — sin cambios)")
w()
for item in sorted(buildings_ok, key=lambda x: x["path"]):
    w(f"- `{item['path']}` [{item['tag']}]")
w()
w("---")
w()

# --- ITEMS ---
write_action_section(
    "7", "ITEMS — Archivos a modificar",
    "Iconos de inventario a 1024x1024 que se renderizan a ~32-64px.\n"
    "Resize a **64x64** para atlas eficiente.\n\n"
    "Ruta base: `_Project/Art/Items/`",
    ["ITEMS_resize_to_64", "ITEMS_resize64_rgba"],
    "64x64 RGBA"
)

items_ok = actions.get("ITEMS_ok", [])
if items_ok:
    w(f"### ITEMS ya correctos ({len(items_ok)} archivos)")
    w()
w("---")
w()

# --- UI ---
write_action_section(
    "8", "UI — Archivos a modificar",
    "Iconos de editor/gameplay a 1024x1024 o 1536x1024.\n"
    "- **Iconos de herramientas:** resize a 128x128\n"
    "- **Backgrounds/intros:** mantener tamano (no van en atlas)\n"
    "- **Todos los RGB:** convertir a RGBA\n\n"
    "Ruta base: `_Project/Art/UI/`",
    ["UI_resize", "UI_resize_rgba"],
    "128x128 RGBA (iconos) / mantener (backgrounds)"
)

ui_ok = actions.get("UI_ok", [])
if ui_ok:
    w(f"### UI ya correctos ({len(ui_ok)} archivos)")
    w()
w("---")
w()

# --- SPELLS ---
write_action_section(
    "9", "SPELLS — Archivos a modificar",
    "Sprites de hechizos a 1024x1024. Resize a **128x128**.\n\n"
    "Ruta base: `_Project/Art/Spells/`",
    ["SPELLS_resize_to_128", "SPELLS_resize128_rgba"],
    "128x128 RGBA"
)

spells_ok = actions.get("SPELLS_ok", [])
if spells_ok:
    w(f"### SPELLS ya correctos ({len(spells_ok)} archivos)")
    w()
w("---")
w()

# --- VFX ---
write_action_section(
    "10", "VFX — Archivos a modificar",
    "Explosiones sobredimensionadas o con extension mixta (.PNG).\n\n"
    "Ruta base: `_Project/Art/VFX/`",
    ["VFX_resize"],
    "256x256 RGBA (o 128x128)"
)

vfx_ok = actions.get("VFX_ok", [])
w(f"### VFX ya correctos ({len(vfx_ok)} archivos — particulas 256x256 RGBA, sin cambios)")
w()
w("---")
w()

# --- MISC ---
misc = actions.get("MISC_review", [])
if misc:
    w("## 11. MISC — Revision manual")
    w()
    w("| # | Archivo | Dimensiones | Modo | Tamano |")
    w("|---|---------|-------------|------|--------|")
    for i, item in enumerate(sorted(misc, key=lambda x: x["path"]), 1):
        parts = item["tag"].split(" ")
        dim = parts[0] if len(parts) > 0 else "?"
        mode = parts[1] if len(parts) > 1 else "?"
        size = parts[2] if len(parts) > 2 else "?"
        w(f"| {i} | `{item['path']}` | {dim} | {mode} | {size} |")
    w()
    w("---")
    w()

# ============================================================
# RESUMEN DE ACCIONES
# ============================================================
w("## 12. Resumen de Acciones por Tipo")
w()
w("| Accion | Archivos | Descripcion |")
w("|--------|----------|-------------|")
for key in sorted(actions.keys()):
    count = len(actions[key])
    if key.endswith("_ok"):
        w(f"| {key} | {count} | Sin cambios |")
    else:
        w(f"| **{key}** | **{count}** | **Requiere modificacion** |")
w(f"| **TOTAL CAMBIOS** | **{need_change}** | |")
w(f"| TOTAL OK | {already_ok} | |")
w()
w("---")
w()

# ============================================================
# PIPELINE
# ============================================================
w("## 13. Pipeline de Normalizacion")
w()
w("> **REGLA FUNDAMENTAL:** Solo se modifican archivos en `unity/Valkur/Assets/_Project/Art/`.")
w("> Read-only audit — no source files are mutated.")
w()
w("### Herramienta: Script Python con Pillow")
w()
w("```bash")
w("pip install Pillow")
w("python scripts/normalize_unity_assets.py --dry-run    # Ver plan sin ejecutar")
w("python scripts/normalize_unity_assets.py --execute     # Ejecutar normalizacion")
w("python scripts/normalize_unity_assets.py --validate    # Validar resultados")
w("```")
w()
w("### Operaciones por tipo:")
w()
w("| Operacion | Descripcion | Categorias afectadas |")
w("|-----------|-------------|---------------------|")
w("| `resize` | Escalar a tamano objetivo (NEAREST para pixel art) | Tiles, Items, UI, Spells, VFX |")
w("| `auto_crop` | Recortar transparencia con `getbbox()` | NPC, Buildings |")
w("| `ensure_rgba` | Convertir RGB->RGBA (detectar fondo por esquinas) | NPC, Buildings, UI, Items |")
w("| `slice_tileset` | Cortar en grid 32x32, descartar vacios | Tiles (tilesets) |")
w("| `upscale` | Escalar 16x16->32x32 con NEAREST | Tiles (rock_grass) |")
w()
w("### Orden recomendado (por fases):")
w()
w("1. **Fase 2a — Tiles** (424 archivos): Critico para Tilemap")
w("2. **Fase 2b — NPC + Characters** (90 archivos): Critico para gameplay")
w("3. **Fase 2c — Items** (48 archivos): Critico para inventario")
w("4. **Fase 2d — UI + Spells + VFX + Buildings** (~215 archivos): Complementario")
w()
w("---")
w()
w("## 14. Grupos de SpriteAtlas para Unity")
w()
w("| Atlas | Contenido | Sprites | Max Texture | Formato |")
w("|-------|-----------|---------|-------------|---------|")
w("| `Atlas_Tiles_Ground` | floor, grass, sand, dirt (32x32) | ~300 | 2048x2048 | RGBA32, Point, Pad=2 |")
w("| `Atlas_Tiles_Dungeon` | dungeon, wall (32x32) | ~200 | 2048x2048 | RGBA32, Point, Pad=2 |")
w("| `Atlas_Characters` | Player + NPC frames (128x128) | ~100 | 4096x4096 | RGBA32, Point, Pad=2 |")
w("| `Atlas_Items` | Item icons (64x64) | ~50 | 512x512 | RGBA32, Point, Pad=2 |")
w("| `Atlas_UI` | HUD/toolbar icons (128x128) | ~80 | 2048x2048 | RGBA32, Bilinear, Pad=2 |")
w("| `Atlas_VFX` | Particles + explosions (256x256) | ~230 | 4096x4096 | RGBA32, Bilinear, Pad=0 |")
w("| `Atlas_Spells` | Projectiles + spells (128x128) | ~20 | 1024x1024 | RGBA32, Point, Pad=2 |")
w()
w("---")
w()
w("*Documento generado automaticamente por `tools/atlas/generate_atlas_doc.py`")
w(f"a partir de `unity_asset_audit.json` ({total_files} archivos, {total_mb} MB).*")

# Write output
content = "\n".join(lines)
with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
    f.write(content)

print(f"Document written to: {OUTPUT_PATH}")
print(f"Total lines: {len(lines)}")
