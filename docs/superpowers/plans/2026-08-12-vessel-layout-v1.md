# Vessel Layout v1 — Appendix 1 Topology

Date: 2026-08-12
Status: Built

## Why

The base graph drew the four-node demonstration vessel the kernel shipped with: one extractor, one
smelter, a hold and a buffer. `docs/Dimenship Base Overview.png` and GDD Appendix 1 describe the real
thing — *one global Resource Storage in the centre, an interconnected array of factories, an array of
refineries, mission docks connected only to storage, a Power Core* — and the view could not be judged
against the concept until it drew that shape.

This is content and layout, not the content pipeline. The vessel is authored in the same hand-written
C# the old one was, so that `2026-08-10-static-content-and-world-state-design.md` can convert it to
`content/scenarios/default_vessel.json` in one move when that spec is built.

## The roster

Decided with the project owner: **one central storage, three factories, two reactors, two launching
bays, plus one extractor**. The reactors take the refineries' place in the chain. The extractor is a
low-income emergency source gathering matter from space, deliberately slower than the reactors it
feeds, and central storage opens with a stock rather than empty.

## The chain

| Stage | Facility | Schematic | Rate |
| :--- | :--- | :--- | :--- |
| Extract | Extractor 01 | `extract_matter` → 2,400 raw matter | 6 ticks a run, 400 a tick |
| Refine | Reactor Alpha, Reactor Beta | `refine_alloy`, 4,000 raw → 800 alloy | 16 ticks a run, 250 raw a tick each |
| Press | Factory Alpha | `press_plate`, 400 alloy → 200 plate | 16 ticks a run |
| Build | Factory Beta | `build_actuator`, 200 plate → 100 actuator | 16 ticks a run |
| Assemble | Factory Gamma | `assemble_frame`, 100 actuator → 50 drone frame | 16 ticks a run |

The factory array is balanced link for link: each stage consumes exactly what the one before it
produces, so a stall shows up as a blocked card downstream rather than as a buffer filling forever.
Raw matter runs at a planned deficit — 400 a tick in against 500 out — and the opening 800,000 in
central storage is what covers it, for a little over two operational hours. Closing that gap is what
the launching bays are for, once missions exist.

## The layout

Grid cells, authored in `BaseGraphLayout.ForDefaultWorld()`. At `GraphGeometry`'s 268 × 168 stride
this is 1,292 × 800 px of content, which fits one screen at 100%.

```
        col 0            col 1          col 2            col 3          col 4
row 0                                   EXTRACTOR 1                     POWER CORE P
row 1                                                                   FACTORY A  4A
row 2   REACTOR A  3A                   CENTRAL STG 2                   FACTORY B  4B
row 3   REACTOR B  3B                                                   FACTORY C  4C
row 4                    BAY A  5A                       BAY B  5B
```

Badges number the chain — extract, store, refine, fabricate, launch. The extractor sits directly above
central storage so the one route bringing matter aboard is a straight line down the middle instead of
an elbow crossing the reactors' lines. Reactor Alpha and Factory Beta share the storage's row, so
their edges are straight; the rest elbow in the gutter. Factory interconnects join adjacent cells
only, which is what keeps an edge from being drawn through the card between them. The power core is
authored like every other card rather than pinned by the view, and is edgeless — which is why it can
take the corner without leaving anything stranded.

## Routes — thirteen lines, nine drawn edges

Storage ↔ facility pairs merge into one double-headed edge, as they already did.

| Link | Lines | Carries |
| :--- | :--- | :--- |
| Extractor → Storage | 1 | raw matter |
| Storage ↔ Reactor Alpha, Storage ↔ Reactor Beta | 4 | raw matter out, alloy back |
| Storage → Factory Alpha | 1 | alloy |
| Factory Alpha → Beta → Gamma | 2 | plate, actuator |
| Factory Gamma → Storage | 1 | drone frames |
| Storage ↔ Bay Alpha, Storage ↔ Bay Beta | 4 | nothing yet |

Intermediates never visit storage. That is what "interconnected" buys, and it is also why a plate has
only one place to go and cannot be raced for by two lines drawing on one buffer.

