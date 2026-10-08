#!/usr/bin/env python3
# Copyright 2026 Capitol Interactive LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""Tests for telewheel_check_manifest.py, on dumps laid out the way aapt2 prints them.

python3 -I Support/Python/test_telewheel_check_manifest.py
"""

import contextlib
import importlib.util
import io
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location(
    "telewheel_check_manifest", os.path.join(HERE, "telewheel_check_manifest.py")
)
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)

ANDROID = "http://schemas.android.com/apk/res/android"


def attribute(name, value, resource="0x01010003"):
    return "        A: %s:%s(%s)=%s" % (ANDROID, name, resource, value)


def quoted(value):
    return '"%s" (Raw: "%s")' % (value, value)


def feature(name, required=None):
    lines = ["      E: uses-feature (line=10)", attribute("name", quoted(name))]
    if required is not None:
        lines.append(attribute("required", required, "0x0101028e"))
    return lines


def activity(name, launcher):
    lines = ["      E: activity (line=30)", attribute("name", quoted(name))]
    if launcher:
        lines += [
            "        E: intent-filter (line=32)",
            "          E: action (line=33)",
            "          " + attribute("name", quoted("android.intent.action.MAIN")),
            "          E: category (line=34)",
            "          "
            + attribute("name", quoted("android.intent.category.LAUNCHER")),
        ]
    return lines


def dump(extra=(), passthrough="false", hands="false"):
    """A manifest with everything a Telewheel Quest build needs, plus `extra` lines."""
    lines = [
        "N: android=%s (line=2)" % ANDROID,
        "  E: manifest (line=2)",
        '    A: package="com.capitolinteractive.telewheel" (Raw: "x")',
    ]
    lines += feature("com.oculus.feature.PASSTHROUGH", passthrough)
    lines += feature("oculus.software.handtracking", hands)
    lines += feature("android.hardware.vr.headtracking", "true")
    for permission in (
        "com.oculus.permission.HAND_TRACKING",
        "android.permission.RECORD_AUDIO",
    ):
        lines += [
            "      E: uses-permission (line=20)",
            attribute("name", quoted(permission)),
        ]
    lines += [
        "      E: meta-data (line=21)",
        attribute("name", quoted("com.oculus.handtracking.version")),
        attribute("value", quoted("V2.0"), "0x01010024"),
        "      E: meta-data (line=22)",
        attribute("name", quoted("com.oculus.supportedDevices")),
        attribute("value", quoted("quest2|questpro|quest3"), "0x01010024"),
    ]
    lines += activity("com.unity3d.player.UnityPlayerGameActivity", True)
    lines += extra
    return "\n".join(lines) + "\n"


def optional(text):
    return {
        label: state for label, _, state in checker.optional_rows(checker.parse(text))
    }


class RequiredAndForbidden(unittest.TestCase):
    def test_a_good_manifest_passes(self):
        rows, ok = checker.check(dump())
        self.assertTrue(ok, rows)

    def test_a_storage_permission_fails(self):
        extra = [
            "      E: uses-permission (line=40)",
            attribute("name", quoted("android.permission.MANAGE_EXTERNAL_STORAGE")),
        ]
        rows, ok = checker.check(dump(extra))
        self.assertFalse(ok)
        self.assertIn(("All files access", False), [(r[0], r[1]) for r in rows])

    def test_a_missing_requirement_fails(self):
        _, ok = checker.check(dump().replace("com.oculus.feature.PASSTHROUGH", "x"))
        self.assertFalse(ok)


class OptionalFeatures(unittest.TestCase):
    def test_a_false_word_is_optional(self):
        states = optional(dump(passthrough="false", hands="false"))
        self.assertEqual(states["Passthrough is optional"], "required=false")
        self.assertEqual(states["Hand tracking is optional"], "required=false")

    def test_a_typed_number_is_read_too(self):
        states = optional(
            dump(passthrough="(type 0x12)0x0", hands="(type 0x12)0xffffffff")
        )
        self.assertEqual(states["Passthrough is optional"], "required=false")
        self.assertEqual(states["Hand tracking is optional"], "required=TRUE")

    def test_a_true_word_is_required(self):
        states = optional(dump(passthrough="true"))
        self.assertEqual(states["Passthrough is optional"], "required=TRUE")

    def test_no_required_attribute_defaults_to_true(self):
        text = dump().replace(attribute("required", "false", "0x0101028e") + "\n", "")
        self.assertIn("not set", optional(text)["Passthrough is optional"])

    def test_an_unreadable_value_is_shown(self):
        states = optional(dump(passthrough="banana"))
        self.assertEqual(
            states["Passthrough is optional"], "not confirmed (could not read: banana)"
        )

    def test_a_missing_feature_is_said(self):
        text = dump().replace("oculus.software.handtracking", "other.feature")
        self.assertEqual(
            optional(text)["Hand tracking is optional"], "not found as a uses-feature"
        )


class Launchers(unittest.TestCase):
    def roots(self, text):
        return checker.parse(text)

    def test_only_the_activity_with_the_launcher_filter_counts(self):
        extra = activity("com.example.Other", False)
        names = checker.launchers(self.roots(dump(extra)))
        self.assertEqual(names, ["com.unity3d.player.UnityPlayerGameActivity"])

    def test_no_launcher_is_reported_as_none(self):
        text = dump().replace("android.intent.category.LAUNCHER", "x")
        self.assertEqual(checker.launchers(self.roots(text)), [])


class CommandLine(unittest.TestCase):
    def run_main(self, text):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "dump.txt")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write(text)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = checker.main([path])
        return code, out.getvalue()

    def test_a_good_manifest_exits_zero_and_names_the_launcher(self):
        code, report = self.run_main(dump())
        self.assertEqual(code, 0)
        self.assertIn("Telewheel Quest manifest: OK", report)
        self.assertIn(
            "Launcher activity: com.unity3d.player.UnityPlayerGameActivity", report
        )

    def test_a_bad_manifest_exits_one(self):
        code, report = self.run_main(dump().replace("V2.0", "V1.0"))
        self.assertEqual(code, 1)
        self.assertIn("PROBLEMS", report)


if __name__ == "__main__":
    unittest.main()
