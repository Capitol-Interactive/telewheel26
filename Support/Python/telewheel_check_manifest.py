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

"""Checks the final Android manifest of a Telewheel Quest build.

The manifest that ships is made by Gradle merging the project's manifest with those of every
package, so what the project asks for is not proof of what the headset sees. CI dumps the built
APK's manifest (aapt2 dump xmltree) and this checks the text of the dump for what a Telewheel
Quest build must, and must not, contain.

    telewheel_check_manifest.py DUMP.txt      print a Markdown report, exit 1 if anything is wrong

The pass/fail checks are plain substring searches on purpose: they do not depend on how aapt2 lays
the tree out, only on the strings being present in the manifest. The information rows (whether the
passthrough and hand-tracking features are optional, which activity launches the app) do read the
tree, so a layout surprise can only make them say "not confirmed", never fail the build.
"""

import argparse
import re
import sys

# Name of the check -> text that must appear in the manifest.
REQUIRED = {
    "Application id": "com.capitolinteractive.telewheel",
    "Passthrough feature": "com.oculus.feature.PASSTHROUGH",
    "Hand tracking permission": "com.oculus.permission.HAND_TRACKING",
    "Hand tracking feature": "oculus.software.handtracking",
    "Hand tracking version": "V2.0",
    "Head tracking": "android.hardware.vr.headtracking",
    "Quest 3 listed as supported": "quest3",
    "Microphone (voice chat)": "android.permission.RECORD_AUDIO",
}

# Name of the check -> text that must not appear.
FORBIDDEN = {
    "All files access": "android.permission.MANAGE_EXTERNAL_STORAGE",
    "Write to shared storage": "android.permission.WRITE_EXTERNAL_STORAGE",
    "Legacy external storage": "requestLegacyExternalStorage",
}

# Entries that must be optional, so the app still installs without them. Only reported: the dump's
# layout is not something to fail a build over.
OPTIONAL_FEATURES = {
    "Passthrough is optional": "com.oculus.feature.PASSTHROUGH",
    "Hand tracking is optional": "oculus.software.handtracking",
}

# Shown for information, not checked: which activity launches the app.
ACTIVITIES = ("UnityPlayerActivity", "UnityPlayerGameActivity")

ELEMENT = re.compile(r"^(?P<indent>\s*)E: (?P<tag>[\w:.-]+)")
# aapt2 prints an attribute as: A: [namespace:]name[(0xID)]=VALUE [(Raw: "...")]
ATTRIBUTE = re.compile(
    r"^(?P<indent>\s*)A: (?:[^\s=(]+:)?(?P<name>[^\s=(:]+)(?:\(0x[0-9a-fA-F]+\))?=(?P<value>.*)$"
)
QUOTED = re.compile(r'^"((?:[^"\\]|\\.)*)"')
TYPED_NUMBER = re.compile(r"^\(type:? 0x[0-9a-fA-F]+\)\s*(0x[0-9a-fA-F]+)")
WORD_BOOLEAN = re.compile(r"^(true|false)(?:\s|$)")


class Element:
    """One element of the dumped manifest, with its attributes (raw text) and children."""

    def __init__(self, tag, indent):
        self.tag = tag
        self.indent = indent
        self.attributes = {}
        self.children = []

    def walk(self):
        yield self
        for child in self.children:
            yield from child.walk()

    def text(self, name):
        """A string attribute's value without its quotes, or None."""
        match = QUOTED.match(self.attributes.get(name, ""))
        return match.group(1) if match else None

    def boolean(self, name):
        """True or False for a boolean attribute, None when it is absent or not understood."""
        value = self.attributes.get(name, "").strip()
        match = TYPED_NUMBER.match(value)
        if match:
            return int(match.group(1), 16) != 0
        match = WORD_BOOLEAN.match(value)
        return match.group(1) == "true" if match else None


