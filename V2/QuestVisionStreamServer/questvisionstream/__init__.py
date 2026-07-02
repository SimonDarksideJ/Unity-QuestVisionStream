"""QuestVisionStream server package."""
from __future__ import annotations

from typing import Any

__version__ = "2.0.0"
__all__ = ["main", "__version__"]


def __getattr__(name: str) -> Any:
    # Lazy so `import questvisionstream` stays cheap (no aiortc/torch import cost)
    # until the server is actually run.
    if name == "main":
        from .server import main

        return main
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
