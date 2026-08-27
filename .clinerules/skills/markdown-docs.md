
# Markdown Docs — Valkur

The full canonical knowledge base lives at:

**[`.github/skills/markdown-docs/SKILL.md`](../../.github/skills/markdown-docs/SKILL.md)**

Read it directly when you need:

| Need | Section in source |
|---|---|
| Project markdownlint config (two `.markdownlintrc` files; MD029 disabled) | "Project Markdownlint Config" |
| MD028 (no blank lines inside blockquotes) — fix pattern | "Common warnings" |
| Ordered-list patterns that work in this project | "Ordered lists" |
| Inline lint disables / re-enables | "Inline disable comments" |
| When to add a new `.markdownlintrc` for a subfolder | "Per-folder config" |

## Quick reference

- Two project-level configs both set `{ "default": true, "MD029": false }`:
  - `d:\Python\RogueLike\.markdownlintrc`
  - `unity/docs/Migration_python_to_unity/.markdownlintrc`
- **MD028** (blank line inside blockquote): use `>` on every line, including separators. To split paragraphs inside a quote, write `>\n>` not `>\n\n>`.
- **MD029** (ordered-list prefix): disabled globally — use `1.` style, `1) 2) 3)` style, or any other; consistent within the same list is what matters.
- **Frontmatter**: YAML between `---` markers. `description` field is mandatory for skills and agents; quote the string when it contains colons or angle brackets.
- **Tables**: leave a blank line before the header row; the separator row uses `|---|---|` (no surrounding pipes needed but they're idiomatic in this repo).

## Hard constraints

- **DO NOT** create a third `.markdownlintrc` without explicit reason — keep configs centralised.
- **DO NOT** use HTML inside Markdown unless the same effect cannot be reached with native syntax.
- **DO** wrap long URLs in `<>` so MD034 (bare URL) stays quiet.
- **DO** preserve heading hierarchy (no skipping H2 → H4 — MD001).
