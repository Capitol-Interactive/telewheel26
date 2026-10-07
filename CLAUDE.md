# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Open Brush (a community fork of Tilt Brush) is a Unity VR painting app. The repo root *is* the Unity project (`Assets/`, `Packages/`, `ProjectSettings/`). `AGENTS.md` holds additional project rules; the important ones are repeated below.

## Environment

- Unity **6000.6.0f1** (see `ProjectSettings/ProjectVersion.txt` and `UNITY_VERSION` in `.github/workflows/build.yml`). `README.md` still says 2022.3.34f1 — that is stale. The project uses URP (`com.unity.render-pipelines.universal`) and OpenXR.
- There is no Unity editor in the cloud container, so C# cannot be compiled or tested here; only the Python and formatting tooling runs.
- Many packages are git dependencies in `Packages/manifest.json` (icosa-mirror forks, UnityGLTF, etc.).
- Unity logs are project-local: `Logs/Editor.log`, `Logs/AssetImportWorkerHW*.log`, `Logs/shadercompiler-*.log`. Check mtime before trusting any other `Editor.log` path.

## Commands

**Run in editor:** open `Assets/Scenes/Main.unity`, import TextMesh Pro essentials when prompted, press Play. Without a headset, WASD/gamepad navigate. Credentials live in a git-ignored `Assets/Secrets.asset` (create via *Create > Secrets Config*, assign in `Main.unity` under App > Config; `SecretsExample.asset` is the placeholder).

**Build:** in the editor use *Open Brush > Build > Do Build* or *Build Window* (`Assets/Editor/BuildTiltBrush.cs`). From a command line use `Support/bin/build.py` (`build.bat` on Windows; `--help` lists options), which drives Unity via `-executeMethod BuildTiltBrush.CommandLine` (`Support/Python/unitybuild/`). Prefer these over Unity's standard build dialog so the correct settings are applied.

**Tests (Unity Test Framework, run from the editor Test Runner):**
- Editor tests: `Assets/Editor/Tests/` (e.g. `TestStrokeCropping`, `TestApiSecurity`, `TestMisc`).
- Play Mode tests: `Assets/Tests/PlayMode/TestHttpApiCommandsPlayMode.cs`.
- Single test headless: `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testFilter "<Fully.Qualified.Name>" -logFile -` (use `PlayMode` for play-mode tests).

