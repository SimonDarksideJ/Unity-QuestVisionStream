"""Grounding DINO open-vocabulary detector (prompt-driven)."""
from __future__ import annotations

import os

import numpy as np

from .base import BaseDetector, Detection, select_device


class GroundingDinoDetector(BaseDetector):
    name = "grounding_dino"

    def __init__(self) -> None:
        import torch
        from transformers import AutoModelForZeroShotObjectDetection, AutoProcessor

        self.torch = torch
        self.device = select_device()
        self.model_id = os.getenv("QVS_DINO_MODEL", "IDEA-Research/grounding-dino-tiny")
        self.prompt = os.getenv("QVS_DINO_PROMPT", "glasses")
        self.conf = float(os.getenv("QVS_DINO_CONF", "0.35"))
        self.text_thres = float(os.getenv("QVS_DINO_TEXT_THRES", "0.25"))

        print(f"[GroundingDINO] Loading {self.model_id} on {self.device} prompt='{self.prompt}'")
        self.processor = AutoProcessor.from_pretrained(self.model_id)
        self.model = AutoModelForZeroShotObjectDetection.from_pretrained(self.model_id).to(self.device)
        self.model.eval()
        print("[GroundingDINO] Model loaded")

    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        import cv2
        from PIL import Image

        h, w = img_bgr.shape[:2]
        pil = Image.fromarray(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB))
        inputs = self.processor(images=pil, text=[[self.prompt]], return_tensors="pt").to(self.device)
        with self.torch.inference_mode():
            outputs = self.model(**inputs)
        results = self.processor.post_process_grounded_object_detection(
            outputs=outputs,
            input_ids=inputs["input_ids"],
            threshold=self.conf,
            text_threshold=self.text_thres,
            target_sizes=[(h, w)],
        )[0]

        detections: list[Detection] = []
        for (x1, y1, x2, y2), label, score in zip(
            results.get("boxes", []), results.get("labels", []), results.get("scores", [])
        ):
            xi1, yi1, xi2, yi2 = (int(v.item() if hasattr(v, "item") else v) for v in (x1, y1, x2, y2))
            if xi2 <= xi1 or yi2 <= yi1:
                continue
            detections.append(
                {"label": str(label), "conf": float(score), "bbox": [float(xi1), float(yi1), float(xi2), float(yi2)]}
            )
        return detections
