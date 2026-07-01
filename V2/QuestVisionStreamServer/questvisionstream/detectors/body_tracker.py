"""MediaPipe Pose body tracker.

Unlike the original — which returned an ndarray image and mistakenly pushed it
over the detections data channel — this returns a proper detection: a bounding
box around the visible pose landmarks, so it conforms to the same wire format as
every other detector.
"""
from __future__ import annotations

import numpy as np

from .base import BaseDetector, Detection, clamp_box


class BodyTracker(BaseDetector):
    name = "body"

    def __init__(self) -> None:
        import mediapipe as mp

        self.mp_pose = mp.solutions.pose
        self.pose = self.mp_pose.Pose(
            static_image_mode=False,
            model_complexity=1,
            enable_segmentation=False,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        print("[Body] MediaPipe Pose ready")

    def detect(self, img_bgr: np.ndarray) -> list[Detection]:
        import cv2

        h, w = img_bgr.shape[:2]
        results = self.pose.process(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB))
        if not results.pose_landmarks:
            return []

        xs: list[float] = []
        ys: list[float] = []
        vis: list[float] = []
        for lm in results.pose_landmarks.landmark:
            xs.append(lm.x * w)
            ys.append(lm.y * h)
            vis.append(getattr(lm, "visibility", 1.0))
        if not xs:
            return []

        x1, y1, x2, y2 = clamp_box(min(xs), min(ys), max(xs), max(ys), w, h)
        if x2 <= x1 or y2 <= y1:
            return []
        conf = float(sum(vis) / len(vis)) if vis else 1.0
        return [{"label": "body", "conf": conf, "bbox": [float(x1), float(y1), float(x2), float(y2)]}]

    def close(self) -> None:
        self.pose.close()
