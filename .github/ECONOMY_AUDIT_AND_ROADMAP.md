# Economy — audit and roadmap

Audited 2026-09-07. Overall **3.9/10** before the work in this document; **7.2/10** after
Phases 0–3, which shipped and were verified the same day.

Axes that moved: faucets 1 → 7, supply and demand 2 → 6, negotiation 1 → 7, pipeline 6 → 8,
price data 4 → 8, instrumentation 3 → 6. Axes that did **not** move, and are what caps the
score: sinks (2) and coupling to progression (2) — gold still buys nothing the player wants.

The finding that frames everything else: **the economy was not broken, it was not
connected.** The pipeline was well built and the data never filled it. Three of the price
pipeline's seven steps resolved to the identity in shipped data, no monster in the game
dropped currency, and the player's wallet started at zero.

## What was measured

All numbers below were read out of the shipped assets, not estimated.

| Axis | Score | Finding |
|---|---|---|
| Faucets | 1 | 0 starting coins, 0/25 monsters dropped currency, 0 quests shipped |
| Sinks | 2 | Buying only; no repair, travel, respec, housing, or death cost |
| Pipeline architecture | 6 | Sound 6-step design, half of it inert |
| Price data coherence | 4 | 66 of 236 items priced 0; sell/buy ratio had no rule |
| Anti-exploit | 7 | No craft arbitrage, broker distrusts the model, refunds correct |
| Supply and demand | 2 | Vendors hold infinite gold, stock never restocks, 0 economy groups |
| Coupling to progression | 2 | Gold buys no power; talents and spells cost points |
| Persistence | 8 | `coins` saved with a `-1` sentinel, restorer documented, tests exist |
| Negotiation / personas | 1 | `discountLimits` imported, serialized, and read by nobody |
| UI and feedback | 7 | `TradeFlourishFX`, HUD counter, chat trading, minimap markers |
| Instrumentation | 3 | No economic telemetry; inflation was not measurable |

### The defects, in the order they mattered

1. **No coin faucet at all.** `DeathDropSystem` dropped inventory, a loot-table roll and an
   XP orb. Not one line of currency. Of 25 monster definitions, exactly **one** (`barbol`)
   carried a `lootTable`; the other 24 read `lootTable: {fileID: 0}`. `QuestDefinition` has
   no gold field to begin with, and zero quests ship. The only way to earn was felling a
   tree — `wood` sells for 1 — or a 3 % roll on an ore node for the `gold` item, worth 1.

2. **`CurrencyWallet.startingCoins` was unreachable.** The component is `AddComponent`-ed
   onto the player by `EntitySetup` and has no inspector to be authored from, so the field
   deserialised to 0 forever. Same defect shape as `ChatSystem._catalog`.

3. **66 items priced at zero**, all of them the mining outputs — every ore and gem, up to
   rarity 4. A zero price is not "free": it falls through to the fallback heuristic and
   resolves to **one coin** for anything stackable. An `eternal_crystal` and a pebble were
   worth the same, and every number in the pipeline behaved correctly while that was true.

4. **Every vendor shipped with `economyGroup: {fileID: 0}`.** Both margin steps therefore
   resolved to a multiplier of exactly 1, so every counter in the world charged identically.
   Nothing distinguishes "no group" from "a group whose margins are 1".

5. **`negotiationDiscount` was never passed by any caller.** All six call sites took the
   `0f` default, so `NPCPersonaDefinition.discountLimits` — imported from the Python build
   and serialized on every persona — reached no price. Every character haggled identically.

6. **The discount was pointed the wrong way on the sell side.** `GetSellPrice` applied the
   same *reduction* as `GetBuyPrice`, so a persona willing to come down 20 % would also have
   paid 20 % *less* for what the player brought in — the friendliest character in the world
   would have been the worst one to sell to. It survived because nothing ever passed a
   non-zero discount: **the direction bug and the field's inertness were hiding each other**,
   and fixing only one would have shipped the other.

### What was already right

Worth recording, because two of these were checked on the assumption they were broken.

- **Crafting is not arbitrable.** All 36 cooking recipes are negative against buying their
  ingredients — from `-3` (churros) to `-11` (paella, asado). That is correct anti-exploit
  design and it was deliberate.
- **`ChatTradeBroker` does not trust the language model.** Item id, direction and count are
  all re-resolved against the live shop, purse and inventory, prices come from the same
  `GetBuyPrice` the Buy button charges, and quantity is cut to what is affordable rather
  than refused. It is the single place in the game that spends money on the strength of a
  sentence the player typed, and it is the most carefully written file in the subsystem.
- **Exactly one arbitrage existed** (`gold`, buy 1 / sell 1) and it is harmless.

## What shipped (Phase 0–2)

### Phase 0 — the faucet

- `MonsterDefinition.coinReward`: `-1` pays nothing, `0` falls back to the heuristic
  `hp/40 + power/4` on the **scaled** stats, `>0` is the designer's number. The `0`
  fallback is what lets every monster shipped before the field existed start paying without
  a data edit — the same contract `xpReward` already uses. It is deliberately *not* derived
  from `xpReward`: that is a difficulty knob, and tying wealth to it would make every XP
  retune a silent economy retune.
