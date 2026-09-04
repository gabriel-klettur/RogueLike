# 216 building templates deleted from the working tree

**Date:** 2026-09-04 · **Status:** recovered, root cause UNKNOWN

## What happened

216 `BuildingTemplateData` assets (432 files with their `.meta`) vanished from the working
tree under `Data/Catalogs/Buildings/`. All were present in `HEAD`; none had been committed
as deleted. `BuildingCatalog.asset` had also been rewritten without them, so Unity held a
catalog of 1258 against 1474 templates once they came back.

Ids **4–313** — the first wave of buildings. That is a RANGE and not an enumeration: 216 ids
were removed from a span of 310, so roughly 94 templates inside the band survived untouched
(demonstrated below by `BuildingTemplate_91`). The band includes `BuildingTemplate_64`, the
door/interior example CLAUDE.md documents, and `BuildingTemplate_68`, a mine.

It surfaced only as a shipped-data test failure:

```text
HarvestingDataTests.EveryMineTemplate_IsWiredToTheMineProfile
  Missing shipped asset at Assets/_Project/Data/Catalogs/Buildings/BuildingTemplate_68.asset
```

## What it was NOT

- **Not `BuildingPropImporter`.** It contains zero `DeleteAsset` calls, and only ever calls
  `catalog.UpsertTemplate` — it cannot remove a template or a catalog entry.
- **Not the coastal import wave that was running.** Templates measured 1176 immediately
  before its Apply and 1258 immediately after: 1176 + 82 = 1258 exactly. The importer also
  assigned new ids from 1393 against a max existing id of 1392, so the 216 gaps were already
  there before it ran.
- **Not the progression seeder.** Its only `AssetDatabase.DeleteAsset` lives in an
  explicitly-confirmed "Regenerate (Overwrite)" menu item that was never invoked.

## What it WAS

**Unknown. Nobody found the deleting mechanism.** Two sessions investigated independently and
each ruled itself out with evidence rather than assertion; the third had already ended. The
list above is what was excluded, not a narrowing towards a culprit — do not read a cause into
it, and in particular do not read one into `BuildingPropImporter` merely because it is the
tool that writes to this folder.

What is known: the EditMode suite had been run many times that day by three concurrent
sessions. CLAUDE.md records a related failure in this same folder — `BuildingPropImporter`
recording 193 template creations on the GLOBAL undo stack, popped by the suite's runtime-editor
undo tests — but that one left the assets PRESENT and empty. This one removed the files.
Either a second mechanism, or the same one grown worse. Neither was demonstrated.

## Recovery

> **Do not start here.** Recovery destroys two pieces of evidence you cannot get back — the
> surviving files' mtimes and the `HEAD`-vs-working-tree schema gap. Run steps 1 and 2 of
> "If it reappears" below first. This section is the detail those steps refer to.

Two halves, and the first alone is not enough.

**1. Restore the files, path-scoped.**

Build the list and PROVE the restore is purely additive before running it — the checks go
first, because the checkout is what they are guarding:

```bash
git status --porcelain -- unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/ \
  | awk '$1=="D"{print $2}' > restore_list.tmp

# Must print 0: nothing on the list may exist in the working tree, or the checkout
# would overwrite a live file rather than restore a missing one.
while read -r f; do [ -e "$f" ] && echo "$f"; done < restore_list.tmp | wc -l

# Must print 0: everything on the list must resolve in HEAD, or the restore is partial.
while read -r f; do git cat-file -e "HEAD:$f" 2>/dev/null || echo "$f"; done < restore_list.tmp | wc -l
```

Only then:

```bash
xargs -a restore_list.tmp -d '\n' -n 60 git checkout --
```

`xargs` in batches because the full list overflows the command line (`Argument list too long`
at 432 paths).

**Never `git checkout --` the whole directory:** 1077 sibling templates were legitimately
modified and a blanket checkout throws those away — including any import that legitimately
updated them.

**2. Re-register them in the catalog.**

Restoring the `.asset` files leaves Unity with more templates on disk than in the catalog, and
`BuildingPropImporter` will NOT fix it — it upserts only MANIFEST entries, and many of the 216
are hand-authored templates no manifest describes. Force-import the folder, then upsert every
template the catalog is missing through `BuildingCatalog.UpsertTemplate`, which is the same
call the importer makes.

## The trap that makes this worse than it looks

**`git checkout --` on a deleted asset restores its COMMITTED state, so in a tree where a
schema has grown since the last commit, every restored asset silently reverts to the OLD
SCHEMA.** The file comes back; fields that only ever existed in the working tree do not. The
failure that follows is a NULL FIELD on a file that is present, which reads as a fresh bug in
whatever consumes it rather than as fallout from the restore.

