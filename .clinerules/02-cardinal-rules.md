# Cardinal rules — always on

1. **Unity MCP console must be clean** (zero errors, zero actionable warnings)
   before declaring any task complete. After every C# change:
   ```
   unityMCP__refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)
   unityMCP__read_console(action="get", types=["error","warning"], page_size="50", format="detailed", include_stacktrace=true)
   ```
   Benign warnings you may leave: MCP WebSocket reconnect after domain reload,
   `Default GameObject Tag: X already registered`, EditMode
   `LogAssert.ignoreFailingMessages` informational lines.

2. **Check existing scripts before creating new ones** — duplicates are the #1
   regression source.

3. **Edit ScriptableObjects, not external JSON.** Catalog data lives in
   `.asset` files; world state lives in `StreamingAssets/` written by runtime
   editors via the `IRepository` pattern.

4. **Assembly boundaries** — `Valkur.Gameplay` must NOT reference `Valkur.UI`
   (circular). Cross-system signaling goes through `ServiceLocator` or
   `GameEvents`. Full table in `.clinerules/skills/valkur-conventions.md`.

5. **Code style** — `[SerializeField] private` + `[Tooltip("…")]`, never public
   fields; `ServiceLocator.Get<T>()`, never raw singletons; ScriptableObjects
   for designer-tunable data; `ObjectPool<T>` for hot-path spawns; static
   mutable state needs a
   `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
   reset (Domain Reload is OFF); `_camelCase` privates, `PascalCase` publics,
   one class per file, `ClassName.Aspect.cs` partials for big files.

6. **Preserve Python game-feel exactly** when porting: `px ÷ 16` → world units
   (PPU=16; Buildings PPU=32), `px/tick × 3.75` → world units/s,
   `ticks ÷ 60` → seconds. Don't eyeball; don't change constants unless the
   bug *is* the value.

7. **Tests stay green.** After non-trivial changes run the EditMode suite
   (`/unity-tests.md`). Never leave a failing suite behind without saying so.

8. **Never report "done" with a dirty console or without verifying.** If Unity
   isn't running or MCP is disconnected, say so explicitly — do not pretend.
