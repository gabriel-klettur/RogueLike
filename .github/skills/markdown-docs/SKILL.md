---
name: markdown-docs
description: "Write and edit Markdown documentation files for this project. Use when creating or editing any .md file: roadmaps, audits, checklists, migration docs, guides. Covers markdownlint rules, ordered-list patterns, inline disable comments, and .markdownlintrc config."
argument-hint: "Describe the document to create or edit"
---

# Markdown Documentation

## When to Use
- Creating or editing any `.md` file in the workspace
- Adding new sections to roadmaps, audits, or checklists
- Diagnosing markdownlint warnings in the Problems panel
- Configuring markdownlint for a new folder

---

## Project Markdownlint Config

Two `.markdownlintrc` files disable MD029 globally:
- `d:\Python\RogueLike\.markdownlintrc`
- `unity/docs/Migration_python_to_unity/.markdownlintrc`

Both contain:
```json
{
  "default": true,
  "MD029": false
}
```

**Critical**: VS Code's markdownlint extension does **not** always pick up parent-directory
`.markdownlintrc` files for deeply nested subdirectories. Always add the inline comment at
the top of any `.md` file that uses non-sequential ordered lists.

---

## Rule: Roadmap / Numbered-Step Documents

### Problem: MD029 "Ordered list item prefix"
Roadmap files number steps globally (1–50) but each phase section resets the linter's
expected counter, causing false positives on every step past 1 in the section.

### Fix — ALWAYS add at the top of the file:
```markdown
<!-- markdownlint-disable MD029 -->
# Document Title
```

This is the **only reliable fix**. `.markdownlintrc` files in parent dirs are not guaranteed
to apply to subdirectories when VS Code's extension walks the config chain.

### When to create a local `.markdownlintrc`
Create one **in addition to** the inline comment when adding a new folder under
`unity/docs/Migration_python_to_unity/` that will contain multiple `.md` files:

```json
{
  "default": true,
  "MD029": false
}
```

Place it at the **folder level** that contains the `.md` files, not a parent.

---

## Common Markdownlint Rules & How to Handle Them

| Rule | Description | Fix |
|------|-------------|-----|
| MD029 | Ordered list prefix must be sequential from 1 | Add `<!-- markdownlint-disable MD029 -->` at top of file when using global step numbers |
| MD013 | Line length > 80 chars | Add `"MD013": false` to `.markdownlintrc`, or `<!-- markdownlint-disable MD013 -->` for specific sections |
| MD033 | Inline HTML not allowed | Add `"MD033": false` to config if HTML is intentional |
| MD041 | First line must be H1 | An inline disable comment before the H1 is fine — markdownlint still reads it |
| MD007 | Unordered list indent must be consistent | Use 2-space or 4-space consistently within a list |
| MD022 | Headings must be surrounded by blank lines | Always add blank line before and after `##` headings |
| MD032 | Lists must be surrounded by blank lines | Always add blank line before and after any list block |

---

## Checklist: Creating a New `.md` File

1. **Does it use non-sequential ordered lists (roadmap, step numbering)?**  
   → Add `<!-- markdownlint-disable MD029 -->` as the very first line.

2. **Is it in a new subfolder without a `.markdownlintrc`?**  
   → Create one alongside the file:  
   ```json
   { "default": true, "MD029": false }
   ```

3. **Does it use tables?**  
   → Ensure all rows have the same number of `|` columns. Misaligned pipes cause linter errors.

4. **Does it have numbered steps per-section that restart from 1?**  
   → Use `<!-- markdownlint-disable MD029 -->` globally, or  
   `<!-- markdownlint-disable-next-line MD029 -->` per-offending-line.

5. **Lines > 80 chars?**  
   → Either wrap the line or add `"MD013": false` to the folder's `.markdownlintrc`.

---

## Document Patterns Used in This Project

### Roadmap / Step files
```markdown
<!-- markdownlint-disable MD029 -->
# Roadmap Title

## Phase N – Description (Steps X-Y)

N. [ ] Step description.
   - Sub-detail.
N+1. [ ] Next step.
```

### Audit / Gap-analysis files
```markdown
# Audit: [System] – [Date]

## Summary

| System | Coverage | Status |
|--------|----------|--------|
| ...    | ...      | ...    |

## Detail

### [Subsystem]
- Finding 1
- Finding 2
```

### Checklist files
```markdown
# Checklist: [Topic]

## Section

- [ ] Item 1
- [x] Item 2 (done)
- [ ] Item 3
```

---

## Key Gotcha

**MD029 is the #1 false-positive in this project** because roadmap documents use a single
continuous numbering scheme (1–50+) split across multiple H2 sections. The linter resets
its counter at each new list block, so steps 7–12 in "Phase 1" look like they should be
1–6 to the linter.

**Solution chain (apply all three for new roadmap/step files):**
1. `<!-- markdownlint-disable MD029 -->` at the very top of the file.
2. `.markdownlintrc` with `"MD029": false` in the **same directory** as the file.
3. Ensure the root `.markdownlintrc` at `d:\Python\RogueLike\.markdownlintrc` also has `"MD029": false`.
