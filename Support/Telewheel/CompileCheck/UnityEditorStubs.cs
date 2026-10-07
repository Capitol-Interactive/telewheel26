// Stand-ins for the few UnityEditor APIs used by Assets/Editor/TelewheelPhotonSetup.cs, so that
// file can at least be type-checked outside Unity. Keep them matching the real signatures.
using System;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MenuItem : Attribute { public MenuItem(string path) { } }

    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok, string cancel) { return true; }
        public static bool DisplayDialog(string title, string message, string ok) { return true; }
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
    }

    public static class AssetDatabase
    {
        public static void Refresh() { }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return null; }
    }

    public static class PlayerSettings
    {
        public static string GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget target) { return string.Empty; }
        public static void SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget target, string defines) { }
    }
}

namespace UnityEditor.Build
{
    public struct NamedBuildTarget
    {
        public static NamedBuildTarget Standalone { get { return default(NamedBuildTarget); } }
        public static NamedBuildTarget Android { get { return default(NamedBuildTarget); } }
    }
}
