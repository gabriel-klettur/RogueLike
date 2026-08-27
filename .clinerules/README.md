# `.clinerules/` — Cline configuration for the Valkur workspace

Port of the `.claude/` system (Claude Code) to Cline. Both trees coexist:
`.claude/` serves Claude Code, `.clinerules/` serves Cline, and the canonical
long-form knowledge bases in `.github/skills/` + `.github/agents/` serve both.

## Layout

```text
.clinerules/
├── 00-cline-system.md        # How this system works + project paths
├── 01-bootstrap.md           # Mandatory context loading order
├── 02-cardinal-rules.md      # Always-on rules (console clean, assemblies, …)
├── 03-tooling-map.md         # Claude Code → Cline tool name reference
├── settings.json             # Versioned workspace settings (env paths)
├── settings.local.json       # Machine-local (gitignored) — UNITY_EDITOR path
├── skills/                   # Knowledge cards → point to .github/skills/
│   ├── asset-pipeline.md
│   ├── markdown-docs.md
│   ├── unity-development.md
│   ├── unity-performance.md
│   ├── unity-testing.md
│   ├── valkur-conventions.md
│   └── vfx-authoring.md
├── agents/                   # Specialist roles — adopt on demand
│   ├── asset-pipeline.md     ├── buildings-editor.md
│   ├── editor-ux-parity.md   ├── editor-wiring-auditor.md
│   ├── particles-editor.md   ├── performance-optimizer.md
│   ├── refactor-modularizer.md
│   ├── spell-vfx-director.md ├── tile-editor.md
│   ├── udemy-inspiration.md  ├── unity-architect.md
│   ├── unity-mcp-guardian.md └── unity-tester.md
└── workflows/                # Slash commands — invoke as /name.md
    ├── unity-clean.md        # Force-recompile + clean the console
    ├── unity-profile.md      # Profiling snapshot (frame timing, markers, GC)
    ├── unity-status.md       # One-screen project status
    ├── unity-test-new.md     # Scaffold a new test in the right folder/namespace
    └── unity-tests.md        # Run EditMode/PlayMode suites via MCP
```

## Usage

- **Rules** (root + `skills/` + `agents/`): Cline injects enabled files into
  context. Toggle them in the Cline Rules panel per task.
- **Workflows**: type `/` in the Cline input and pick one, e.g.
  `/unity-tests.md play`.
- **Agents**: when a task matches a role, read the file and adopt the role —
  or ask Cline to do it ("act as the buildings-editor agent").

## Maintenance

- Changed a convention? Edit **both** `.claude/` and `.clinerules/`
  (they are content-identical except for tool names: `mcp_unity_*` there,
  `unityMCP__*` here; `Read/Grep/Edit` there, `read_files/search_codebase/
  editor` here).
- The long-form docs in `.github/skills/` and `.github/agents/` are shared —
  edit them once.
