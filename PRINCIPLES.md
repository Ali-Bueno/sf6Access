# Engineering & Accessibility Principles

> **Purpose:** This is the authoritative project-level source for
> engineering, accessibility, testing, documentation,
> reverse-engineering, and repository-hygiene rules.
>
> Agent-specific model orchestration belongs in the global `AGENTS.md`
> or `CLAUDE.md`, not here. When an AI coding assistant works in this
> repository, it should read this file before making changes.

## 1. General engineering principles

-   Prefer simple, readable solutions over clever ones. Prefer
    composition over inheritance.
-   Do not duplicate logic. Factor shared behavior into one clear
    service, adapter, helper, or abstraction.
-   Keep files, folders, names, and responsibilities coherent as the
    project grows.
-   Match the surrounding code: naming, idioms, formatting, and comment
    density.
-   Refactor files that grow beyond roughly 200--300 lines when they
    contain multiple responsibilities.
-   Consider development, testing, and production/release behavior
    separately. Debug-only paths and test values must not leak into
    release builds.
-   Avoid one-off scripts and throwaway files in the repository.
-   Temporary files must live in the session scratch location, never in
    the repository root or a general code directory. Delete temporary
    artifacts created for the task when they are no longer needed.

## 2. Scope discipline

-   Change only what the user requested.
-   Fix only the requested bug. Do not introduce a new framework,
    dependency, pattern, or technology merely to solve a local bug.
-   Avoid changing established architecture unless architecture is
    explicitly part of the task.
-   Before changing code, consider likely ripple effects on callers,
    state, lifecycle, and adjacent systems.
-   Never overwrite `.env` or equivalent local configuration.
-   Do not add DLLs, native libraries, packages, or other runtime
    dependencies silently. If a genuinely new dependency is required,
    ask first.

## 3. Comments and documentation

-   **Code should be read, not studied.** Prefer self-explanatory code
    and short comments.
-   Comment only when the *reason* cannot be understood from the code
    itself. Keep normal comments to roughly 1--2 lines.
-   A `<summary>`, docstring, or equivalent API description should
    normally be one concise sentence.
-   Do not put essays, debugging stories, bug history, example traces,
    rejected approaches, or multi-paragraph justifications inside source
    files.
-   Durable explanations belong in `reference/` or another dedicated
    documentation location. Source code may contain one short comment
    linking to that documentation when useful.
-   As a practical warning sign, if comments approach roughly 15--20% of
    a source file, review whether the explanation belongs in
    documentation instead.
-   Keep commit messages concise as well; long engineering narratives
    belong in project documentation.

### `STATUS.md`

-   `STATUS.md` is a dashboard, not a design document.
-   Keep entries short: current state, known blocker if any, and next
    step.
-   Put implementation details, investigation history, rationale, and
    long explanations in `reference/`, then link to them from
    `STATUS.md`.
-   If a status-table row becomes a paragraph, it is too long.

## 4. Testing and build

-   Compile after every code change. A change that does not build is not
    finished.
-   Write thorough tests for major functionality where practical.
-   For accessibility mods, prioritize tests around deterministic logic
    such as state tracking, navigation calculations,
    anti-spam/state-change behavior, formatting, filtering, and other
    code that can run without the game.
-   Use the framework's normal build command.
-   Where practical, configure the project build to deploy the resulting
    mod artifact to the game's mod/plugin directory automatically so the
    test loop remains one step.
-   When a build produces a large log, inspect and report the relevant
    errors rather than preserving huge raw output in project
    documentation.

## 5. No magic numbers

-   Never hardcode unexplained offsets, IDs, indices, thresholds,
    timings, ranges, addresses, or other domain-specific constants.
-   Derive values from authoritative sources: the game's
    data/components/configuration/APIs, runtime state, metadata, or the
    relevant library/framework headers.
-   Prefer reading the authoritative value at runtime over guessing a
    literal.
-   If a literal is genuinely unavoidable, use a named constant and
    document where it came from and why it is correct.

## 6. Accessibility architecture and PRISM

-   **PRISM is the default and authoritative accessibility/screen-reader
    layer.** Repository: <https://github.com/ethindp/prism>
-   Use the prebuilt PRISM release; do not compile the PRISM repository
    merely to consume it.
-   Route all speech/accessibility output through a single project-owned
    sink or adapter so backend details do not leak throughout gameplay
    code.
-   Do not introduce direct Tolk integration as an alternative
    accessibility path.
-   Do not ship a separate `tolk.dll` merely for PRISM. PRISM's
    supported Windows screen-reader/TTS behavior is provided through
    `prism.dll` as appropriate.
-   Prismatoid bindings exist for supported environments. Use the
    binding appropriate to the host when it can actually load there.
