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

"""Generate and check Unity .meta files for files written outside the editor.

Unity needs a .meta file with a unique GUID next to every asset under Assets/.
Code written without the editor has none, so this script creates them with
fresh GUIDs and can verify that every GUID in the project is unique.

    telewheel_meta.py generate Assets/Scripts/Telewheel Assets/Tests/EditMode
    telewheel_meta.py check

File types it does not know are reported and left alone; Unity creates their
.meta file the first time it imports them.
"""

import argparse
import os
import re
import sys
import uuid

_FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
{footer}"""

_SCRIPT_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: {order}
  icon: {{instanceID: 0}}
{footer}"""

_ASMDEF_META = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
{footer}"""

_TEXT_META = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
{footer}"""

_FONT_META = """fileFormatVersion: 2
guid: {guid}
TrueTypeFontImporter:
  externalObjects: {{}}
  serializedVersion: 4
  fontSize: 16
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontName: {font_name}
  fontNames:
  - {font_name}
  fallbackFontReferences: []
  customCharacters:{space}
  fontRenderingMode: 0
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
{footer}"""

_TEXT_EXTENSIONS = (".json", ".txt", ".md")

# Unity writes a trailing space after these empty keys; spelled out here so no source line
# ends in whitespace.
_FOOTER = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
_GUID_RE = re.compile(r"^guid: ([0-9a-f]{32})\s*$", re.MULTILINE)


def _read_guid(meta_path):
    """Return the GUID stored in a .meta file, or None if it has none."""
    with open(meta_path, encoding="utf-8-sig") as handle:
        match = _GUID_RE.search(handle.read())
    return match.group(1) if match else None


def _all_meta_files(project_root):
    """Yield the path of every .meta file under Assets/ and Packages/."""
    for top in ("Assets", "Packages"):
        for dirpath, _dirnames, filenames in os.walk(os.path.join(project_root, top)):
            for name in filenames:
                if name.endswith(".meta"):
                    yield os.path.join(dirpath, name)


def collect_guids(project_root):
    """Map each GUID in the project to the list of .meta files that use it."""
    guids = {}
    for meta_path in _all_meta_files(project_root):
        guid = _read_guid(meta_path)
        if guid:
            guids.setdefault(guid, []).append(meta_path)
    return guids


def _template_for(path, is_dir):
    """Pick the .meta template for a path, or None if the type is unknown."""
    if is_dir:
        return _FOLDER_META
    if path.endswith(".cs"):
        return _SCRIPT_META
    if path.endswith(".asmdef"):
        return _ASMDEF_META
    if path.endswith(_TEXT_EXTENSIONS):
        return _TEXT_META
    if path.endswith(".ttf"):
        return _FONT_META
    return None


def _new_guid(used):
    """Return a GUID that is not in `used`, and add it to `used`."""
    while True:
        guid = uuid.uuid4().hex
        if guid not in used:
            used[guid] = []
            return guid


def _paths_under(top):
    """Return `top` and everything below it (files and folders)."""
    paths = [top]
    for dirpath, dirnames, filenames in os.walk(top):
        paths.extend(os.path.join(dirpath, name) for name in dirnames)
        paths.extend(os.path.join(dirpath, name) for name in filenames)
    return sorted(paths)


def _meta_content(path, template, used):
    """Fill a .meta template for `path` with a fresh GUID."""
    font_name = os.path.splitext(os.path.basename(path))[0].replace("-", " ")
    return template.format(
        guid=_new_guid(used),
        order=0,
        font_name=font_name,
        footer=_FOOTER,
        space=" ",
    )


def generate(project_root, roots):
    """Create missing .meta files under `roots`. Returns (created, skipped)."""
    used = collect_guids(project_root)
    created = []
    skipped = []
    for root in roots:
        for path in _paths_under(os.path.join(project_root, root)):
            if path.endswith(".meta") or os.path.exists(path + ".meta"):
                continue
            template = _template_for(path, os.path.isdir(path))
            if template is None:
                skipped.append(path)
                continue
            with open(path + ".meta", "w", encoding="utf-8", newline="\n") as handle:
                handle.write(_meta_content(path, template, used))
            created.append(path + ".meta")
    return created, skipped


def check(project_root):
    """Return a list of problems: duplicate GUIDs and unreadable .meta files."""
    problems = []
    for guid, metas in collect_guids(project_root).items():
        if len(metas) > 1:
            problems.append("duplicate guid %s: %s" % (guid, ", ".join(sorted(metas))))
    for meta_path in _all_meta_files(project_root):
        if _read_guid(meta_path) is None:
            problems.append("no guid in %s" % meta_path)
    return problems


def main(argv=None):
    """Command line entry point."""
    parser = argparse.ArgumentParser(description=__doc__.split("\n", maxsplit=1)[0])
    parser.add_argument(
        "--project-root",
        default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."),
    )
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate", help="create missing .meta files")
    gen.add_argument("roots", nargs="+", help="folders relative to the project root")
    sub.add_parser("check", help="verify every GUID is unique")
    args = parser.parse_args(argv)
    project_root = os.path.abspath(args.project_root)

    if args.command == "generate":
        created, skipped = generate(project_root, args.roots)
        for path in created:
            print("created", os.path.relpath(path, project_root))
        for path in skipped:
            print("skipped (unknown type)", os.path.relpath(path, project_root))
        return 0

    problems = check(project_root)
    for problem in problems:
        print(problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
