"""
Asset convention auditor for the Valkur Unity project.

Enforces every rule in `.github/skills/asset-pipeline/SKILL.md`
(sections "Naming Convention" / "Where assets live" / "Forbidden patterns")
on the assets under `unity/Valkur/Assets/`. Designed to run from CI, from
a pre-commit hook, or by hand:

    python tools/atlas/audit_asset_conventions.py             # report only
    python tools/atlas/audit_asset_conventions.py --strict    # exit 1 on any violation
    python tools/atlas/audit_asset_conventions.py --json out.json

Stdlib only. The matching EditMode test `AssetConventionsTests` runs the
same checks inside Unity so the same violations are caught from both
sides.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parents[2]
UNITY_ASSETS = REPO_ROOT / "unity" / "Valkur" / "Assets"
PROJECT_ROOT = UNITY_ASSETS / "_Project"

# ── Whitelists ───────────────────────────────────────────────────────────────

# Top-level entries allowed directly under Assets/ (anything else there is a
# violation — everything authored by us must live under _Project/).
ASSETS_ROOT_ALLOWED: set[str] = {
    "_Project",
    "Tests",
    "Settings",
    "Scenes",
    "Screenshots",
    "Resources",
    "StreamingAssets",
    "TextMesh Pro",            # Unity package install location
    "InputSystem_Actions.inputactions",
    "InputSystem_Actions.inputactions.meta",
    "UniversalRenderPipelineGlobalSettings.asset",
    "UniversalRenderPipelineGlobalSettings.asset.meta",
}

# Folders that DO NOT have to follow the snake_case-inside-PascalCase rule:
# vendor packs (preserve original drop), tier-2 recovery (active code), and
# catalog buckets (loaded by string name via Resources.Load).
PATH_PREFIX_WHITELIST_RELATIVE: tuple[str, ...] = (
    "_Project/Art/VFX/Vendor/",
    "_Project/Audio/Vendor/",
    "_Project/Data/Vendor/",
    "_Project/Data/Backups/",
    "_Project/Data/Catalogs/",
    "_Project/Data/ChatPersonas/",   # asset bucket loaded by string name
    "_Project/Data/Vendor",
    "_Project/Data/RuntimeJson/",
    "_Project/Data/LightPresets/",
    "_Project/Data/Bosses/",
    "_Project/Data/Worlds/",
    "_Project/Resources/",            # has its own dedicated rules below
    "TextMesh Pro/",                  # third-party package
    "Tests/",                         # tests sometimes need scaffolding files
    "Settings/",                      # Unity-required settings asset names
    "Scenes/",                        # bootstrap scenes
)

# Files allowed at Resources/ root (loaded by string name).
RESOURCES_ROOT_ALLOWED: set[str] = {
    "AudioCatalog.asset",
    "AudioCatalog.asset.meta",
    "SlashVfxCatalog.asset",
    "SlashVfxCatalog.asset.meta",
    "TileCatalog.asset",
    "TileCatalog.asset.meta",
}

# Top-level whitelist for Resources/ subfolder names (these are loaded by
# string path, so renaming them mid-project breaks Resources.Load callers).
RESOURCES_ROOT_FOLDER_ALLOWED: set[str] = {
    "Buildings",
    "Catalogs",
    "Input",
    "Placeholders",
    "Spells",
    "Tiles",
    "UI",
}

# ── Regex patterns ───────────────────────────────────────────────────────────

UPPERCASE_EXT_RE = re.compile(r"\.(PNG|JPG|JPEG|OGG|WAV|MP3|TIF|TIFF|BMP|GIF)$")
TOOLING_TEMP_PREFIX_RE = re.compile(
    r"^(ChatGPT[\s_]|screenshot[_-]|untitled([._-]|$))",
    re.IGNORECASE,
)
ITERATION_SUFFIX_RE = re.compile(
    r"_(old|copy|new|final|v\d+|tmp)\.[a-z0-9]+$",
    re.IGNORECASE,
)
FORBIDDEN_CHARS_RE = re.compile(r"[(),']| {2,}")  # spaces handled separately
INIT_TEST_SCENE_RE = re.compile(r"^InitTestScene\d+\.unity(\.meta)?$")
BACKUP_FOLDER_RE = re.compile(r"^(_?backups?|OLD|.+_old)$", re.IGNORECASE)

# ── Violation model ──────────────────────────────────────────────────────────


@dataclass
class Violation:
    rule: str
    path: str
    detail: str = ""

    def __str__(self) -> str:
        loc = self.path
        return f"  [{self.rule}] {loc}" + (f"  — {self.detail}" if self.detail else "")


@dataclass
class Report:
    violations: list[Violation] = field(default_factory=list)
    files_scanned: int = 0
    folders_scanned: int = 0

    def add(self, rule: str, path: Path, detail: str = "") -> None:
        rel = path.relative_to(REPO_ROOT).as_posix()
        self.violations.append(Violation(rule=rule, path=rel, detail=detail))

    @property
    def violation_count(self) -> int:
        return len(self.violations)

    def grouped(self) -> dict[str, list[Violation]]:
        out: dict[str, list[Violation]] = {}
        for v in self.violations:
            out.setdefault(v.rule, []).append(v)
        return out


# ── Helpers ──────────────────────────────────────────────────────────────────


def _rel_posix(path: Path) -> str:
    try:
        return path.relative_to(UNITY_ASSETS).as_posix()
    except ValueError:
        return path.as_posix()


def is_in_whitelist(path: Path) -> bool:
    rel = _rel_posix(path)
    return any(rel.startswith(p) for p in PATH_PREFIX_WHITELIST_RELATIVE)


def has_space(name: str) -> bool:
    return " " in name


# ── Individual rule checks ───────────────────────────────────────────────────


def check_assets_root(report: Report) -> None:
    """Nothing un-whitelisted may live at Assets/ root."""
    if not UNITY_ASSETS.exists():
        return
    for entry in UNITY_ASSETS.iterdir():
        if entry.name in ASSETS_ROOT_ALLOWED:
            continue
        if INIT_TEST_SCENE_RE.match(entry.name):
            report.add("init_test_scene_committed", entry,
                       "test runner artifact — must be in .gitignore + git-rm'd")
            continue
        report.add("assets_root_loose_entry", entry,
                   "everything authored by Valkur lives under _Project/")


def check_resources_root(report: Report) -> None:
    """Resources/ root must contain only canonical catalog SOs + whitelisted folders."""
    res = PROJECT_ROOT / "Resources"
    if not res.exists():
        return
    for entry in res.iterdir():
        if entry.is_dir():
            if entry.name not in RESOURCES_ROOT_FOLDER_ALLOWED:
                report.add("resources_root_unknown_folder", entry,
                           "add to RESOURCES_ROOT_FOLDER_ALLOWED if intentional")
            continue
        if entry.name in RESOURCES_ROOT_ALLOWED:
            continue
        report.add("resources_root_loose_file", entry,
                   "Resources/ ships whole — move into a subfolder")


def check_filename(report: Report, path: Path) -> None:
    """Per-file naming rules (extension case, tooling-temp prefixes, iteration suffixes, spaces)."""
    name = path.name
    rel = path.relative_to(REPO_ROOT).as_posix()

    # Vendor packs and TMP keep their original names; skip per-file checks
    # there but still record violations for code-quality forbidden patterns.
    in_vendor = is_in_whitelist(path)

    if UPPERCASE_EXT_RE.search(name):
        report.add("uppercase_extension", path,
                   f"rename to lowercase '{UPPERCASE_EXT_RE.search(name).group(0).lower()}'")

    if TOOLING_TEMP_PREFIX_RE.match(name):
        report.add("tooling_temp_filename", path,
                   "rename before committing (no ChatGPT/screenshot/untitled prefixes)")

    if not in_vendor:
        if ITERATION_SUFFIX_RE.search(name):
            report.add("iteration_suffix", path,
                       "drop _old/_copy/_new/_final/_vN/_tmp — git tracks history")

        if has_space(name) and not name.endswith(".meta"):
            # .meta sidecars inherit the source name; a space in the source
            # is the original sin and is reported once on the source file.
            report.add("filename_has_space", path,
                       "filenames must use snake_case — no spaces")

        if FORBIDDEN_CHARS_RE.search(name):
            report.add("forbidden_chars_in_filename", path,
                       "characters '(),' break tooling — rename")


def check_folder(report: Report, path: Path) -> None:
    """Per-folder naming rules (no _backups, no spaces, snake_case below top level)."""
    name = path.name
    rel = _rel_posix(path)

    # Always flag _backups except the whitelisted Data/Backups exception.
    if BACKUP_FOLDER_RE.match(name):
        if not rel.startswith("_Project/Data/Backups"):
            report.add("backup_folder_in_assets", path,
                       "git is the backup; only _Project/Data/Backups/ is whitelisted")

    if is_in_whitelist(path):
        return  # vendor packs / catalogs / etc. keep their original casing

    if has_space(name):
        report.add("folder_has_space", path,
                   "folder names must be snake_case — no spaces")


def walk_and_check(report: Report) -> None:
    if not UNITY_ASSETS.exists():
        return
    for path in UNITY_ASSETS.rglob("*"):
        # Skip Unity-generated junk we can't control.
        rel = path.relative_to(UNITY_ASSETS).as_posix()
        if rel.startswith(("Library/", "Temp/", "Logs/", "obj/", "Build/")):
            continue
        if path.is_dir():
            report.folders_scanned += 1
            check_folder(report, path)
        else:
            report.files_scanned += 1
            check_filename(report, path)


# ── Entry point ──────────────────────────────────────────────────────────────


def run() -> Report:
    report = Report()
    check_assets_root(report)
    check_resources_root(report)
    walk_and_check(report)
    return report


def emit_text(report: Report) -> None:
    if report.violation_count == 0:
        print(f"OK — {report.files_scanned} files / {report.folders_scanned} folders, no convention violations.")
        return
    print(f"FAIL — {report.violation_count} violation(s) across {report.files_scanned} files.\n")
    for rule, vs in sorted(report.grouped().items(), key=lambda kv: -len(kv[1])):
        print(f"[{rule}] x{len(vs)}")
        for v in vs[:50]:
            print(str(v))
        if len(vs) > 50:
            print(f"  ... {len(vs) - 50} more")
        print()


def emit_json(report: Report, dest: Path) -> None:
    payload = {
        "files_scanned": report.files_scanned,
        "folders_scanned": report.folders_scanned,
        "violation_count": report.violation_count,
        "violations": [asdict(v) for v in report.violations],
    }
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"Wrote {dest} ({report.violation_count} violations)")


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Audit Valkur asset organization conventions.")
    parser.add_argument("--strict", action="store_true",
                        help="Exit 1 if any violation is found (use in CI).")
    parser.add_argument("--json", type=Path, default=None,
                        help="Write the report to this path as JSON.")
    args = parser.parse_args(argv)

    report = run()
    if args.json:
        emit_json(report, args.json)
    else:
        emit_text(report)

    if args.strict and report.violation_count > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
