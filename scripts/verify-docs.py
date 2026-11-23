#!/usr/bin/env python3
"""Validate required active documentation and its relative Markdown links."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    "README.md",
    "ROADMAP.md",
    "PLAN.md",
    "CONTRIBUTING.md",
    "CHANGELOG.md",
    "SECURITY.md",
    "CODE_OF_CONDUCT.md",
    "THIRD_PARTY_NOTICES.md",
    "docs/README.md",
    "docs/architecture/ARCHITECTURE.md",
    "docs/architecture/DATA_LIFECYCLE.md",
    "docs/development/SETUP.md",
    "docs/development/TESTING.md",
    "docs/development/CODE_STYLE.md",
    "docs/development/ADDING_CONTENT.md",
    "docs/development/RELEASING.md",
    "docs/REPOSITORY_PROFILE.md",
]
LINK_PATTERN = re.compile(r"!?\[[^\]]*\]\(\s*(?:<([^>]+)>|([^\s)]+))")


def main() -> int:
    errors: list[str] = []

    for relative in REQUIRED:
        if not (ROOT / relative).is_file():
            errors.append(f"required documentation file is missing: {relative}")

    for relative in REQUIRED:
        source = ROOT / relative
        if not source.is_file():
            continue

        content = source.read_text(encoding="utf-8")
        for match in LINK_PATTERN.finditer(content):
            raw_target = match.group(1) or match.group(2) or ""
            target = unquote(raw_target.split("#", 1)[0].split("?", 1)[0])
            if not target or "://" in target or target.startswith(("mailto:", "data:")):
                continue

            resolved = (source.parent / target).resolve()
            try:
                resolved.relative_to(ROOT.resolve())
            except ValueError:
                errors.append(f"{relative}: link escapes the repository: {raw_target}")
                continue

            if not resolved.exists():
                line = content.count("\n", 0, match.start()) + 1
                errors.append(f"{relative}:{line}: missing relative link target: {raw_target}")

    if errors:
        print("Documentation validation failed:")
        for error in errors:
            print(f"  - {error}")
        return 1

    print(f"Documentation validation passed ({len(REQUIRED)} required files checked).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
