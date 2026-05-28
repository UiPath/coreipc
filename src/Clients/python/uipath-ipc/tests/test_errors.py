"""Unit tests for the RemoteException + from_error mapping."""

from __future__ import annotations

from uipath_ipc import RemoteException
from uipath_ipc.wire import Error


def test_simple_error_maps_to_remote_exception() -> None:
    err = Error(message="boom")
    exc = RemoteException.from_error(err)
    assert exc.message == "boom"
    assert exc.type_name is None
    assert exc.stack_trace is None
    assert exc.inner is None
    assert str(exc) == "boom"


def test_error_with_type_name_renders_in_str() -> None:
    err = Error(message="boom", type_name="System.InvalidOperationException")
    exc = RemoteException.from_error(err)
    assert exc.type_name == "System.InvalidOperationException"
    assert str(exc) == "[System.InvalidOperationException] boom"


def test_error_with_stack_trace_preserved() -> None:
    err = Error(message="boom", stack_trace="at Foo.Bar()")
    exc = RemoteException.from_error(err)
    assert exc.stack_trace == "at Foo.Bar()"


def test_nested_error_chain() -> None:
    leaf = Error(message="inner", type_name="System.NullReferenceException")
    mid = Error(message="middle", type_name="System.InvalidOperationException", inner_error=leaf)
    outer = Error(message="outer", type_name="System.AggregateException", inner_error=mid)

    exc = RemoteException.from_error(outer)

    # outer
    assert exc.message == "outer"
    assert exc.type_name == "System.AggregateException"
    assert isinstance(exc.inner, RemoteException)
    # middle
    assert exc.inner.message == "middle"
    assert exc.inner.type_name == "System.InvalidOperationException"
    assert isinstance(exc.inner.inner, RemoteException)
    # leaf
    assert exc.inner.inner.message == "inner"
    assert exc.inner.inner.type_name == "System.NullReferenceException"
    assert exc.inner.inner.inner is None


def test_cause_chain_matches_inner_chain() -> None:
    """Python's `__cause__` is set so `raise X from Y` semantics work in tracebacks."""
    leaf = Error(message="inner")
    outer = Error(message="outer", inner_error=leaf)
    exc = RemoteException.from_error(outer)

    assert exc.__cause__ is exc.inner


def test_no_inner_means_no_cause() -> None:
    exc = RemoteException.from_error(Error(message="boom"))
    assert exc.__cause__ is None
