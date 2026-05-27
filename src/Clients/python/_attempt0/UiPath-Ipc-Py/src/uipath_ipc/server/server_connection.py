"""Per-connection state on the server side, mirroring ServerConnection.cs."""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from ..connection import Connection
from .dispatcher import Dispatcher
from .router import Router

logger = logging.getLogger(__name__)


class ServerConnection:
    """Manages a single client connection on the server side.

    Creates a Connection and Dispatcher, then listens for messages.
    """

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        router: Router,
        request_timeout: float | None,
        debug_name: str = "",
        max_message_size: int = 2 * 1024 * 1024,
    ) -> None:
        self._connection = Connection(
            reader, writer, debug_name=debug_name, max_message_size=max_message_size
        )
        self._dispatcher = Dispatcher(router, request_timeout, self._connection)

    async def listen(self) -> None:
        """Listen for messages until the connection closes."""
        try:
            await self._connection.listen()
        except Exception as ex:
            logger.debug("ServerConnection %s closed: %s", self._connection.debug_name, ex)