Here, `BuildingTemplateData` had gained `interactable` and `destruction` since the last commit,
so all 216 came back on the pre-`destruction` schema. `HarvestingDataTests` went from
"asset missing" to `Template 68 draws a mine and cannot be mined`.

Bounding it is cheap — ask the modified siblings which fields the working tree added:

```bash
git diff -U0 -- unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/ | grep -E '^[+-]  [a-z]'
  1079 + interactable
  1079 + destruction
```

That is the complete list of what a restore can have dropped. Re-check exactly those.

**Run it BEFORE anyone commits, and that is not a nicety.** The trick works only because the
siblings are still uncommitted: it reads the gap between `HEAD` and the working tree, and a
commit closes that gap. Once the tree is committed the two agree, the diff comes back empty,
and there is no longer any record of which fields the restored assets are missing — the
damage stays, the evidence does not. If a restore like this happens on a dirty tree, do the
diff first and write the field list down.

**Measured fallout:** `interactable` — one template in the catalog is true (#108
`forest_decoration/corrupto/totem_podrido`) and it survived, so nothing lost. `destruction` —
3 mines (fixed first) and **125 trees**. `Buildings/vegetation/` holds only `tree_*`: 125 bare
and 12 wired, and every bare one fell inside the restored id band while all 12 wired ones were
survivors. `Buildings/nature/` lost nothing — its bare entries are bushes and coastal props
that were never harvestable.

Rewired through `AssetDatabase` + `SetDirty` + `SaveAssets`, guarded on `assetPath` still
starting with `Buildings/vegetation/tree_` so a renamed template is refused. Guard on the
PATH, never on the id band: the band is evidence about what happened, not a rule about what a
template is.

Final state: 1474 templates on disk and in the catalog, all resolving through
`Resources.Load`; 573 wired — `DP_tree_common` 553, `DP_fish_school` 16, `DP_mine_iron` 4.

### The mines, and what they say about the id band

The four templates `HarvestingDataTests` names are the cleanest control in this incident,
because they straddle the deletion:

| Template | Deleted? | `destruction` after restore |
|---|---|---|
| 68 | yes | null — rewired to `DP_mine_iron` |
| 91 | **no** | intact |
| 210 | yes | null — rewired |
| 211 | yes | null — rewired |

All four ids sit inside the 4–313 band, and 91 kept its working-tree wiring while the other
three lost theirs. So **the band is a range, not an enumeration**: 216 ids were deleted across
a span of 310, and roughly 94 templates inside it were untouched. Do not treat "id ≤ 313" as
the damaged set — it over-reports by about a third, and a repair guarded on the band would
rewrite assets that were never harmed.

That is the same reason both repairs guard on `assetPath` instead: 68/210/211 were only
rewired after confirming their path still contains `mine`, exactly as the 125 trees were
guarded on `Buildings/vegetation/tree_`. A path guard refuses a renamed template; an id guard
cannot tell a survivor from a casualty.

The mines are also why the schema trap was found at all. They were repaired first, on the
assumption that three nulls were the whole of it, and the tree count only came out when the
question was widened from "which templates does a test name" to "which fields can a restore
have dropped". A shipped-data test finds the damage it happens to cover; the schema diff finds
the rest.

## If it reappears

The first two steps capture evidence that RECOVERY DESTROYS. Do them before touching
anything — restoring rewrites the files, and committing closes the schema gap.

1. **Capture the mtimes of the surviving siblings.**

   ```bash
   find unity/Valkur/Assets/_Project/Data/Catalogs/Buildings -name 'BuildingTemplate_*.asset' \
     -printf '%T@ %p\n' | sort -n > /tmp/building_mtimes.txt
   ```

   This is the one piece of evidence the 2026-09-04 write-up lacks, and it is the only one
   that could name the mechanism: it brackets the deletion in time against each session's
   activity. `git checkout --` rewrites every restored file's mtime, so after recovery the
   question can no longer be asked.

2. **Run the schema diff and write the field list down** — see the trap section above. It
   reads the gap between `HEAD` and the working tree, so it must run before any commit. The
   output is the complete list of fields a restore can silently drop.

3. **Confirm the scope.** Deletions are invisible in the Unity console and surface only as a
   data test failing:

   ```bash
   git status --porcelain -- unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/ \
     | awk '$1=="D"' | wc -l
   ```

4. **Recover with the two halves above, in that order** — files first, then catalog
   re-registration. Neither alone is enough.

5. **Re-check every field the step-2 diff named**, on every restored asset. Guard any bulk
   repair on `assetPath`, never on an id range: the range contains survivors.

6. **Do not commit over a working tree in this state.** Nothing here was committed; a commit
   would have made the loss permanent AND erased the step-2 evidence.
