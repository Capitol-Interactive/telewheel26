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

The checks are plain substring searches on purpose: they do not depend on how aapt2 lays the
tree out, only on the strings being present in the manifest.
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

# Entries that must be optional, so the app still installs without them. aapt2 prints a false boolean as
# "(type 0x12)0x0". Only reported: the dump's layout is not something to fail a build over.
OPTIONAL_FEATURES = {
    "Passthrough is optional": "com.oculus.feature.PASSTHROUGH",
    "Hand tracking is optional": "oculus.software.handtracking",
}

# Shown for information, not checked: which activity launches the app.
ACTIVITIES = ("UnityPlayerActivity", "UnityPlayerGameActivity")


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


def optional_rows(text):
    """Information rows: is each feature's `required` attribute false? Never a failure."""
    rows = []
    blocks = re.split(r"(?m)^\s*(?=E: )", text)
    for label, name in OPTIONAL_FEATURES.items():
        state = "not confirmed (could not read the attribute)"
        for block in blocks:
            if (
                name not in block
                or "uses-feature" not in block.split("\n", maxsplit=1)[0]
            ):
                continue
            match = re.search(
                r"required\(0x[0-9a-fA-F]+\)=\(type 0x12\)(0x[0-9a-fA-F]+)", block
            )
            if match:
                state = (
                    "required=false"
                    if int(match.group(1), 16) == 0
                    else "required=TRUE"
                )
            break
        rows.append((label, "info", state))
    return rows


def activities(text):
    """The launcher activity classes mentioned in the dump, in order of first appearance."""
    return [name for name in ACTIVITIES if name in text]


def report(rows, ok, found_activities):
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
    print(report(rows + optional_rows(text), ok, activities(text)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
