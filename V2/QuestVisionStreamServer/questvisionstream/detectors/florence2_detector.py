"""Florence-2 detector (Microsoft). Runs a caption-style `<OD>` task.

Note: Florence-2 does not perform well on MPS, so it prefers CUDA then CPU. Runs
every N frames (``QVS_FLORENCE_SKIP``), returning cached detections in between.
"""
from __future__ import annotations

import os
from typing import Any

import numpy as np

from .base import BaseDetector, Detection, clamp_box

DETECTION_PROMPT = "<OD>"


class Florence2Detector(BaseDetector):
    name = "florence2"

    def __init__(self) -> None:
        import torch
        from transformers import AutoModelForCausalLM, AutoProcessor

        self.torch = torch
        # Florence underperforms on MPS; prefer CUDA else CPU.
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.model_name = os.getenv("QVS_FLORENCE_MODEL", "microsoft/Florence-2-base")
        self.conf = float(os.getenv("QVS_FLORENCE_CONF", "0.30"))
        self.frame_skip = max(1, int(os.getenv("QVS_FLORENCE_SKIP", "2")))
        self.ignore = {"person", "car", "truck", "bus", "motorcycle", "bicycle"}

        print(f"[Florence2] Loading {self.model_name} on {self.device}")
        self.processor = AutoProcessor.from_pretrained(self.model_name, trust_remote_code=True)
        self.model = AutoModelForCausalLM.from_pretrained(
            self.model_name,
            trust_remote_code=True,
            torch_dtype=torch.float32,
            attn_implementation="eager",
        ).to(self.device)
        self.model.eval()
        self.model_dtype = next(self.model.parameters()).dtype
        self._frame_count = 0
        self._last: list[Detection] = []
        print("[Florence2] Model loaded")

    def _move(self, batch: dict[str, Any]) -> dict[str, Any]:
        out: dict[str, Any] = {}
        for k, v in batch.items():
            if isinstance(v, self.torch.Tensor):
                if k in ("input_ids", "attention_mask", "position_ids"):
                    out[k] = v.to(device=self.device, dtype=self.torch.long)
                elif k == "pixel_values":
                    out[k] = v.to(device=self.device, dtype=self.model_dtype)
                else:
                    out[k] = v.to(device=self.device, dtype=(self.model_dtype if v.is_floating_point() else v.dtype))
            else:
                out[k] = v
        return out

    @staticmethod
    def _parse(parsed: Any, task: str) -> tuple[list, list, list]:
        data = parsed.get(task) if isinstance(parsed, dict) else None
        if isinstance(parsed, dict) and data is None:
            data = parsed.get("OD") or parsed.get("<OD>")
        data = data or parsed
        if isinstance(data, dict) and ("bboxes" in data or "boxes" in data):
            bboxes = data.get("bboxes") or data.get("boxes") or []
            labels = data.get("labels") or [""] * len(bboxes)
            scores = data.get("scores") or [1.0] * len(bboxes)
            return bboxes, labels, scores
        return [], [], []

    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        from PIL import Image

        if img_bgr is None or img_bgr.size == 0:
            return []
        self._frame_count += 1
        if self._frame_count % self.frame_skip != 0:
            return self._last

        h, w = img_bgr.shape[:2]
        pil = Image.fromarray(img_bgr[..., ::-1])  # BGR -> RGB
        inputs = self._move(self.processor(text=DETECTION_PROMPT, images=pil, return_tensors="pt"))
        with self.torch.inference_mode():
            generated_ids = self.model.generate(
                **inputs, max_new_tokens=256, num_beams=1, do_sample=False, use_cache=False,
                return_dict_in_generate=False,
            )
        text = self.processor.batch_decode(generated_ids, skip_special_tokens=False)[0]
        parsed = self.processor.post_process_generation(text, task=DETECTION_PROMPT, image_size=(w, h))
        bboxes, labels, scores = self._parse(parsed, DETECTION_PROMPT)

        detections: list[Detection] = []
        for bbox, label, score in zip(bboxes, labels, scores):
            score = float(score) if score is not None else 1.0
            if score < self.conf or (label and label.lower() in self.ignore):
                continue
            x1, y1, x2, y2 = bbox
            if max(x2, y2) <= 1.5:  # normalized coords
                x1, x2, y1, y2 = x1 * w, x2 * w, y1 * h, y2 * h
            xi1, yi1, xi2, yi2 = clamp_box(x1, y1, x2, y2, w, h)
            if xi2 <= xi1 or yi2 <= yi1:
                continue
            detections.append({"label": label or "object", "conf": score, "bbox": [float(xi1), float(yi1), float(xi2), float(yi2)]})

        self._last = detections
        return detections
