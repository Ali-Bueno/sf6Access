# Citywide listening and choosing a place by name

Prototype prepared on 2026-10-01; native listening test pending. The user explicitly wants both
choosing a named place from the other end of the city and recognizing places while exploring.
Do not launch or control the game for this test.

## Controls and audible meaning

- Hold R3/V and choose **Places by name** for an alphabetical view of known fixed places. Distance
  does not filter this list or change its ordering. Rows retain their real destination categories,
  positions and landmark references; selecting a row uses the existing route beacon.
- In **Guidance**, choose **Listen across the city** to survey known places by turning the camera.
  This deliberately returns to exploration, cancelling existing guidance. It uses the most aligned
  known fixed place, with no 100 m cutoff, and names it after the normal focus dwell. The short
  category sound comes from the same place after its name is actually spoken. Distances and sounds
  describe straight-line locations, not a confirmed path through streets or walls.
- Tap R3 after a city name to choose it. Selection exits city listening and starts ordinary guidance.
  Another R3 cancels guidance, as for every other destination. To leave the survey without choosing,
  use Guidance / Explore freely. No new button or automatic walking was added.
- Geometry radar and actual interaction announcements remain available. Routine nearby object
  sounds, passing notices and local route previews yield during the survey; they resume on exit.
  Z states the listening mode and can repeat the heard city focus.

City listening pauses when the game loses foreground focus. The place cache is keyed by readable
city and situation, so a day/night transition cannot stamp an old five-second cache with a new
context. Empty city directories give the existing map-learning hint instead of entering a silent survey.

Collinear landmarks cannot all occupy the same focus: the closer wins an exact angular tie. The
alphabetical view retains the others. A three-degree improvement is required to replace a retained
focus, so slight camera corrections do not alternate names. The standard focus dwell still applies.

## Sources and honest coverage

`FastTravelCatalog` supplies the current city's native travel/rest points without opening the phone.
`MapIconSnapshot` supplies previously captured localized map labels and positions. Saved spots and
registered static facilities add known locations. The new view excludes dynamic actor targets,
crowds, nearby traversal objects and position-less commands; other picker categories remain available.

There is no verified source here for every unvisited shop, arcade, district or building in the city.
The October 1 logs show native travel points, but no successful phone-map capture yet. Opening the
phone map is still the existing way to learn more icon-based locations; its complete coverage and
day/night freshness need native verification. A known coordinate is not proof that a business is
open, an NPC is present, a door is usable or the destination is reachable on the current navmesh.

`AccessTargetCatalog` previously stopped refreshing facilities after its first nonempty result. It
now refreshes on the existing five-second cadence and retains copied static discoveries for the
readable city/manager scope. Empty or temporarily unreadable registry results do not erase known
places. City/manager changes or an unreadable scope clear that memory. No native object is retained;
this memory is session-only. Name/kind and three-dimensional distance deduplicate entries. The
existing eight-metre merge radius is a source-merge tolerance, not a proof of floor identity.

## Code and validation

- `CityListeningMode`: explicit mode; selection, normal exploration and city reset leave it.
- `CityPlaceDirectory`: alphabetic view without changing a destination's semantics.
- `CityPlaceFocus`: angular selection, hysteresis, and actual-spoken eligibility for sound.
- `FieldAimHooks`: one city candidate for speech and R3, fresh city/situation/bearing guards.
- `WorldSonarHooks`: uses that spoken candidate in the existing Focused lane, with a quiet fixed
  city volume and two-second gap. No extra repeating sound layer or new audio assets.
- `FacilityMemory`: bounded refresh scheduling and plain-value location retention.

Offline tests cover far/near competition, focus wobble, delayed speech callbacks, disappearing
sources, invalid positions, collinear targets, alphabetical stability across player movement,
preserved destination categories, facility refresh after first discovery and scope invalidation.
Release build/deployment passed with zero warnings/errors; 520 navigation checks passed. These tests
do not simulate successful native calls or establish listening usability.

Live checks: choose a named distant place; survey the same place from far away; confirm the name,
sound and R3 selection agree; test a nearby crate in the same direction; compare two almost-aligned
places; cancel/leave/re-enter; return from a fight/menu; open the phone map and check for additional
named entries. Inspect `City listening focus:`, `R3 Choose/Cancel`, `Known facilities:` and
`Map places captured` in the user's resulting log.

## Sound identity decision

This revision uses recognizable category sounds plus the place's actual name. It does not assign a
different tune to every place. Permanent individual signatures would need stable native identity
and a small learnable set; the current localized-name/rounded-position cache key or changing picker
index is unsuitable. A few important landmark signatures can be evaluated after the city survey
works, without replacing the object sounds the user already distinguishes.

The design draws on spatial landmark callouts, deliberate surroundings previews and a chosen
destination beacon in [Microsoft Soundscape](https://www.microsoft.com/en-us/research/product/soundscape/features/).
Separating the straight-line city survey from the checked walking beacon is this mod's design
decision; the source does not validate SF6's native routes or these particular controls.
