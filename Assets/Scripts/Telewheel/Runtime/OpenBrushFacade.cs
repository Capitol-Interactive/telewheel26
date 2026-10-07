// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenBrush.Multiplayer;
using TiltBrush;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Telewheel
{
    /// <summary>
    /// The only place Telewheel touches Open Brush internals. Everything else in Telewheel goes
    /// through here, so an Open Brush change only ever breaks this one file.
    /// </summary>
    public static partial class OpenBrushFacade
    {
        /// <summary>The plain Marker brush, used for every drawing so they all look alike.</summary>
        public static readonly Guid MarkerBrushGuid = new Guid("429ed64a-4e97-4466-84d3-145a861ef684");

        // ----- App lifecycle -----

        public static bool AppExists
        {
            get { return App.Instance != null; }
        }

        /// <summary>True once App.Awake has loaded the user config.</summary>
        public static bool ConfigLoaded
        {
            get { return App.Instance != null && App.UserConfig != null; }
        }

        public static bool Enabled
        {
            get { return ConfigLoaded && App.UserConfig.Telewheel.Enabled; }
        }

        public static UserConfig.TelewheelConfig Settings
        {
            get { return App.UserConfig.Telewheel; }
        }

        public static Scene AppScene
        {
            get { return App.Instance.gameObject.scene; }
        }

        /// <summary>Settings that must be in place before Open Brush's own Start() runs.</summary>
        public static void PrepareConfig()
        {
            App.UserConfig.Flags.SkipIntro = true;
            App.UserConfig.Flags.DisableAutosave = true;
        }

        public static bool IsStandardState
        {
            get { return App.CurrentState == App.AppState.Standard; }
        }

        public static bool IsReadyToPlay
        {
            get
            {
                return AppExists && IsStandardState && App.Scene != null
                    && BrushCatalog.m_Instance != null && !BrushCatalog.m_Instance.IsLoading
                    && SketchControlsScript.m_Instance != null && PointerManager.m_Instance != null
                    && PanelManager.m_Instance != null && SketchSurfacePanel.m_Instance != null
                    && InputManager.m_Instance != null && SketchMemoryScript.m_Instance != null;
            }
        }

        public static void SubscribeStateChanged(Action<App.AppState, App.AppState> handler)
        {
            if (AppExists)
            {
                App.Instance.StateChanged += handler;
            }
        }

        public static void UnsubscribeStateChanged(Action<App.AppState, App.AppState> handler)
        {
            if (AppExists)
            {
                App.Instance.StateChanged -= handler;
            }
        }

        /// <summary>
        /// False when Open Brush is running view-only (no headset and not in desktop mode): drawing
        /// would not work, so the game says so.
        /// </summary>
        public static bool CanCreate
        {
            get { return App.AppAllowsCreation(); }
        }

        public static bool IsMonoscopic
        {
            get { return App.Config != null && App.Config.m_SdkMode == SdkMode.Monoscopic; }
        }

        // ----- Game mode: trimmed UI, one brush, no world grabbing -----

        public static void EnterGameMode()
        {
            PanelManager.m_Instance.SetTelewheelMode(true);
            SketchControlsScript.m_Instance.m_DisableWorldGrabbing = true;
            SelectMarkerBrush();
            DisableOpenBrushMultiplayerUi();
        }

        public static void ExitGameMode()
        {
            SketchControlsScript.m_Instance.m_DisableWorldGrabbing = false;
            ApiManager.Instance.ForcePainting = ApiManager.ForcePaintingMode.None;
            PanelManager.m_Instance.SetTelewheelMode(false);
            SketchSurfacePanel.m_Instance.EnableDefaultTool();
        }

        public static bool SelectMarkerBrush()
        {
            BrushDescriptor brush = BrushCatalog.m_Instance.GetBrush(MarkerBrushGuid);
            if (brush == null)
            {
                brush = BrushCatalog.m_Instance.DefaultBrush;
            }
            if (brush == null)
            {
                return false;
            }
            BrushController.m_Instance.SetActiveBrush(brush);
            return true;
        }

        public static string ActiveBrushName
        {
            get
            {
                BrushDescriptor brush = BrushController.m_Instance.ActiveBrush;
                return brush == null ? "(none)" : brush.name;
            }
        }

        public static void SetBrushColor(Color color)
        {
            App.BrushColor.CurrentColor = color;
        }

        /// <summary>Lets the player paint (draw turn) or blocks painting and hides the tool (everything else).</summary>
        public static void SetDrawingAllowed(bool allowed)
        {
            ApiManager.Instance.ForcePainting = allowed
                ? ApiManager.ForcePaintingMode.None
                : ApiManager.ForcePaintingMode.ForcedOff;
            if (allowed)
            {
                SketchSurfacePanel.m_Instance.EnableDefaultTool();
            }
            else
            {
                SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.EmptyTool);
            }
        }

        public static void SetPanelsVisible(bool visible)
        {
            SketchControlsScript.m_Instance.RequestPanelsVisibility(visible);
        }

        public static BaseTool.ToolType ActiveToolType
        {
            get { return SketchSurfacePanel.m_Instance.GetCurrentToolType(); }
        }

        public static void ResetWorldPose()
        {
            App.Scene.Pose = TrTransform.identity;
        }

        // ----- Preferences: mixed reality, handedness, camera -----

        /// <summary>True when this device can show passthrough (mixed reality).</summary>
        public static bool PassthroughSupported
        {
            get { return App.VrSdk != null && App.VrSdk.PassthroughMode != PassthroughMode.None; }
        }

        public static bool IsPassthroughActive
        {
            get
            {
                TiltBrush.Environment env = SceneSettings.m_Instance == null
                    ? null : SceneSettings.m_Instance.GetDesiredPreset();
                return env != null && env.isPassthrough;
            }
        }

        /// <summary>
        /// Switches between the passthrough environment and Open Brush's default virtual one. Returns
        /// false when there is nothing to switch to, or Open Brush refused (it will not leave
        /// passthrough while other players are in a multiplayer room).
        /// </summary>
        public static bool SetPassthrough(bool on)
        {
            EnvironmentCatalog catalog = EnvironmentCatalog.m_Instance;
            if (catalog == null || SceneSettings.m_Instance == null)
            {
                return false;
            }
            TiltBrush.Environment target = on
                ? catalog.AllEnvironments.FirstOrDefault(e => e.isPassthrough)
                : catalog.DefaultEnvironment;
            if (target == null || (!on && target.isPassthrough))
            {
                target = catalog.AllEnvironments.FirstOrDefault(e => !e.isPassthrough);
            }
            if (target == null)
            {
                return false;
            }
            SceneSettings.m_Instance.SetDesiredPreset(target, keepSceneTransform: true);
            return SceneSettings.m_Instance.GetDesiredPreset() == target;
        }

        /// <summary>The names of the environments a host can pick for everyone (not passthrough, which is personal).</summary>
        public static List<string> SelectableEnvironmentNames()
        {
            var names = new List<string>();
            EnvironmentCatalog catalog = EnvironmentCatalog.m_Instance;
            if (catalog != null)
            {
                foreach (TiltBrush.Environment env in catalog.AllEnvironments)
                {
                    if (!env.isPassthrough)
                    {
                        names.Add(env.name);
                    }
                }
            }
            return names;
        }

        /// <summary>Switches to the environment called <paramref name="name"/> (the host's pick). False if there is none.</summary>
        public static bool ApplyEnvironment(string name)
        {
            EnvironmentCatalog catalog = EnvironmentCatalog.m_Instance;
            if (catalog == null || SceneSettings.m_Instance == null || string.IsNullOrEmpty(name))
            {
                return false;
            }
            TiltBrush.Environment target = catalog.AllEnvironments.FirstOrDefault(
                e => !e.isPassthrough && string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return false;
            }
            SceneSettings.m_Instance.SetDesiredPreset(target, keepSceneTransform: true);
            return SceneSettings.m_Instance.GetDesiredPreset() == target;
        }

        /// <summary>True when the brush hand is the right hand (the wand, with the menu, is the left).</summary>
        public static bool WandOnRight
        {
            get { return InputManager.m_Instance.WandOnRight; }
            set { InputManager.m_Instance.WandOnRight = value; }
        }

        /// <summary>Switches to Open Brush's camera tool for taking a photo.</summary>
        public static void EnablePhotoTool()
        {
            SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.MultiCamTool);
        }

        // ----- Head and pointer -----

        public static Transform Head
        {
            get { return ViewpointScript.Head; }
        }

        /// <summary>
        /// The ray the player points with: the brush controller in VR, the screen centre on desktop.
        /// </summary>
        public static bool TryGetPointerRay(out Ray ray)
        {
            if (!IsMonoscopic && InputManager.Brush.IsTrackedObjectValid)
            {
                Transform attach = InputManager.m_Instance.GetBrushControllerAttachPoint();
                if (attach != null)
                {
                    ray = new Ray(attach.position, attach.forward);
                    return true;
                }
            }
            Transform head = ViewpointScript.Head;
            if (head != null)
            {
                ray = new Ray(head.position, head.forward);
                return true;
            }
            ray = default(Ray);
            return false;
        }

        /// <summary>
        /// Desktop mode only: the ray through a virtual mouse cursor. The mouse normally moves Open
        /// Brush's own drawing reticle, so the game keeps its own cursor and moves it with the
        /// mouse (unless Alt is held, which turns the camera instead).
        /// </summary>
        public static bool TryGetCursorRay(ref Vector2 cursor, bool moveCursor, out Ray ray)
        {
            Transform head = ViewpointScript.Head;
            Camera camera = head == null ? null : head.GetComponent<Camera>();
            if (camera == null)
            {
                camera = Camera.main;
            }
            if (camera == null)
            {
                return TryGetPointerRay(out ray);
            }
            if (moveCursor && Mouse.current != null && !CameraLookHeld)
            {
                cursor += Mouse.current.delta.ReadValue() * 1.5f;
            }
            cursor.x = Mathf.Clamp(cursor.x, 0f, Screen.width);
            cursor.y = Mathf.Clamp(cursor.y, 0f, Screen.height);
            ray = camera.ScreenPointToRay(new Vector3(cursor.x, cursor.y, 0f));
            return true;
        }

        /// <summary>True while Alt is held, which makes the mouse turn the desktop camera.</summary>
        public static bool CameraLookHeld
        {
            get
            {
                return InputManager.m_Instance.GetKeyboardShortcut(
                    InputManager.KeyboardShortcut.PositionMonoCamera);
            }
        }

        public static bool PrimaryPressedThisFrame
        {
            get { return InputManager.m_Instance.GetCommandDown(InputManager.SketchCommands.Activate); }
        }

        public static bool PrimaryHeld
        {
            get { return InputManager.m_Instance.GetCommand(InputManager.SketchCommands.Activate); }
        }

        /// <summary>Stops Open Brush shortcuts (WASD, undo, ...) while the player is typing.</summary>
        public static bool KeyboardShortcutsDisabled
        {
            get { return InputManager.m_Instance.DisableKeyboardShortcuts; }
            set { InputManager.m_Instance.DisableKeyboardShortcuts = value; }
        }

        /// <summary>Tells Open Brush a Telewheel control took this click, so it does not also paint.</summary>
        public static void EatPaintInput()
        {
            SketchControlsScript.m_Instance.EatGazeObjectInput();
        }

        public static bool IsPointingAtOpenBrushUi
        {
            get { return SketchControlsScript.m_Instance.IsUserInteractingWithUI(); }
        }

        // ----- Strokes -----

        public static int ActiveStrokeCount
        {
            get { return SketchMemoryScript.m_Instance.GetAllUnselectedActiveStrokes().Count; }
        }

        public static bool IsStrokeInProgress
        {
            get { return PointerManager.m_Instance.IsMainPointerCreatingStroke(); }
        }

        public static bool CanUndo
        {
            get { return SketchMemoryScript.m_Instance.CanUndo(); }
        }

        /// <summary>The main canvas's strokes as bytes, or an empty array for a blank canvas.</summary>
        public static byte[] SerializeActiveStrokes()
        {
            List<Stroke> strokes = SketchMemoryScript.m_Instance.GetAllUnselectedActiveStrokes();
            if (strokes.Count == 0)
            {
                return new byte[0];
            }
            return MultiplayerStrokeSerialization.SerializeMemoryList(strokes);
        }

        /// <summary>
        /// Wipes the canvas, undo history, layers and any shown drawing. This is Open Brush's own
        /// "new sketch" reset (the same code the Clear Sketch button uses), so nothing is left behind.
        /// </summary>
        public static void ClearEverything()
        {
            // NewSketch also resets the "force painting" override, which the game uses to keep the
            // canvas locked between turns, so put it back.
            ApiManager.ForcePaintingMode forcePainting = ApiManager.Instance.ForcePainting;
            SketchControlsScript.m_Instance.NewSketch(fade: false);
            ApiManager.Instance.ForcePainting = forcePainting;
        }

        /// <summary>Reads serialized strokes. Layer indices are squashed so no extra layers appear.</summary>
        public static List<Stroke> ReadStrokes(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }
            using (var stream = new MemoryStream(data))
            {
                return SketchWriter.GetStrokes(stream, allowFastPath: true, squashLayers: true);
            }
        }

        public static CanvasScript CreateStageCanvas()
        {
            return App.Scene.AddLayerNow();
        }

        /// <summary>
        /// Shows previously read strokes, display-only, on a fresh canvas fitted into a box of
        /// <paramref name="maxExtent"/> units centred on <paramref name="worldCenter"/>.
        /// Returns the number of strokes shown.
        /// </summary>
        public static int ShowStrokesOnStage(
            List<Stroke> strokes, Vector3 worldCenter, float maxExtent, out CanvasScript stage)
        {
            stage = null;
            if (strokes == null || strokes.Count == 0)
            {
                return 0;
            }
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (Stroke stroke in strokes)
            {
                if (stroke.m_ControlPoints == null)
                {
                    continue;
                }
                foreach (PointerManager.ControlPoint point in stroke.m_ControlPoints)
                {
                    if (!any)
                    {
                        bounds = new Bounds(point.m_Pos, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point.m_Pos);
                    }
                }
            }
            if (!any)
            {
                return 0;
            }

            float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = Mathf.Clamp(maxExtent / Mathf.Max(extent, 0.01f), 0.05f, 3f);

            stage = CreateStageCanvas();
            stage.Pose = TrTransform.TRS(
                worldCenter - scale * bounds.center, Quaternion.identity, scale);

            foreach (Stroke stroke in strokes)
            {
                stroke.m_IntendedCanvas = stage;
                SketchMemoryScript.m_Instance.MemoryListAdd(stroke);
            }
            SketchMemoryScript.m_Instance.RenderStrokesDirectly(strokes);
            stage.BatchManager.FlushMeshUpdates();
            return strokes.Count;
        }

        /// <summary>How many strokes are alive on a canvas (the stage, in practice).</summary>
        public static int StrokeCountOn(CanvasScript canvas)
        {
            return SketchMemoryScript.m_Instance.GetAllActiveStrokes(canvas).Count;
        }

        /// <summary>
        /// Draws a simple squiggle through the API stroke builder, as if the player had drawn it.
        /// Used by the self-test and by AutoPlay bots. Points are in metres from the drawing centre.
        /// </summary>
        public static int DrawSyntheticStroke(IList<Vector3> pathMeters, Color color)
        {
            if (pathMeters == null || pathMeters.Count < 2)
            {
                return 0;
            }
            var path = new List<TrTransform>(pathMeters.Count);
            foreach (Vector3 meters in pathMeters)
            {
                path.Add(TrTransform.T(TwLayout.ToUnits(meters)));
            }
            var paths = new List<IEnumerable<TrTransform>> { path };
            List<Stroke> strokes = DrawStrokes.DrawNestedTrList(
                paths, TrTransform.identity, new List<Color> { color });
            return strokes.Count;
        }

        // ----- Panels -----

        /// <summary>Panels Telewheel keeps on screen, as the panel manager reports them.</summary>
        public static List<BasePanel> TelewheelPanels()
        {
            var panels = new List<BasePanel>();
            foreach (PanelManager.PanelData data in PanelManager.m_Instance.GetAllPanels())
            {
                if (data.m_Panel != null && data.AvailableInCurrentMode)
                {
                    panels.Add(data.m_Panel);
                }
            }
            return panels;
        }
    }
}
