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
using System.Xml;

/// <summary>
/// What a Telewheel Quest build needs in its Android manifest beyond Open Brush's: no all-files
/// storage permission (Telewheel keeps its data in the app's own folder), passthrough, and hand
/// tracking. Every step is "make sure this is so", so running it twice, or on a manifest that Meta's or
/// Mikesky's build hooks already touched, gives the same result. Uses only System.Xml so the tests run
/// outside Unity (Support/Telewheel/Tests).
/// </summary>
internal static class TelewheelAndroidManifest
{
    internal const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    internal const string ToolsNamespace = "http://schemas.android.com/tools";

    internal const string ManageExternalStorage = "android.permission.MANAGE_EXTERNAL_STORAGE";
    internal const string WriteExternalStorage = "android.permission.WRITE_EXTERNAL_STORAGE";
    internal const string ReadExternalStorage = "android.permission.READ_EXTERNAL_STORAGE";
    internal const string PassthroughFeature = "com.oculus.feature.PASSTHROUGH";
    internal const string HandTrackingPermission = "com.oculus.permission.HAND_TRACKING";
    internal const string HandTrackingFeature = "oculus.software.handtracking";
    internal const string HandTrackingFrequency = "com.oculus.handtracking.frequency";
    internal const string HandTrackingVersion = "com.oculus.handtracking.version";

    internal static void Apply(XmlDocument doc)
    {
        XmlElement root = doc.DocumentElement ?? throw new InvalidOperationException("Missing Android manifest.");
        XmlElement app = root.SelectSingleNode("application") as XmlElement
            ?? throw new InvalidOperationException("Missing Android application element.");
        root.SetAttribute("xmlns:tools", ToolsNamespace);

        // Telewheel never touches shared storage. The first needs a manual grant on Quest ("All files
        // access"), which the Meta store questions and which used to block startup; Unity adds the other
        // two itself while "Force SD card permission" is on. Its own folder needs none of them.
        MarkRemoved(doc, root, "uses-permission", ManageExternalStorage);
        MarkRemoved(doc, root, "uses-permission", WriteExternalStorage);
        MarkRemoved(doc, root, "uses-permission", ReadExternalStorage);
        RemoveApplicationAttribute(app, "requestLegacyExternalStorage");

        // Passthrough (mixed reality). Optional, so the app still installs on a device without it.
        SetFeatureRequired(doc, root, PassthroughFeature, false);

        // Hands: lets players press Telewheel's buttons without controllers. Controllers keep working.
        GetOrCreate(doc, root, "uses-permission", HandTrackingPermission);
        SetFeatureRequired(doc, root, HandTrackingFeature, false);
        SetMetadata(doc, app, HandTrackingFrequency, "HIGH");
        SetMetadata(doc, app, HandTrackingVersion, "V2.0");
    }

    // Keeps the element and tells the manifest merger to drop it from every library manifest too.
    private static void MarkRemoved(XmlDocument doc, XmlElement parent, string tag, string name)
    {
        XmlElement node = GetOrCreate(doc, parent, tag, name);
        node.SetAttribute("node", ToolsNamespace, "remove");
    }

    private static void RemoveApplicationAttribute(XmlElement app, string name)
    {
        app.RemoveAttribute(name, AndroidNamespace);
        AddToolsValue(app, "remove", "android:" + name);
    }

    // tools:remove and tools:replace hold a comma-separated list; add to it, never overwrite it.
    private static void AddToolsValue(XmlElement element, string attribute, string value)
    {
        string existing = element.GetAttribute(attribute, ToolsNamespace);
        if (string.IsNullOrEmpty(existing))
        {
            element.SetAttribute(attribute, ToolsNamespace, value);
        }
        else if (Array.IndexOf(existing.Split(','), value) < 0)
        {
            element.SetAttribute(attribute, ToolsNamespace, existing + "," + value);
        }
    }

    private static void SetFeatureRequired(XmlDocument doc, XmlElement parent, string name, bool required)
    {
        XmlElement feature = GetOrCreate(doc, parent, "uses-feature", name);
        feature.SetAttribute("required", AndroidNamespace, required ? "true" : "false");
        // A library manifest may ask for required=true; this build decides.
        AddToolsValue(feature, "replace", "android:required");
    }

    private static void SetMetadata(XmlDocument doc, XmlElement parent, string name, string value)
    {
        XmlElement node = GetOrCreate(doc, parent, "meta-data", name);
        node.SetAttribute("value", AndroidNamespace, value);
        AddToolsValue(node, "replace", "android:value");
    }

    private static XmlElement GetOrCreate(XmlDocument doc, XmlElement parent, string tag, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is XmlElement element && element.Name == tag &&
                element.GetAttribute("name", AndroidNamespace) == name)
            {
                return element;
            }
        }
        XmlElement created = doc.CreateElement(tag);
        created.SetAttribute("name", AndroidNamespace, name);
        parent.AppendChild(created);
        return created;
    }
}
