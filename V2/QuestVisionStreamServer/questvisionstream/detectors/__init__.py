"""Detector registry — the single source of truth for detector names.

The server CLI derives its ``--detector`` choices from ``DETECTOR_NAMES`` so the
name→implementation mapping can never drift (the original had ``owl2`` in the CLI
but ``owlv2`` in the registry, and ``grounding_dino`` was unreachable).

Each entry is a zero-arg factory that lazily imports and constructs the detector,
so heavy model dependencies are only imported for the detector actually selected.
"""
from __future__ import annotations

from typing import Callable

from .base import BaseDetector, Detection, draw_detections

__all__ = [
    "BaseDetector",
    "Detection",
    "draw_detections",
    "DETECTOR_NAMES",
    "get_detector",
]


def _make_yolo() -> BaseDetector:
    from .yolo_detector import YoloDetector

    return YoloDetector()


def _make_florence2() -> BaseDetector:
    from .florence2_detector import Florence2Detector

    return Florence2Detector()


def _make_owlv2() -> BaseDetector:
    from .owlv2_detector import Owlv2Detector

    return Owlv2Detector()


def _make_grounding_dino() -> BaseDetector:
    from .grounding_dino_detector import GroundingDinoDetector

    return GroundingDinoDetector()


def _make_body() -> BaseDetector:
    from .body_tracker import BodyTracker

    return BodyTracker()


_REGISTRY: dict[str, Callable[[], BaseDetector]] = {
    "yolo": _make_yolo,
    "florence2": _make_florence2,
    "owlv2": _make_owlv2,
    "grounding_dino": _make_grounding_dino,
    "body": _make_body,
}

#: Valid detector names — use this for CLI choices so they always match.
DETECTOR_NAMES: list[str] = list(_REGISTRY.keys())


def get_detector(name: str) -> BaseDetector:
    """Construct the named detector (loading its model). Raises on unknown name."""
    factory = _REGISTRY.get(name)
    if factory is None:
        raise ValueError(f"Unknown detector '{name}'. Available: {', '.join(DETECTOR_NAMES)}")
    return factory()