**Throughput is sized just above the stage each line serves** — 260 against a reactor's 250 a tick,
and the same margin down the array — rather than at the 4,000 a haulage main would carry. A line an
order of magnitude faster than its facility fills the buffer it feeds and then sits on it, and a line
with nowhere to put its load is blocked by the engine's reckoning and red by the view's: the graph
would report a healthy vessel as a broken one. Over 600 ticks the lines now read `High` on 79–100% of
ticks, `Blocked` on 0–31%, and the four bay links `Idle` throughout.

Seven edges meet at central storage, and every edge leaves and arrives at the centre of the side
facing its other end, so edges approaching from the same side would overprint. `BaseGraphFocus` now
counts the fan per card rather than per pair of cards and hands `EdgePolyline` the next offset, which
spreads the whole fan and subsumes the parallel-pair case it replaced.

## Buffers are drawn inside their facility

Nine facilities means nine buffers, and nine more cards would have buried the one-central-storage
reading the concept is built on. A buffer has no placement: it is drawn inside the card of the
facility that works it, and a route ending at the buffer ends at that facility.

`BaseGraphNodes.DrawnStorages` is that rule, as a pure function beside the layout rather than inside
the view, so *every route endpoint is drawn somewhere* and *no route begins and ends on the same card*
are assertions in `Core.Tests`. `ExecutorCard` grew a buffer-fill reading, because with the buffer
folded away that card is now the only place on the graph where a starved facility can be seen.

## Energy

Standing draw is 7,700 — a 4,000 sink, 1,100 across eight facilities, 200 apiece for thirteen lines.
Full production adds 2,108: energy is charged in proportion to the work done in a tick, so the
extractor's 3,650 a run spreads over six ticks and each 4,800 spreads over sixteen. The vessel peaks
at 9,808 against a capacity of 10,000 and nothing is ever refused. The remaining reserve is
deliberate — it is the room a fuel-burning power core needs when capacity stops being a constant.

## Two systems visibly absent

- **A reactor does not make power.** Appendix 1's power core burns refined material; the engine has no
  fuel burn and `EnergyCapacity` is still a constant. A reactor refines and draws power like any other
  facility. Open question 3 of the world-state spec is where the fuel loop is decided.
- **A launching bay does nothing.** There is no acquisition system, so a bay carries no schematic,
  reports `IDLE` forever, and its link to storage draws in the idle band. That is the honest rendering
  of a system that does not exist, and it keeps the missing resupply loop visible on the graph.

`FacilityType` gained `Reactor` and `LaunchBay` for these. `Refinery` remains in the enum and no
facility is one; the GDD's *Mission Dock* and this vessel's *launching bay* are the same facility under
two names, which the enum's doc comment records.

## Verification

`dotnet build DimenshipGame.sln` builds `Dimenship.Core`, `Dimenship.Shell` and the Godot project
clean. `Dimenship.Core.Tests` is 139 green and `Dimenship.Shell.Tests` 46, including new assertions
that the whole chain produces a drone frame within an operational hour, that the opening stock
outlasts that hour, that a bay never leaves idle, and that the vessel runs above 90% of capacity
without ever starving a facility.

**Not verified here:** anything on screen. The Godot editor is not available in this environment, so
the layout's appearance, the folded buffers, the idle bay edges and the two new icons are unconfirmed
until someone opens the editor.

**Also unverified for a reason worth recording:** the test project does not compile against the NUnit
version its `csproj` pins (`4.6.1`), which added `Assert.Throws<T>(Action)` overloads that are
ambiguous with the existing `TestDelegate` ones at roughly thirty call sites. This predates this
change — it reproduces on a clean checkout of `master` — so the suite above was run against a copy
pinned to `4.4.0`, and the pin in the repository is left as it was found.

## Follow-ups

1. **Reactor fuel and power production.** Until it lands, a reactor is a refinery with a different icon.
2. **Acquisition missions.** What makes a launching bay do anything, and what refills the raw matter
   the extractor cannot keep up with.
3. **Haulage has little slack.** Sizing each line a few percent above its stage is what keeps the
   edges honest, and it also means a line is close to being the binding constraint. If a stage is ever
   rebalanced, its line has to move with it.
4. **The NUnit pin**, above. One line, and until it moves nobody can run the tests from a clean clone.
