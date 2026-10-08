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
using System.IO;
using System.Xml;
using NUnit.Framework;

namespace Telewheel.Tests
{
    // These run against the project's real Assets/Plugins/Android/AndroidManifest.xml and the real
    // AndroidStoreManifest / TelewheelAndroidManifest sources (linked into this project), so they see what
    // the build sees. They only run here: the Unity Test Runner cannot reference Editor code from a test
    // assembly.
    public class TelewheelAndroidManifestTests
    {
        private const string A = TelewheelAndroidManifest.AndroidNamespace;
        private const string T = TelewheelAndroidManifest.ToolsNamespace;

        private static XmlDocument LoadProjectManifest()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Assets", "Plugins", "Android", "AndroidManifest.xml")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            Assert.IsNotNull(dir, "could not find the project's AndroidManifest.xml above " + AppContext.BaseDirectory);
            var doc = new XmlDocument();
            doc.Load(Path.Combine(dir, "Assets", "Plugins", "Android", "AndroidManifest.xml"));
            return doc;
        }

        private static XmlNamespaceManager Namespaces(XmlDocument doc)
        {
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", A);
            ns.AddNamespace("tools", T);
            return ns;
        }

        private static XmlElement Find(XmlDocument doc, string tag, string name)
        {
            return doc.SelectSingleNode(
                "/manifest/" + tag + "[@android:name='" + name + "']", Namespaces(doc)) as XmlElement
                ?? doc.SelectSingleNode(
                    "/manifest/application/" + tag + "[@android:name='" + name + "']", Namespaces(doc)) as XmlElement;
        }

        private static XmlDocument Configured(bool telewheel)
        {
            XmlDocument doc = LoadProjectManifest();
            AndroidStoreManifest.Configure(doc, true, false, telewheel);
            return doc;
        }

        [Test]
        public void StorageAccessIsRemovedFromTheManifestAndAnyLibrary()
        {
            XmlDocument doc = Configured(true);
            XmlElement permission = Find(doc, "uses-permission", TelewheelAndroidManifest.ManageExternalStorage);
            Assert.IsNotNull(permission);
            Assert.AreEqual("remove", permission.GetAttribute("node", T));
            foreach (string name in new[] { TelewheelAndroidManifest.WriteExternalStorage, TelewheelAndroidManifest.ReadExternalStorage })
            {
                XmlElement other = Find(doc, "uses-permission", name);
                Assert.IsNotNull(other, name + " should be marked so a library cannot add it");
                Assert.AreEqual("remove", other.GetAttribute("node", T));
            }
            var app = (XmlElement)doc.SelectSingleNode("/manifest/application");
            Assert.IsFalse(app.HasAttribute("requestLegacyExternalStorage", A));
            StringAssert.Contains("android:requestLegacyExternalStorage", app.GetAttribute("remove", T));
            Assert.AreEqual("false", app.GetAttribute("allowBackup", A), "the rest of the application element is untouched");
        }

        [Test]
        public void PassthroughIsOptionalButAsked()
        {
            XmlElement feature = Find(Configured(true), "uses-feature", TelewheelAndroidManifest.PassthroughFeature);
            Assert.IsNotNull(feature);
            Assert.AreEqual("false", feature.GetAttribute("required", A));
            Assert.AreEqual("android:required", feature.GetAttribute("replace", T));
        }

        [Test]
        public void HandTrackingIsAskedForAndOptional()
        {
            XmlDocument doc = Configured(true);
            Assert.IsNotNull(Find(doc, "uses-permission", TelewheelAndroidManifest.HandTrackingPermission));
            XmlElement feature = Find(doc, "uses-feature", TelewheelAndroidManifest.HandTrackingFeature);
            Assert.AreEqual("false", feature.GetAttribute("required", A));
            Assert.AreEqual("HIGH", Find(doc, "meta-data", TelewheelAndroidManifest.HandTrackingFrequency).GetAttribute("value", A));
            Assert.AreEqual("V2.0", Find(doc, "meta-data", TelewheelAndroidManifest.HandTrackingVersion).GetAttribute("value", A));
        }

        [Test]
        public void OpenBrushsQuestEntriesAreStillThere()
        {
            XmlDocument doc = Configured(true);
            XmlElement headTracking = Find(doc, "uses-feature", "android.hardware.vr.headtracking");
            Assert.AreEqual("true", headTracking.GetAttribute("required", A));
            XmlElement devices = Find(doc, "meta-data", "com.oculus.supportedDevices");
            StringAssert.Contains("quest3", devices.GetAttribute("value", A));
            Assert.IsNotNull(Find(doc, "uses-permission", "android.permission.RECORD_AUDIO"), "the microphone is needed for voice chat");
        }