def parse(text):
    """The elements of an `aapt2 dump xmltree` listing, as a list of root elements."""
    roots = []
    stack = []
    for line in text.splitlines():
        element = ELEMENT.match(line)
        attribute = None if element else ATTRIBUTE.match(line)
        if not element and not attribute:
            continue
        indent = len((element or attribute).group("indent"))
        while stack and stack[-1].indent >= indent:
            stack.pop()
        if element:
            node = Element(element.group("tag"), indent)
            (stack[-1].children if stack else roots).append(node)
            stack.append(node)
        elif stack:
            stack[-1].attributes[attribute.group("name")] = attribute.group("value")
    return roots


def check(text):
    """Returns (rows, ok): one (label, passed, detail) per check, and whether all passed."""
    rows = []
    for label, needle in REQUIRED.items():
        found = needle in text
        rows.append((label, found, needle if found else "MISSING: " + needle))
    for label, needle in FORBIDDEN.items():
        found = needle in text
        rows.append((label, not found, "PRESENT: " + needle if found else "absent"))
    return rows, all(passed for _, passed, _ in rows)


def optional_state(features):
    """What to say about the `required` attribute of the uses-feature elements for one feature."""
    if not features:
        return "not found as a uses-feature"
    states = [feature.boolean("required") for feature in features]
    if any(state is True for state in states):
        return "required=TRUE"
    if all(state is False for state in states):
        return "required=false"
    raw = features[0].attributes.get("required")
    if raw is None:
        return "required is not set (the default is true)"
    # Show what was there, so the next run says how aapt2 prints it.
    return "not confirmed (could not read: %s)" % raw.replace("`", "'")[:80]


def optional_rows(roots):
    """Information rows: is each feature's `required` attribute false? Never a failure."""
    elements = [node for root in roots for node in root.walk()]
    rows = []
    for label, name in OPTIONAL_FEATURES.items():
        features = [
            node
            for node in elements
            if node.tag == "uses-feature" and node.text("name") == name
        ]
        rows.append((label, "info", optional_state(features)))
    return rows


def launchers(roots):
    """Class names of the activities with a MAIN action and a LAUNCHER category."""
    names = []
    for root in roots:
        for node in root.walk():
            if node.tag not in ("activity", "activity-alias"):
                continue
            filters = [child for child in node.children if child.tag == "intent-filter"]
            for intent in filters:
                values = {
                    child.text("name")
                    for child in intent.children
                    if child.tag in ("action", "category")
                }
                if {
                    "android.intent.action.MAIN",
                    "android.intent.category.LAUNCHER",
                } <= values:
                    names.append(node.text("name") or "(unnamed)")
    return names


def activities(text):
    """The Unity activity classes mentioned in the dump, in order of first appearance."""
    return [name for name in ACTIVITIES if name in text]


def report(rows, ok, found_activities, found_launchers):
    lines = [
        "## Telewheel Quest manifest: " + ("OK" if ok else "PROBLEMS"),
        "",
        "| Check | Result | Detail |",
        "| --- | --- | --- |",
    ]
    for label, passed, detail in rows:
        # A check is True or False; an information row carries its own word.
        result = passed if isinstance(passed, str) else ("pass" if passed else "FAIL")
        lines.append("| %s | %s | `%s` |" % (label, result, detail))
    lines.append("")
    lines.append("Launcher activity: " + (", ".join(found_launchers) or "not found"))
    lines.append("Activities mentioned: " + (", ".join(found_activities) or "none"))
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n", maxsplit=1)[0])
    parser.add_argument(
        "dump", help="text from: aapt2 dump xmltree --file AndroidManifest.xml APK"
    )
    args = parser.parse_args(argv)
    try:
        with open(args.dump, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
    except OSError as error:
        print("Could not read the manifest dump: %s" % error, file=sys.stderr)
        return 1
    if not text.strip():
        print("The manifest dump is empty.", file=sys.stderr)
        return 1
    rows, ok = check(text)
    roots = parse(text)
    print(report(rows + optional_rows(roots), ok, activities(text), launchers(roots)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
