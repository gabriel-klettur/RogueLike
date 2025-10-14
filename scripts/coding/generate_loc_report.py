#!/usr/bin/env python3
from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Dict, Iterable, List, Set, Tuple

# Extensiones de texto/código consideradas por defecto
DEFAULT_INCLUDE_EXTS: Set[str] = {
    ".py",
    ".md",
    ".json",
    ".yml",
    ".yaml",
    ".ini",
    ".toml",
    ".txt",
    ".csv",
    ".xml",
    ".cfg",
    ".bat",
    ".ps1",
    ".sh",
    ".js",
    ".ts",
    ".tsx",
    ".jsx",
    ".css",
    ".html",
    ".htm",
    ".rst",
}

# Carpetas a excluir por defecto (por nombre del segmento de ruta)
DEFAULT_EXCLUDE_DIRS: Set[str] = {
    ".git",
    "venv",
    ".venv",
    "env",
    ".env",
    "__pycache__",
    ".mypy_cache",
    ".ruff_cache",
    ".pytest_cache",
    ".tox",
    ".cache",
    ".idea",
    ".vscode",
    "node_modules",
    "logs",
    ".direnv",
    ".conda",
    ".eggs",
    "build",
    "dist",
}

@dataclass
class FileStat:
    rel_path: str
    loc: int


def parse_args() -> argparse.Namespace:
    repo_root_guess = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(
        description=(
            "Genera reportes de líneas de código (LOC) por carpeta y archivo, "
            "ordenados descendentemente."
        )
    )
    parser.add_argument(
        "--root",
        type=str,
        default=str(repo_root_guess / "src"),
        help="Directorio raíz a analizar (por defecto, la carpeta src/ del proyecto)",
    )
    parser.add_argument(
        "--output-dir",
        type=str,
        default=str(repo_root_guess / "logs" / "coding"),
        help="Directorio donde se guardarán los reportes (por defecto logs/coding/)",
    )
    parser.add_argument(
        "--includes",
        type=str,
        default=None,
        help=(
            "Lista separada por comas de extensiones a incluir (e.g., .py,.md). "
            "Por defecto se usan extensiones de texto/código comunes."
        ),
    )
    parser.add_argument(
        "--exclude-dirs",
        type=str,
        default=None,
        help=(
            "Lista separada por comas de nombres de carpetas a excluir (por nombre de segmento). "
            "Por defecto: .git, venv, .venv, __pycache__, caches, IDE, node_modules"
        ),
    )
    parser.add_argument(
        "--all-files",
        action="store_true",
        help=(
            "Intentar contar líneas de TODOS los archivos (no sólo por extensión). "
            "No recomendado: binarios pueden dar recuentos no representativos."
        ),
    )
    parser.add_argument(
        "--print-top",
        type=int,
        default=0,
        help=(
            "Si > 0, en el Markdown sólo mostrará los N archivos con más LOC por carpeta. "
            "El CSV siempre incluye todos."
        ),
    )
    return parser.parse_args()


def normalize_rel(path: Path, root: Path) -> str:
    try:
        rel = path.relative_to(root)
    except Exception:
        rel = path
    # Normalizar separadores a '/'
    rel_str = str(rel).replace("\\", "/")
    return rel_str if rel_str else "."


def is_excluded(path: Path, exclude_names: Set[str], root: Path) -> bool:
    try:
        parts = path.relative_to(root).parts
    except Exception:
        parts = path.parts
    return any(part in exclude_names for part in parts)


def should_count_file(file_path: Path, include_exts: Set[str], all_files: bool) -> bool:
    if all_files:
        return True
    return file_path.suffix.lower() in include_exts


def count_lines_fast(file_path: Path) -> int:
    """Cuenta líneas rápidamente leyendo en binario y contando saltos de línea."""
    try:
        with file_path.open("rb") as f:
            return sum(1 for _ in f)
    except Exception:
        return 0


def collect_stats(
    root: Path,
    include_exts: Set[str],
    exclude_dirs: Set[str],
    all_files: bool,
) -> Tuple[Dict[str, int], Dict[str, List[FileStat]]]:
    """
    - dir_totals[dir_key] = total LOC recursivo de esa carpeta
    - dir_files[dir_key] = lista de archivos DIRECTOS en esa carpeta (no recursivo)
    """
    dir_totals: Dict[str, int] = {}
    dir_files: Dict[str, List[FileStat]] = {}

    # Pre-poblar con todas las carpetas del proyecto, incluso si quedarán con 0 LOC
    # Incluir la raíz explícitamente
    all_dirs: List[Path] = [root]
    for p in root.rglob("*"):
        if p.is_dir():
            if is_excluded(p, exclude_dirs, root):
                continue
            all_dirs.append(p)
    for d in all_dirs:
        key = normalize_rel(d, root)
        dir_totals.setdefault(key, 0)
        dir_files.setdefault(key, [])

    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if is_excluded(path, exclude_dirs, root):
            continue
        if not should_count_file(path, include_exts, all_files):
            continue

        loc = count_lines_fast(path)

        # 1) Asignar archivo a su carpeta directa
        parent_key = normalize_rel(path.parent, root)
        dir_files.setdefault(parent_key, []).append(
            FileStat(rel_path=normalize_rel(path, root), loc=loc)
        )

        # 2) Acumular totales recursivos para esta carpeta y todas sus ancestras hasta root
        current = path.parent
        while True:
            key = normalize_rel(current, root)
            dir_totals[key] = dir_totals.get(key, 0) + loc
            if current == root:
                break
            current = current.parent

    # dir_totals y dir_files ya incluyen todas las carpetas por pre-población

    return dir_totals, dir_files


