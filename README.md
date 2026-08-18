# RimRound — Community Patch + Expansion (EXPECT JANKINESS)
---
> **Note:** this build ports the complete **RimRoundFeedOther** companion from the
> community fork **`6retroforlife9-gif/RimRound`** (branch `Feed-other-and-fixes-to-1.6`)
> and documents the flesh-dimension / void-maze and other content that was already
> land-locked in the codebase. See [Credits](#credits) and [What's New](#whats-new).

## What is RimRound?
A weight-gain mod for RimWorld. This community fork adds a **density-aware liquid-food
network**, an **Anomaly-backed flesh dimension**, a **gluttonium exposure** system,
**meld/void bioweapons**, and a suite of **horny social interactions** — plus the
`RimRoundFeedOther` companion (feed-other socials, idle eating, Food Network v2,
prisoner fattening, reliability fixes) ported from the community fork.

---

## Flesh Dimension & Void Realm (Anomaly)
The **void maze** is a true pocket-map flesh dimension (Anomaly's pocket-map system,
underground, 30 °C, breathable warm-flesh floor):

- **Void Portal** (`RR_VoidPortal`) — an entry token/building. Interact to step into
  the maze; a warm "void warmth" hediff is applied while you linger inside.
- **The maze itself** — `RR_VoidMazeMap` biome (`RR_VoidMazeBiome`), mapgen
  `RR_VoidMazeGen`, `RR_FleshFloor` warm-flesh terrain, a return portal at the heart.
- **Void Warmth** — inside the maze your hunger is suppressed (and eventually killed),
  and you slowly gain weight.
- **Void Saturation** — the longer you stay, the more you balloon. At full saturation,
  your accumulated excess **tears free as a bloated "void echo"** that hunts you.
- **Void Echoes** — spawned echoes carry your weight and a `RR_VoidEchoVigor` hediff
  (extra move/manip, armor), are capturable on Anomaly holding platforms, and want you
  back inside them.
- **Void Gluttonium** — high-value harvesting from the maze, usable for ultra-tech cooking.
- **Void Fascination** — "the void calls to you": the longer you linger, the more
  the void makes you hungry and fills you — and it shifts mood (see below).

> **Mood note:** being inside the flesh dimension now applies a **ramping, weight-
> opinion-scaled mood** (dread for weight-haters, bliss for weight-likers), plus hunger
> suppression, a movement penalty, weight gain and intimacy. Gluttonium exposure and
> the social interactions also apply moods (see [Mood & Thoughts](#mood--thoughts)).

## Gluttonium
- **Gluttonium ore** radiates weight gain in a radius, applying `RR_GluttoniumExposure`.
- **Glut Bricks** (`RR_GlutBrick`) — processed blue-tinted bricks (3 raw → 10 bricks,
  lower radiation).
- **Hazmat suit** (`RR_Apparel_HazSuit`) — 85% radiation protection from gluttonium fiber.
- Buildings from gluttonium/glut bricks leak weak radiation; protection scales with a
  gluttonium-resistance stat.
- Resistance to gluttonium is influenced by the victim's **ToxicResistance** (wasters are
  tough).
- Additional gluttonium generation pass (`RR_ScatterGluttoniumLumps`) so it doesn't
  displace vanilla ore.

## Meld, Bloat & Void Bioweapons (Anomaly)
- **Meld disease** — fleshbeasts merge into pawns, self-damaging and applying
  `RR_MeldGrowth` (converts meld mass → weight, auto-seals bleeding; weight-opinion moods).
- **Melding creature contacts** — early creatures (Fingerspike) deal more self-damage /
  give less weight; a swarm accumulates.
- **Meld Aerosol** — craftable shells/grenades apply `RR_MeldAerosol` (pink RR gas);
  at max progression a pawn **detonates** into `RR_BlobWall` clusters that drop
  `RR_VoidGluttonium`.
- **Bloated unnatural corpses** — killed unnatural corpses balloon ~810 kg then explode
  into blob walls, leaving void gluttonium; witnesses react by weight opinion.
- **Blob walls** (`RR_BlobWall` / mineable `RR_BlobWallMineable`) + **Feast of the Void**.
- **Obelisk mutation** — Twisted Obelisk mutations add weight + gluttonium exposure;
  weight-likers get a mood boost.

## Horny Social Interactions
Pawns initiate weight-opinion-scaled interactions (each grants staged thoughts + adjusts
`SEX_Intimacy`):

- **Fondling** — Neutral+ initiator & Thick+ recipient.
- **Smothering** — heavier pawn presses a smaller one; weight-dependent intensity.
- **Exploring** — smaller pawn with Like+ explores a larger pawn's curves.
- **Wet Smothering** — lactating pawn force-f feeds milk (requires `Lactating`).
- **Tail Groping** — for ratkin/tail races.
- **Shared Feeding** — two pawns share a lazy feeding session at a machine.
- **Close Encounters** — via `CloseContactUtility` (requires real touch range; grapple-lock).

## SpeakUp Dialogue Integration
Custom dialogue for all horny interactions, conditional on weight-opinion trait
(requires **SpeakUp**).

## Feeding Tube System (density-aware liquid food)
- Pipes/vats/processors/distillery/faucets/auto-feeders carry **liquid food with per-batch
  nutrition density** (nutrition + fullness + ingredients preserved per FIFO batch).
- **Food Processor** converts hoppered solid food → liters, preserving each food's density.
- **Nutrient Distillery** condenses density (rotation-aware input/output ports).
- **Food Converter** (`RR_FoodConverter`) — bridges the VNPE paste network into the liter
  network (requires VNPE).
- **Food Network v2** (`RimRoundFeedOther.dll`) — conserved FIFO batch storage with secure
  store/draw transactions, per-tank saved state, and legacy import/export.
- Feed buildings live under the **Network** build tab; pipes are drag-constructable.
- **Requires** Vanilla Expanded Framework + Vanilla Nutrient Paste Expanded.

## RimRoundFeedOther companion (ported — see Credits)
- **Feed Other / Share Meal** — pawns feed each other as social recreation; right-click
  feeding, bedside feeding, post-meal social.
- **Idle underweight eating** — thin, idle colony pawns auto-eat to fullness.
- **Prisoner fattening** — bed-lock + controlled direct feeding under `RR_Fatten`.
- **Automatic feeders & beds** — bed-linked auto-feeders, correct double/3x3-bed handling.
- **Reliability fixes** — hoverchair, NotRegalBed, orbital relief, auto-milk expression,
  weight-opinion abilities, beta trait generation.
- **7-page settings window** (`RimRound Patch` main button).

## RimRound Extra Events (RREE)
Separate DLL (`1.6/ExternalMods/RimRoundExtraEvents`): Mutagenic Enbiggener Fallout,
FatToxicBuildup, FatCarcinoma, enbiggener smoke mortars/launcher/IEDs, Mobility Mechanites.

## Mood & Thoughts
- **Works correctly:** Gluttonium exposure (staged, weight-opinion-scaled −50 to +15);
  Void fascination inside the flesh dimension (ramping, weight-opinion-scaled −30 to +22);
  Fondle/Smother/Explore/WetSmother/TailGrope thoughts; Force-Fed / Was-Force-Fed;
  lactation milked thoughts; meld-growth; bloated-corpse witness; shared feeding;
  weight-opinion moods.
- **Known gaps (checked 2026):** (none outstanding — the flesh-dimension mood and
  void-fascination wiring are now fixed.)

## Mod Integrations
- **RimVore-2** (6 patches): weight ↔ vore chance/capacity, digested-prey weight gain,
  weight-opinion vore AI, etc.
- **Intimacy — Lovin' / Socio Butterfly**: gluttonium ↔ `SEX_Intimacy`; weight-likers
  3× more likely to start force-feed conversations.
- **Lactation Expansion** (5 patches): milk scales with weight, opinion moods, spiked
  milk, fullness-lactation, nursing station.
- **SpeakUp** (12 reply templates).

## Dependencies
- **Required:** Harmony, Humanoid Alien Races, Vanilla Expanded Framework,
  **Vanilla Nutrient Paste Expanded**
- **Required for void/meld content:** Anomaly DLC
- **Recommended:** SpeakUp, RimVore-2, Intimacy series, Lactation Expansion
- **Optional:** Odyssey, Biotech DLC

---

## What's New (changelog highlights since the last README in May)
- Flesh dimension / void maze pocket map, void portal + return, void warmth/saturation,
  void echoes (git: `4415bea`…`fcb661`).
- Melding fleshmass, meld aerosol, blob walls/void gluttonium, bloated unnatural corpses.
- Gluttonium exposure system + radiation, glut brick, hazmat suit, extra gen pass,
  ToxicResistance interaction.
- Horny social interactions (fondle/smother/explore/wet-smother/tail-grope/close-contact).
- Density-aware liquid-food network, food converter (VNPE bridge), processor fix,
  Network build tab, drag-build pipes, retired-pipe→steel save repair.
- Ported `RimRoundFeedOther` companion (feed-other, idle eating, Food Network v2,
  prisoner fattening, reliability fixes, settings UI).

## Credits
- **Ported companion (`RimRoundFeedOther`)** from the community fork
  **`6retroforlife9-gif/RimRound`** (branch `Feed-other-and-fixes-to-1.6`, build v1.0.69.22):
  https://github.com/6retroforlife9-gif/RimRound — builds on the original by **Niwatori401**.
- **Sound/asset credits:** see **ATTRIBUTION** (CC-BY voice/SFX).
- Community-contributed content: SwellGlow (Galactase/Bun), meatslop clothing (Gosuke),
  various fixes (digifox_, Toggle, prototype99).

## License
Unlicense unless otherwise specified; some assets CC-BY (see ATTRIBUTION). The ported
`RimRoundFeedOther` content follows the upstream repository's terms.
