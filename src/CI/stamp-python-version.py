"""Rewrite the Python package's pyproject.toml version line to match the
pipeline's $(FullVersion).

Converts the .NET-flavoured version produced by azp-initialization.yaml
to a PEP 440-valid string for Python packaging:

  "2.5.1"                   ->  "2.5.1"                   (release)
  "2.5.1-20260528-08"       ->  "2.5.1+20260528.08"       (local version)

The wheel built right after this step will carry the new version.

Usage:
    python stamp-python-version.py <full-version> <pyproject-toml-path>
"""

from __future__ import annotations

import re
import sys
from pathlib import Path


def to_pep440(full_version: str) -> str:
    if "-" not in full_version:
        return full_version
    base, rest = full_version.split("-", 1)
    return f"{base}+{rest.replace('-', '.')}"


def main() -> int:
    if len(sys.argv) != 3:
        print(
            "usage: stamp-python-version.py <full-version> <pyproject-toml-path>",
            file=sys.stderr,
        )
        return 2

    full_version, pyproject_path = sys.argv[1], Path(sys.argv[2])
    pep440 = to_pep440(full_version)

    print(f"Stamping {pep440!r} (from {full_version!r}) into {pyproject_path}")

    content = pyproject_path.read_text(encoding="utf-8")
    new_content, count = re.subn(
        r'^version\s*=\s*"[^"]+"',
        f'version = "{pep440}"',
        content,
        count=1,
        flags=re.MULTILINE,
    )
    if count == 0:
        print(
            f"ERROR: no version line found in {pyproject_path}", file=sys.stderr
        )
        return 1
    pyproject_path.write_text(new_content, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
