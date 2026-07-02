"""OWLv2 open-vocabulary zero-shot detector."""
from __future__ import annotations

import os

import numpy as np

from .base import BaseDetector, Detection, clamp_box, select_device


class Owlv2Detector(BaseDetector):
    name = "owlv2"

    def __init__(self) -> None:
        import torch
        from transformers import AutoModelForZeroShotObjectDetection, AutoProcessor

        self.torch = torch
        self.device = select_device()
        self.model_id = os.getenv("QVS_OWLV2_MODEL", "google/owlv2-base-patch16-ensemble")
        self.queries = [q.strip() for q in os.getenv("QVS_OWLV2_QUERIES", "glasses,scissors,phone").split(",") if q.strip()]
        self.conf = float(os.getenv("QVS_OWLV2_CONF", "0.30"))

        print(f"[OWLv2] Loading {self.model_id} on {self.device} queries={self.queries}")
        self.processor = AutoProcessor.from_pretrained(self.model_id)
        self.model = AutoModelForZeroShotObjectDetection.from_pretrained(self.model_id).to(self.device)
        self.model.eval()
        print("[OWLv2] Model loaded")

    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        import cv2
        from PIL import Image

        h, w = img_bgr.shape[:2]
        pil = Image.fromarray(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB))
        inputs = self.processor(images=pil, text=[self.queries], return_tensors="pt").to(self.device)
        with self.torch.inference_mode():
            outputs = self.model(**inputs)
        results = self.processor.post_process_object_detection(
            outputs=outputs, target_sizes=[(h, w)], threshold=self.conf
        )[0]

        detections: list[Detection] = []
        for box, score, lab in zip(results.get("boxes", []), results.get("scores", []), results.get("labels", [])):
            x1, y1, x2, y2 = clamp_box(*[float(v) for v in box.tolist()], w, h)
            if x2 <= x1 or y2 <= y1:
                continue
            idx = int(lab)
            label = self.queries[idx] if 0 <= idx < len(self.queries) else str(idx)
            detections.append({"label": label, "conf": float(score), "bbox": [float(x1), float(y1), float(x2), float(y2)]})
        return detections
