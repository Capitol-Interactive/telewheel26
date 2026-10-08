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

using System.Collections.Generic;
using TiltBrush;

namespace Telewheel
{
    /// <summary>
    /// Trims Open Brush's panels down to what the game needs by hiding buttons. Which buttons to
    /// hide is decided by what each one does (its command or tool), never by its name, and every
    /// button hidden is logged so a local test can confirm the list.
    /// </summary>
    public static class TwModeAdapter
    {
        // Straight edge, mirror, environment, advanced and file/cloud buttons: not part of the game.
        private static readonly HashSet<SketchControlsScript.GlobalCommands> s_HiddenCommands =
            new HashSet<SketchControlsScript.GlobalCommands>
            {
                SketchControlsScript.GlobalCommands.StraightEdge,
                SketchControlsScript.GlobalCommands.StraightEdgeShape,
                SketchControlsScript.GlobalCommands.StraightEdgeMeterDisplay,
                SketchControlsScript.GlobalCommands.SymmetryPlane,
                SketchControlsScript.GlobalCommands.MultiMirror,
                SketchControlsScript.GlobalCommands.LightingLdr,
                SketchControlsScript.GlobalCommands.LightingHdr,
                SketchControlsScript.GlobalCommands.MorePanels,
                SketchControlsScript.GlobalCommands.AdvancedTools,
                SketchControlsScript.GlobalCommands.AdvancedPanelsToggle,
                SketchControlsScript.GlobalCommands.SaveOptions,
                SketchControlsScript.GlobalCommands.SaveGallery,
                SketchControlsScript.GlobalCommands.SaveAndUpload,
                SketchControlsScript.GlobalCommands.UploadToGenericCloud,
                SketchControlsScript.GlobalCommands.ShowWindowGUI,
                SketchControlsScript.GlobalCommands.MultiplayerTogglePanel,
                SketchControlsScript.GlobalCommands.OpenColorOptionsPopup,
            };

        // Teleport and the colour dropper are not part of the game either.
        private static readonly HashSet<BaseTool.ToolType> s_HiddenTools =
            new HashSet<BaseTool.ToolType>
            {
                BaseTool.ToolType.TeleportTool,
                BaseTool.ToolType.DropperTool,
            };

        /// <summary>Hides every unwanted button on the panels that are on screen. Returns how many were newly hidden.</summary>
        public static int HideUnusedButtons(List<string> log)
        {
            int hidden = 0;
            foreach (BasePanel panel in OpenBrushFacade.TelewheelPanels())
            {
                foreach (BaseButton button in panel.GetComponentsInChildren<BaseButton>(true))
                {
                    string reason = ReasonToHide(button);
                    if (reason == null || !button.gameObject.activeSelf)
                    {
                        continue;
                    }
                    button.gameObject.SetActive(false);
                    hidden++;
                    if (log != null)
                    {
                        log.Add("hid " + panel.Type + "/" + button.name + " (" + reason + ")");
                    }
                }
            }
            return hidden;
        }

        private static string ReasonToHide(BaseButton button)
        {
            OptionButton option = button as OptionButton;
            if (option != null && s_HiddenCommands.Contains(option.m_Command))
            {
                return option.m_Command.ToString();
            }
            ToolButton tool = button as ToolButton;
            if (tool != null && s_HiddenTools.Contains(tool.Tool))
            {
                return tool.Tool.ToString();
            }
            return null;
        }
    }
}
