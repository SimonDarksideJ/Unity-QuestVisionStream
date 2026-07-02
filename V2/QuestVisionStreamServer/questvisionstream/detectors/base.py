"""Detector abstraction.

Detectors return **detections only** (no image, no drawing) so the hot path stays
headless-friendly — the previous version drew boxes on every frame even when
running without a display. Drawing for the optional debug window is done
separately in the video processor from the returned detections.
"""
from __future__ import annotations

import abc
from typing import TYPE_CHECKING, TypedDict

if TYPE_CHECKING:
    import numpy as np


class Detection(TypedDict):
    """Wire-format detection: bbox is ``[x1, y1, x2, y2]`` in frame pixels."""

    label: str
    conf: float
    bbox: list[float]


def select_device() -> str:
    """Pick the best available torch device (MPS → CUDA → CPU)."""
    try:
        import torch

        if getattr(torch.backends, "mps", None) and torch.backends.mps.is_available():
            return "mps"
        if torch.cuda.is_available():
            return "cuda"
    except Exception:
        pass
    return "cpu"


def resolve_half(requested: bool, device: str) -> bool:
    """FP16 inference is only reliable on CUDA — Ultralytics errors on CPU and
    is flaky on MPS. Refuse the request (with the caller logging why) elsewhere."""
    return requested and device == "cuda"


def clamp_box(x1: float, y1: float, x2: float, y2: float, w: int, h: int) -> tuple[int, int, int, int]:
    """Clamp a box to image bounds and return integer coordinates."""
    xi1 = int(max(0, min(round(x1), w - 1)))
    yi1 = int(max(0, min(round(y1), h - 1)))
    xi2 = int(max(0, min(round(x2), w - 1)))
    yi2 = int(max(0, min(round(y2), h - 1)))
    return xi1, yi1, xi2, yi2


class BaseDetector(abc.ABC):
    """Base class for all detectors. Load models in ``__init__`` (constructed lazily
    by the registry, so import cost is only paid for the selected detector)."""

    name: str = "base"

    @abc.abstractmethod
    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        """Run detection on a BGR image, returning zero or more detections."""
        raise NotImplementedError

    def close(self) -> None:  # pragma: no cover - optional cleanup hook
        """Release model resources. Overridden where needed."""


def draw_detections(img: np.ndarray, detections: list[Detection]) -> None:
    """Draw boxes + labels onto ``img`` in-place (debug display path only)."""
    import cv2

    for det in detections:
        x1, y1, x2, y2 = (int(v) for v in det["bbox"])
        cv2.rectangle(img, (x1, y1), (x2, y2), (0, 255, 0), 2)
        cv2.putText(
            img,
            f"{det['label']} {det['conf']:.2f}",
            (x1, max(0, y1 - 6)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (0, 255, 0),
            2,
            cv2.LINE_AA,
        )
