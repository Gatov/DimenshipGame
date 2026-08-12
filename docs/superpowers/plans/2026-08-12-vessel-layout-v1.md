# Vessel Layout v1 — the GDD's Production Layer

Date: 2026-08-12
Status: Built

## Why

The base graph drew the four-node demonstration vessel the kernel shipped with: one extractor, one
smelter, a hold and a buffer. `docs/Dimenship Base Overview.png` and GDD Appendix 1 describe the real
thing — *one global Resource Storage in the centre, an interconnected array of factories, a
separating tier, mission docks connected only to storage, a Power Core* — and the view could not be
judged against the concept until it drew that shape.

**GDD v0.9 landed while this was being built** and named the layer: the four schedulable facility
kinds (Mission Dock, Resource Storage, Matter Reactor, Factory), Matter Mix as the one recovered
material, the five standardized reactor outputs, passive systems as state cards, and the Emergency
Hydrogen Extractor with its recovery invariant. The vessel below is that vocabulary, not a paraphrase
of it: `FacilityType`, every item id and every facility label come from §5.8 and §5.9.

This is content and layout, not the content pipeline. The vessel is authored in the same hand-written
C# the old one was, so that `2026-08-10-static-content-and-world-state-design.md` can convert it to
`content/scenarios/default_vessel.json` in one move when that spec is built.

## The roster

Decided with the project owner: **one Resource Storage, three Factories, two Matter Reactors, two
Mission Docks, plus the Emergency Hydrogen Extractor**. Reactors take the refineries' place in the
chain. Missions do not exist, so Resource Storage opens with the Matter Mix they would have
recovered, and the extractor trickles hydrogen in against the day it runs out.

## The chain

| Stage | Facility | Schematic | Rate |
| :--- | :--- | :--- | :--- |
| Extract | Emergency Hydrogen Extractor | `extract_hydrogen` → 240 hydrogen | 60 ticks a run, 4 a tick |
| Separate | Matter Reactor Alpha | `separate_basic`, 4,000 Matter Mix → 800 Basic Metals | 16 ticks a run, 250 a tick |
| Separate | Matter Reactor Beta | `separate_technical`, 4,000 Matter Mix → 400 Technical Materials | 16 ticks a run, 250 a tick |
| Press | Factory Alpha | `press_components`, 400 Basic Metals → 200 components | 16 ticks a run |
| Assemble | Factory Beta | `assemble_modules`, 200 components + 100 Technical Materials → 100 modules | 16 ticks a run |
| Frame | Factory Gamma | `assemble_frames`, 100 modules → 50 robot frames | 16 ticks a run |

**Two reactors, two processing modes.** The GDD makes mode selection the reactor's optimization
decision, so the two are configured differently and the factory array's middle stage draws on both.
Configured identically they would be one facility drawn twice.

The array is balanced link for link: each stage consumes exactly what the one before it produces, so
a stall shows up as a blocked card downstream rather than as a buffer filling forever. Matter Mix has
no source at all — that is what missions are for — so the opening 3,600,000 in Resource Storage
covers the 500 a tick the reactors separate, which is two operational hours.

`synthesize_basic` (12,000 hydrogen → 400 Basic Metals, twice the time and four times the energy) is
defined and unconfigured. Nothing selects a schematic yet, so it sits in the catalog as the path the
GDD's recovery invariant requires, and the hydrogen accumulating in storage is what would pay for it.

## The layout

Grid cells, authored in `BaseGraphLayout.ForDefaultWorld()`. At `GraphGeometry`'s 268 × 168 stride
this is 1,292 × 800 px of content, which fits one screen at 100%.

```
        col 0            col 1          col 2            col 3          col 4
row 0                                   EXTRACTOR  1                    POWER CORE P
row 1                                                                   FACTORY A  4A
row 2   REACTOR A  3A                   RESOURCE STG 2                  FACTORY B  4B
row 3   REACTOR B  3B                                                   FACTORY C  4C
row 4                    BAY A  5A                       BAY B  5B
```

Badges number the chain — extract, store, separate, fabricate, launch. The extractor sits directly
above Resource Storage so the one route bringing anything aboard without a mission is a straight line
down the middle instead of an elbow crossing the reactors' lines. Reactor Alpha and Factory Beta share the storage's row, so
their edges are straight; the rest elbow in the gutter. Factory interconnects join adjacent cells
only, which is what keeps an edge from being drawn through the card between them. The power core is
authored like every other card rather than pinned by the view, and is edgeless — which is why it can
take the corner without leaving anything stranded.

## Routes — fourteen lines, ten drawn edges

Storage ↔ facility pairs merge into one double-headed edge, as they already did.

| Link | Lines | Carries |
| :--- | :--- | :--- |
| Extractor → Storage | 1 | hydrogen |
| Storage ↔ Reactor Alpha, Storage ↔ Reactor Beta | 4 | Matter Mix out; Basic Metals and Technical Materials back |
| Storage → Factory Alpha | 1 | Basic Metals |
| Storage → Factory Beta | 1 | Technical Materials |
| Factory Alpha → Beta → Gamma | 2 | components, modules |
| Factory Gamma → Storage | 1 | robot frames |
| Storage ↔ Dock Alpha, Storage ↔ Dock Beta | 4 | nothing yet |