-   For BepInEx hosts whose target framework cannot load the current
    .NET Prismatoid package, P/Invoke the stable PRISM C API from
    `prism.dll` instead of falling back to a separate Tolk architecture.
-   Accessibility output must remain centralized so platform/backend
    implementation can change without rewriting gameplay features.

## 7. Framework-specific rules

Identify the game engine and modding framework before writing
implementation code. Do not apply rules from one framework to an
unrelated engine.

### BepInEx / Unity only

These rules apply to BepInEx Unity mods, not native mods, REFramework,
UE4SS, Java mods, or unrelated hosts.

-   Build with `dotnet build`.
-   Configure the current project's `.csproj` to copy the built mod DLL
    to the game's plugin directory when appropriate.
-   Harmony is already supplied by BepInEx where configured; do not add
    a redundant Harmony dependency.
-   Do not modify the project's established BepInEx NuGet references
    without a task-specific reason.
-   For IL2CPP games, use the proper IL2CPP/native reflection and
    interop mechanisms. Incorrect reflection can produce wrong method
    names or crashes.
-   Do not perform expensive global object searches such as
    `FindObjectOfType` every frame. Cache references and respect object
    lifecycle.
-   Before starting a new BepInEx/Unity mod, read the project's BepInEx
    reference documentation, if present (for example
    `reference/engines/bepinex/`), covering version selection,
    Cpp2IL/metadata compatibility, interop generation, Unity-version
    repairs, Harmony/native safety, deployment, and runtime traps.

### Other engines

-   RE Engine: prefer REFramework and its own hooking/runtime
    facilities.
-   Unreal Engine: use UE4SS or the project's established native
    approach.
-   Native C/C++ or other native games: use the project's established
    native hooks/instrumentation.
-   Do not introduce Harmony or BepInEx into an unrelated engine merely
    because they are familiar.

## 8. Reverse-engineering workflow

### Identify the target first

Before decompiling or hooking, identify the engine, language/runtime,
architecture, and relevant packaging or protection so the correct tool
and modding framework are chosen.

Useful identification tools include:

  -----------------------------------------------------------------------
  Tool                                Purpose
  ----------------------------------- -----------------------------------
  Detect It Easy (DIE)                Detect compiler, language, packer,
                                      and often engine clues

  PE-bear                             Inspect PE headers,
                                      imports/exports, and sections
  -----------------------------------------------------------------------

### Static analysis

  -----------------------------------------------------------------------
  Target                  Preferred tools         Notes
  ----------------------- ----------------------- -----------------------
  Native C/C++ / PE       Ghidra; IDA Free as an  Enable useful
                          alternative             RTTI/demangling
                                                  analysis where
                                                  applicable

  .NET managed assemblies ILSpy; dnSpyEx when     Prefer read-only
                          editing/debugging is    inspection unless
                          needed                  modification is
                                                  intentional

  Unity IL2CPP            Il2CppDumper and Cpp2IL Recover metadata,
                                                  methods, and interop
                                                  information

  Unity assets            AssetRipper             Inspect/extract assets
                                                  and scenes
  -----------------------------------------------------------------------

### Dynamic/runtime analysis

Use runtime analysis when it is more reliable than guessing static
values, especially for structures, pointers, state, and authoritative
runtime data.

Useful tools include Cheat Engine, x64dbg, ReClass.NET, and Frida.

### Tool hygiene

-   Prefer evidence from the actual game/runtime over assumptions.
-   Record durable discoveries in concise reference documentation
    instead of rediscovering them repeatedly.
-   Do not paste enormous decompiler dumps or raw logs into permanent
    project docs. Preserve the useful symbol, structure, address
    derivation, call flow, or conclusion with enough provenance to
    reproduce it.

## 9. Accessibility-mod design defaults

Unless a specific project intentionally chooses otherwise:

-   Preserve player agency. Accessibility should expose information and
    controls, not play the game for the player.
-   Prefer manual exploration over automatic pathfinding or
    auto-solving.
-   Use audio cues, speech, spatial information, and state-change
    feedback to communicate information that a sighted player receives
    visually.
-   Avoid speech/audio spam. Announce state changes rather than
    repeating unchanged information every frame.
-   Keep controls economical and compatible with normal game input;
    avoid unnecessary extra keys.
-   Prefer controller/joystick-compatible interaction when the host game
    supports it.
-   Derive navigation and interaction information from real game state
    rather than guessed geometry or hardcoded map knowledge whenever
    possible.

## 10. Publishing

-   Maintain a clear English `README.md` describing what the mod makes
    accessible, installation requirements, the modding framework, PRISM
    requirements, and known limitations.
-   Avoid committing unnecessary binaries and generated artifacts.
-   Do not create GitHub releases automatically. Create a release only
    when the user explicitly requests it.