- `DeathDropSystem.TryDropCoins`, gated on the same hostile-faction check the loot tables
  use, with a ±35 % variance rolled from the **same** `System.Random` as the loot pool — so
  the day a run carries a seed, one field makes both reproducible.
- `CoinDropSpawner` is now the single owner of "put coins on the ground". The shell (layer,
  sprite, collider, sorting, chunking) moved out of `PlayerDeathDropSystem`, which now goes
  through it: a purse spilled on death and a reward minted on a kill must look identical.
- `PlayerDefinition.startingCoins` (25 on all six classes), applied by
  `EntitySetup.InitPlayerStats` and overwritten a moment later by `RestoreCoins` on a load.

Measured payouts against the shipped catalogue: trash barbol **4**, `knight_red` **6**,
`dark_vampire` **12**, `barbol_boss` **52**, `barbol_gigante` **252** — against a
`knight_longsword` at 120 and a `wizard_staff_lvl_3` at 600. Roughly thirty mid-tier kills
for a real weapon.

### Phase 1 — the data

- All 65 unpriced items given a rarity ladder: **6 / 14 / 30 / 65 / 140** buy, sell at half.
  `experience_orb` is deliberately left at 0 and whitelisted as not-merchandise.
- `EconomyGroupDefinition.typeMargins` — a per-`itemType` margin layer between the per-item
  override and the group default. It exists because the catalogue has six types and 64
  minerals: "the smith deals fairly in ore" is one row here and 64 rows in `itemMargins`.
  A whitelist would have said something different and worse — that the character *refuses*
  everything else, which turns a specialist into a wall.
- `EconomyGroupSeeder` (`Valkur > Economy > Seed Economy Groups`) creates the five groups
  and wires them. Specialist rates are `1.00 / 1.00`; outside the trade is `1.35 / 0.70`.
  The smith takes `mineral` as well as `blacksmith`, and that second entry is load-bearing:
  the 64 minerals are the entire output of the mining profession and no vendor was their
  buyer, which is most of why nobody noticed they were priced at zero.
- Negotiation is live, and `ApplyPremium` is the sell-side twin of `ApplyDiscount` — same
  strength, pointed the same way, at the player's benefit.

Measured after wiring — the reason to walk across town, in one line:

| Item | Base b/s | At the smith | At the cook |
|---|---|---|---|
| `eternal_crystal` | 140/70 | 140/70 | 189/49 |
| `knight_longsword` | 120/60 | 120/60 | 162/42 |
| `beef` | 12/5 | 16/4 | 12/5 |

### Phase 2 — the economic cycle

`MarketCycle` (Core, pure) plus `MarketService` (Gameplay, the live owner). Four phases —
Boom, Peak, Bust, Trough — over a seed-derived cycle of 6 to 14 days, amplitude capped at
**±25 %**, resolved as a pure function of `(seed, day)`.

Applied **after** the margins and **before** the discount. After the margins because a boom
should lift a specialist's fair price and a generalist's mark-up in the same proportion;
before the discount because haggling is the last word — 20 % off what the thing costs
*today*, not off a pre-cycle number the player never sees. A per-vendor price override
skips the margins but **not** the cycle, or the overridden items would be the only stable
prices in the world and an arbitrage against the cycle itself.

Persisted through the save metadata bag (`market.seed`, `market.day`, `market.seed_source`)
rather than a typed field: this is world state, it is two integers, the bag is already the
project's answer for run-level facts, and going through it means no schema bump and no
migration for saves that predate the market.

Console: `market`, `market forecast [days]`, `market on|off`, `marketday [+n]`,
`marketseed <n> [source]`, `coins [n]`.

## Bitcoin: the decision, and why

The request was to back the economy on Bitcoin's price. **The idea (economic cycles) is
good; the source (a live BTC feed) is not, and the two separate cleanly.** What shipped is
the idea, with a documented door for the source.

### Why a live feed was rejected

Ordered by severity, not by ease of fixing.

1. **It is client-authoritative in a single-player game.** Any price feed the client reads,
   the player controls — system clock, proxy, edited cache. A mechanic the player can change
   by editing a clock is not a constraint, so nothing balanced against it means anything.
2. **It destroys determinism.** The project already carries debt here (`DeathDropSystem`
   and `HarvestDropResolver` both document having no run seed). An exogenous live multiplier
   makes a run irreproducible *by construction*: no replays, no balance tests, no
   reproducible bug reports, and a save loaded tomorrow is a different game than yesterday.
3. **On a daily scale BTC is noise, not a cycle.** It is a random walk. The player cannot
   learn it, predict it, or act on it, so it is a tax with a theme. "Economic cycle" implies
   legible phases; a price feed gives variance.
4. **It requires the network.** API key, rate limits, outages, and the game phoning home —
   consent and privacy. And an offline path is needed anyway, so it means building **two**
   economies and testing one.