def write_reports(
    output_dir: Path,
    root: Path,
    dir_totals: Dict[str, int],
    dir_files: Dict[str, List[FileStat]],
    include_exts: Set[str],
    exclude_dirs: Set[str],
    print_top: int,
) -> List[Path]:
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    md_path = output_dir / f"coding_report_{timestamp}.md"
    csv_dirs_path = output_dir / f"coding_dirs_{timestamp}.csv"
    csv_files_path = output_dir / f"coding_files_{timestamp}.csv"

    # Ordenar carpetas por LOC descendente
    sorted_dirs = sorted(dir_totals.items(), key=lambda kv: kv[1], reverse=True)

    # Markdown
    with md_path.open("w", encoding="utf-8") as md:
        md.write(f"# Informe de Líneas de Código por Carpeta y Archivo\n\n")
        md.write(f"- Proyecto: `{normalize_rel(root, root)}`\n")
        md.write(f"- Generado: {datetime.now().isoformat(timespec='seconds')}\n")
        md.write(
            f"- Extensiones incluidas: {', '.join(sorted(include_exts)) if include_exts else 'TODOS (--all-files)'}\n"
        )
        md.write(
            f"- Carpetas excluidas: {', '.join(sorted(exclude_dirs)) if exclude_dirs else '(ninguna)'}\n\n"
        )

        for dir_key, total_loc in sorted_dirs:
            files = sorted(dir_files.get(dir_key, []), key=lambda fs: fs.loc, reverse=True)
            md.write(f"## {dir_key} — Total LOC: {total_loc}\n\n")
            if not files:
                md.write("(Sin archivos directos con extensiones consideradas)\n\n")
            else:
                if print_top and print_top > 0:
                    show_files = files[:print_top]
                    note_extra = len(files) - len(show_files)
                else:
                    show_files = files
                    note_extra = 0

                md.write("Archivo | LOC\n")
                md.write("--- | ---:\n")
                for fs in show_files:
                    abs_path = (root / fs.rel_path).resolve()
                    uri = abs_path.as_uri()
                    md.write(f"[`{fs.rel_path}`]({uri}) | {fs.loc}\n")
                md.write("\n")
                if note_extra > 0:
                    md.write(f"_... y {note_extra} archivos más en esta carpeta._\n\n")

            direct_loc = sum(fs.loc for fs in files)
            subfolders_loc = max(total_loc - direct_loc, 0)
            md.write(f"LOC directos en esta carpeta: {direct_loc}\n")
            md.write(f"LOC en subcarpetas: {subfolders_loc}\n\n")

    # CSV (carpetas)
    with csv_dirs_path.open("w", newline="", encoding="utf-8") as fcsv:
        writer = csv.writer(fcsv)
        writer.writerow(["directory", "total_loc", "num_files_direct_in_dir"])
        for dir_key, total_loc in sorted_dirs:
            writer.writerow([dir_key, total_loc, len(dir_files.get(dir_key, []))])

    # CSV (archivos)
    with csv_files_path.open("w", newline="", encoding="utf-8") as fcsv:
        writer = csv.writer(fcsv)
        writer.writerow(["directory", "file", "loc"])
        for dir_key, files in dir_files.items():
            for fs in sorted(files, key=lambda x: x.loc, reverse=True):
                writer.writerow([dir_key, fs.rel_path, fs.loc])

    return [md_path, csv_dirs_path, csv_files_path]


def main() -> int:
    args = parse_args()

    root = Path(args.root).resolve()
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    include_exts: Set[str]
    if args.includes:
        include_exts = {ext.strip().lower() for ext in args.includes.split(",") if ext.strip()}
        # Asegurar que empiecen por punto
        include_exts = {ext if ext.startswith(".") else f".{ext}" for ext in include_exts}
    else:
        include_exts = set(DEFAULT_INCLUDE_EXTS)

    exclude_dirs: Set[str]
    if args.exclude_dirs:
        exclude_dirs = {d.strip() for d in args.exclude_dirs.split(",") if d.strip()}
    else:
        exclude_dirs = set(DEFAULT_EXCLUDE_DIRS)

    dir_totals, dir_files = collect_stats(
        root=root,
        include_exts=include_exts,
        exclude_dirs=exclude_dirs,
        all_files=args.all_files,
    )

    outputs = write_reports(
        output_dir=output_dir,
        root=root,
        dir_totals=dir_totals,
        dir_files=dir_files,
        include_exts=include_exts if not args.all_files else set(),
        exclude_dirs=exclude_dirs,
        print_top=args.print_top,
    )

    print("Reportes generados:")
    for p in outputs:
        print(" -", p)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