Intermediates never visit storage. That is what "interconnected" buys, and it is also why a component
has only one place to go and cannot be raced for by two lines drawing on one buffer.

**Throughput is sized just above the stage each line serves** — 260 against a reactor's 250 a tick,
and the same margin down the array — rather than at the 4,000 a haulage main would carry. A line an
order of magnitude faster than its facility fills the buffer it feeds and then sits on it, and a line
with nowhere to put its load is blocked by the engine's reckoning and red by the view's: the graph
would report a healthy vessel as a broken one. Over 600 ticks the lines now read `High` on 79–100% of
ticks, `Blocked` on 0–31%, and the four bay links `Idle` throughout.

Eight edges meet at Resource Storage, and every edge leaves and arrives at the centre of the side
facing its other end, so edges approaching from the same side would overprint. `BaseGraphFocus` now
counts the fan per card rather than per pair of cards and hands `EdgePolyline` the next offset, which
spreads the whole fan and subsumes the parallel-pair case it replaced.

## Buffers are drawn inside their facility

Eight facilities means eight buffers, and eight more cards would have buried the one-global-storage
reading the concept is built on. A buffer has no placement: it is drawn inside the card of the
facility that works it, and a route ending at the buffer ends at that facility.

`BaseGraphNodes.DrawnStorages` is that rule, as a pure function beside the layout rather than inside
the view, so *every route endpoint is drawn somewhere* and *no route begins and ends on the same card*
are assertions in `Core.Tests`. `ExecutorCard` grew a buffer-fill reading, because with the buffer
folded away that card is now the only place on the graph where a starved facility can be seen.

## Energy

Standing draw is 7,900 — a 4,000 sink, 1,100 across eight facilities, 200 apiece for fourteen lines.
Full production adds about 1,560: energy is charged in proportion to the work done in a tick, so the
extractor's 3,650 a run spreads over sixty ticks and each 4,800 spreads over sixteen. The vessel peaks
around 9,460 against a capacity of 10,000 and nothing is ever refused. The reserve is deliberate — it
is the room a fuel-burning Power Core needs when capacity stops being a constant, and the room the
emergency synthesis needs on the day it is the only thing running.

## Two systems visibly absent

- **The Power Core makes no power here.** GDD §5.9 makes it a state card that burns refined material;
  the engine has no fuel burn and `EnergyCapacity` is still a constant. A Matter Reactor separates and
  draws power, and is explicitly not the Power Core. Open question 3 of the world-state spec is where
  the fuel loop is decided.
- **A Mission Dock does nothing.** There is no acquisition system, so a dock carries no schematic,
  reports `IDLE` forever, and its link to storage draws in the idle band. That is the honest rendering
  of a system that does not exist, and it keeps the missing resupply loop — the only source of Matter
  Mix — visible on the graph.

`FacilityType` is now `Extractor`, `MatterReactor`, `Factory`, `MissionDock`: the GDD's own names.
`Refinery` is gone, along with its icon, rather than left as a second word for the separating tier.

## Verification

`dotnet build DimenshipGame.sln` builds `Dimenship.Core`, `Dimenship.Shell` and the Godot project
clean. `Dimenship.Core.Tests` is 139 green and `Dimenship.Shell.Tests` 46, including new assertions
that the whole chain produces a robot frame within an operational hour, that the opening Matter Mix
outlasts that hour, that a Mission Dock never leaves idle, and that the vessel runs above 90% of
capacity without ever starving a facility.

**Not verified here:** anything on screen. The Godot editor is not available in this environment, so
the layout's appearance, the folded buffers, the idle bay edges and the two new icons are unconfirmed
until someone opens the editor.

**Also unverified for a reason worth recording:** the test project does not compile against the NUnit
version its `csproj` pins (`4.6.1`), which added `Assert.Throws<T>(Action)` overloads that are
ambiguous with the existing `TestDelegate` ones at roughly thirty call sites. This predates this
change — it reproduces on a clean checkout of `master` — so the suite above was run against a copy
pinned to `4.4.0`, and the pin in the repository is left as it was found.

## Follow-ups

1. **The Power Core's fuel burn.** Until it lands, `EnergyCapacity` is a constant and no facility
   produces energy.
2. **Acquisition missions.** What makes a Mission Dock do anything, and the only thing that can
   restock Matter Mix. The vessel has two operational hours of it and no way to make more.
3. **The extractor's place on the graph.** GDD §5.8 keeps it out of the automation graph; the mock
   draws it as a read-only card, which is what §5.8's table now records. When state cards exist it
   may belong in that strip instead, and nothing in the layout depends on where it sits.
4. **The three unused reactor outputs.** Rare Metals, Chemical Feedstock and Phase Materials are named
   by the GDD and absent from the catalog: no schematic consumes them, and an item nothing produces or
   consumes is a row of zeroes on every storage panel.
5. **Haulage has little slack.** Sizing each line a few percent above its stage is what keeps the
   edges honest, and it also means a line is close to being the binding constraint. If a stage is ever
   rebalanced, its line has to move with it.
6. **The NUnit pin**, above. One line, and until it moves nobody can run the tests from a clean clone.
