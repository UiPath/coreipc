"""Smoke tests — does the package even import?"""

import uipath_ipc


def test_package_imports() -> None:
    assert uipath_ipc.__doc__ is not None
