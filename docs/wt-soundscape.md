# World Tour — a soundscape for independent navigation (2026-09-27)

Tester: *"quisiera algo para poder ir a donde yo quiera… añadir audio cues a las escaleras también, como
hice con los carteles… haz un análisis profundo e intentemos llegar a una forma más independiente de
navegar, para que el jugador pueda decidir a dónde quiere ir en el mundo abierto."*

Independent means: the player builds a picture of the surroundings and chooses a direction themselves,
instead of only being pulled to a target someone picked. This document is the analysis behind that and
the plan; companion docs: `wt-orientation.md` (zones, where am I, picker), `wt-free-navigation.md` (look
sweep, passing by). Sources: the shared playbook `D:\code\modding projects\reference\audio-navigation\`
(A Hero's Call, Kirandur, United Minecraft, Swamp, wall sonification) and the user's Kakarot, Diablo II
and Among Us mods.

## 1. What a sighted player gets, and what the mod gives

| Question | Sighted player | Mod today |
|---|---|---|
| Where am I? | signs, the section banner | zone announcements + Z (**done**, confirmed 2026-09-27) |
| What is around me? | everything in view | people (N, people sweep, NPC voices), breakable signs (beacon), shops in passing, places by looking — **ladders and doors only spoken within 12 m, no sound** |
| What is that way? | look | look sweep for people and places (built, untested) |
| Can I walk that way? | see the street | radar (B / Shift+B, off by default, historically noisy) |
| How do I get there? | walk | route-aware beacon + R3 aim + ladder routes (**done**) |
| How do I get up/down? | see the ladder | only inside a mission route |

## 2. The audio today (inventory)

- **Pull lane (homing pulses, `HomingCue`: pan = side, octave down = behind, repeat rate = distance):**
  mission/destination beacon (`mission beacon.mp3`), nearest intact breakable within 25 m
  (`breakable object.mp3`), nearest person within 25 m (`npc beacon.mp3`). Precedence is hand-coded:
  mission > breakable > person, but **the breakable never yields to the mission** (tester: it was silent
  otherwise), so two pulses can sound at once.
- **Ambient:** a random nearby NPC speaks through its own game emitter every 7–15 s (real 3D).
- **Walls:** the radar's `impassable`/`exit` cues and stair/drop tones, continuous only with Shift+B.
- **Unused assets:** `door.mp3` (2 s long), `interactable.mp3`.
- `AudioService` mixes without a voice cap or priority; arbitration is by convention in each hook.

## 3. Diagnosis

1. **Every sound pulls; nothing describes.** All three pulses answer "where is the nearest X", none
   answers "what is around me". The layout — where the ladders, doors and ways out are — reaches the
   player only as speech within 12 m.
2. **Vertical access is invisible outside a mission.** Ladders are the only way onto roofs (the navmesh
   treats roofs as islands); the player hears about one only if a mission route needs it.
3. **Cues go through walls.** Nothing checks line of sight or walkability for the pulses (the beacon to
   a *chosen* destination is route-aware; the others are straight-line).
4. **"Can I go that way" is the weakest lane.** The radar is off by default after many redesigns.

## 4. Rules distilled from the references

1. One dominant cue at a time; arbitrate, never a chord (A Hero's Call, United Minecraft, Among Us
   `TaskBeacon`, Diablo II synth).
2. Separate lanes: a quiet awareness lane ("what is around"), a louder pull lane ("where am I going"),
   speech for identity. Never say and sonify the same fact at the same moment.
3. One fixed, learnable timbre per category; 6–8 categories at most, then speech for details.
4. Direction by pan, front/back by an octave drop, distance on ONE channel (never two).
5. Look to preview, one button to lock (Kakarot explore mode, Diablo II beacon offers); once locked, the
   beacon follows the real route and says so; distinct arrival.
6. Vertical elements are their own type (ladders, drops), not "walls".
7. Gate cues on reachability/line of sight, or route them along the path.
8. Prototype small and listen live; fewer reliable cues beat dense ones (Diablo II reverted its
   8-direction radar after listening).

## 5. The design: four lanes

| Lane | What it answers | Sounds | State |
|---|---|---|---|
| **Destination (pull)** | where am I going | mission beacon, route-aware, R3 aim | done |
| **Surroundings (awareness)** | what is around, by ear | ladder "tink-tink-tink" (rising), door knock — one cue at a time, taking turns among the 3 nearest within 25 m, quieter with distance | **built now** (`AccessSonarHooks`) |
| **Words** | which thing, how far | zones, Z, look sweep, passing by, picker | done / built |
| **Walls** | can I go that way | radar cues | exists (Shift+B); to revisit |

The independent loop: *hear* a ladder to the left → *turn* the camera, the look sweep names it
("Escalera de mano para subir a 8 metros") → *tap R3* to go there (or pick it in the picker, category
"Escaleras y puertas") → the beacon and the route lead to it.

## 6. Built in this pass (not yet verified in game)

- `sounds/ladder.mp3` (0.34 s, three rising metallic taps) and `sounds/door knock.mp3` (0.3 s, two
  knocks), generated with ffmpeg; **replace them with your own files of the same name** at will.
- `Hooks/WorldTour/AccessSonarHooks.cs`: ladders and doors (`FieldAccessCatalog`: `Om011000`,
  `Om016000`, `Om017000`) within `FieldBeaconHooks.HOME_RANGE_M` (25 m); one cue, then the cue's length
  plus 700 ms of silence; rotates over the 3 nearest; pan by camera bearing, octave down behind
  (`HomingCue` semantics), volume 0.40 near → 0.12 at 25 m (`SonarRota`); a ladder sounds at the end you
  would walk to; skips the chosen destination (the pull beacon has it); silent in menus, dialogue, the
  pad tutorial, the picker, and 1.2 s after an interrupting line.
- `FieldAccessCatalog` keeps entries out to 25 m (spoken exploration lines still stop at 12 m) and
  tags each with `AccessKind`.
- Ladders and doors are a destination category (`DestinationCategory.Access`, "Escaleras y puertas"):
  the look sweep names them and R3 can lock them; the picker lists them. "Passing by" leaves them to the
  exploration reader.

## 7. Roadmap and decisions for the tester

1. **Breakable beacon → awareness lane?** It is a pull pulse that never yields, so with a mission it
   sounds together with the mission beacon. Option: make it an awareness earcon like ladders (heard,
   not followed) and let R3/look lock it when wanted. Your call — you asked for it as it is.
2. **"Alrededor" (around me)**: one key speaking the nearest thing in each quadrant ("Delante: Beat
   Square, 120 m. Derecha: escalera, 8 m…"), the Microsoft Soundscape "Around me" callout.
3. **Reachability for earcons**: a cue for something not walkable from here (roof ladder seen from the
   street below) played muffled (lower rate/volume), from a navmesh reach check (the same query the
   route uses, rate-limited).
4. **More earcon categories, each needing an in-game check first:** treasure chests (`Om018000`), lifts
   (`Om006000`), shop entrances (`Om008000`, confirmed), warps (`Om013000`/`Om026000`).
5. **Continuous guidance** (camera follows the route while walking) — see `wt-free-navigation.md` §3C.
6. **Walls lane**: re-evaluate the radar as the "can I walk that way" answer now that the other lanes
   exist, starting with the one-shot B readout.

## 8. In-game checklist

- [ ] `AccessSonarHooks initialized`; near a ladder: `Access sonar: first cue, Ladder at N m`.
- [ ] Ladder and door cues are told apart instantly; left/right is right; behind sounds lower.
- [ ] Never two access cues at once; not tiring; quiet enough under the beacons and speech.
- [ ] Turning toward a heard ladder: the look sweep names it; R3 then leads to its foot.
- [ ] Picker: "Escaleras y puertas" lists the nearby ones.
- [ ] Are the generated sounds OK, or will you supply your own?
