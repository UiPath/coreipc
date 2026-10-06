"""Rewrite the Python package's pyproject.toml version line to match the
pipeline's $(FullVersion).

Converts the .NET-flavoured version produced by azp-initialization.yaml to a
PEP 440-valid string for Python packaging:

  "2.5.3"               ->  "2.5.3"                (release, unchanged)
  "2.5.3-20260724-01"   ->  "2.5.3.dev2026072401"  (dev pre-release)

A build carrying a SemVer suffix (a non-release CI build) becomes a PEP 440
DEV release — a genuine pre-release that PyPI accepts and that pip/uv skip by
default — rather than a local ("+") segment. A local segment is wrong here on
two counts: PyPI rejects it on upload, and pip/uv treat it as a FINAL release
(so a CI build would masquerade as the real release, and land on public PyPI
stripped to a clean release). The suffix's digits form the monotonic .devN
number. NOTE: .devN sorts BEFORE its base release, so bump the base version in
the csproj right after cutting a release, or later dev builds look "older".

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
    # The pipeline's FullVersion suffix is the build number (digits), e.g.
    # "20260724-01" -> ".dev2026072401". Strip any non-digits and drop leading
    # zeros via int() so it is a valid, monotonic PEP 440 dev number.
    digits = re.sub(r"\D", "", rest) or "0"
    return f"{base}.dev{int(digits)}"


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
