# World Tour — navigation soundscape

**Latest prototype:** [Citywide listening](wt-city-listening.md) gives known fixed places a shared
spoken/audio focus at any distance, plus an alphabetical picker view. Category sounds are retained;
the survey replaces nearby incidental output and ends when a destination is selected. Native test pending.

**2026-10-01: simultaneous short cues and local walking previews built; listening verification pending.**

**Latest follow-up, after the 14:08–14:13 test:** the player reports improvement but R3 failed to
cancel a breakable. The log confirms repeated `R3 Aim` on `Objeto rompible`; the old cancellation
branch was person-only. R3 now cancels **any** active selection or explicit mission guidance before
reading a replacement. A later press can select a freshly spoken focus. Cancellation clears cached
beacon steering and queued route speech immediately. Hold-to-browse is unchanged.

Rest/day-night destinations now have a category after Guidance, plus a Z location readout, from
native `IsMyRoom`/HOME data. Explicit fixed-place rows give a nearby landmark reference to support
learning the map. Rest uses the existing place sound. Actual home contacts use `IsHome()` for a short
interaction label. See [orientation details and native limits](wt-orientation.md). These changes
are built and offline-tested; finding/entering the refuge still needs the user-led live test.

The player reported too much speech and too many sounds, while still wanting to choose where to go
and navigate dynamically by ear. The redesign keeps manual movement, existing route and ladder
queries, and the established route/radar sounds. It introduces shared output priorities and an explicit choice
between free exploration (now the default) and following a destination. It does not claim that every mission is now
playable without sight.

## 1. Evidence and design requirements

The local game log on 2026-10-01 contained approximately 14 speech requests around 07:25:06–08:
mission direction, NPCs, places, obstacle descriptions and compass interruptions. At 07:26:20–21,
the automatic compass interrupted the requested nearby list. Separate readers each obeyed their own
cooldown, but collectively flooded the screen reader. Frequent interruptions also postponed route
resolution while the old beacon kept playing.

The user's next test found excessive silence instead: radar and look names seemed to stop, and
removing compass speech harmed exploration. The 09:41–09:51 log confirms sensing continued while
policy suppressed output. Radar contacts were still sampled at 09:51:31; mission sounds continued
at 09:51:33. A ladder was read at 09:47:59 and an open door at 09:50:34, but guidance had disabled
their exploration cues. No audio-device failure is evidenced by that log.

Code review also found a 1.5-second turn window could expire while speech occupied the reader;
NPC classification failures were cached indefinitely while the avatar remained visible. The follow-up
removes that focus deadline, retries incomplete NPC naming after one second and restores compass,
radar and nearby sounds. The user explicitly requested free exploration as the initial mode.

The next request explicitly allows simultaneous sounds: short, distinct signals should reveal both
the surroundings and possible destinations. Four independent audio voices now cover route, geometry,
focused object and nearby discovery. Fast travel has its own cue and destination
category. Walking previews describe actual first legs from native routes, rather than presenting an
object's straight-line bearing as a street to follow.

The following listening pass found two practical problems: losing a selected person forced a menu
detour, and place/object cues sounded too similar. The earlier direct retarget-after-loss behavior is
superseded by the explicit cancel-first control above. The
player also requested silent steering: automatic spoken turn instructions and the redundant initial
route-distance recital are removed. A proximity notice is simply "Near [name]"; contact remains a
separate confirmed event. Place/object sounds now use distinct material textures and rhythms.
The old 280 ms crop at half speed removed the second shop event and third ladder strike; the new
260 ms patterns retain all events when behind the player. Pitch changes timbre, not event spacing.

External research:

