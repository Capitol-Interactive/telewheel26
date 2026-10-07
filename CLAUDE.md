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

## Project rules (from AGENTS.md)

- **Never commit or push temporary design, planning, or future-work documents.** Keep them untracked and local unless the user explicitly asks for them to be versioned.
- **Brush visual parity:** for every brush with noticeable visual differences from the old Unity version (excluding surface shaders), copy the old Unity shader and make only the minimal changes needed to support URP.
- For brush parity work and screenshot analysis, use the normal `m_Material` path and ignore `m_TestingMaterial`. Don't assume `m_TestingMaterial` is the active material, a URP migration target, or repurposable; verify the runtime material when it matters.
- **UnityGLTF shader variants:** the empty/no-keyword variant must be retained when filtering a Shader Variant Collection. The audited importer-reachable URP-only set is 384 PBRGraph + 8 UnlitGraph entries (392 total, including each empty set), retaining alpha test, texture transforms, instancing, clearcoat, iridescence, sheen, specular, transmission and dispersion. Do not substitute the older 75-entry subset; re-audit importer keyword behaviour whenever UnityGLTF changes (`Support/Python/generate-unitygltf-urp-shader-variants.py`).
