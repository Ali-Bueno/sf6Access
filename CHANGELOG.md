# Changelog

## Unreleased

### World Tour — character creator rebuilt (colors, traits, presets)
- The avatar creator (also used for Battle Hub avatars) got a full rework based on a complete
  sweep of the game's creator code. New in this version:
  - **Colors are now spoken.** The game stores avatar colors as raw numbers with no names, so the
    mod converts the ACTUAL color applied to your character into words — "dark red", "light blue",
    "pale brown" — announced with the part it belongs to (skin, hair, chest hair, right eye, left
    eyebrow, upper eyelashes…). This works both when picking swatches from a color grid and while
    moving the hue/saturation/lightness sliders of the color picker popup, in all 13 game languages.
  - **Preset grids announce the item's real name** (hair styles, eyes, noses, mouths, ears, beards,
    expressions, body/face presets, body type and gender identity) plus its position ("3 of 24"),
    read from the game's own data instead of just a bare number.
  - Category and sub-category names now come from the game's localized text (previously hardcoded
    English).
  - Sliders, the eyelash type spinners, the physique triangle, paint slots, the voice list and the
    recipe (save/load/share) screens are all wired for reading as well.
- All of this was built from the game's decompiled code and **needs an in-game pass**: enter the
  creator, walk every category, and report what reads wrong or stays silent
  (`re2_framework_log.txt` tells the rest).

## v0.5.0 — 07/07/2026

> The avatar screens below were tested in the **Avatar Arcade**. The same menus are likely shared
> with World Tour, but that has NOT been verified yet — World Tour itself is the next big goal
> (see "What's next").

### Internal rework — please report anything that stopped reading
- The whole menu-reading engine was rebuilt: all ~50 screen readers now run on a shared
  screen-adapter architecture instead of each hook rolling its own polling scaffold. This is an
  internal change — every menu should read exactly as before — but it touched almost every screen
  hook in the mod. **If any menu that used to read went silent or misbehaves in this version,
  please report it** (a note saying which menu, plus `re2_framework_log.txt`, is enough).

### Command list on Modern controls
- The move list now speaks Modern commands with real words instead of raw notation: directions and
  motions by name ("down", "quarter circle forward"), the Special Move / Super Art buttons by name,
  the unified attack buttons as "light/medium/heavy attack", the hold-the-button glyph as "hold",
  and the arrow between autocombo steps as a pause — all localized. Before, a Modern move read like
  "2 SM" or "auto hd LowS next LowS".

