# World Tour — navigating freely, without a menu (2026-09-27)

Tester, after the orientation pass (`wt-orientation.md`) worked: *"quiero que me ayudes a pensar en algo
para navegar más libremente, sin usar un menú"*. The destination picker answers "take me to X" but it is
a menu: you stop, open it, browse, choose. What is missing is what a sighted player does all the time —
look around, notice things, walk toward one, read the shop signs going past.

## 1. What the 2026-09-27 log says

- **R3 has become the steering wheel.** While walking to the objective, R3 was tapped about once a second
  (`Aimed at 'objetivo' (74 m) … (70 m) … (66 m) … (4 m)`, 16 aims in 14 s). The player walks a leg,
  the camera drifts, R3 puts it back. It works, but it is a hand job the mod could do.
- **Aiming kept losing its calibration** ("Could not aim (not calibrated yet)") after every menu, phone
  or fight, because `MapAimHooks` reset `CameraAimService` whenever the player lost control. **Fixed:**
  the calibration (which euler component is the yaw) is a property of the controller type and now
  survives; a failed write is retried and a half-collected calibration restarts
  (`CameraAimService.Release`); a new city still resets everything.
- **Zone banners bounced** at the Seaside Street / Buckler edge (6 in 14 s). **Fixed:**
  `ZoneAnnounceGate` — a section heard in the last 30 s is only announced after 3 s of staying in it
  (checked against the live section id when it comes due); a section new to the player is still
  announced at once, with the banner's own wording.

## 2. The gesture that is already there: looking

World Tour movement is camera-relative, so the right stick is how a blind player points. Two readers
already answer a look: the compass (`FieldHeadingHooks`, "noreste" as the camera crosses a sector) and
the people sweep (`FieldAimHooks`, "Chun-Li, 12 metros" as a named person enters the aim cone — only
while the camera is turning, the rule A Hero's Call uses, so walking straight is silent). Dragon Ball Z
Kakarot's explore mode does the same with every map point of interest. Extending the look to PLACES
turns the whole city into something you can scan and choose from without opening anything.

## 3. Options

**A. Look to find places, tap R3 to go there — PROTOTYPED** (`FieldPlaceSweepHooks`).
Stand, turn the camera: each known place entering the aim cone (half a clock hour) is named with its
distance, interrupting (in a sweep the newest name is what the camera points at), and while standing the
look input freezes for the compass's 50 ms beat so you can stop on it — the nearest one per look, so a
whole street does not read out at once: "Beat Square a 120
metros", "Style Lab Beauty Salon, tienda a 80 metros", "Objetivo de la historia a 60 metros". Tap R3
within 4 s, still standing and facing it, and it becomes the destination (beacon, route, ladders, aiming
all follow). While walking, places are still named when you turn (queued, not interrupting), but R3 keeps
meaning "point me down my route" — a nudge of the stick mid-walk never changes the destination.
Turns that are not the player's are ignored: the mod's own R3 aim plus 250 ms of settling
(`CameraAimService.OwnTurnUntil`, also used by the people sweep now) and the camera blending back for a
second after a dialogue, menu or fight. Silent while an interaction prompt is up. Polls half a poll
after the people sweep so the two never speak on the same frame.

**Trade-off to decide:** standing at a bend, turning the camera by hand, hearing a place and then tapping
R3 within 4 s now switches the destination to that place ("Hacia <place>…" says so). If that turns out to
steal R3 when you only wanted to re-aim, the alternative is to offer the looked-at place only while no
destination has been chosen (following the story objective), or to require two taps. Places = the picker's catalogue
without the crowd (`PlaceCache`, rebuilt at most every 5 s): story objective, fast-travel points, the
game's registry (masters, facilities), the phone-map snapshot, saved spots. Masters within 25 m are left
to the people sweep. Keys are stable across the 5 s rebuilds (the story objective by category, masters
by name, everything else by name and position).

**B. Passing by — PROTOTYPED** (`FieldPassingHooks`). While walking, a place that comes within 12 m
(three arrival bands) is named once with its clock direction: "Style Lab Beauty Salon, tienda, a las 3,
a 9 metros". It is news again only after the player has been 24 m away; at most one every 2 s; never the
current destination, the story objective, masters or the crowd (NPC shopkeepers do count). Whatever is in
range on arriving in the field, after a fast travel (a jump no walk could make) or right after saving a
spot there is taken as known, not announced. Queued, never interrupting. This is the
shop-sign reading a sighted player does along a street, and it builds the mental map that option A then
lets you use.

**C. Continuous guidance — PROPOSED, not built (needs your decision).** A toggle that does what the log
shows the player doing by hand: while a destination is set and the avatar walks, the camera follows the
route's next leg by itself (the same aim R3 does, repeated), silently; speech only at bends ("gira a la
derecha", the route already knows the turn: `BeaconDestination.Turn`), every so many metres, and on
arrival. The player still walks and stops; touching the right stick pauses it for a few seconds. It is
the biggest time saver, but it moves the camera for you, so it is your call (the 2026-09-12 rule was:
no auto-walk, no teleport; this is neither, but it is closer to both).

**D. Streets and junctions — RESEARCH.** In Metro City the sections ARE street names ("Faith Avenue",
"Seaside Street", "East Bay Avenue", "South Street - Zona de Beat Square"), and they are now announced.
The next step would be, at a junction, "a la derecha: Seaside Street". Needs the section at a point that
is not the player's: `SectionMapSettingCatalogUserDataHolder.GetSectionMapSettingData(sectionId)` →
`MinPos/MaxPos` world boxes (decompiled, unverified; boxes of crossing streets overlap), or the navmesh
branching at the player's polygon. Worth a probe first.

**E. Follow the street without a destination — RESEARCH.** Ask the pathfinder for a route to a point
some metres ahead in the camera's direction and aim at its first leg, so pushing forward follows the
street round a corner instead of into a wall. Shares its machinery with C; only after C is settled.

## 4. Recommendation

A + B now (built, off nothing: no new keys, nothing to open), C next if you want it, D as a probe. A and B
together give the loop a sighted player has: walk, notice ("pasando por…"), look around ("…a 80 metros"),
pick with R3, walk.

## 5. Controls after this change

- Turn the camera (right stick) while standing: places and people ahead are named.
- Tap R3 / V: if a place was just named and you still face it, it becomes the destination; then the
  camera points down the route (as before).
- Hold R3 / V: the destination picker (unchanged).
- Walking: shops, landmarks and saved spots within 12 m are named once as you pass.

## 6. In-game checklist

- [ ] `FieldPlaceSweepHooks initialized` / `FieldPassingHooks initialized`.
- [ ] Standing, turn slowly: places are named once each, nearest in each direction; not repeated while
      the camera wobbles; named again after looking away and back.
- [ ] Right after a name, tap R3: "Hacia <place>, N metros" and the beacon leads there. After 4 s, or
      facing elsewhere, R3 aims at the previous destination as before.
- [ ] Walking: R3 taps never change the destination; places are still named when you turn.
- [ ] Walking along Beat Square: shop names one by one, with clock direction; not repeated at the edge.
- [ ] Too much speech? Say which (sweep, passing, compass, people) — each has its own switch point.
- [ ] R3 aiming works right after a menu or a fight, without turning the camera first.
- [ ] At the Seaside Street / Buckler edge: one announcement, not a string of them.