**Lint/format** use [prek](https://prek.j178.dev/) with `.pre-commit-config.yaml`:
```bash
uv tool install prek && prek install      # one-time; also: dotnet tool install -g dotnet-format
prek run -a                                # run all checks manually
```
Hooks: `dotnet format whitespace` for C# (excludes `Assets/ThirdParty`, `Packages/`, `Assets/Photon/`), black/flake8/pylint for `Support/` Python, yamllint/yamlfmt, gitleaks. Style follows standard C#/Python conventions plus `.editorconfig` (not the old Google style in `Support/`). Use `git config blame.ignoreRevsFile .git-blame-ignore-revs` to skip formatting-only commits in blame.

**HTTP API testing:** the Open Brush HTTP API only exists while the app runs or the editor is in Play mode — check that before testing HTTP commands.

**Brush screenshot comparison (old vs new):**
```
.venv-ssim\Scripts\python.exe Support\Python\compare-brush-screenshots.py --old-dir ..\open-brush-fast\Support\Screenshots\brushes-postfx-disabled --new-dir Support\Screenshots\brushes-postfx-disabled
```
Create the env if missing: `uv venv .venv-ssim --python 3.14` then `uv pip install --python .venv-ssim\Scripts\python.exe -r Support\Python\requirements-brush-screenshots.txt`. The script reports full-image RGB SSIM and prioritises by SSIM near the dilated union of rendered brush pixels; don't go back to full-frame pixel MAE.

## Architecture

All gameplay code is in `Assets/Scripts/` (`TiltBrush` namespace). It is built around MonoBehaviour singletons wired together in `Assets/Scenes/Main.unity`; the large ones to know are `App.cs` (startup, config, app state), `SketchControlsScript.cs` (input → tool/mode dispatch), `SketchMemoryScript.cs` (the stroke list and undo/redo stack), `PointerManager.cs` (the brush pointers, symmetry/mirror), and `BrushCatalog.cs`.

- **Strokes and brushes:** a stroke is a `Stroke` stored in `SketchMemoryScript`, rendered by a `BaseBrushScript` subclass (`Scripts/Brushes/`: `GeometryBrush`, `QuadStripBrush`, `HullBrush`, `PbrBrushScript`, etc.) that builds meshes into a `GeometryPool`. Each brush is a `BrushDescriptor` asset (under `Assets/Resources/Brushes/`) holding its GUID, material, and audio-reactive/export metadata; `Manifest*.asset` (`Manifest`, `Manifest_Experimental`, `Manifest_Zapbox`) selects which brushes ship in a build. Brush GUIDs are persisted in sketches, so never change them.
- **Undo/redo:** every user-visible mutation is a `BaseCommand` subclass in `Scripts/Commands/`, executed through `SketchMemoryScript` and parented to the active undo for composite operations. New edits should be commands.
- **Widgets:** non-stroke canvas objects (models, images, video, camera paths, stencils, guides) derive from the `GrabWidget` family in `Scripts/Widgets/`.
- **Save/load:** `Scripts/Save/` — `.tilt` files (`TiltFile`, `SketchWriter`, `SaveLoadScript`, `SketchMetadata`) and the local/cloud sketch catalogs. Exports (glTF/GLB via UnityGLTF plugins in `Scripts/UnityGLTF Plugins/`, FBX/OBJ/USD, etc.) are in `Scripts/Export/`.
- **HTTP/scripting API:** `Scripts/API/`. Endpoints are static methods tagged `[ApiEndpoint("name", "description")]` and split across partial `ApiMethods.*.cs` files; `ApiManager` serves them, marshalling onto the main thread (`ApiMainThreadObserver`). `API/Lua` hosts MoonSharp Lua scripting; `Assets/Editor/Lua*.cs` generate Lua docs/skills. Add new commands as `[ApiEndpoint]` methods rather than a new transport.
- **Input/XR:** `Scripts/Input/` abstracts devices over the Input System/OpenXR; `Assets/RuntimeActionBindings.json` and `steam_controller_bindings.vdf` hold bindings.
- **Other subsystems:** `Multiplayer/` (Photon Fusion; the CI pulls a specific Fusion branch), `Layers/`, `Playback/` (stroke replay), `Poly/`+`Sharing/` (Icosa/Sketchfab/Google services), `Tools/` (the in-app tools), `GUI/` (VR panels and popups), `TiltBrushCpp` (native plugin source).
- **Editor tooling:** `Assets/Editor/` (build pipeline, brush/panel editors and audits such as `BrushAudioReactiveAudit`, `BrushUvExportAudit`, `ShaderStripping`). Python utilities are in `Support/bin/` and `Support/Python/`. CI is `.github/workflows/build.yml` (game-ci/unity-builder, `buildMethod: BuildTiltBrush.CommandLine`).

## Telewheel (the game built on Open Brush)

Telewheel is a VR telephone-Pictionary party game (spin a wheel for a word, draw it in 3D for 60s, the next player guesses it, and so on, then reveal and vote). It runs inside Open Brush: it restyles and trims Open Brush's UI rather than replacing it. Plain Open Brush is `--Telewheel.Enabled false`.

- **`Assets/Scripts/Telewheel/Core/`** — game rules with no Unity references (`Telewheel.Core.asmdef`, `noEngineReferences`): `ChainPlanner` (who handles which chain on which turn), `MatchMachine` (the Pass & Play state machine), `WheelPhysics`, `WordDeck`, `VoteTally`, design tokens (`TwTokens`) and all player-facing text (`TwCopy`). Keep it engine-free so it can be unit-tested here.
- **`Runtime/`** — Unity glue. `TwBootstrap` starts everything from `RuntimeInitializeOnLoadMethod` (no edit to `Main.unity`), `TwDirector` puts Open Brush into game mode and runs the game, `TwGame` maps match phases to screens, `TwSelfTest` checks every Open Brush hook in the running app. **All Open Brush internals are reached only through `OpenBrushFacade`**, so an Open Brush change breaks one file.
- **`UI/`** — world-space UI built in code from the design system (no prefabs): `TwUi`/`TwGfx` builders, `TwButton`, `TwPointer` (VR controller ray, or screen centre on a desktop), the wheel, keyboard and `Tw*Screen` classes. Everything is sized in metres under a root scaled x10 (Open Brush units are decimetres).
- **Edits to existing Open Brush files** are small and marked `// Telewheel`: `PanelManager` (a Telewheel panel-availability mode), `SketchControlsScript` (two hooks that repurpose Sketchbook as Submit, New Sketch as Clear), `UserConfig` (the `Telewheel` section), `ToolButton` (a getter), and `API/ApiMethods.Telewheel.cs`.
- Drawings only exist as bytes between turns: a finished drawing is serialized, the canvas is wiped with Open Brush's own `NewSketch`, and later drawings are re-created display-only on a stage layer. The Marker brush is used for every drawing.
- Design source of truth: the Telewheel design system artifact (tokens, components, voice) and the "Telewheel Components" Google Doc (rules). Colours, type and copy in `TwTokens`/`TwCopy` mirror them.

**Testing Telewheel**
- Logic tests (run anywhere with the .NET 8 SDK; the same sources run in the Unity Test Runner as `Assets/Tests/EditMode/Telewheel`): `dotnet test Support/Telewheel/Tests/Telewheel.Core.Tests.csproj`.
- Compile check for the Unity-facing code without Unity (uses the real UnityEngine API from NuGet and stand-ins for Open Brush in `Support/Telewheel/CompileCheck/OpenBrushStubs.cs`; keep the stubs matching the real signatures): `dotnet build Support/Telewheel/CompileCheck/Telewheel.CompileCheck.csproj`.
- New files made outside the editor need `.meta` files with unique GUIDs: `python3 Support/Python/telewheel_meta.py generate <folders>` then `... check`.
- In the editor without a headset use desktop mode (`--Flags.EnableMonoscopicMode true`, or hold M while pressing Play). The on-screen overlay shows the game state and self-test; F8 re-runs the self-test, F9 hides the overlay, F10 opens the system menu. The self-test report is also written to `telewheel-selftest.txt` in the persistent data path and served by the HTTP API (`telewheel.state`, `telewheel.selftest.report`).
- `--Telewheel.AutoPlay true --Telewheel.TimeScale 10 --Telewheel.Players 4 --Telewheel.Rounds 1` makes bots play a whole match unattended.
- Word lists are `Assets/Resources/Telewheel/words_*.txt` (the raunchy list is a placeholder). Voice-over clips dropped into `Resources/Telewheel/Audio` as `vo_get_ready`, `vo_spin`, `vo_guess`, `vo_vote`, `vo_nailed`, `vo_drifted`, `vo_round_over`, `vo_game_over` play automatically.
- Online play. **Rules and flow** are engine-free in `Core/Net` and tested with bots: `OnlineMatchHost` (simultaneous turns; `NetMessage`/`NetCodec` are the wire protocol), `OnlineRoomHost` (lobby roster, join handshake, rules, seats, leavers), `OnlineMatchClient` (turns host messages into state for the screens), `OnlineSession` (a room plus what runs with it), `OnlineBot`, and `LoopbackNetwork` (in-memory transport). A transport only has to implement `INetHostPort` / `INetClientPort`. The draw, guess, reveal and vote screens read `IMatchView`, which both `PassAndPlaySession` and `OnlineMatchClient` implement, so `TwGame` runs either. **Backends** (`Runtime/TwOnline.cs`): `TwPracticeBackend` (a local room of computer players, no network) and `TwUnavailableBackend`, which makes Play Online say what is missing (no Photon SDK, an app id missing from the Secrets asset, or the Photon transport not connected yet) instead of hanging. The Photon Fusion transport itself is **not built yet**; the real backend will be assigned to `TwOnline.Backend`.
- **Trying online without a network:** Play Online has PRACTICE: HOST and PRACTICE: JOIN buttons in the editor and development builds (or with `--Telewheel.FakeOnline true`); computer players join the lobby, and joining starts a match by itself. `--Telewheel.AutoPlay true --Telewheel.FakeOnline true` plays a whole practice online match unattended.
- **Photon setup (needed only for real online play).** The SDK and the app ids are never in git. (1) `python3 Support/bin/setup-photon.py` downloads the SDK into the git-ignored `Assets/Photon`, from the same mirror branch the CI uses (`status` shows what is installed). (2) Open the project and run *Telewheel > Online > Set Up Photon*, which adds `MP_PHOTON`, `CROSS_PLATFORM_INPUT` and `PHOTON_VOICE_DEFINED` (plus Fusion's own symbols; `MOBILE_INPUT` for Android) to the PC and Quest targets. Fusion's installer also edits `ProjectSettings.asset` and a plain build without the SDK breaks if `MP_PHOTON` is committed there (CI checks this), so keep that file's Photon changes local. (3) Create `Assets/Secrets.asset` (*Create > Secrets Config*, git-ignored), add one entry with Service `PhotonFusion` and the Fusion app id in `ClientId`, another with Service `PhotonVoice` and the Voice app id, and assign it in `Main.unity` under App > Config. (4) *Telewheel > Online > Check Photon Setup* reports what is still missing.

## Project rules (from AGENTS.md)

- **Never commit or push temporary design, planning, or future-work documents.** Keep them untracked and local unless the user explicitly asks for them to be versioned.
- **Brush visual parity:** for every brush with noticeable visual differences from the old Unity version (excluding surface shaders), copy the old Unity shader and make only the minimal changes needed to support URP.
- For brush parity work and screenshot analysis, use the normal `m_Material` path and ignore `m_TestingMaterial`. Don't assume `m_TestingMaterial` is the active material, a URP migration target, or repurposable; verify the runtime material when it matters.
- **UnityGLTF shader variants:** the empty/no-keyword variant must be retained when filtering a Shader Variant Collection. The audited importer-reachable URP-only set is 384 PBRGraph + 8 UnlitGraph entries (392 total, including each empty set), retaining alpha test, texture transforms, instancing, clearcoat, iridescence, sheen, specular, transmission and dispersion. Do not substitute the older 75-entry subset; re-audit importer keyword behaviour whenever UnityGLTF changes (`Support/Python/generate-unitygltf-urp-shader-variants.py`).
