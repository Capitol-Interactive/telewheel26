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

// Stand-ins for Open Brush / TextMeshPro / Input System types, copied from the real signatures,
// so the Telewheel sources can be compiled outside Unity to catch typos and type errors.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight, MidlineLeft, Midline, MidlineRight }
    public enum TextWrappingModes { NoWrap, Normal }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TMP_FontAsset : UnityEngine.Object
    {
        public List<TMP_FontAsset> fallbackFontAssetTable;
        public static TMP_FontAsset CreateFontAsset(Font font) { return null; }
    }
    public static class TMP_Settings { public static TMP_FontAsset defaultFontAsset { get { return null; } } }
    public abstract class TMP_Text : MonoBehaviour
    {
        public string text { get; set; }
        public float fontSize { get; set; }
        public bool enableAutoSizing { get; set; }
        public Color color { get; set; }
        public TextAlignmentOptions alignment { get; set; }
        public TextOverflowModes overflowMode { get; set; }
        public TextWrappingModes textWrappingMode { get; set; }
        public TMP_FontAsset font { get; set; }
        public Color32 outlineColor { get; set; }
        public float outlineWidth { get; set; }
        public bool richText { get; set; }
        public RectTransform rectTransform { get { return null; } }
    }
    public class TextMeshPro : TMP_Text { }
}

namespace UnityEngine.InputSystem
{
    public class KeyControl { public bool wasPressedThisFrame { get { return false; } } }
    public class Mouse
    {
        public static Mouse current { get { return null; } }
        public Vector2Control delta { get { return null; } }
    }
    public class Vector2Control { public Vector2 ReadValue() { return default(Vector2); } }
    public class Keyboard
    {
        public static Keyboard current { get { return null; } }
        public KeyControl f8Key { get { return null; } }
        public KeyControl f9Key { get { return null; } }
        public KeyControl f10Key { get { return null; } }
        public KeyControl enterKey { get { return null; } }
        public event Action<char> onTextInput;
    }
}

namespace OpenBrush.Multiplayer
{
    public static class MultiplayerStrokeSerialization
    {
        public static byte[] SerializeMemoryList(List<TiltBrush.Stroke> strokeList) { return null; }
    }
}

