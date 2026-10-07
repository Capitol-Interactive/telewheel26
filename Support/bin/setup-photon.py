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

"""Fetch the Photon Fusion + Voice SDK into Assets/Photon, the way CI does.

The SDK is not part of this repository (Assets/Photon is git-ignored); online play needs it on
disk before Unity opens the project. This copies it from the icosa-mirror repository, the same
branch the CI build uses, without overwriting anything already there.

    setup-photon.py fetch      download the SDK into Assets/Photon (add --force to replace it)
    setup-photon.py status     show what is installed and whether the Photon app ids are set
    setup-photon.py defines    print the scripting define symbols online play needs

After fetching, open the project in Unity and run Telewheel > Online > Set Up Photon, which adds
the define symbols. The Photon app ids go in Assets/Secrets.asset (see CLAUDE.md), never in git.
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile

DEFAULT_REPO = "https://github.com/icosa-mirror/photon-fusion"
DEFAULT_BRANCH = "fusion-2.1.2-voice-2.63-realtime5-unity6"

# The same symbols the CI build adds to csc.rsp when it has the Photon SDK.
DEFINES = [
    "MP_PHOTON",
    "CROSS_PLATFORM_INPUT",
    "PHOTON_VOICE_DEFINED",
    "FUSION_WEAVER",
    "FUSION2",
    "FUSION_2_1_OR_NEWER",
]
ANDROID_EXTRA_DEFINES = ["MOBILE_INPUT"]

# Only these paths may be written, whatever the downloaded repository contains.
ALLOWED_TOP_LEVEL = ("Assets/Photon", "Assets/Photon.meta")

# Unity's SecretsConfig.Service values.
SERVICE_PHOTON_FUSION = 5
SERVICE_PHOTON_VOICE = 6


def default_project_root():
    """The Unity project root: two levels above Support/bin."""
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


def sdk_dir(root):
    return os.path.join(root, "Assets", "Photon")


def is_allowed(relative_path):
    """True if a path from the downloaded repository may be copied into the project."""
    normal = relative_path.replace("\\", "/")
    if normal.startswith("/") or ".." in normal.split("/"):
        return False
    return any(
        normal == top or normal.startswith(top + "/") for top in ALLOWED_TOP_LEVEL
    )


def read_versions(root):
    """Return (fusion, realtime) version strings, or None for any that cannot be read."""
    fusion = None
    realtime = None
    build_info = os.path.join(sdk_dir(root), "Fusion", "build_info.txt")
    if os.path.isfile(build_info):
        with open(build_info, encoding="utf-8") as handle:
            match = re.search(r"build:\s*(\S+)", handle.read())
        fusion = match.group(1) if match else None
    realtime_file = os.path.join(
        sdk_dir(root), "PhotonRealtime", "Code", "RealtimeClientVersion.cs"
    )
    if os.path.isfile(realtime_file):
        with open(realtime_file, encoding="utf-8") as handle:
            match = re.search(r'Version\s*=\s*"([^"]+)"', handle.read())
        realtime = match.group(1) if match else None
    return fusion, realtime


def sdk_installed(root):
    """True if the Fusion runtime assembly definition is in place."""
    return os.path.isfile(
        os.path.join(sdk_dir(root), "Fusion", "Runtime", "Fusion.Unity.asmdef")
    )


def secrets_status(root):
    """Return {service number: has a non-empty ClientId} from Assets/Secrets.asset, or None if it is missing."""
    path = os.path.join(root, "Assets", "Secrets.asset")
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    found = {}
    # Unity writes each entry as "- Service: N" followed by indented fields.
    for block in re.split(r"\n\s*- Service:", "\n" + text)[1:]:
        number = re.match(r"\s*(\d+)", block)
        client = re.search(r"ClientId:[ \t]*(\S*)", block)
        if number:
            found[int(number.group(1))] = bool(client and client.group(1))
    return found


def copy_allowed(source, root, force):
    """Copy the allowed paths from a downloaded repository into the project. Returns (copied, skipped)."""
    if force:
        for top in ALLOWED_TOP_LEVEL:
            target = os.path.join(root, *top.split("/"))
            if os.path.isdir(target):
                shutil.rmtree(target)
            elif os.path.isfile(target):
                os.remove(target)
    copied = 0
    skipped = 0
    for folder, dirnames, filenames in os.walk(source):
        dirnames[:] = [name for name in dirnames if name != ".git"]
        for name in filenames:
            full = os.path.join(folder, name)
            relative = os.path.relpath(full, source).replace(os.sep, "/")
            if os.path.islink(full) or not is_allowed(relative):
                skipped += 1
                continue
            destination = os.path.join(root, *relative.split("/"))
            if os.path.exists(destination):
                skipped += 1
                continue
            os.makedirs(os.path.dirname(destination), exist_ok=True)
            shutil.copy2(full, destination)
            copied += 1
    return copied, skipped


def clone(repo, branch, destination):
    """Shallow-clone one branch of the repository. Returns True on success."""
    command = [
        "git",
        "clone",
        "--quiet",
        "--depth",
        "1",
        "--branch",
        branch,
        repo,
        destination,
    ]
    print("Downloading %s (%s)..." % (repo, branch))
    try:
        result = subprocess.run(command, check=False)
    except FileNotFoundError:
        print(
            "git was not found. Install git, or use --source with a folder you downloaded."
        )
        return False
    return result.returncode == 0


def command_fetch(args):
    root = os.path.abspath(args.project_root)
    if not os.path.isdir(os.path.join(root, "Assets")):
        print("%s does not look like the Unity project (no Assets folder)." % root)
        return 1
    if sdk_installed(root) and not args.force:
        print("The Photon SDK is already in Assets/Photon. Use --force to replace it.")
        return command_status(args)
    with tempfile.TemporaryDirectory(prefix="photon-sdk-") as scratch:
        source = args.source
        if source is None:
            source = os.path.join(scratch, "mirror")
            if not clone(args.repo, args.branch, source):
                print("Could not download the SDK. Check your network and git access.")
                return 1
        copied, skipped = copy_allowed(source, root, args.force)
    print("Copied %d files (%d already there or not allowed)." % (copied, skipped))
    if not sdk_installed(root):
        print(
            "The SDK is not complete: Assets/Photon/Fusion/Runtime/Fusion.Unity.asmdef is missing."
        )
        return 1
    return command_status(args)


def command_status(args):
    root = os.path.abspath(args.project_root)
    installed = sdk_installed(root)
    fusion, realtime = read_versions(root)
    print(
        "Photon SDK in Assets/Photon: %s"
        % ("yes" if installed else "NO - run: setup-photon.py fetch")
    )
    if installed:
        print("  Fusion version:   %s" % (fusion or "unknown"))
        print("  Realtime version: %s" % (realtime or "unknown"))
    secrets = secrets_status(root)
    if secrets is None:
        print(
            "Assets/Secrets.asset: missing - create it (see CLAUDE.md, 'Photon app ids')"
        )
    else:
        print("Assets/Secrets.asset: found")
        print(
            "  Fusion app id: %s"
            % ("set" if secrets.get(SERVICE_PHOTON_FUSION) else "MISSING")
        )
        print(
            "  Voice app id:  %s"
            % ("set" if secrets.get(SERVICE_PHOTON_VOICE) else "MISSING")
        )
    print(
        "Next: in Unity run Telewheel > Online > Set Up Photon to add the define symbols."
    )
    return 0 if installed else 1


def command_defines(_args):
    print(" ".join(DEFINES))
    print("Android adds: %s" % " ".join(ANDROID_EXTRA_DEFINES))
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n", maxsplit=1)[0])
    parser.add_argument("--project-root", default=default_project_root())
    parser.add_argument("--repo", default=DEFAULT_REPO)
    parser.add_argument("--branch", default=DEFAULT_BRANCH)
    parser.add_argument(
        "--source", help="use this folder instead of downloading (for offline use)"
    )
    parser.add_argument(
        "--force", action="store_true", help="replace an existing Assets/Photon"
    )
    parser.add_argument(
        "command", nargs="?", default="fetch", choices=["fetch", "status", "defines"]
    )
    args = parser.parse_args(argv)
    handlers = {
        "fetch": command_fetch,
        "status": command_status,
        "defines": command_defines,
    }
    return handlers[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