5. **Platform and regulatory risk.** Real crypto prices driving a game economy reads as
   speculation. Storefront policy, age rating, and "elements of chance" regulation in
   several jurisdictions — disproportionate risk for a flavour feature.
6. **It amplifies whatever is broken underneath.** At 1/10 on faucets, multiplying prices by
   the market changes nothing: multiplying zero is still zero. This was the decisive one —
   the cycle had to come after Phase 0 and 1 or it would have been decoration.

### What an outside index CAN honestly decide

The **seed**, not the rules. `marketseed <n> [source]` is the only door, it writes the
number into the save, and nothing re-reads it while playing. Read once, cached, replayable.
An index decides the *flavour* of a stretch of days; `MarketCycle` keeps deciding the
*rules*, authored and bounded. If a weekly BTC close is wanted as that seed, it is one class
calling `SetSeed(value, "btc")` once per week — and every property above survives, because
by the time the game is running the number is just an integer in the save file.

The honest steelman for the original idea, kept because two thirds of it is satisfied by
what shipped: a real marketing hook (still available through the seed), a shared world event
everyone sees on the same day (this needs a shared seed, which the seed door provides), and
content that generates itself without authoring cost (that is exactly what `MarketCycle` is).

### Phase 3 — the vendor's own money

Vendors had infinite coin and finite stock that never came back, which is exactly backwards:
the player could sell an unbounded quantity of anything forever — so the game's only real
gold faucet was uncapped — while the shop itself ran dry and stayed dry.

- `VendorConfigDefinition.coinFloat` (400) and `restockSeconds` (600), with `VendorNPC.Purse.cs`
  holding the runtime half. Both features ship **off at 0**, and that is forced rather than
  chosen: a key absent from an already-shipped `.asset` deserialises to 0, so reading that as
  an *empty* purse would have made all five vendors refuse to buy anything the instant the
  field was added, silently. The seeder authors the real values.
- Money circulates. What the player spends goes **into** the vendor's purse; without that the
  float only ever falls and a busy shop becomes permanently insolvent instead of busy.
- The purse is debited **before** the item leaves the bag. The other order destroys the
  player's goods for a vendor who then turns out to be broke.
- Restocking is **proportional**, so `restockSeconds` reads as "empty to full" whatever the
  shop's size — a fixed per-tick amount refills a four-slot mage and a seventy-seven-slot
  lumberjack at wildly different rates from the same number.
- `ChatTradeBroker.QuoteSell` cuts the quantity to what the vendor can afford, mirroring the
  cut `QuoteBuy` already makes on the player's purse. "I can take two of those" is the answer
  a shopkeeper gives; refusing five because they cannot cover all five is the same mistake in
  the other direction. `VendorShopUI` surfaces the refusal as a toast — a Sell button that
  silently does nothing reads as a broken button, not as a shopkeeper who is short today.

## Verification

Full EditMode suite: **7841 tests**, 3 failures, none in the economy — two were another
session's deliberate `NPCCastState` contract change and one was a bug in this work's own
fixture (below). The five economy fixtures then ran green: **33 / 33**, console 0 errors and
0 warnings. Shipped data re-read from disk in the same probe as memory: 236 items, none
priced 0, **0 memory/disk disagreements**; five vendor configs, all wired, purse and restock
matching on both sides.

Two traps this work walked into, both worth recognising again:

- **`AddComponent` does not run `Awake` in EditMode**, so `VendorEconomyService.Instance`
  stays null however carefully a fixture builds one — and `VendorNPC.GetSellPrice` then takes
  its LEGACY branch (a flat `sellPriceMultiplier` of 0.5) instead of the margin pipeline.
  `VendorPurseTests` first shipped asserting a 50-coin price against a 25-coin reality and
  **failed a correct implementation**. It reads the price off the vendor now and counts.
- **A successful `refresh_unity` and a clean console are not proof an edit compiled.** It
  returned `resulting_state: "idle"` without rebuilding the Tests assembly — measured, asm
  09:59:00 against an edit at 10:04:29 — and reflection could not tell the two apart, because
  the type resolved on both. The timestamp comparison is what sees it, and
  `AssetDatabase.ImportAsset(ForceUpdate | ForceSynchronousImport)` plus
  `CompilationPipeline.RequestScriptCompilation()` is what forces it.

## Still open

- **Sinks.** Repair, fast travel, respec cost, storage, housing. The cycle moves prices in a
  world where the only thing to spend on is inventory.
- **Coupling to progression.** Gold still buys no power; talents and spells cost points.
  Until gold buys something the player wants, the cycle is a number on a shop window.
- **Cooking ingredients have no gathering route.** `beef`, `potato`, `onion` and `herbs` are
  produced by no harvest table, so cooking is a pure gold sink with no input path.
- **Quest rewards.** `QuestDefinition` has `xpReward`, `skillPointReward` and `itemRewards`
  and no coin field. Zero quests ship, so it costs nothing today.
- **Economic telemetry.** Coins minted and coins burned per session, so inflation is a
  number rather than a feeling.
