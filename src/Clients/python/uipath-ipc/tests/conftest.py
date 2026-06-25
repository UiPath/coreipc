"""Top-level pytest configuration.

Integration tests (marked with ``@pytest.mark.integration``) run by
default. Pass ``--no-integration`` to skip them and keep the loop fast.
"""

from __future__ import annotations

import pytest


def pytest_addoption(parser: pytest.Parser) -> None:
    parser.addoption(
        "--no-integration",
        action="store_true",
        default=False,
        help="Skip integration tests that talk to the .NET IpcSample.ConsoleServer.",
    )


def pytest_collection_modifyitems(
    config: pytest.Config, items: list[pytest.Item]
) -> None:
    if not config.getoption("--no-integration"):
        return
    skip_integration = pytest.mark.skip(reason="--no-integration")
    for item in items:
        if "integration" in item.keywords:
            item.add_marker(skip_integration)
