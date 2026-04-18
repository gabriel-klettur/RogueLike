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
    architectural_audit_2026-04-08.md
    professionalization_audit_2026-02-22.md
    editor_and_feature_depth_gap_2026-04-18.md   # ⚠️ READ FIRST: corrects 90% claim
  01_execution/editors/
    per_editor_checklists.md                     # actionable per-editor work items
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

- ⚠️ **`03_audits/editor_and_feature_depth_gap_2026-04-18.md`** — corrects the "90 % migrated" claim. The 11 in-game editors are at ~5–35 % feature coverage despite being marked DONE in the parity matrix. Start here.
- `03_audits/architectural_audit_2026-04-08.md` — last full architectural audit (compile, asmdefs, tests, performance).
- `03_audits/architectural_audit_2026-02-22.md` — historical baseline for the April 2026 audit.
- `03_audits/professionalization_audit_2026-02-22.md`
- `01_execution/editors/per_editor_checklists.md` — per-editor checklists (panels, sub-panels, services, undo) generated from the gap analysis.

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
