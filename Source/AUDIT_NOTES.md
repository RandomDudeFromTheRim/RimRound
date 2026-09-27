# RimRound audit notes (September 2026, `chud` branch)

These notes cover a review and fix pass on the code added in August 2026 and
on the RimRoundFeedOther port. They say what was broken, what changed, what
was checked and what still needs an in-game test.

All of it is on `chud`, commits `3529e4c` through `653e9d2`. Each commit
message has more detail (`git log 051f7e7..chud`).

**Nothing here has been tested in-game.** Everything compiles, and every
Harmony patch target was checked against the game assembly, but none of it has
been played. The checklist at the end covers what to try first.

---

## 1. Features that were broken or never worked

These are the most important findings. Each of these features looked
finished in the code but could not work in-game.

| Feature | What was wrong | Fix | Commit |
|---|---|---|---|
| **Echo capture → feeding → voidmilk** | Anomaly only shows "Capture" for humanlikes that are platform-capturable mutants. Echoes are plain humanlikes, so there was never a capture button and nothing after it could be reached. Downed entity-faction pawns also die 50–90% of the time. | Harmony postfixes on `CompHoldingPlatformTarget.StudiedAtHoldingPlatform` / `CanBeCaptured`. Echoes spawn with a 0% chance to die when downed. | `90fd729` |
| **Voidmilk production** | A pawn on a holding platform is despawned, so `pawn.Map` was always null and no milk was ever made. | Uses `MapHeld` / `PositionHeld`. | `3529e4c` |
| **Void portal** | Nothing in the mod ever spawned it (dev mode only). It also had no `tickerType`, so the timeout, fleshbeast spawns and cleanup never ran. | New Anomaly-only "Void portal" incident. The portal now ticks and has a "Seal portal" button. | `7c370a8`, `71a1d48` |
| **Food pipes** | The FeedOther port made every food pipe fully transparent. The food converter did nothing on Food Network v2, which is the default. | Normal pipes are visible again. A separate **hidden food pipe** was added. The converter works on both networks. | `2464afb` |
| **Mutator obelisk weight gain** | It patched a method that doesn't exist (`TryMutate`), behind a mod check that compared a packageId to display names. It never applied. | Patches `CompObelisk_Mutator.DoMutation`, and only reacts to successful mutations. | `7c370a8` |
| **Biological Warfare integration** | `PatchOperationFindMod` was given a packageId but compares display names, so it never applied. The ammo also rotted away in 0.75 days and had two recipes. | Rebuilt as a folder loaded through `loadFolders.xml` `IfModActive`. It no longer rots and has one recipe. | `7093624` |
| **Unnatural corpse bloat** | Ran every frame, so it stacked while paused and skipped steps at speed. Its state was lost on save/load. It raced vanilla's corpse despawn and often did nothing. | Now a saved `GameComponent` ticked per game tick. It bursts at 590 ticks, before vanilla removes the corpse. | `e41ea27` |
| **FeedOther unpatching** | The bootstrap removed *every* RimRound patch on a method, which also deleted the heavy-pawn downed pose. | Only unpatches the specific RimRound patch class. | `315b0a2` |
| **Voidmilk afterglow** | The hediff never expired, and a second drink gave no weight. | Lasts about 0.75 days. Each drink tops it up and adds about 4 kg per full dose. | `5e3fb80` |
| **Shared feeding thought** | It had no duration, so it expired immediately. | 2-day social memory with an opinion bonus. | `7c370a8` |
| **Void fascination** | Gained about 0.12/day instead of about 1/day, so the later stages were unreachable. | Fills in about a day. | `71a1d48` |

### Smaller bugs fixed

- **Perks:** every pawn got +50% eating, and Makes All The Rules read the Breakneck Buffet level (`7c370a8`).
- **RV2 roll bonus:** the weight-opinion factor compounded on every modifier in a roll chain. It now applies once per roll (`9241a6b`).
- **RV2 struggles:** the patch overwrote RV2's required struggles (default 60) with 0–4, so fat prey escaped almost at once. It now scales RV2's value (`9241a6b`).
- **Wet smother without Biotech:** logged a missing-def error on every interaction check (`315b0a2`).
- **Missing Anomaly:** twisted meat and blob-wall drops caused load errors without Anomaly (`7093624`).
- **Void maze exits:** forced returns dropped pawns at the map centre, and destroying the portal stranded pawns in the maze (`3529e4c`).
- **Close encounters:** no longer stun drafted, downed or mentally broken pawns (`7c370a8`).

---

## 2. Weight gain: permanent vs temporary

`HediffUtility.QueueWeightGain(pawn, kilos, durationTicks = 0)` is **permanent
by default**. A duration above 0 means the weight comes back off later.

**Now permanent:**
- Void saturation (ambient gain in the maze)
- Shared feeding
- Voidmilk and its afterglow
- Echo release (+15 kg to each released pawn)
- Mutator obelisk
- RV2 digestion
- Wet smother milk
- Gluttonium force-feed
- Gluttonium exposure
- Sudden weight gain (fattening gas)
- Unnatural corpse bloat (the victim keeps what they gain)

**Still temporary, as requested:** fattening bullets, except the advanced
gun's version, which was already permanent.

---

## 3. Void echo: how it works now

1. **Capture:** down an echo in the void maze. It won't die from being downed,
   and "Capture" appears as it does for other Anomaly entities.
2. **Feed:** select a player-owned holding platform with an echo on it and use
   **"Feed a colonist to the echo"**. It asks for confirmation, then picks a
   free adult colonist within 60 tiles who can reach the platform. After 600
   ticks (about 10 in-game seconds) the colonist is swallowed and kept safe as a
   world pawn, not killed.