        [Test]
        public void ApplyingItTwiceChangesNothing()
        {
            XmlDocument once = Configured(true);
            string first = once.OuterXml;
            TelewheelAndroidManifest.Apply(once);
            TelewheelAndroidManifest.Apply(once);
            Assert.AreEqual(first, once.OuterXml);
        }

        [Test]
        public void ABuildThatIsNotTelewheelIsLeftAlone()
        {
            XmlDocument doc = Configured(false);
            Assert.IsNull(Find(doc, "uses-feature", TelewheelAndroidManifest.PassthroughFeature));
            Assert.IsNull(Find(doc, "uses-permission", TelewheelAndroidManifest.HandTrackingPermission));
            XmlElement storage = Find(doc, "uses-permission", TelewheelAndroidManifest.ManageExternalStorage);
            Assert.IsNotNull(storage);
            Assert.AreEqual(string.Empty, storage.GetAttribute("node", T));
            var app = (XmlElement)doc.SelectSingleNode("/manifest/application");
            Assert.AreEqual("true", app.GetAttribute("requestLegacyExternalStorage", A));
        }

        [Test]
        public void ItWritesOutAsOrdinaryAndroidAndToolsAttributes()
        {
            string xml = Configured(true).OuterXml;
            StringAssert.DoesNotContain("d1p1", xml, "namespace prefixes should be the declared ones, not generated");
            StringAssert.DoesNotContain("p2:", xml);
            StringAssert.Contains("tools:node=\"remove\"", xml);
            StringAssert.Contains("android:required=\"false\"", xml);
            StringAssert.Contains("tools:replace=\"android:required\"", xml);
        }

        [Test]
        public void TheEntriesDoNotDependOnTheStoreFlag()
        {
            XmlDocument doc = LoadProjectManifest();
            AndroidStoreManifest.Configure(doc, false, false, true);
            Assert.IsNotNull(Find(doc, "uses-feature", TelewheelAndroidManifest.PassthroughFeature));
            Assert.AreEqual(
                "remove",
                Find(doc, "uses-permission", TelewheelAndroidManifest.ManageExternalStorage).GetAttribute("node", T));
        }

        [Test]
        public void AnAndroidXrBuildKeepsThemToo()
        {
            XmlDocument doc = LoadProjectManifest();
            AndroidStoreManifest.Configure(doc, true, true, true);
            Assert.IsNotNull(Find(doc, "uses-feature", TelewheelAndroidManifest.PassthroughFeature));
            Assert.IsNotNull(Find(doc, "uses-permission", TelewheelAndroidManifest.HandTrackingPermission));
        }

        [Test]
        public void ARequiredFeatureAPackageAskedForIsMadeOptional()
        {
            var doc = new XmlDocument();
            doc.LoadXml(
                "<manifest xmlns:android=\"" + A + "\" xmlns:tools=\"" + T + "\"><application/>" +
                "<uses-feature android:name=\"" + TelewheelAndroidManifest.PassthroughFeature +
                "\" android:required=\"true\" tools:replace=\"android:required,android:version\"/></manifest>");
            TelewheelAndroidManifest.Apply(doc);
            XmlElement feature = Find(doc, "uses-feature", TelewheelAndroidManifest.PassthroughFeature);
            Assert.AreEqual("false", feature.GetAttribute("required", A));
            Assert.AreEqual("android:required,android:version", feature.GetAttribute("replace", T), "kept, and not doubled");
            Assert.AreEqual(
                1,
                doc.SelectNodes(
                    "/manifest/uses-feature[@android:name='" + TelewheelAndroidManifest.PassthroughFeature + "']",
                    Namespaces(doc)).Count,
                "no second element for the same feature");
        }

        [Test]
        public void ARemoveListThatAlreadyExistsIsExtendedNotReplaced()
        {
            var doc = new XmlDocument();
            doc.LoadXml(
                "<manifest xmlns:android=\"" + A + "\" xmlns:tools=\"" + T + "\">" +
                "<application tools:remove=\"android:label\" android:requestLegacyExternalStorage=\"true\"/>" +
                "</manifest>");
            TelewheelAndroidManifest.Apply(doc);
            var app = (XmlElement)doc.SelectSingleNode("/manifest/application");
            Assert.AreEqual("android:label,android:requestLegacyExternalStorage", app.GetAttribute("remove", T));
            TelewheelAndroidManifest.Apply(doc);
            Assert.AreEqual("android:label,android:requestLegacyExternalStorage", app.GetAttribute("remove", T));
        }
    }
}
