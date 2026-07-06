"""YOLO detector (Ultralytics). Default detector."""
from __future__ import annotations

import os

import numpy as np

from .base import BaseDetector, Detection, resolve_half, select_device


def _env_ignore() -> set[str]:
    # Empty by default: ignoring `person` (and vehicles) silently drops the most
    # common thing in an indoor test — you'd see "nothing found" while YOLO was
    # in fact detecting you. Opt back in with QVS_YOLO_IGNORE="person,car,...".
    raw = os.getenv("QVS_YOLO_IGNORE", "")
    return {c.strip() for c in raw.split(",") if c.strip()}


class YoloDetector(BaseDetector):
    name = "yolo"

    def __init__(self) -> None:
        from ultralytics import YOLO

        self.device = select_device()
        # Bare name → Ultralytics auto-downloads on first use. Override with a
        # local path (e.g. models/yolo11n.pt) to pin a specific weights file.
        self.model_path = os.getenv("QVS_YOLO_MODEL", "yolo11n.pt")
        # 0.6 is a high bar for a low-res 640x480 passthrough frame — few boxes
        # clear it, so the stream reads as "nothing found". 0.35 surfaces real
        # objects while still filtering noise; raise via QVS_YOLO_CONF.
        self.conf = float(os.getenv("QVS_YOLO_CONF", "0.35"))
        # Latency levers (see DEPLOY docs): smaller imgsz + fp16 = faster.
        self.imgsz = int(os.getenv("QVS_YOLO_IMGSZ", "640"))
        half_requested = os.getenv("QVS_YOLO_HALF", "false").strip().lower() in {"1", "true", "yes", "on"}
        self.half = resolve_half(half_requested, self.device)
        if half_requested and not self.half:
            print(f"[YOLO] QVS_YOLO_HALF ignored: fp16 requires CUDA (device={self.device})")
        self.ignore = _env_ignore()

        print(f"[YOLO] Loading {self.model_path} on {self.device} (imgsz={self.imgsz}, half={self.half})")
        self.model = YOLO(self.model_path)
        print("[YOLO] Model loaded")

        # "Searching for" list, queried straight from the loaded weights so it
        # stays accurate if the model is swapped. Shows what's active vs ignored.
        names = list(self.model.names.values())
        active = sorted(n for n in names if n not in self.ignore)
        print(f"[YOLO] Searching for {len(active)} classes (conf≥{self.conf}): {', '.join(active)}")
        if self.ignore:
            ignored = sorted(n for n in names if n in self.ignore)
            print(f"[YOLO] Ignoring {len(ignored)}: {', '.join(ignored)}")

    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        results = self.model(
            img_bgr,
            conf=self.conf,
            device=self.device,
            imgsz=self.imgsz,
            half=self.half,
            verbose=False,
        )[0]

        detections: list[Detection] = []
        if results.boxes is None:
            return detections

        for box in results.boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            cls_id = int(box.cls[0]) if box.cls is not None else -1
            conf = float(box.conf[0]) if box.conf is not None else 0.0
            label = results.names[cls_id] if cls_id >= 0 else "object"
            if label in self.ignore:
                continue
            detections.append({"label": label, "conf": conf, "bbox": [float(x1), float(y1), float(x2), float(y2)]})
        return detections
