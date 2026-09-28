# RimRound — Community Patch + Expansion (EXPECT JANKINESS)
---
> **Note:** this build ports the complete **RimRoundFeedOther** companion from the
> community fork **`6retroforlife9-gif/RimRound`** (branch `Feed-other-and-fixes-to-1.6`)
> and adds a large amount of new content on top: the flesh dimension, the gorge
> constrictor, the sweet slime, the Feedees meme, a fattening quest and a stack of mod
> integrations. See [What's New](#whats-new) and [Credits](#credits).

## What is RimRound?
A weight-gain mod for RimWorld. This community fork adds a **density-aware liquid-food
network**, an **Anomaly-backed flesh dimension** with its own creatures, a **gluttonium**
exposure system, **meld/void bioweapons**, the **Feedees** meme, touch interactions that
actually play out, and integrations with a long list of other mods — plus the
`RimRoundFeedOther` companion (feed-other socials, idle eating, Food Network v2, prisoner
fattening, reliability fixes) ported from the community fork.

Pawns that can't gain weight (androids, mechanoids and the like) don't get RimRound's
character-description and perks tabs.

---

## Flesh Dimension & Void Realm (Anomaly)
The **void maze** is a true pocket-map flesh dimension (Anomaly's pocket-map system,
underground, 30 °C, breathable warm-flesh floor):

- **Void Portal** (`RR_VoidPortal`) — tears open on a home map (Anomaly incident) once
  someone is Chubby or heavier. Interact to step into the maze; a warm "void warmth"
  hediff applies while you linger. Can be sealed while nobody is inside. Carry downed
  pawns back out through it.
- **The maze itself** — `RR_VoidMazeMap` biome, mapgen `RR_VoidMazeGen`, `RR_FleshFloor`
  warm-flesh terrain, a return portal at the heart.
- **Void Warmth / Saturation** — hunger is suppressed and you slowly gain weight; the
  longer you stay the more you balloon, until your excess **tears free as a bloated
  "void echo"** that hunts you.
- **Void Echoes** — carry your weight and a `RR_VoidEchoVigor` hediff, are capturable on
  holding platforms, and can be milked for **voidmilk**. A **void echo milking rig**
  (holding-platform facility) raises the yield and pipes it straight into the feeding
  network.
- **Bloatgas geysers** and **living walls** — geysers swell and erupt with fattening gas;
  flesh walls, blob walls and gluttonium veins bleed bloatgas when cut open.
- **Void Gluttonium** — harvested from the maze: ultra-tech cooking (Feast of the Void)
  and constrictor-vat mutations.
- **Void Fascination** — a ramping, weight-opinion-scaled mood while inside (dread for
  weight-haters, bliss for weight-likers).
- **Studying the void seam** (codex, Advanced) pays off in stages: longer runs, fewer
  fleshbeasts, a bigger haul home.

## Gorge Constrictor (Anomaly)
A fleshbeast like an engorged leech, arriving **swollen with 1100–1600 kg of slurry**. It
hunts the map, latches onto someone and pumps its load into them.

- **Fight it off before it latches:** every hit spills part of its load (filth and gas),
  so a colony that shoots it down to size can keep the victim from bursting.
- **Latched:** the leech rides behind its victim at the size of what it still holds and
  shrinks as it pumps; its tail wraps the belly; a proboscis feeds them. Victims burst if
  it still holds enough to take them past their limit — **tear it off** (float menu) in
  time. Muffled SpeakUp lines, and a sharper "about to burst" mood near the end.
- **Studying it** (codex entry, Advanced) unlocks **binding**: a research and ritual turn a
  lure into a **bound constrictor** that never bursts anyone and lets go when they're full.
- **Constrictor vat** — stores and **refuels** a bound constrictor with nutriment (hauled
  food, the VNPE paste net or RimRound's food network) and grows **mutations** into it
  (quicker pumping, a bigger sac, euphoric mucus, persuasive afterglow, pushing past the
  burst point...). Study levels gate the stronger mutations.
- Its monitor face is **most content when it's nearly spent**, and can be turned off in
  the settings.

## Sweet Slime (Anomaly)
A translucent pink stalker after Lobotomy Corporation's *Melting Love* — **never lethal**.

- It hunts **invisibly** (disruptor flares reveal it), goes for sleepers and loners, and
  gives them a **smothering hug and a light puffkiss**, then bursts over them in pink
  slime (red letter). Afterwards it is spent and visible for a while.
- It never fights back: it **flees**, heals while hidden, shrugs off fists (heavy blunt
  armour), and anyone punching it gets **stuck in it** — stunned, slimed and puffed.
- It leaves a **seed**: warm and pleasant at first, then flushed and spreading to anyone
  close, until it **melts the host into a slime cocoon** (sized to the host). Break it open
  for +40 kg, or leave it two days: +160 kg and 1–2 **slime spawn**. Surgery extracts the
  seed.
- Victims are **coated in pink slime** — drawn over the pawn like firefoam, sticky and
  fireproof.

## Feedees Meme & Rituals (Ideology)
- **Feedee** role (Obese or heavier) and a **worship** ritual at the feedee's bed: only
  weight-likers take part, offerings are fed to the feedee, quality scales with size.
  Weight-haters are set against the meme.
- **Calling the seam** — a Feedees-only ritual that tears open a void seam instead of
  waiting for one.

## Gluttonium
- **Gluttonium ore** radiates weight gain (`RR_GluttoniumExposure`); **glut bricks**,
  a **hazmat suit**, leaking gluttonium buildings, ToxicResistance-scaled resistance and
  an extra generation pass.
- **Gluttonium diffuser** — a fuelled security building that vents fattening gas: always
  on, or **ambush** mode when hostiles come into view.
- **Stretch serum** (drug lab) — raises the soft fullness limit and stomach elasticity,
  stacking up to five doses.

## Meld, Bloat & Void Bioweapons (Anomaly)
- **Meld disease**, **meld aerosol** shells and grenades (a pawn at full progression
  detonates into blob walls and void gluttonium), **Biological Warfare** integration.
- **Bloated unnatural corpses** — an awoken corpse swells and bursts instead of killing;
  its victim survives, heavier.
- **Obelisk mutations** add weight and gluttonium exposure.

## Events & Quests
- **Fattening commission** — a friendly faction sends a guest who wants to go home
  bigger. The target runs from Obese to Gelatinous I; days and rewards scale with it, and
  the Empire pays in **honor**. Miss it and goodwill drops; lose the guest and it drops
  hard. Guests too big to walk are collected by transport.
- **Void surge** — RimRound's toxic fallout: a pink haze over the whole map, and anyone
  outdoors builds **void exposure** that fattens them gradually and fades under a roof.

## Touch Interactions
Pawns start weight-opinion-scaled touch interactions (each grants staged thoughts and
adjusts `SEX_Intimacy`), and each one **plays out as what it is** while the other pawn is
held in place (never pulled out of a drafted, player-ordered or sleeping state):

- **Fondling** — they lean in and knead; hearts if it's welcome.
- **Smothering** — the heavier pawn **backs the other into the nearest wall** and leans
  their whole bulk in (or pins them where they stand).
- **Exploring** — the smaller pawn circles a larger one, feeling each side.
- **Wet Smothering** — a lactating pawn holds someone to their chest (babies get a real
  breastfeeding job).
- **Tail Groping** — only on pawns that actually have a tail (tail body part, tail gene
  or a HAR tail).
- **Shared Feeding** — two pawns eat side by side at a feeding machine.

## Feeding Tube System (density-aware liquid food)
- Pipes/vats/processors/distillery/faucets/auto-feeders carry **liquid food with per-batch
  nutrition density** (nutrition + fullness + ingredients preserved per FIFO batch).
- **Food Processor**, **Nutrient Distillery**, **Food Converter** (VNPE paste → liters),
  **Food Network v2** (conserved FIFO batches, secure transactions, per-tank state).
- **Auto-feeders** hose up to 9 cells and say why they can't reach a pawn.
- Feed buildings live under the **Network** build tab; pipes are drag-constructable.

## RimRoundFeedOther companion (ported — see Credits)
- **Feed Other / Share Meal**, **idle underweight eating**, **prisoner fattening**,
  bed-linked auto-feeders, reliability fixes, and a **7-page settings window**
  (`RimRound Patch` main button).

## RimRound Extra Events (RREE)
Separate DLL (`1.6/ExternalMods/RimRoundExtraEvents`), always loaded:

- **Enbiggener gas weapons** — enbiggener mortar shells (Mortars research), an enbiggener
  smoke launcher, and IED enbiggener smoke traps (built from enbiggener shells). The gas
  builds **mutagenic enbiggener buildup**, which now actually fattens (up to 2 kg/hour at
  full, fading over days) and can seed **fattening growths** that keep adding weight until
  a doctor excises them.
- **Fattening pirates** — flab grenadiers and pirate/mercenary enbiggunners now turn up in
  pirate raids.
- **Experimental appetite stimulant** — a drug (drug lab, after gluttonium drugs research)
  that stacks hunger, eating speed and digestion; enough doses and the user starts
  binging on anything edible.
- **Compressor suits** — fat-storage apparel (Metabolic Textiles research) that can hold
  and release stored weight.
- **Weight-gain totems** — small art pieces from quest rewards that raise weight gain
  nearby.
- **Fat comms console** — a buildable console that can summon a Diabolus threat (Biotech).
- *Retired:* the **Mutagenic Enbiggener Fallout** event no longer fires — void surge fills
  that role (and the old condition never cleared itself). *Unfinished and not in game:*
  the scale trap, the archo fat statue and mobility mechanites.

## Mod Integrations
- **SpeakUp** — dialogue for every touch interaction, muffled lines while constricted,
  and chitchat (with replies) about getting slimed, carrying a seed, feedee worship,
  seeing someone burst and commission guests.
- **RimVore-2** — weight ↔ vore chance/capacity, digested-prey weight gain, AI.
- **Intimacy series** — gluttonium ↔ `SEX_Intimacy`, shared feeding.
- **Lactation Expansion** — milk scales with weight, opinion moods, spiked milk.
- **Biological Warfare** — meld aerosol as a chemical-weapon family.
- **Vanilla Nutrient Paste Expanded** (required) and **Reimagined Progression** — food
  network bridge, research-tree compat.
- **SwellGlow** (bundled) — adipose genes (hardy adipose, buoyant frame, gluttonium
  tolerance) and the **Swellkin** xenotype, which turns up now and then in mixed factions.
- **Odyssey** unique weapons — **gluttonium rounds / pellets** traits (puff targets up;
  mostly wears off). With **Unique Weapons Unbound**, fitting one costs gluttonium.
- **Royalty / Vanilla Persona Weapons** — the **gorging** persona trait feeds its wielder
  whenever it kills or downs someone.
- **Vanilla Weapons Expanded – Non-Lethal** — **gluttonium dart gun** (sedates and
  swells) and **sweet-slime foam grenades** (put out fires, coat everyone in slime).
- **Vehicle Framework** — caravan diet and fullness handling.

## Dependencies
- **Required:** Harmony, Humanoid Alien Races, Vanilla Expanded Framework,
  **Vanilla Nutrient Paste Expanded**
- **Required for the flesh dimension, constrictor, sweet slime and meld content:** Anomaly
- **Required for the Feedees rituals:** Ideology
- **Recommended:** SpeakUp, RimVore-2, Intimacy series, Lactation Expansion
- **Optional:** Royalty, Biotech, Odyssey, and the integrations above

---

## What's New
- **Gorge constrictor**: load-based fight-off, bursting, tearing off, binding research and
  ritual, the **constrictor vat** with mutations, study payoffs, codex entries; latched
  look redone (rides behind its victim, sized by what it holds).
- **Sweet slime**: invisible non-lethal stalker, seed, spread, slime cocoons, slime spawn,
  pink slime coat, extraction surgery.
- **Feedees** meme: worship ritual, seam-calling ritual, feedee role.
- **Fattening commission** quest; **void surge** event (void exposure).
- **Gluttonium diffuser**, **stretch serum**, **void echo milking rig**, SwellGlow
  **adipose genes** and **Swellkin**.
- Touch interactions **act themselves out** (wall pin, kneading, circling, nursing,
  tail stroking); shared feeding shows eating.
- Integrations: SpeakUp chitchat, VNPE Reimagined, Odyssey/UWU weapon traits, gorging
  persona trait, VWE Non-Lethal weapons.
- **RREE made to work**: enbiggener gas and fattening growths now fatten, the IED uses
  enbiggener shells, the appetite stimulant is craftable (and keeps its bonus through
  saves), fattening pirates join raids; the fallout event is retired in favour of void surge.
- Tail groping only targets pawns that actually have a tail.
- Fixes: auto-feeder disconnects and hose range, void-portal lifecycle, stale constrictor
  state through save/load, the pump animation freezing when zoomed out, character tabs
  hidden for non-weight pawns, several Player.log errors.

Earlier: flesh dimension / void maze, void echoes, meld content, gluttonium exposure,
the density-aware food network, and the ported `RimRoundFeedOther` companion.

## Credits
- **Ported companion (`RimRoundFeedOther`)** from the community fork
  **`6retroforlife9-gif/RimRound`** (branch `Feed-other-and-fixes-to-1.6`, build v1.0.69.22):
  https://github.com/6retroforlife9-gif/RimRound — builds on the original by **Niwatori401**.
- The **sweet slime** is inspired by *Melting Love* from Project Moon's *Lobotomy
  Corporation* ([wiki](https://lobotomycorporation.wiki.gg/wiki/Melting_Love)).
- **Sound/asset credits:** see **ATTRIBUTION** (CC-BY voice/SFX).
- Community-contributed content: SwellGlow (Galactase/Bun), meatslop clothing (Gosuke),
  various fixes (digifox_, Toggle, prototype99).

## License
Unlicense unless otherwise specified; some assets CC-BY (see ATTRIBUTION). The ported
`RimRoundFeedOther` content follows the upstream repository's terms.
