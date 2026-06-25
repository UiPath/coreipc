"""Smoke tests — package imports and exposes the documented public surface."""

import uipath_ipc


def test_package_imports() -> None:
    assert uipath_ipc.__doc__ is not None


def test_public_surface() -> None:
    expected = {
        "ClientTransport",
        "IpcClient",
        "IpcConnection",
        "NamedPipeClientTransport",
        "RemoteException",
        "TcpClientTransport",
    }
    assert expected <= set(uipath_ipc.__all__)
    for name in expected:
        assert getattr(uipath_ipc, name) is not None


def test_py_typed_marker_present() -> None:
    """PEP 561: a py.typed file signals to type checkers that the package
    has inline type information."""
    from importlib import resources

    pkg = resources.files(uipath_ipc)
    assert (pkg / "py.typed").is_file()
