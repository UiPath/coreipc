"""Top-level pytest configuration.

Adds the ``--integration`` CLI flag which gates tests marked with
``@pytest.mark.integration``. Without the flag, those tests are skipped
so the default ``pytest`` run stays fast.
"""

from __future__ import annotations

import pytest


def pytest_addoption(parser: pytest.Parser) -> None:
    parser.addoption(
        "--integration",
        action="store_true",
        default=False,
        help="Run integration tests against the .NET IpcSample.ConsoleServer.",
    )


def pytest_collection_modifyitems(
    config: pytest.Config, items: list[pytest.Item]
) -> None:
    if config.getoption("--integration"):
        return
    skip_integration = pytest.mark.skip(reason="needs --integration")
    for item in items:
        if "integration" in item.keywords:
            item.add_marker(skip_integration)
