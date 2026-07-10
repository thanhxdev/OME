#!/usr/bin/env python3
"""Generate version header from git tags."""

import subprocess
import sys
from datetime import datetime


def get_git_version():
    try:
        version = subprocess.check_output(
            ["git", "describe", "--tags", "--always", "--dirty"],
            stderr=subprocess.DEVNULL
        ).decode().strip()
        return version
    except (subprocess.CalledProcessError, FileNotFoundError):
        return "unknown"


def get_git_commit():
    try:
        commit = subprocess.check_output(
            ["git", "rev-parse", "--short", "HEAD"],
            stderr=subprocess.DEVNULL
        ).decode().strip()
        return commit
    except (subprocess.CalledProcessError, FileNotFoundError):
        return "unknown"


def main():
    version = get_git_version()
    commit = get_git_commit()
    timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S UTC")

    print(f"Git Version: {version}")
    print(f"Git Commit:  {commit}")
    print(f"Timestamp:   {timestamp}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
