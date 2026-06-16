# Changelog

## v0.3.5 — 16/06/2026

### Training
- Recording / playback slots now read their slot number and data as you navigate them, the same way the reversal slots already did. The slot number is taken from the slot's own index, so it reads correctly (1 to 8) instead of being off.
- Reversal special moves now announce their strength variant (Light / Medium / Heavy / Overdrive) and re-read it when you change it with left/right.
- Music player / BGM menu now reads the playlist tab (All / Playlist 1-5) as you switch it and the focused track as you move through the list.

### Rewards / Fighting Pass
- The Rewards screen now reads as you navigate it: the tab (Fighting Pass / Challenge / Kudos / Master Pass) is announced when you switch, and the focused row of each tab is read.
- Battle Pass rewards are announced with their name, whether they are a Free or Premium reward, and their claimed status ("Claimed" / "Not claimed").
- Returning to an already-read reward now re-announces it (moving right then back left no longer went silent).
- Item preview (confirm a reward, or press the preview button) reads the item name, its description, and the action button. For premium items it reads the purchase options as you move between them ("Obtain Premium Pass" / "Obtain Premium Pass & Tier +10").
- The "reissue Fighting Pass" warning dialog is read: the warning message on open, each reward in the list (with "Sold out, already obtained" for items you already have), and the proceed button.
- The shop purchase dialog reads the product and price on open (e.g. "Premium Rewards. Price 100 Fighter Coins") and the focused choice (Use Fighter Coins / Cancel) as you move.

### Online & rooms
- Custom room join / invitations screen now reads each room row as you navigate it (room comment, the room master / who invited you, entrants, room ID and rules), not just the tabs (Rooms with Friends / Rooms You've Been Invited To).

## v0.3.0 — 15/06/2026

### Training
- Stage select: the BGM selection (Q/E) is announced when you change it (Stage BGM / Character BGM / track).
- Reversal move-selection submenu now reads (the move lists and category tabs), and the instability/cursor fight that made it freeze is gone.
- Reversal move-selection submenu: the Super Art tab move list now reads (it uses a different list control than the other tabs).
- Reversal slots: each slot reads its number, assigned move (or "Empty"), on/off state, and delay; the on/off toggle and the assigned move are announced as they change.
- Reversal "Delay Settings" submenu (R) reads the frame value, announcing only the value as you change it with left/right.
- CPU Level is read when a player is set to CPU (the control-type slot becomes CPU Level), announcing the value and updating it on left/right.
- Frame data is announced during training (startup / total / advantage) when a move is performed.
- Frame data and Attack Data (combo damage / hits) now respect their display options: each is only announced when its toggle is enabled in the Screen Display Settings, and frame data no longer leaks into replays.
- Combo trials no longer re-read the recipe mid-combo before you finish the attempt.
- Combo trials no longer read the attack-data combo damage on top of the trial feedback.
- Vitality and Super Art gauge values are read correctly when scrolling (1P vitality and 1P/2P super art were stuck announcing 0).

### Arcade
- Game settings menu (Difficulty, Rounds, Stages, Bonus Stages): the value is now announced when changed with left/right, not only when moving up/down between rows.
- Stage results are announced (Score, Time / Vitality / Finish bonuses, Subtotal, Total) after the victory quote.
- Bonus stage (car crush) results are announced (Score, Time bonus, Clear bonus, Total).
- Ending artwork cards are announced as you page through them ("Special Artwork: ...", "SF Legacy: ...").

### Battle
- Round winner is no longer announced for the wrong side (P1 winning was announced as P2 / the CPU).
- Opponent rank (tier + division, or Master rating) is announced on the ranked VS screen.
- Extreme Battle: the rule banner and the objective list are announced, with progress called out as each step changes.

### Command list
- The command list now reads the inputs for the control type actually selected. It used to always read the Classic notation, so on Modern controls the commands were wrong; it now follows the Classic/Modern input-type tab and re-reads the focused move when you switch it.

### Online & rooms
- Room search menu options are no longer swapped (View invitations / Search rooms, etc.).
- Search-by-name and search-by-code dialogs are read: title, prompt, the name field, and the Cancel / Search buttons; the typed text is announced and the buttons read every time you move to them.
- Notifications / mailbox list reads each message as you navigate.
- Replay-info menu (after picking a replay) reads its options: view each player's details, add to favorites, commentary on/off, and watch replay.
- "Opponent found! Accept the match?" confirmation screen (reached by pressing Back/Escape during ranked/casual matchmaking) is now read: the prompt plus the opponent's connection — type (WiFi / Cable) and signal strength (0-5) — and any name/rank text shown in the widget.
- "Match cancelled" is announced when that confirmation screen closes without the match starting (you backed out, the opponent declined, or it timed out); accepting it stays silent since the VS screen reads instead.
- Experimental: the rank-up / promotion screen (when your league rank changes after ranked matches — Bronze, Silver, Gold, etc.) is announced with the new rank. Untested in a real rank-up — please report if it misreads.

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