### What's next
- The next version will focus on making **World Tour itself** accessible — exploring the world,
  knowing what is around you and how to interact with it, quests and objectives, field dialogue and
  encounters. This is by far the biggest feature so far and will take a good while; the groundwork
  (a full map of the game's World Tour code) is already done.

### Localization
- Every message the mod speaks with its own words (damage/price labels, "Completed", "Slot 3", "Empty", command inputs like "quarter circle forward, heavy punch", the skill tree words, the title-screen prompt, etc.) is now localized to ALL the game's languages: Japanese, English, French, Italian, German, Spanish (incl. Latin American), Russian, Polish, Brazilian Portuguese, Korean, Traditional and Simplified Chinese, and Arabic — following the game's own language setting. Previously only English, Spanish and Portuguese were covered.
- The translations live in plain text files (`reframework/plugins/managed/SF6Access.lang/*.txt`, one per language, `key=text`): anyone can correct or improve a translation with a text editor, no recompiling needed. Missing keys fall back to English.

### Versus / matchmaking
- The VS screen now announces the point values with the rank: League Points for ranked tiers ("Diamond 3 12345 LP") and the Master Rating for Master players ("Master 1497 MR"), for both sides.

### Fighting Ground
- Combo Trials list: each trial now announces whether you have already completed it ("Completed" / "Not completed"), read from your save data — the on-screen check mark was invisible to the screen reader.
- Training Attack Data: finished combos are no longer silently dropped when the game's hit counter and the data panel disagree by a hit (multi-hit supers). The panel's own final numbers are always announced, so the damage readout matches what actually landed.

### Avatar Arcade — master fights and results
- Master-fight pause menu: every tab now reads correctly — the move lists (Special / Super Arts / Other) announce the focused move with its command, category, labeled damage and description; the Perks tab reads each perk with its tooltip; the Battle Information tab reads the enemy (name and level) and each Drop Lock objective with its reward; the Items tab reads each item with its owned count ("x2") and description, and the "use this item?" popup reads its question, effect and the Yes/No buttons.
- Avatar battle results: the EXP summary no longer reads the animating count-up; the "New Special Move" popup now reads everything on it (move name, style, command, notes and description) instead of stopping halfway; and the "New Fighting Style" popup reliably includes the master's name.

### Avatar Arcade — shops and items
- The smartphone's item app ("View consumable and sellable items") is now read: category tabs, each item with its owned count and description, and the use-item confirmation.
- The shop is now read: the top menu (Buy / Sell / Enhance / Color), the buy (general and apparel), sell, enhance and dye gear lists — each item with its name, labeled price, effect and description — the category tabs, the buy/sell mode line ("Get - Takeout" / "Sell - All") when you toggle it, and the purchase/sale popup (quantity, running total and its buttons). The dye detail window reads its cost and each gear variant with the dyes it needs.
- New shortcut: G (keyboard) or Start (gamepad) reads your money on demand — your Zenny in the avatar shops and item app, and your Drive Tickets and Fighter Coins in the in-game store.

### In-game store
- Each product now announces its price with the currency it costs — "300 Fighter Coins" or "50 Drive Tickets" — after the product name, since the currency was only shown as an icon. Products bought through the platform store (real money) are left as they were.
- The Hub Goods Shop (the gear store reached from the in-game store) is now read: each gear piece with its name, price and the stats it grants.
- Gear in the avatar shops (buy, enhance) now announces its stats ("Defense 5, Kick Strength 13") after the price, read from the game's own item data when the on-screen compare panel isn't available.

### Avatar Status menu
- Skills tab (skill tree): the tree is now navigable. Each node announces its name, its state (acquired / available / locked / unavailable), category, cost and description. Switching skill trees announces the tree number, and the G key announces your available skill points and your coins. The "Unlock this skill?" confirmation reads the skill, its cost and the Yes/No buttons. The "Reset Skills" dialog reads its heading, the reset resource you have (which is separate from your coins) and its Yes / No / View Skills buttons.
- Special Moves and Super Arts tabs: empty move-set slots no longer go silent when several are in a row (each is read with its slot), and the move command is now read in your control scheme, including Modern/Casual inputs. The set-type tab (Grounded / Aerial / Super Arts) is announced when you switch it with Tab.
- Move Set screen: the preset list, the directional move slots (each with its trigger input and the assigned move, or "Empty"), and the Grounded / Aerial / Super Arts tab are now read.
- The Special Moves and Super Arts screens are also read inside Avatar Training, which uses a separate menu.

### Avatar Arcade
- Avatar Arcade top screen: the selected mode's description is now read as you move through the course list, and the G key announces your avatar's equipped style (name and rank) along with its combat stats (Vitality, Punch, Kick, Throw, Unique Attack, Defense).
- Avatar Training menu: its options (opponent state, block, counter, input/attack display, etc.) and their values are now read as you navigate and change them.
- Avatar Battle Settings: Control Type, Button Preset and Control Settings are now read as you move through them and as you change their values.

## v0.4.0 — 20/06/2026

### Battle Hub
- Navigable menus now read the focused option as you move through them: the action menu when you walk up to another player, the arcade cabinet menu (Change Character, Spectate, Wait on P1/P2 Side, etc.), the Rival AI (V-Rival) menu, and the Avatar Random Match mode list.
- Stage selector announces the stage name as you change it.
- League / rank selector (used in V-Rival): the rank tier and its level are announced — e.g. "Diamond" when picking a tier, then "Diamond 3" when picking the level — read from the game's own league data instead of the "Unspecified" placeholder.
- The social wheel now reads as you navigate it: the phrase list announces each focused phrase, and the sticker list announces each focused sticker name.
- The text chat window and the player list (fast travel / send message) now read the focused option as you move through them.
- Walking up to another player and opening the access menu now also announces that player's profile — name, title, LP and MR — alongside the menu options.
- The room Comment submenu (the preset comment picker) now reads each preset comment as you navigate the list.
- The text chat window (opened with T) now announces itself when it opens (the channel and destination, e.g. "Chat. To: Hub") and reads each element of the input bar as you move along it — Message, Send, Phrases and Stickers — which were silent icon buttons before. The phrases, stickers and typed/received text were already read.

### Versus
- The VS screen now announces each player's rank (e.g. "Iron 1", "New Challenger 1", or "Master" with rating), resolved from the game's league data. This fixes the previous wrong "Master" announced for low-rank or unranked players.
- The VS screen now also announces each player's control type (Classic / Modern / Dynamic) alongside their name and rank — e.g. "INGRID LeosKhai Classic Diamond 2 vs INGRID ... Modern Diamond 3". Only human-controlled sides report it, so a CPU opponent doesn't add noise. The control-type name is read from the game, so it is announced in your game's language.

### Story & dialogue
- Cutscene / scene subtitles now follow the in-game Subtitles option: they are only read by the screen reader when subtitles are enabled, so you can turn them off if you prefer the voiced lines alone.
- Battle Hub NPC dialogue (the "Special Talk" conversations) is now read line by line — speaker name and line — the same way scene subtitles are, and it also follows the Subtitles option.

### Online match results
- After an online match the post-match displays are announced: the winner's victory quote and the win count / win rate.
- The post-match rank gauge is announced once with your rank, your LP (or MR at Master) and the change from the match — e.g. "Gold 1. 1400 LP. +30". It is read from the final result data, so it speaks the finished value immediately instead of waiting for the on-screen LP count-up, and no longer repeats itself while the number animates.

### Fighting Ground
- On the Tutorials, Character Guides and Combo Trials lists, the control-type display toggle (Classic / Modern / Dynamic), changed with L2/R2 or Z/C, is now announced with the screen reader instead of only playing a sound.

### News / Mailbox
- The news headline list now reads each headline as you scroll through it, instead of only the first item.
- Opening a news article (with Confirm) now reads it aloud — the title and full body — instead of opening silently.
- The reward item dialog now announces the selected item or the focused button ("Receive" / "Close") as you navigate it, instead of reading the whole list only once on open, and it now speaks immediately when it opens instead of waiting behind the article being read.

## v0.3.5 — 16/06/2026

### Training
- Recording / playback slots now read their slot number and data as you navigate them, the same way the reversal slots already did. The slot number is taken from the slot's own index, so it reads correctly (1 to 8) instead of being off.
- Reversal special moves now announce their strength variant (Light / Medium / Heavy / Overdrive) and re-read it when you change it with left/right.
- Music player / BGM menu now reads the playlist tab (All / Playlist 1-5) as you switch it and the focused track as you move through the list.
- Training character-specific (unique) settings list now reads each row as you navigate it (character, setting name and value); changing the value with left/right reads only the new value, not the whole row again.
- Music player playlist edit window (opened with T) now reads the focused track and which side you are on (all tracks / playlist).

### Rewards / Fighting Pass
- The Rewards screen now reads as you navigate it: the tab (Fighting Pass / Challenge / Kudos / Master Pass) is announced when you switch, and the focused row of each tab is read.
- Battle Pass rewards are announced with their name, whether they are a Free or Premium reward, and their claimed status ("Claimed" / "Not claimed").
- Returning to an already-read reward now re-announces it (moving right then back left no longer went silent).
- Item preview (confirm a reward, or press the preview button) reads the item name, its description, and the action button. For premium items it reads the purchase options as you move between them ("Obtain Premium Pass" / "Obtain Premium Pass & Tier +10").
- The "reissue Fighting Pass" warning dialog is read: the warning message on open, each reward in the list (with "Sold out, already obtained" for items you already have), and the proceed button.
- The shop purchase dialog reads the product and price on open (e.g. "Premium Rewards. Price 100 Fighter Coins") and the focused choice (Use Fighter Coins / Cancel) as you move.

### Online & rooms
- Custom room join / invitations screen now reads each room row as you navigate it — room name, the room master / who invited you, entrant count, room ID code and the rule string — not just the tabs (Rooms with Friends / Rooms You've Been Invited To). The first attempt for this still read nothing (it pulled the data from the wrong object); this is now fixed and confirmed working, including when there is a single invitation.

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
