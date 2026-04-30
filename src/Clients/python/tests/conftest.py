import os

import pytest


@pytest.fixture
def pipe_name(request):
    """Generate a unique pipe name per test, short enough to remain human-readable."""
    name = f"coreipc-test-{os.getpid()}-{abs(hash(request.node.nodeid)) & 0xFFFFFF:x}"
    return name
