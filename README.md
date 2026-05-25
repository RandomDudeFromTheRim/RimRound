# RimRound - Community Patch + Expansion

> **⚠️ LLM-Assisted Project — Expect Bugs AND stuff pulled out the AI's ass**
> This codebase was developed with heavy LLM assistance. Edits are applied rapidly across many interconnected systems (Harmony patches, XML defs, comps, hediffs, VEF pipes). Some patches may conflict, edge cases are not fully tested, and save compatibility is not guaranteed between builds.... some stuff might not just exist lmao. You have been warned.
>
> **Will run poorly or break without:** Harmony, Humanoid Alien Races, Vanilla Expanded Framework.
> **Heavily recommended:** SpeakUp, RimVore-2, Intimacy series (Lovin' + Socio Butterfly), Lactation Expansion, Ushanka's Biological Warfare.
> **Requires Anomaly DLC** for all void/meld/fleshbeast content.

A weight gain mod for RimWorld. This is a community fork with extensive bugfixes, new content, and deep integration with other mods.

---

## Bugfixes & Core Improvements
- Fixed NRE in `RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier` when pawns have no `story` or `bodyType`
- Fixed NRE in `Hediff_Weight.CurStageIndex` when `this.pawn` is null during world-tick stat eval
- Fixed `GetBodyTypeWeightRequirementMultiplierByDefName` returning 0 for unknown body type suffixes (caused instant max stage)
- Re-enabled 3 disabled Harmony patches (DrawBodyGenes, shell clothing, head cover adjustments)
- Fixed caravan diet mode saving (`SaveCaravanPatchUtility` uncommented)
- Filled 3 NotImplementedException stubs (ConvertWeightOpinion, HungerDroneUtility, ModCompatibilityUtility)
- Fixed `return 0` bug that caused all unknown body type suffixes to instantly max weight stage

## Feeding Tube System (Refactored) (I DONT FUCKING KNOW IF IT ACTUALLY WORKS)
The custom pipe network has been replaced with **VEF's PipeSystem** framework:
- Pipes (`RR_FoodPipe`, `RR_UndergroundFoodPipe`) use `PipeSystem.Building_Pipe`
- Valves (`RR_FoodValve`) use `PipeSystem.Building_PipeValve`
- Storage vats use `PipeSystem.CompProperties_ResourceStorage` with built-in bar rendering
- Auto-feeders, processors, and distillery use `PipeSystem.CompResource` for network access
- Network overlay visualization via VEF's pipe network tool
- Auto grid-based flood fill for connectivity (no manual merge/split)
- **Requires Vanilla Expanded Framework**

## Gluttonium Radiation System
- **Gluttonium ore** radiates weight gain in a radius, applying `RR_GluttoniumExposure` hediff
- Exposure builds up, adding weight gain requests over time
- **Glut Bricks** (`RR_GlutBrick`): processed blue-tinted bricks for construction (3 raw → 10 bricks, lower rads)
- **Hazmat suit** (`RR_Apparel_HazSuit`): 85% radiation protection, crafted from gluttonium fiber
- Buildings made from gluttonium or glut bricks leak weak radiation
- Protection scales with gluttonium resistance stat on apparel

## Horny Social Interactions
Pawns can initiate social interactions based on weight opinion:

- **Fondling** — Neutral+ initiator touches a Thick+ recipient. Mood based on recipient's weight opinion.
- **Smothering** — Larger pawn (Chubby+, heavier) presses against a smaller pawn. Weight-dependent intensity.
- **Exploring** — Smaller pawn with Like+ opinion explores a larger pawn's curves.
- **Wet Smothering** — Like+ lactating pawn force-feeds milk to a smaller pawn. Requires the `Lactating` hediff.
(ratkin/races that add tails) - **Tail Groping** - because that chunk is kinda alluring...?
All interactions can spark **social fights** if the recipient has Hate/Dislike weight opinion.

## SpeakUp Dialogue Integration
Custom dialogue for all horny interactions, conditional on each pawn's weight opinion trait. Requires **SpeakUp** mod.

## Anomaly & Void Content

### Blob Walls & Void Gluttonium
- `RR_BlobWall`: skin walls with CornerFiller linking... currently only really acquirable from unnatural corpse going boom
- `RR_BlobWallMineable`: mineable nodes that yield `RR_VoidGluttonium`... probably unavailable as of now. Tough luck!
- `RR_VoidGluttonium`: high-value resource for ultra-tech cooking
- **Feast of the Void**: a meal that bypasses soft limits entirely (i am unsure if this is actually useful lmao)

### Bloated Unnatural Corpses
Harmony patch on `Pawn.TakeDamage`: intercepts unnatural corpse kill damage.
- Corpse rapidly gains 810kg over 5 seconds
- Then explodes into blob walls, leaving voidgluttonium around it
- Witness reactions based on weight opinion
- Attempts RV2 vore integration if RimVore-2 is installed (doesnt work lol)
- Gives the victim sickness and some gain of weight upon corpse rupture

### Meld Aerosol Bioweapon (HORRIBLY BROKEN, USE AT YOUR OWN RISK)
Craftable at a Drug Lab: combine gluttonium + twisted meat to create meld aerosol shells/grenades.
- On impact, applies `RR_MeldAerosol` hediff to all pawns in radius and makes them thiccer
- At max progression, pawn transforms into a fleshbeast
- **Transformation depends on pawn mass:**
  - Weight sev < 0.1 → Fingerspike
  - 0.1–0.5 → Toughspike/Trispike
  - 0.5–2.0 → Bulbfreak
  - > 2.0 → **Dreadmeld** (only from extremely fat pawns)
this system might be stuck at 15%, didnt test it much lol

### Obelisk Mutator Weight Gain
When the Twisted Obelisk mutates a pawn, they also gain weight + gluttonium exposure. Weight-likers get a mood boost.

### MELD DISEASE
Getting attacked by fleshbeasts has a chance to make them begin merging into your pawn, dealing the fleshbeastss damage and auto-sealing any bleeding on pawn while also making it's weight increase... pawns react differently to it based on their weight opinion. The hits can stack, which can cause your pawn to balloon out if surrounded by fleshbeasts. Be on the lookout and prepare to use the liposuction surgery if fleshbeasts go loose...

## Mod Integrations

### RimVore-2 (6 integration patches)
- Weight affects vore success chance (fat prey harder to process)
- Predator capacity scales with body weight
- Digested prey converts to lasting weight gain
- Weight opinion affects vore proposal AI (haters refuse fat prey)
- Heavier prey need more struggles to escape
- Bloat visual + moodlets on vore initiation

### Intimacy — A Lovin' Expansion
- Gluttonium exposure affects `SEX_Intimacy` need (rises for fans, drops for haters)

### Intimacy — Socio Butterfly
- Weight-liking pawns are 3x more likely to start conversations for force-feeding

### Lactation Expansion (5 integration patches)
- Milk yield scales with weight stage (up to 15x at Gelatinous)
- Weight opinion affects milking mood (haters get -12, lovers get +8)
- Gluttonium-exposed pawns produce spiked milk
- Fullness increases lactation rate
- Nursing station building auto-feeds hungry pawns

### SpeakUp
- 12 reply templates with dialogue for all horny interactions
- Dialogue changes based on speaking pawn's traits and weight opinion

## Dependencies
- **Required:** Harmony, Humanoid Alien Races, Vanilla Expanded Framework
- **Required for void/meld content:** Anomaly DLC
- **Recommended:** SpeakUp, RimVore-2, Intimacy series (Lovin' + Socio Butterfly), Lactation Expansion, Ushanka's Biological Warfare
- **Optional:** Odyssey DLC, Biotech DLC

## License
This project is, unless otherwise specified, licensed under the Unlicense. Specific assets may be licensed under CC-BY.