- Capcom describes Master Actions breaking objects and crossing to distant platforms. **A walking
  route or ladder plan cannot prove that a destination is impossible.** Retain clear feedback about
  missing walking connections, partial routes, height and action opportunities. Ladders are the
  vertical connectors this mod models; they are not the game's only vertical traversal mechanic.
  [Capcom, September 2022](https://blog.playstation.com/2022/09/15/street-fighter-6-new-world-tour-battle-hub-details-four-legendary-faces-join-the-roster/).
- Masters, shops, missions and items serve different goals; hostile enemies can chase the avatar and
  trigger combat. Destination choice must remain deliberate and survive an ordinary fight.
  [Capcom, April 2023](https://blog.playstation.com/2023/04/20/street-fighter-6-showcase-new-gameplay-details-future-fighters-revealed-and-demo-launched/).
- Xbox recommends separate audio controls and reducing competing audio under assistive speech;
  environmental information can also work better as spatial sound than repeated narration.
  [XAG 105](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/105),
  [XAG 106](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/106).
- Distinct, consistent object/event sounds support play without vision.
  [Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/ensure-sound-music-choices-for-each-key-objects-events-are-distinct-from-each-other/).

Our design inference is to give walking one dominant guidance sound, looking one deliberate identity,
and speech to meaningful decisions. The specific timing/volume constants are mod UX choices, not
values asserted by those sources. Official manual pages returned HTTP 403; day/night and bus-stop
unlock details were not newly verified through them. Existing runtime fast-travel findings remain in
[wt-orientation.md](wt-orientation.md).

## 2. Player workflow and controls

| Intent | Action | Feedback |
|---|---|---|
| Follow the game objective | Hold R3 / V; Guidance; Follow the tracked mission | Follows the game's selected mission source, with existing story fallback |
| Choose a shop, master, mission marker or person | Hold R3 / V; choose a category and entry | One selected destination owns the route beacon |
| Explore without an automatic objective | Default on startup; return with Hold R3 / V; Guidance; Explore freely | Environment sounds and names; mission guidance waits for explicit selection |
| Identify something by looking | Face it briefly, while standing or walking | One nearby person/place/access/breakable is named |
| Follow the name just heard | In free exploration, tap R3 / V while still facing it | Chooses that target and aims down its route; no four-second timeout |
| Stop any active guidance | Tap R3 / V again | Cancels the selected destination or tracked mission and returns to free exploration |
| Replace a lost person | Tap R3 / V to cancel; face another target, hear its name, tap again | Selects the new target without opening the exploration menu |
| Find where to change day/night | Hold R3 / V; Rest: day and night; choose an entry | Guides toward a native hangout point; actual door contact remains separate |
| Ask for orientation | Z | Zone, nearby place, landmark, known hangout, facing and current interaction; in exploration, up to three checked walking directions |
| Discover where a path leads | Face a checked walking direction briefly | "This way: walking route toward [place], [distance]"; R3 can select the spoken place |
| Hear orientation while turning | Turn the camera | Short cardinal/intercardinal word after the heading settles |
| Notice a nearby place or access | Walk near it or arrive in its area | Known name and bearing; "Interact" only at confirmed contact |
| Ask about people / geometry | N / Start; B | Existing explicit readouts, protected from automatic speech |
| Quiet individual environmental systems | Shift+N; Shift+B | Exploration sounds; geometry radar |

Picker controls stay unchanged: arrows or d-pad for entries/categories, L1/R1 or PageUp/PageDown
for categories, Decide/Enter chooses, Cancel/Backspace closes. The picker remembers the highlighted
identity within each category, even if walking changes distance order. All ordinary movement remains
the player's responsibility; this change adds no continuous camera steering, auto-walk or teleport.

A chosen destination persists through menus and fights and is **not** cleared merely by getting within
four metres. Choose free exploration or the tracked mission to end/change it. Explicit choices also
protect against accidental retargeting while browsing the phone map; captured map points remain
available in the destination picker. Hovering the phone map never selects a navigation goal.

When neither a selected destination nor a readable mission exists, exploration cues and the
look-then-R3 selection are available automatically. Choosing Explore freely keeps that preference
even if a mission becomes readable later.

## 3. Hearing the route

The existing mission sound supplies steering:

- Stereo side tells which way to turn.
- Low pitch means behind; normal pitch means off to a side; a fifth higher means aligned ahead.
- Alignment enters at ±15° and leaves at ±20° to avoid pitch flicker.
- Repeat spacing retains the earlier responsive steering cadence. During speech, the cue uses 60%
  of its normal volume so it can still guide without dominating the voice.

The sound points to the next confirmed route corner or ladder approach. It is not a compass heading.
Speech covers route availability, nearby arrival (distinct from contact), ladder access
and known traversal limits. The former periodic clock-direction/distance recitals are removed;
R3 and Z supply requested detail. Turns are conveyed by the beacon alone; there is no automatic
"turn left/right" instruction or extra distance recital when a walking route first becomes usable.

A missing query answer cannot become a confident straight-line route. Pending data holds briefly;
old steering expires. R3 explains that the route is refreshing/unconfirmed. A **confirmed absent
walking path** can still expose a direction, explicitly identified as such, while the player looks
for a master action, ledge or entrance.

## 4. Hearing and discovering the surroundings

One focused scan (`FieldAimHooks`) combines people, known places, ladders/doors and breakables.
A focus must settle for 300 ms; walking does not disable it. Menus and the mod's own camera-turn
window hold scanning, then the currently faced object can be named without requiring another turn.
An unheard current focus retries after longer speech. The R3 candidate is published only after its
name is actually sent to the reader and stays selectable while that same focus remains current.

`WorldSonarHooks` uses the following object identities, in exploration and alongside selected guidance:

| Source | Sound |
|---|---|
| Ladder | Three metallic rung strikes |
| Door | Two low wooden knocks |
| Shop/service | Register click followed by one bright bell |
| Travel point | Rough rising electronic zip |
| Place, saved spot or pin | One hollow, airy swell |
| Breakable | Dry irregular crackle |

`WorldObjectSounds` synthesizes and caches these patterns using the existing audio engine; it adds
no dependency. Each lasts 260 ms, both ahead and behind, with stereo bearing and distance gain.
The sparse blocked-route breakable hint uses the same crackle as sonar. Radar and destination timbres
are unchanged by this listening follow-up. The bundled legacy object MP3s are retained but sonar no
longer plays cropped versions of them. An audition fixture contains ladder, door, breakable, shop,
place and travel, first ahead and then behind.

Known places may be heard to 100 m; local access points and breakables use the existing 25 m
catalogue. The focused source repeats. Nearby hints rotate between the nearest representative of
each other kind, with two seconds of silence after each hint, including while walking. A row of
crates therefore cannot hide the only ladder. Independent audio voices may overlap. These cues continue beneath
speech at 60% normal volume; dialogue, menus and fights still pause them. They describe a direction
to an object, **not verified walkability**; selecting it requests the actual route.

Unrelated repeating NPC distance speech, automatic population counts, injected NPC voice lines and
the independent nearest-NPC homing pulse remain removed. N and Z retain explicit readouts. Compass
words return through the shared policy after a deliberate, settled turn, with sector hysteresis.
Nearby known places are named once within 12 m, on arrival or while passing, including during guidance.
Ladders/doors have their own 12 m proximity notices; focused or contact accesses suppress duplicate
proximity lines. Confirmed current access targets say "Interact: name"; unnamed ladder/door targets
reuse the matching catalog label. Proximity alone does not authorize that interaction announcement.
Fast-travel contacts use the current access target plus the native fast-travel predicate or the
confirmed fast-travel object type. Nearby map icons alone cannot produce "Interact".

Radar open/closed, stair and approach-to-wall cues stay available during guidance and duck under
speech. They still announce state changes, rather than repeatedly describing an unchanged wall.

The 10:43–10:47 listening test exposed stale opening cues: at 10:46:51.421, PASSABLE Right played with
only 0.24 m clearance; at 10:47:02.988, PASSABLE Left played at 0.91 m. That session's measured body
thresholds were 1.52 m blocked and 4.56 m open. A queued opening survived the interval while a new
obstacle was being confirmed and could play when its audio lane became free. `RadarCueAdmission`
now checks the current valid, finite measurement against the matching threshold at emission. An
unsupported cue waits; other supported beams remain eligible. This fixes the demonstrated old-data
contradiction. The logs do not establish another geometry/sampling cause; that remains a live-test
question, not proof that every perceived false positive is solved.

`RadarCueMemory` records a front wall as heard only after its proximity tone actually plays with
current blocking evidence. A busy lane cannot create a fictitious heard wall and a redundant later
opening. If the tone plays before the existing blocked-confirmation interval finishes, a possible
reopening remains pending: current obstruction still vetoes it, while early recovery can announce
open space even when the stable clearance state never transitioned. Confirmation timing and silent
initialization are unchanged. Regression tests use the production state machine, admission and memory.
A breakable in the
front half within the existing 12 m access range gives a sparse hint at most every six seconds, rather
than a second homing direction. Shift+B remains the radar toggle; radar starts enabled.

## 5. Shared feedback contract

`FieldFeedback` is the entry point for automatic field speech and navigation cue reservations.

- Explicit readouts, picker speech and dialogue own the reader first.
- Automatic priority: interaction → route state → focused scan → compass → zone → incidental.
- One pending utterance per source, replaced by its latest information; obsolete/expired information
  is dropped. Native Tolk receives no backlog of automatic navigation sentences.
- Source validity is checked again at dispatch. Discovery/interaction memory commits on actual
  emission, not on merely proposing a line.
- A new line can replace its source's obsolete active line after the shared anti-burst floor.
- The sink protects queued as well as interrupting requested speech. `Tolk.IsSpeaking()` can extend
  protection when supported; a conservative text-length estimate remains the fallback. Speech-rate
  variation must be checked with the player's reader.
- Four independent voices permit overlapping route, geometry, focused and nearby cues; each voice
  finishes its own sample before accepting another. The same source cannot echo across two voices.
  Legacy samples are bounded after pitch shifting: route 220 ms, geometry 280 ms, focus 360 ms, nearby
  280 ms. Object patterns fit both object lanes at a fixed 260 ms, with rhythm preserved across pitch.
  A short attack/tail fade avoids trimming clicks; a soft ceiling protects coincident peaks.
  Decoded PCM is cached and each MP3 decoder is disposed immediately. Game audio is unchanged.
- Menus, fights, dialogue, tutorials and the destination picker reject automatic output and discard
  pending field announcements. A sample already playing may finish its short tail.

Geometry/route sensing is independent of speech admission. Background ladder searches must share the
native query budget fairly with current steering. Temporary missing native data is never proof of
an impossible path. Approach candidates retry unreadable queries with bounded backoff rather than
being permanently exhausted by transient failures.

Incomplete avatar names/classifications retry at one-second intervals while present. Successful
classifications refresh at the existing 30-second cache lifetime; continuously seeing a temporarily
unreadable avatar must never preserve a null name for the rest of the session.

### Local walking previews

In free exploration, up to eight nearby static destinations share the existing native query budget
(one attempt per 250 ms). Candidate coverage includes different bearings; the spoken direction comes
only from the first leg of a successful, string-pulled route. Actor positions and partial/off-mesh
routes are excluded. Selected guidance pauses these background previews.

Evidence expires after 2.5 seconds, a two-metre move, passing the first leg, or leaving its sampled
3D segment by more than half the live capsule radius. City/situation changes clear it. A query failure
replaces an old success. Z reports up to three different clock directions or explicitly says none
are checked yet. Facing a checked leg can announce its destination once after settling. R3 selects
the last actually spoken target only while its focus/path evidence is still current.

These are local walking choices toward known names, not a discovered street graph. Nearby names
still come from the game's zones, landmarks, facilities and map data; unnamed staircases or rooms
cannot acquire invented names. Geometry sensor stair cues remain separate from destination previews.

## 6. Live destinations and remaining limits

Loaded NPC selections carry copied component/owner identities, city, name and contact kind. Fresh live
lists update their coordinates at 500 ms intervals without retaining engine wrappers. Stable route
keys let a person move without restarting the route every update. Same-named neighbors remain distinct.

An unreadable or truncated source pauses confirmation. Confirmed disappearance or identity mismatch
stops the guide. R3 cancels a selected target regardless of loss/readability. Once back in exploration,
hearing a focused replacement and tapping R3 selects it without a menu; the guide never silently
attaches to a newly spawned person.

The 10:47 test heard Chelsea after James disappeared, but R3 still reported James lost. The exact
look-away cause was not logged; sonar's separate breakable focus does not prove NPC focus changed.
The code could discard a spoken target when another raw scan candidate won, so spoken ownership now
uses the target's own live identity and bearing. A 45-degree cone permits normal aim corrections;
300 ms look-away grace retains memory only, never authorizes selection outside the cone. Another
spoken name replaces it. Context, identity or position read failures suspend selection; fresh context
recovery permits a new announcement. R3 logs its decision and rejection reason for the next test.

Any active guidance cancels on R3 and rearms the look scan. This includes places, breakables and an
explicitly followed mission with an unreadable target. Cancellation never consumes a new candidate
on that same press. Holding R3 still opens the picker.
Interaction is confirmed only by the game's current access list matching the selected actor/owner,
or by the existing mission-contact path. Mere proximity remains explicitly unconfirmed.

Known limitations:

- No persistent NPC identity across respawns; loaded-instance continuity needs in-game verification.
- Static phone-map snapshots are remembered places, not proof that an NPC/mission is currently
  available. Valid-empty versus unready icon groups is still unresolved, so empty groups are retained
  conservatively. Day/night freshness needs a separate runtime pass.
- Facilities without a live identity retain position-based guidance. Mission contact fallback is
  limited when the game's HUD access target is unavailable.
- Partial ladder routes and native prompts still need the existing Boluo/roof test. The prompt HUD
  requests did not fire in prior tests; the mod cannot promise all master-action interactions.
- The design reduces competing output in code; comfortable volume, left/right and practical
  sightless traversal still require listening inside World Tour.

## 7. Validation and the next listening pass

Run `dotnet build` in `SF6Access/` (deploys to the game) and
`dotnet run --project tests/Navigation.Tests` at the repository root. The dependency-free suite exercises
feedback priority/expiry, requested speech protection, duration reservations, delayed scan recovery,
compass settling/hysteresis, nearby sound category coverage, scan dwell, selection
memory, actor loss/identity, R3 recovery, approach retries and the existing navigation geometry.
See `STATUS.md` for the final check count. These checks do not simulate successful native game calls.

`dotnet run --project tests/Audio.Tests` exercises the production decoder, trimming, pitch, stereo,
fades and mixer against all eleven bundled MP3s and the new object patterns. The current four-source
render overlaps for 141.1 ms with peak 0.612; stress mixing remains bounded. Pattern checks verify
260 ms duration in both directions, stereo, brightness change and preservation of the ladder, door
and shop rhythms. WAV listening fixtures and measurements are generated in
the task's temporary artifact directory, not committed. This verifies rendered audio, not listening
comfort or discrimination under game sound.

Live observation, 2026-10-01 at 10:25–10:29: the deployed Release assembly loaded, radar and
look/compass announcements continued during movement, a travel focus was recognized, and R3 selected
Alexis. Returning to Explore freely triggered `queryPathSync=True`, `Exist=True`, 20 points and a
readable first leg after `createTraceLine` (10:28:46.595). No new preview sentence or fast-travel contact
was observed in that interval. External input was already moving the player, so automated controls
were stopped to avoid interfering. This confirms native execution, not the listening acceptance list.
The final translation-only update was copied separately: a repeated build could not overwrite the
running game's locked DLL, whose SHA-256 still matched the successfully deployed Release build.

The player's next feedback confirmed that place/object cues were confusing and loss recovery required
the menu. The sound-pattern/R3/quiet-steering follow-up above was subsequently built and deployed
with the game closed: 366 navigation checks and 283 production-audio checks pass, with no build
warnings or errors. In the next live test the player confirmed the revised object sounds are easier
to distinguish. The 10:43 session loaded the new cues and reported loss of selected NPC James at
10:47:11; R3 replacement, walking-preview speech and fast-travel contact are still being checked.

In-game acceptance:

- [ ] Walk toward a mission for one minute through a busy street: hear steering and useful events,
      with quieter environment cues still available, and no competing speech backlog.
- [ ] Start a new session: free exploration is active; turning speaks compass words and focused names.
- [ ] Hear route, radar and two distinct environment sounds together; each stays brief and identifiable.
- [ ] In exploration, face a checked route, hear where it leads and select it with R3. Turn away, cross
      its first corner or change city before R3: no stale selection. Z lists current walking choices.
- [ ] A nearby fast-travel icon has its distinct cue; only actual access contact says "Interact".
- [ ] Hold the same NPC in view while a long readout finishes: its name becomes audible afterward.
- [ ] Walk past a ladder or door with several crates nearby: hear the access sound and its name.
- [ ] Approach an actual interaction: hear "Interact" once; no stale proximity line follows it.
- [ ] Follow two same-side corners and a close corner; route stays fresh while a long N/Z readout runs.
- [ ] Hold R3, select a shop; scan another place, then tap R3: cancel, with no replacement on that press.
- [ ] Within four metres of a shop/NPC, keep guiding until interaction is available; do not switch goals.
- [ ] Explore freely; scan while walking or standing, wait more than four seconds, tap R3 while still
      focused; repeat for a ladder, breakable and moving NPC.
- [ ] Talk/fight/open phone, then return: no stale words; selected destination and exploration preference
      behave as intended. After actual city travel no old coordinates are followed.
- [ ] Missing/replaced NPC: hear loss once, cancel with R3; hear another person and tap again to choose.
      Temporary read failure and a newly spoken target must never prevent cancellation.
- [ ] Follow a person, place, breakable and tracked mission in turn; R3 cancels each with no extra ping.
- [ ] Find Rest: day and night without opening the phone map; Z names the native hangout. Select it,
      follow the beacon and compare the arrival point to the actual door. Interact is contact-only.
- [ ] Browse a fixed place twice after turning: its landmark reference stays the same. With no real
      minimap north, only landmark distance is given. Check day/night menu speech after entering.
- [ ] Sweep past openings and walls while walking: no delayed opening over a currently measured wall,
      and no delayed wall after a confirmed opening. Check in both a busy street and a quiet corner.
- [ ] Follow a beacon through several corners: steering uses sound; no automatic spoken turn instructions.
- [ ] Roof/blocked route: correct ladder/foot/height feedback, sparse breakable hint, no claimed complete
      walking route through the obstacle.
- [ ] Read N/Z with the player's normal speech rate: no interruption by automatic navigation;
      necessary route changes resume afterward.
- [ ] Tune listening comfort only after this pass: route cue level, scan dwell, environmental hint rate.
