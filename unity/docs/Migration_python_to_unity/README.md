# Migration Python to Unity - Documentation Hub

This directory contains the migration knowledge base for the Valkur project.
It is organized by purpose (overview, execution, assets, audits) to keep planning,
status tracking, and technical decisions easy to navigate.

---

## Directory Structure

```text
Migration_python_to_unity/
  README.md
  00_overview/
    migration_program_overview.md
  01_execution/
    roadmap_50_steps.md
    phase_00_baseline_and_parity.md
  02_assets/
    phase_02_asset_pipeline_plan.md
    phase_02_sprite_atlas_normalization_report.md
  03_audits/
    architectural_audit_2026-02-22.md
    professionalization_audit_2026-02-22.md
```

---

## Quick Navigation

### 1) Program-level view
- `00_overview/migration_program_overview.md`

### 2) Execution tracking
- `01_execution/roadmap_50_steps.md`
- `01_execution/phase_00_baseline_and_parity.md`

### 3) Assets and content pipeline
- `02_assets/phase_02_asset_pipeline_plan.md`
- `02_assets/phase_02_sprite_atlas_normalization_report.md`

### 4) Architecture and quality audits
- `03_audits/architectural_audit_2026-02-22.md`
- `03_audits/professionalization_audit_2026-02-22.md`

---

## Naming Convention (applied)

- Lowercase snake_case filenames.
- Prefix folders by intent (`00_`, `01_`, `02_`, `03_`).
- Include date in time-bound audit reports.
- Keep one clear concern per document.

---

## Editorial Standard

Each document should include:
1. Purpose and scope.
2. Last update date.
3. Current status (when applicable).
4. Traceable references to code/files.
5. Explicit next steps and pending items.
