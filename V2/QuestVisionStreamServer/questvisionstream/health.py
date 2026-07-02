"""Minimal dependency-free HTTP health endpoint.

Runs a tiny asyncio server so orchestrators (Docker healthcheck, HF Spaces,
load balancers) can probe liveness without pulling in a web framework.
"""
from __future__ import annotations

import asyncio
import json
from typing import Callable

from .config import ServerConfig

StatusFn = Callable[[], dict]

# A probe sends its whole request immediately; anything slower is a stuck or
# hostile client and gets disconnected (slowloris guard).
HEADER_TIMEOUT_S = 5.0
MAX_HEADER_LINES = 100


async def start_health_server(config: ServerConfig, get_status: StatusFn) -> asyncio.AbstractServer:
    async def read_request(reader: asyncio.StreamReader) -> None:
        await reader.readline()  # request line
        for _ in range(MAX_HEADER_LINES):  # drain headers, bounded
            line = await reader.readline()
            if line in (b"\r\n", b"\n", b""):
                return

    async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            await asyncio.wait_for(read_request(reader), timeout=HEADER_TIMEOUT_S)
            body = json.dumps(get_status()).encode()
            writer.write(
                b"HTTP/1.1 200 OK\r\n"
                b"Content-Type: application/json\r\n"
                b"Connection: close\r\n"
                b"Content-Length: " + str(len(body)).encode() + b"\r\n\r\n" + body
            )
            await writer.drain()
        except asyncio.TimeoutError:
            print("[Health] Dropping slow client (header timeout)")
        except Exception as exc:
            print(f"[Health] Request error: {exc}")
        finally:
            try:
                writer.close()
            except Exception:
                pass

    server = await asyncio.start_server(handle, config.host, config.health_port)
    print(f"[Health] http://{config.host}:{config.health_port}/")
    return server
