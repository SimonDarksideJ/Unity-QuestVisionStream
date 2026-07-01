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


async def start_health_server(config: ServerConfig, get_status: StatusFn) -> asyncio.AbstractServer:
    async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            await reader.readline()  # request line
            while True:  # drain headers
                line = await reader.readline()
                if line in (b"\r\n", b"\n", b""):
                    break
            body = json.dumps(get_status()).encode()
            writer.write(
                b"HTTP/1.1 200 OK\r\n"
                b"Content-Type: application/json\r\n"
                b"Connection: close\r\n"
                b"Content-Length: " + str(len(body)).encode() + b"\r\n\r\n" + body
            )
            await writer.drain()
        except Exception:
            pass
        finally:
            try:
                writer.close()
            except Exception:
                pass

    server = await asyncio.start_server(handle, config.host, config.health_port)
    print(f"[Health] http://{config.host}:{config.health_port}/")
    return server
