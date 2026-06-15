# Changelog

## v0.3.0 — 15/06/2026

### Training
- Reversal move-selection submenu now reads (the move lists and category tabs), and the instability/cursor fight that made it freeze is gone.
- Frame data is announced during training (startup / total / advantage) when a move is performed.
- Frame data and Attack Data (combo damage / hits) now respect their display options: each is only announced when its toggle is enabled in the Screen Display Settings, and frame data no longer leaks into replays.
- Combo trials no longer re-read the recipe mid-combo before you finish the attempt.
- Vitality and Super Art gauge values are read correctly when scrolling (1P vitality and 1P/2P super art were stuck announcing 0).

### Battle
- Round winner is no longer announced for the wrong side (P1 winning was announced as P2 / the CPU).
- Opponent rank (tier + division, or Master rating) is announced on the ranked VS screen.
- Extreme Battle: the rule banner and the objective list are announced, with progress called out as each step changes.

### Online & rooms
- Room search menu options are no longer swapped (View invitations / Search rooms, etc.).
- Search-by-name and search-by-code dialogs are read: title, prompt, the name field, and the Cancel / Search buttons; the typed text is announced and the buttons read every time you move to them.
- Notifications / mailbox list reads each message as you navigate.
- Replay-info menu (after picking a replay) reads its options: view each player's details, add to favorites, commentary on/off, and watch replay.

### Rewards & misc
- Cosmetic / reward unlock toasts are announced ("Obtained ...").
- The reward-claim dialog reads the received item.
- Credits / staff roll are read line by line.
- Fixed navigation lag in the Fighting Ground / online menus (the name + description are still combined, but now spoken immediately).
- Title screen prompt corrected (keyboard key is F).

## v0.2.0 — 12/06/2026
- Training mode polish, combo announcements, VS screen and more.

## v0.1.0 — 12/06/2026
- First public release.
