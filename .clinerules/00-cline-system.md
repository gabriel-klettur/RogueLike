# Cline System — how this workspace configures Cline

This workspace was originally configured for Claude Code (`.claude/`). This
`.clinerules/` directory is the Cline port of that system. The mapping:

| Claude Code | Cline equivalent | Where |
|---|---|---|
| `.claude/skills/*/SKILL.md` (knowledge cards) | Rules | `.clinerules/skills/*.md` |
| `.claude/agents/*.md` (sub-agent roles) | On-demand roles | `.clinerules/agents/*.md` |
| `.claude/commands/*.md` (slash commands) | Workflows | `.clinerules/workflows/*.md` — invoke as `/name.md` |
| `.claude/settings.json` (env vars) | Workspace settings | `.clinerules/settings.json` |
| `.claude/settings.local.json` (machine-local) | Local overrides | `.clinerules/settings.local.json` (gitignored) |

The canonical long-form knowledge bases live in `.github/skills/*/SKILL.md` and
`.github/agents/*.agent.md` — the files here are quick-reference pointers to
them. `.claude/` remains the source for Claude Code; keep both in sync when you
change conventions (same edit, both trees).

## Roles (agents) in Cline

Cline has no sub-agent config files. When a task matches a role in
`.clinerules/agents/`, **read that file and adopt the role** until the task is
done. When a role says "hand off to `unity-tester`", read the corresponding
agent file and continue in that role (or ask the user to re-prompt with it).

## Project paths (from `.clinerules/settings.json` → `env`)

| Variable | Value |
|---|---|
| `VALKUR_UNITY_PROJECT` | `d:/Python/RogueLike/unity/Valkur` |
| `VALKUR_PYTHON_REF` | `d:/Python/RogueLike/python` |
| `VALKUR_INSPIRATION` | `d:/Python/RogueLike/unity/Udemy_Inspiration/DungeonGunnerCourse` |
| `UNITY_EDITOR` | machine-local — see `.clinerules/settings.local.json` |

Use these instead of hardcoding paths in scripts and CLI invocations.

## Toggles

Every file under `.clinerules/` can be toggled on/off in Cline's
Rules/Workflows panel. Keep the root rules (`00`-`03`) always on; toggle
`skills/` and `agents/` files per task to manage context size.