3. **Echo weight:** each colonist fed adds +0.06 weight severity (about 60 kg),
   capped at 1.6. This is set directly, so the echo's body sprite might not
   update until its next graphics refresh. Worth checking.
4. **Voidmilk:** 2 + 2 × (swallowed colonists) about every 10 in-game hours
   (25,000 ticks). The echo's weight does **not** affect this.
5. **No weight loss:** while it holds colonists (void vigor), the echo's hunger
   rate is 0, rest doesn't fall and it can't mental break. It doesn't slim down
   or starve.
6. **Release:** **"Release the consumed (N)"** gives everyone back, each with a
   permanent +15 kg. They're also released automatically if the echo dies or
   loses void vigor.

---

## 4. Flesh dimension changes

- **Sprites:** the flesh wall, hardened shell and gluttonium vein wall are now
  linked 4×4 atlases (they used to be flat placeholder squares). They match
  vanilla Anomaly fleshmass: shaded lumps, thick dark outline, muted pink. The
  flesh floor and bloat geyser are also new.
  - Generated by `Source/TextureGen/fleshtex.py` (needs numpy + pillow;
    `python3 fleshtex.py <out_dir>`). The output is deterministic, so edit the
    palette or shapes there rather than editing the PNGs.
  - **No Anomaly DLC textures are in the repo.** They were only used as a
    visual reference.
  - Vanilla's lumps spill over the outline more than ours do. If you want them
    closer to vanilla, the outline shape in `fleshtex.py` is the place to
    change.
- **Bloatgas geysers** (`RR_BloatGeyser`, up to 8 per maze):
  - Pressure builds over 2–4 in-game hours, 2.5× faster with a pawn within 7
    cells.
  - They hiss as a warning, then erupt fattening gas over about 5 cells.
  - A direct blast adds void saturation, plus intimacy for pawns whose weight
    opinion is Like or higher.
  - Inspecting one shows its pressure. There's a dev button to force an
    eruption.
- **Walls release gas:** flesh walls, blob walls and gluttonium veins release a
  puff of fattening gas when destroyed or mined (`CompGasOnDestroy`).
- **Maze generation** (`GenStep_RRVoidMaze`) is split into small named steps.
  It no longer keeps a reference to the generated map on the shared def.

---

## 5. What was checked, and how

- **Compile:** both projects build with 0 errors and 0 warnings.
  - `RimRound` was built from exactly the 331 files listed in its `.csproj`.
  - `RimRoundFeedOther` was built from all 37 of its listed files.
  - Built against RimWorld 1.6 reference assemblies (Krafs.Rimworld.Ref
    1.6.4871) and the repo's Harmony 2.4.1.
  - The DLLs in `1.6/Assemblies/` were rebuilt in `653e9d2`.
- **Harmony patches:** every patch target was checked against the game and mod
  assemblies (method exists, parameter names and types match).
  - RimRound: 106 OK, 0 problems.
  - FeedOther: 79 OK, 0 problems.
  - One patch in each finds its method at runtime and can only be checked in
    game: `PortraitsCache_RenderPortrait_DisablePawnPortraitRotationForPawnsInBed`
    and `Ability_StartCooldown_WeightOpinionSettingPatch`. Neither was changed.
- **RimVore-2:** checked against the `testing` branch. Every type, member and
  postfix parameter RimRound uses exists. RV2's own RimRound integration still
  compiles against current RimRound.
- **Biological Warfare:** checked against BW 1.6. Every parent def, class
  field and def reference resolves.
- **XML patches:** the XPath patch operations were checked against the defs
  they target.
- **Project files:** every new `.cs` file is in `RimRound.csproj`. Five older
  files are left out on purpose (the `DisabledPatches` folder,
  `MeldOvergrowth_DamagePatch.cs` and
  `PawnRenderer_RenderPawnInternal_AdjustHeadDrawDepth.cs`). These were
  excluded before this pass.

---

## 6. Playtest checklist

Do these in roughly this order. Anything that throws will show up in the log
(`Player.log`, or the dev-mode debug log).

1. **Load a save** with and without Anomaly, Biotech, RV2 and BW. Check for
   red errors at startup.
2. **Food pipes:** build a normal pipe (visible) and a hidden pipe over it
   (replaces it). The food grid overlay should show while placing
   vats/feeders. Check that the converter moves paste on Food Network v2.
3. **Void portal:** dev-trigger the "Void portal" incident (needs a colonist
   who is Chubby or heavier). Enter, then check the maze timeout sends pawns
   back beside the portal. Try "Seal portal".
4. **Maze:** look at the walls and floor. Wait near a geyser for an eruption,
   and mine a flesh wall for the gas puff.
5. **Echo:** down an echo and capture it. Feed a colonist, then check the echo
   visibly gains weight. Wait about 10 in-game hours for voidmilk, then use
   Release.
6. **Save and load mid-way** through the echo holding colonists, and during a
   corpse bloat.
7. **RV2:** check fat prey needs noticeably *more* struggles than thin prey,
   and that vore weight stays after digestion.
8. **Mutator obelisk:** a successful mutation should add weight.

---

## 7. Moving this work to your own machine

- Everything is pushed to `chud`. `git pull` on your machine gets the code, the
  rebuilt DLLs, the textures and this file.
- Rebuild in Visual Studio as usual if you want your own DLLs. They'll just
  replace the ones committed here.
- The temporary tools used for the checks (the Roslyn compile checker, the
  Harmony target checker and the XPath checker) lived in the cloud session's
  scratch space and are not in the repo.
