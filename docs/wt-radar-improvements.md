# World Tour radar — improvement plan (2026-09-12)

**2026-09-14 implementation checkpoint:** first automatic exploration/access prototype
is built and deployed, not yet verified in game. Implemented: radar on by default,
independent front debounce, enabled interaction-shape points, close-turn preservation
and turn speech, explicit route-loss/approach state, contact-based mission confirmation,
nearby ladder/door discovery with identity memory, and active context-action readout.
See `docs/sf6-screens.md` -> "Follow-up: independent exploration radar" for exact behavior,
bounds and remaining limitations. Twenty-seven pure navigation checks pass. The broader
navmesh-exit, unified-sensor and world-connectivity ideas below remain proposals.

**Original status (2026-09-12): PLAN. See the implementation checkpoint above for subsequent changes.** Direction set by the user on 2026-09-12: **keep the
radar-based navigation** (three beams, open/closed cues, approach tone, stairs/drop, B one-shot,
Shift+B continuous, the homing beacons) **and improve it**. No auto-walk, no teleport, nothing that
moves the avatar. Sources: the radar family under `Services/WorldTour/FieldRadar*`,
`FieldNavRadar*`, `NavMesh*`, `HomingCue`; the dated history in `STATUS.md`; the shared playbook
`D:\code\modding projects\reference\audio-navigation\`; the stub sweep recorded in
`docs/sf6-screens.md` § "native navigation infrastructure leads".

## Review against current working code (2026-09-14)

User request: improve locating and reaching objectives with the existing navigation,
without additional key presses or auto-walk. This is a code review and proposal, not an
implemented change or an in-game verification. Existing uncommitted changes were reviewed
as part of the current implementation.

Confirmed in code:
- `FieldNavPath.Enabled` defaults to true, but `FieldNavRadarHooks._continuous` defaults
  to false. The radar still requires Shift+B; the hands-free requirement is not met.
- `FieldNavPath.AimPoint` can restart its bearing at a bend inside `ARRIVED_M` (4 m),
  then aim beyond that bend. A close necessary turn can therefore be skipped. Corner
  advancement needs a separate geometric criterion from destination arrival.
- `BeaconDestination` exposes `NoRoute`, but the automatic `MissionBeaconHooks` does
  not read it. Only the on-demand `MapAimHooks` announces it. Query failure, throttling
  or an unreadable corner also replaces the aim with a direct bearing rather than
  preserving a recent valid route or announcing that guidance is unavailable.
- Spoken mission distance uses route metres when available; ping cadence still uses
  direct destination distance. Direction refers to the route aim, while the announced
  distance refers to the final destination, with no separate distance to the turn.
- Arrival uses a 4 m Euclidean distance alone, without confirming an interaction or
  a remaining walkable approach. Nearby across a wall is not confirmed arrival.
- `ApproachFinder` accepts the first reachable sampled point near the objective at
  the player's elevation. This proves reachability of that point, not that it leads to
  an entrance, stairs or the objective. In `RouteTo`, the fallback also loses approach
  metadata, so it can be treated as an ordinary destination.

Recommended first increment: enable the existing radar automatically with its field
gates; preserve close turns; distinguish route guidance from a direct target bearing;
announce route loss automatically once per transition; distinguish nearby, approach
point reached and confirmed interaction. Then add stable alignment feedback and brief
turn events (advance warning, turn now, missed turn) instead of relying on the current
8/15-second mission announcements. Keep player movement under the player's control.

Evaluate with a nearby right-angle turn, an objective across a wall, a target on another
floor, a temporarily unavailable route, and final approach to an NPC. Measure successful
arrival/interactions, missed turns and contradictory/repeated cues; compilation alone
cannot validate these behaviors.

Revisions to the older proposal below: a navmesh polygon boundary is not necessarily
a doorway or junction; graph connectivity and a route to a probe endpoint do not prove
that the straight segment to it is clear. Do not suppress obstacle evidence merely
because its beam contains the route aim. Physical clearance and route guidance need to
agree before announcing a usable passage. Investigate navmesh exits after improving
the already available route guidance.

## Game-code follow-up: exploration without an objective (2026-09-14)

Technical evidence and exact types are recorded in `docs/sf6-screens.md`, under
"Follow-up: independent exploration radar". The game exposes ladder endpoints,
door-side/lock state, contact-shape constraints and contextual action guides. These
offer more useful labels than inferring every object from three rays. Runtime mapping
is still required; generated interfaces contain no native implementation bodies.

Keep the current radar and sound vocabulary, with three coordinated responsibilities:

1. **Immediate surroundings:** avatar-clearance and ground checks explain obstacles
   and drops. Directional sound stays responsive while the player turns normally.
   Camera-relative directions remain consistent; actual travel direction determines
   collision urgency. Clearance alone means space ahead, not a guaranteed exit.
2. **Local exploration:** keep short-lived world positions and identities for verified
   nearby accesses. Announce a ladder, door, or confirmed passage on approach, even
   without a mission. Rotating the camera changes its bearing, not its identity; this
   avoids forgetting or re-announcing the same opening on each beam transition.
   Revalidate state and discard on streaming/area changes. Do not persist native wrappers.
3. **Chosen destination:** the existing mission/map choice continues to supply the goal.
   Guide along actual turns, then toward a valid access shape. Confirm interaction using
   matching contact data and active prompts. If a ladder or door bridges route sections,
   validate both ends and availability before recommending it; never substitute an
   arbitrary reachable point as though it were an entrance.

Proposed experience (examples, not implemented behavior): walking without a target
announces "ladder on your right" once; turning toward it provides a bearing; on approach
the game's actual action prompt is read. With a selected objective, add "turn right"
and near-target alignment as needed. A locked or wrong-side entrance explains why the
route stops. All detection and speech activate automatically; movement and actions stay
under normal player control. Nearby exploration must not automatically switch the goal
to whichever object happens to be nearest.

Standing still should remain useful: after a deliberate turn settles, allow one short
description of the relevant access in view and a stable alignment cue. Do not require
walking to discover where to set off, or narrate every pedestrian and polygon boundary.
Preserve the detected access position briefly so a player who overshoots can turn back
and locate it. Use changes of identity, availability and approach stage to re-arm speech.

First prototype should combine the current radar fixes with automatic contact/action
diagnostics and one confirmed ladder/door family. Next validate portal edges from our
existing path report to improve turns and candidate passage positions. Broader exploration
then groups connected walkable regions into meaningful passages; polygon degree alone
does not establish a junction. Whole-city entrance connectivity remains an investigation,
not an available solved feature.

Acceptance cases: explore a block without an objective; find and return to a passed
entrance; approach the same ladder from below/above; distinguish a locked door from a
wall; reach and interact with an NPC around a close corner. Capture missed openings,
false exit cues, extra turns, repeated speech and actual successful interactions. None
of these cases is marked passed until tested in World Tour.

## 1. What the radar cannot do today, and why

- **It measures geometry, not walkability.** A beam is "passable" when the waist-height body sweep
  sees ≥ 6 m of clearance and "blocked" at ≤ 2 m. So an *exit* is "a beam that happens to look
  far", not "a place you can go". A 1.6 m alley at 6 m subtends ~5°: three beams at 90° never see
  it (the ray-fan simulation of 2026-09-08 showed even 24 rays miss it). This is the tester's
  standing blocker: *"nunca logro saber con precisión dónde hay una salida"*.
- **The beacons point straight.** Only the mission beacon aims at the route corner (unverified in
  game); the NPC and breakable beacons pan straight at the target, through walls.
- **Two sensors, two filters, two clocks.** The spoken front verdict samples with the camera's
  `TerrainRayFilter` (what stops the *camera*), the cue channel with the avatar's own
  `CharacterController.FilterInfo`; they run at 100 ms and 33 ms and never share a reading. One
  wall can be spoken by one channel and toned by the other.
- **No memory of the world.** Everything is camera-relative and reactive; "where am I" is a
  separate key. Streets and intersections have no names in SF6's data, so the only structure the
  mod can name is the one it derives itself.

## 2. The idea in one line

Same radar, better senses: the **navmesh** becomes the source of truth for "exit" and "structure",
the **route** is shared by every beacon, the **rays** keep doing what they are good at (fast
"blocked ahead", approach tone, stairs, drop), and one **speech arbiter** keeps the field quiet.

## 3. Improvements, ordered by value ÷ risk

### Level 0 — Debt that distorts every test (cheap, do first)

1. **One filter.** The spoken front class (`FieldNavRadarService.Sample`) casts with the avatar's
   `CharacterController.FilterInfo`, like the cue channel (`FieldRayCaster`). Retire
   `TerrainRayFilter` from the speech path (root cause #1 in § 9c, never removed).
2. **B reuses the last continuous sample** instead of a second 15-cast `FieldRadarSense.Measure`
   on the same frame.
3. **Side flicker must not restart the front debounce.** Drop `LeftBlocked`/`RightBlocked` from
   `NavReading.SameStateAs` for the front-verdict confirmation.
4. **One owner per wall.** A near front wall: the verdict channel speaks, the cue channel tones,
   both from the same 100 ms sample (the 33 ms sensor keeps only the approach-tone distance).
5. **Capsule cast instead of three parallel rays.** `via.physics.System.castCapsule(Capsule, vec3
   start, vec3 end, FilterInfo, ShapeCastResult)` answers "does my body fit" in one call: 15 → 5
   casts per evaluation, 450 → 150 casts/s (the rate that exposed every lifetime bug). Needs a
   `SharedInstance` `ShapeCastResult`, same discipline as `CastRayResult`.
6. **Mirrored sides** — the pending in-game check since 2026-09-04, run before anything else.

### Level 1 — Every beacon follows the route (highest practical value, proven API)

- NPC and breakable beacons aim at `FieldNavPath.AimPoint` through `BeaconDestination`, exactly
  like the mission beacon. Only one beacon is ever active (yield order mission > breakable >
  passer-by), so the 250 ms query budget is unchanged: one destination, one route.
- Cadence paces on **walked route metres** (`PathInfo.calcDistance`), not straight-line.
- **On-course variant:** when the camera is within ±15° of the aim point the ping plays a distinct,
  steadier form (same file, centred, no pitch drop) — the Soundscape "on-course" beat. Correcting
  heading by ear removes most of the need for the spoken clock line.
- **Spoken line on events, not timers:** clock-hour change, distance band halved/doubled, corner
  reached ("turn left"), arrival. The 8 s / 15 s repeat becomes a fallback only when nothing
  changed for 20 s. (The person tracker already does this; reuse its rules.)
- Unreachable is spoken once and honestly: `DestNodeNotFound` → "no route, going straight";
  `PathNotFound` → "not reachable from here".

### Level 2 — Real exits from the navmesh (the answer to "where is the exit")

Re-enable the navmesh openings finder, rebuilt on the engine's own link edges:

- **Bind `findMapHandle` by shape** (name + 0 parameters). `NavMeshNodes.cs` still binds it by
  bare name — the diagnosed root cause of the 2026-09-08 crash, never fixed. Try
  `findMapHandleBase(MapType)` / `getManagers` as the streamed-section alternative if the plain
  handle returns nothing.
- **Bound the node search.** `queryClosestNode(vec3, NodeQueryInfo)` with
  `NodeQueryInfo.setRegion(Sphere)` (a few metres around the player) — never the unbounded
  overload; `queryNode()` city-wide is banned.
- **Use the engine's link edges, not vertex matching.** `NodeInfo.LinkBoundaryEdgeCount` +
  `queryLinkBoundaryEdge(uint)` → `via.LineSegment { start, end }`; `queryClosestLinkBoundaryEdge
  (vec3)` gives the nearest one directly. Width = segment length, bearing = midpoint, passable =
  width ≥ capsule diameter and the linked node is not `Wall`. This replaces the hand-rolled
  `SharedEdge()` and removes half the engine calls.
- Keep the numbered `NavMesh step N` trace before every engine call; keep `MESH_WAYS_ENABLED` as
  the kill switch. Refresh once per body radius of movement, never per frame.

How the radar uses it:

- **"Way opens" means an exit.** A beam may only emit the exit cue when a link edge lies inside
  its ±45° sector within reach; the cue **pans to the real bearing of that edge**, not ±90°. Ray
  clearance alone can no longer produce "exit".
- **"Way closes" stays ray-based** (fast, reliable, already the low-chatter part).
- **B one-shot appends the exits:** "Exits: ahead, 3 metres wide, at 5 metres; right, 1.5 metres
  wide, at 8 metres" (`AppendWaysOut` exists and is dead code today).
- **Structure announcements (once per polygon change):** entering a node narrower than ~3 m →
  "narrow passage"; a node with ≥ 3 passable link edges → "junction: left, ahead" with the
  bearings. This is the "cross street" the docs asked for, derived from the navmesh and labelled
  as ours. It is the hands-free "where am I" the user asked for, without street names.

Fallback if `NodeInfo` proves unsafe at runtime: **route depth probes** with the proven
`queryPathSync` — confirm a ray-passable beam with one query to a point 6 m along it
(`DestNodeNotFound` → demote to unknown, no exit cue). Shares the 250 ms budget with the beacon,
so beams refresh every ~0.75 s: slower, but every "exit" is true.

### Level 3 — The radar knows the route

- One bit per beam: "this beam contains the route corner". That beam's exit cue uses the beacon's
  timbre (double chirp), and its approach tone is suppressed unless the game's own collision says
  blocked — the radar stops arguing with the beacon.
- When the corner falls behind the player (walked past the turn) the beacon's pitch drop already
  says so; add the spoken "turn around" only after 5 m of wrong-way progress.

### Level 4 — One speech arbiter for the field

`SpeechQueue` in front of `ScreenReaderService` (World Tour only; menus untouched). Priorities:
blocked ahead > exit / junction > beacon line > district / banner > radar class change. Higher
interrupts, lower is dropped inside 1.5 s of a higher one. The per-hook `READER_HOLD_MS` blocks
go away. Key presses always answer.

## 4. Runtime experiments, in order (each a go/no-go)

1. **Mirrored sides** (Level 0.6) and **one filter** (0.1): corridor test, both sides fire at
   ~2 m on the correct side; a waist wall that stops the avatar reads blocked.
2. **Capsule cast:** `castCapsule` with the avatar filter returns the same distances as the three
   parallel rays on the same wall; no crash over a long walk with a `SharedInstance` result.
3. **Route beacons for NPC / breakable:** log the aim corner vs the target; the ping pans at the
   corner; the on-course form is audible at ±15°.
4. **Navmesh, one press:** `MESH_WAYS_ENABLED=true` after the shape fix and the bounded query.
   Expect trace lines up to `queryLinkBoundaryEdge`, sane counts (vertices 3–8, edges 1–6,
   widths 0.5–20 m). If the game dies, the last `NavMesh step` line names the call.
5. **Exit cue gated by the navmesh:** in a yard with one gate, the exit cue fires once, panned at
   the gate; a straight street with no side openings never says "exit".
6. **Junction / narrow passage:** walk Metro City's main street into an alley: one "narrow
   passage"; at the crossing: one "junction: left, ahead".

## 5. Risks (all already hit once in this repo)

- `CreateInstance` of interface / abstract / generic types → GC-thread crash with a clean log.
  `NodeQueryInfo` and `ShapeCastResult` are concrete classes: create once, `Globalize()`, cache.
- One `queryPathSync` per tick, fresh `PathQueryReport` each time; the beacon and the depth probes
  share that budget.
- Bind every overloaded method by shape (`FieldProbeService.FindByShape` / `FieldNavPath.Find`).
- By-ref reference-type parameters are forbidden; `findIntersection(ref vec3, LineSegment)` is
  fine (value types), `queryPath` (delegate) is not.
- `AccessViolationException` is uncatchable: the numbered trace line is the only diagnostic.

## 6. Open decisions (user)

1. Level 2 structure announcements ("narrow passage", "junction"): on by default in continuous
   mode, or only in the B readout?
2. Beacon spoken line: event-driven with a 20 s fallback (proposed), or keep the 8 s / 15 s timer?
3. Order: Level 1 (route beacons) before Level 2 (navmesh exits), or the reverse? Level 1 is
   proven API and helps reaching NPCs and breakables at once; Level 2 is the exit blocker but
   carries the crash risk.
