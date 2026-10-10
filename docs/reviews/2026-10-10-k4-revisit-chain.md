# K4 — The Revisit Chain and the Treatment Lines

Date: 2026-10-10
Ticket: K4 (#70), from D2 (`docs/superpowers/specs/2026-09-24-storage-topology-and-direct-routes-design.md`, Decision 3)

## What was built

Content, plus three small pieces of code the content needed.

- **Items.** `plate_blank` and `hardened_blank` are workpieces, and `bulkhead` is ordinary.
- **Recipes.** All three are unlocked on the shipped vessel and take 8 ticks a run at a work rate
  of 100, like the rest of the production chain.

  | Recipe | Facility | In | Out | Energy |
  | :--- | :--- | :--- | ---: | ---: |
  | `form_blanks` | factory | 200 Basic Metals | 100 Plate Blank | 2,400 |
  | `harden_blanks` | matter reactor | 100 Plate Blank | 100 Hardened Blank | 4,800 |
  | `assemble_bulkheads` | factory | 100 Hardened Blank + 100 Component | 50 Bulkhead | 2,400 |

  Forming uses the same metal per run as pressing, so it competes for Factory Alpha's time and the
  hold's Basic Metals rather than for a new raw material. Hardening costs twice a separation run's
  energy, which is D2's "real cost against separation". One run's inputs and output take under 5%
  of a facility buffer.
- **Treatment lines.** The four routes of Decision 3, on one new archetype, `treatment_line`. It
  carries 13 a tick against the 12.5 a stage moves, and draws 200, like every other line. The
  routes are 5 ticks long to Reactor Alpha and 6 to Reactor Beta, and all four are built at start.
- **Content version** moves to `0.2.0`. The Matter Reactor's purpose sentence now mentions
  treatment, and the Factory's mentions bulkheads.
- **Saves.** A save missing a storage, facility or route that the scenario has is reported as
  drift, one error per node (`WorldSave.CheckLayout`). Nothing is added to the loaded world in
  silence. The save already holds the vessel whole, and nothing builds or removes a node in play,
  so a gap like this only arises when the scenario gained the node after the save was written. A
  pre-K4 save is also refused on its `contentVersion`. This check names what is missing.
- **Drawing.** The spec's open item on placement was right. From Factory Alpha (4, 1) to either
  reactor, the elbow's midpoint fell in the middle of Resource Storage's column, and both edges
  drew straight across that card. Moving a card would have changed the route lengths, which are
  the Manhattan distances between cards, so the geometry changed instead.
  `GraphGeometry.EdgePolyline` now takes the cards. An elbow that would cross a card moves to the
  nearest gutter that keeps clear of all of them, and an elbow already clear keeps its midpoint,
  so no existing edge moved. In the running game, the Reactor Alpha edge takes the gutter left of
  column 1. The Reactor Beta edge's fan offset pushes it out of that gutter, so it takes column 3's
  side and runs along the empty row 3.

## What the planner does with it

Nothing. Its supply is the hold, and it routes every leg through the hold, so a bulkhead draft
carries `WorkpieceNotAccepted` on its blank legs and cannot be approved. That is the honest answer
until buffer-to-buffer planning exists, which is the deferred part of K5b. Until then the chain
runs only from tasks queued by hand.
`WorkpieceTests.OnTheShippedVessel_TheChainRuns_FormTreatFinish_WhenQueuedByHand` runs it:

1. Factory Alpha presses and forms.
2. Reactor Alpha hardens, over the treatment lines.
3. Factory Alpha finishes, and 50 bulkheads reach the hold within 1,500 ticks.

No blank is left anywhere.

## The baseline, re-run

All eight replay scripts were run on the commit before K4 and on K4. **Every metric in every
report is identical.** The only lines that differ are the content version and the final state
hash, which now covers four more lines and three more unlocked recipes:

| Script | Hash before | Hash after |
| :--- | :--- | :--- |
| situation-a-priority | `9ec050b702f8…` | `c32e11d94cef…` |
| situation-a | `9b56c72352bc…` | `a0fbd4fe2045…` |
| situation-b-alone | `7734b5cccb78…` | `b1638b36c171…` |
| situation-b-contested | `5223e16f5dd0…` | `f2127738b269…` |
| situation-b-hold | `875b4d2ef21e…` | `097ec6d5cd21…` |
| situation-b-priority | `b35255f4d8da…` | `783665bd9f0c…` |
| situation-b | `b19ee0677455…` | `78388ab89df1…` |
| smoke | `28eb3b516372…` | `dfbd4f446ed5…` |

So the M3, K1, K2 and K5b figures still describe the vessel, and the reports at the end of this
document are the reference from here on. That follows from what the scripts ask for: none of them
orders a bulkhead, which the planner would refuse anyway, and nothing in them is throttled.
Throttled time is zero for every facility in every script.

### Energy

The four lines add 800 of standing draw. The figures below are the shipped catalog's numbers added
up by hand, with every slot built:

| | Before K4 | After K4 |
| :--- | ---: | ---: |
| Standing: Stabilization Array, 8 facilities, built lines | 8,300 | 9,100 |
| Plus every producer running, with both reactors separating | 9,861 | 10,661 |
| Plus both reactors hardening instead | — | 11,261 |
| Capacity | 10,000 | 10,000 |

A fully built vessel running all five machines at once used to fit, with 139 to spare. Now it is
661 over capacity, or 1,261 over with both reactors hardening.

D2 allowed a lower draw for treatment lines "if M3 shows the vessel's budget was sized without
room for them". M3 does not show that, because no script runs everything at once, so the draw
stays at 200. Starvation on a full build is the energy price of treatment, and it is visible
(`StarvedTicks`, Throttled time). If the project owner wants the old headroom back, a treatment
line drawing 25 restores it while the reactors separate.

## Not done here

- **Item icons** for the three new items. The resource strip draws their slots empty, as it
  already does for the construction units.
- **A harness command to queue a task by hand.** A scripted demand goes through the planner, so a
  replay cannot run the chain yet. E1 needs one of these two: that command, or buffer-to-buffer
  planning.
- **D2's open item on stranding.** `Enqueue` still accepts `form_blanks` on Factory Beta, which has
  no route to a reactor. That remains D4's.

## Reports

### situation-a

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 9000
- Interventions (commands applied): 12

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 | Normal |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 316 | 316 | 1000 | 0 | Normal |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 337 | 337 | 1000 | 0 | Normal |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | 1771 | 571 | 500 | 0 | Normal |
| expedition_1_modules | module | 500 | 1500 | 1500 | 2018 | 518 | 500 | 0 | Normal |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | 2971 | 571 | 500 | 0 | Normal |
| expedition_2_modules | module | 500 | 2700 | 2700 | 3096 | 396 | 500 | 0 | Normal |
| component_reserve | component | 2000 | 3000 | 3000 | 3289 | 289 | 2000 | 0 | Normal |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | 4100 | 500 | 500 | 0 | Normal |
| expedition_3_modules | module | 500 | 3900 | 3900 | 4235 | 335 | 500 | 0 | Normal |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | 5371 | 571 | 500 | 0 | Normal |
| expedition_4_modules | module | 500 | 5100 | 5100 | 5496 | 396 | 500 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 60 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 27 | 400 |
| technical_materials | 8 | 246 |
| component | 16 | 200 |
| module | 6 | 100 |
| robot_frame | 1 | 25 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 766 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 60 | 600 |
| factory_b_buffer | 42 | 475 |
| factory_c_buffer | 13 | 475 |
| dock_a_hold | 674 | 999 |
| dock_b_hold | 539 | 999 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 10 | 1200 | 0 |
| factory_b | 8 | 960 | 0 |
| factory_c | 1 | 120 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 240 | 8568 | 72 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 8820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 1008 | 6264 | 527 | 1 | 0 | 1200 | 0 |
| factory_b | 1280 | 6211 | 234 | 0 | 0 | 960 | 0 |
| factory_c | 320 | 7255 | 969 | 0 | 0 | 120 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `a0fbd4fe2045ade795d66c12f9b92cc2c9e7340df772458b78cb69e8fc02afc5`

### situation-b

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 6000
- Interventions (commands applied): 5

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 180 | 180 | 1000 | 0 | Normal |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 757 | 757 | 1000 | 0 | Normal |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 1452 | 1452 | 1000 | 0 | Normal |
| upgrade_components | component | 4000 | 0 | 0 | 602 | 602 | 4000 | 0 | Normal |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1317 | 717 | 500 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 15 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 23 | 478 |
| technical_materials | 2 | 182 |
| component | 8 | 228 |
| module | 2 | 100 |
| robot_frame | 0 | 24 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 778 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 77 | 796 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 0 | 475 |
| dock_a_hold | 265 | 808 |
| dock_b_hold | 740 | 800 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 6 | 720 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 5244 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 688 | 4574 | 18 | 0 | 0 | 720 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 160 | 5117 | 544 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 4549 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `78388ab89df1fb27cc8cadac866ef661f6efdbf4663f5b4e49e8e22b9002f91e`