namespace TiltBrush
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ApiEndpoint : Attribute { public ApiEndpoint(string endpoint, string description, string exampleUsage = null) { } }

    public enum SdkMode { Unset = -1, UnityXR, Monoscopic, Ods }
    public class Config { public SdkMode m_SdkMode; }

    public struct TrTransform
    {
        public Vector3 translation; public Quaternion rotation; public float scale;
        public static TrTransform identity = TR(Vector3.zero, Quaternion.identity);
        public static TrTransform T(Vector3 t) { return new TrTransform { translation = t, rotation = Quaternion.identity, scale = 1 }; }
        public static TrTransform TR(Vector3 t, Quaternion r) { return TRS(t, r, 1); }
        public static TrTransform TRS(Vector3 t, Quaternion r, float s) { return new TrTransform { translation = t, rotation = r, scale = s }; }
    }

    public class UserConfig
    {
        public struct FlagsConfig { public bool DisableAutosave; public bool SkipIntro { get; set; } }
        public FlagsConfig Flags;
        public struct TelewheelConfig
        {
            public bool Enabled { get; set; }
            public bool SelfTestOnStart { get; set; }
            public bool AutoPlay { get; set; }
            public float TimeScale { get; set; }
            public int Players { get; set; }
            public int Rounds { get; set; }
            public int Seed { get; set; }
            public bool DebugOverlay { get; set; }
        }
        public TelewheelConfig Telewheel;
    }

    public class BatchManager { public void FlushMeshUpdates() { } }
    public class CanvasScript : MonoBehaviour
    {
        public TrTransform Pose { get; set; }
        public BatchManager BatchManager { get { return null; } }
    }
    public class SceneScript : MonoBehaviour
    {
        public TrTransform Pose { get; set; }
        public CanvasScript ActiveCanvas { get; set; }
        public IEnumerable<CanvasScript> LayerCanvases { get { return null; } }
        public CanvasScript AddLayerNow() { return null; }
        public void ClearLayerContents(CanvasScript c) { }
        public void ResetLayers(bool notify = false) { }
    }
    public enum PassthroughMode { None, FBPassthrough, OpenXREnvionmentBlendMode, Zapbox }
    public class VrSdk { public PassthroughMode PassthroughMode { get; private set; } }
    public class Environment : ScriptableObject { public bool isPassthrough; }
    public class EnvironmentCatalog : MonoBehaviour
    {
        public static EnvironmentCatalog m_Instance;
        public IEnumerable<Environment> AllEnvironments { get { return null; } }
        public Environment DefaultEnvironment { get { return null; } }
    }
    public class SceneSettings : MonoBehaviour
    {
        public static SceneSettings m_Instance;
        public Environment GetDesiredPreset() { return null; }
        public void SetDesiredPreset(Environment env, bool forceTransition = false, bool keepSceneTransform = false, bool hasCustomLights = false, bool skipFade = false) { }
    }
    public class BrushColorController { public Color CurrentColor { get; set; } }
    public class BrushDescriptor : ScriptableObject { public Guid m_Guid; }
    public class BrushCatalog : MonoBehaviour
    {
        public static BrushCatalog m_Instance;
        public bool IsLoading { get { return false; } }
        public BrushDescriptor GetBrush(Guid guid) { return null; }
        public BrushDescriptor DefaultBrush { get { return null; } }
    }
    public class BrushController : MonoBehaviour
    {
        public static BrushController m_Instance;
        public BrushDescriptor ActiveBrush { get { return null; } }
        public void SetActiveBrush(BrushDescriptor brush) { }
    }

    public class App : MonoBehaviour
    {
        public enum AppState { Error, LoadingBrushesAndLighting, FadeFromBlack, FirstRunIntro, Intro, Loading, QuickLoad, Standard, MemoryExceeded, Saving, Reset, Uploading, AutoProfiling, OfflineRendering }
        public static App Instance { get { return null; } }
        public static UserConfig UserConfig { get { return null; } }
        public static Config Config { get { return null; } }
        public static SceneScript Scene { get { return null; } }
        public static BrushColorController BrushColor { get { return null; } }
        public static VrSdk VrSdk { get { return null; } }
        public static AppState CurrentState { get { return AppState.Standard; } }
        public static bool AppAllowsCreation() { return true; }
        public event Action<AppState, AppState> StateChanged;
    }

    public class ApiManager : MonoBehaviour
    {
        public enum ForcePaintingMode { None, ForcedOn, ForcedOff, ForceNewStroke, WasForceNewStroke }
        public static ApiManager Instance { get { return null; } }
        public ForcePaintingMode ForcePainting;
    }
    public static class DrawStrokes
    {
        public static List<Stroke> DrawNestedTrList(IEnumerable<IEnumerable<TrTransform>> pathEnumerable, TrTransform tr, List<Color> colors = null) { return null; }
    }

    public class StrokeData { }
    public class Stroke : StrokeData
    {
        public PointerManager.ControlPoint[] m_ControlPoints;
        public CanvasScript m_IntendedCanvas;
    }
    public class PointerManager : MonoBehaviour
    {
        public struct ControlPoint { public Vector3 m_Pos; }
        public static PointerManager m_Instance;
        public bool IsMainPointerCreatingStroke() { return false; }
    }
    public class SketchMemoryScript : MonoBehaviour
    {
        public static SketchMemoryScript m_Instance;
        public List<Stroke> GetAllUnselectedActiveStrokes() { return null; }
        public List<Stroke> GetAllActiveStrokes(CanvasScript layer) { return null; }
        public void MemoryListAdd(Stroke stroke) { }
        public void RenderStrokesDirectly(List<Stroke> strokes) { }
        public bool CanUndo() { return false; }
        public bool CanRedo() { return false; }
    }
    public static class SketchWriter
    {
        public static List<Stroke> GetStrokes(Stream stream, bool allowFastPath, bool squashLayers = false) { return null; }
    }

    public abstract class BaseTool { public enum ToolType { SketchSurface, DropperTool, MultiCamTool, TeleportTool, EraserTool, EmptyTool } }
    public class SketchSurfacePanel : MonoBehaviour
    {
        public static SketchSurfacePanel m_Instance;
        public void EnableDefaultTool() { }
        public void EnableSpecificTool(BaseTool.ToolType t) { }
        public BaseTool.ToolType GetCurrentToolType() { return BaseTool.ToolType.SketchSurface; }
    }

    public class SketchControlsScript : MonoBehaviour
    {
        public enum GlobalCommands
        {
            Null, NewSketch, StraightEdge, Undo, Redo, SymmetryPlane, MultiMirror, LightingHdr, LightingLdr, MorePanels, AdvancedTools,
            AdvancedPanelsToggle, SaveOptions, SaveGallery, SaveAndUpload, UploadToGenericCloud, ShowWindowGUI, Sketchbook, StraightEdgeShape,
            StraightEdgeMeterDisplay, ToggleSettings, MultiplayerTogglePanel, OpenColorOptionsPopup,
        }
        public static SketchControlsScript m_Instance;
        [NonSerialized] public bool m_DisableWorldGrabbing;
        public void RequestPanelsVisibility(bool bVisible) { }
        public void NewSketch(bool fade) { }
        public void EatGazeObjectInput() { }
        public bool IsUserInteractingWithUI() { return false; }
    }

    public class BasePanel : MonoBehaviour
    {
        public enum PanelType { SketchSurface, Color, Brush, AdminPanelMobile, ToolsBasicMobile, ToolsBasic, MemoryWarning, AdminPanel }
        public PanelType Type { get { return PanelType.Color; } }
    }
    public class BaseButton : MonoBehaviour { }
    public class OptionButton : BaseButton { public SketchControlsScript.GlobalCommands m_Command; }
    public class ToolButton : BaseButton { public BaseTool.ToolType Tool { get { return BaseTool.ToolType.SketchSurface; } } }
    public class PanelManager : MonoBehaviour
    {
        public class PanelData { public BasePanel m_Panel; public bool AvailableInCurrentMode { get { return true; } } }
        public static PanelManager m_Instance;
        public void SetTelewheelMode(bool enabled) { }
        public List<PanelData> GetAllPanels() { return null; }
    }

    public abstract class ControllerInfo { public bool IsTrackedObjectValid { get { return false; } } }
    public class InputManager : MonoBehaviour
    {
        public enum SketchCommands { Activate, AltActivate }
        public static InputManager m_Instance;
        public static ControllerInfo Brush { get { return null; } }
        public Transform GetBrushControllerAttachPoint() { return null; }
        public enum KeyboardShortcut { PositionMonoCamera }
        public bool GetKeyboardShortcut(KeyboardShortcut s) { return false; }
        public bool GetCommandDown(SketchCommands c) { return false; }
        public bool GetCommand(SketchCommands c) { return false; }
        public bool DisableKeyboardShortcuts { get; set; }
        public bool WandOnRight { get; set; }
    }
    public class ViewpointScript : MonoBehaviour { public static Transform Head { get { return null; } } }
}
