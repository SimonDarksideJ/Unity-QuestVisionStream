"""Detection log: a JSONL record per payload sent, faithful to the wire."""
from __future__ import annotations

import json
import os
import re

import numpy as np

from questvisionstream.detection_log import DetectionLog, create_detection_log
from questvisionstream.video_processor import VideoProcessor
from questvisionstream.webrtc_server import WebRTCServer

from conftest import make_config


def _make_server(config):
    return WebRTCServer(config, lambda: VideoProcessor(config, lambda img: [], lambda p: None))


def _payload(frame: int, dets):
    return {
        "type": "detections",
        "frame": frame,
        "pts": frame * 3000,
        "width": 480,
        "height": 480,
        "detections": dets,
    }


def _read(path):
    with open(path, encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


def test_config_default_off_and_env_override(tmp_path):
    assert make_config().detection_log == ""
    p = str(tmp_path / "d.jsonl")
    assert make_config(QVS_DETECTION_LOG=p).detection_log == p


def test_create_returns_none_when_disabled(tmp_path):
    assert create_detection_log("") is None
    assert create_detection_log("   ") is None
    assert isinstance(create_detection_log(str(tmp_path / "d.jsonl")), DetectionLog)


def test_logs_exact_payload_with_metadata(tmp_path):
    path = tmp_path / "d.jsonl"
    log = DetectionLog(str(path))
    dets = [{"label": "cup", "conf": 0.82, "bbox": [1.0, 2.0, 3.0, 4.0]}]
    payload = _payload(7, dets)
    log.log("user@example.com", payload)
    log.close()

    records = _read(path)
    assert len(records) == 1
    rec = records[0]
    # payload byte-faithful to what the client received
    assert rec["payload"] == payload
    # send-side metadata for correlation
    assert rec["client"] == "user@example.com"
    assert rec["count"] == 1
    assert isinstance(rec["t_mono"], float)
    assert "ts" in rec


def test_count_reflects_detections_and_appends_within_run(tmp_path):
    path = tmp_path / "d.jsonl"
    log = DetectionLog(str(path))
    log.log("c", _payload(1, []))
    log.log("c", _payload(2, [{"label": "a", "conf": 0.5, "bbox": [0, 0, 1, 1]}]))
    log.close()

    records = _read(path)
    assert [r["count"] for r in records] == [0, 1]
    assert [r["payload"]["frame"] for r in records] == [1, 2]


def test_truncates_per_run(tmp_path):
    path = tmp_path / "d.jsonl"
    first = DetectionLog(str(path))
    first.log("c", _payload(1, []))
    first.close()

    # A fresh logger on the same path (new server process) starts clean.
    second = DetectionLog(str(path))
    second.log("c", _payload(2, []))
    second.close()

    records = _read(path)
    assert [r["payload"]["frame"] for r in records] == [2]


def test_bad_path_disables_without_raising(tmp_path):
    # A path whose parent is a file (not a dir) can't be created — must degrade
    # to a no-op instead of tearing down the session.
    blocker = tmp_path / "blocker"
    blocker.write_text("x")
    log = DetectionLog(str(blocker / "nested" / "d.jsonl"))
    log.log("c", _payload(1, []))  # should not raise
    log.close()


# ---------------------------------------------- per-connection capture dir ----


def test_capture_dir_config_default_and_override(tmp_path):
    assert make_config().capture_dir == ""
    assert make_config(QVS_DUMP_DIR=str(tmp_path)).capture_dir == str(tmp_path)


def test_capture_dir_disables_shared_log(tmp_path):
    # With a capture dir, the shared single-file log is off (per-connection logs
    # under the capture dir take over); without it, the fallback log is created.
    assert _make_server(make_config(QVS_DUMP_DIR=str(tmp_path))).detection_log is None
    fallback = _make_server(make_config(QVS_DETECTION_LOG=str(tmp_path / "d.jsonl")))
    assert fallback.detection_log is not None


def test_session_dir_naming_and_sanitize(tmp_path):
    server = _make_server(make_config(QVS_DUMP_DIR=str(tmp_path)))
    path = server._session_dir("simon.darkside.jackson@googlemail.com")
    parent, name = os.path.split(path)
    assert parent == str(tmp_path)
    # <YYYYMMDD-HHMMSS>_<sanitized-client> — '@' is not filesystem-friendly.
    assert re.fullmatch(r"\d{8}-\d{6}_simon\.darkside\.jackson_googlemail\.com", name), name
    # A socket-peer identity with ':' is sanitized too.
    assert "_" in os.path.basename(server._session_dir("192.168.1.5:54321"))
    # Not created just by computing the path (lazy — no empty folders).
    assert not os.path.exists(path)


def test_per_connection_layout_log_and_dumps_colocated(tmp_path):
    """The session's detection log and its frame dumps land in one folder."""
    server = _make_server(make_config(QVS_DUMP_DIR=str(tmp_path)))
    session_dir = server._session_dir("client@host")

    # 1) detection log for this connection writes into the session folder
    log = create_detection_log(os.path.join(session_dir, "detections.jsonl"))
    log.log("client@host", _payload(1, [{"label": "cup", "conf": 0.5, "bbox": [0, 0, 1, 1]}]))
    log.close()

    # 2) frame dumps redirected to the same folder via set_capture_dir
    proc = VideoProcessor(make_config(QVS_DUMP_DIR=str(tmp_path)), lambda i: [], lambda p: None)
    proc.set_capture_dir(session_dir)
    proc.frame_count = 7
    proc._dump_frame(np.zeros((4, 4, 3), dtype=np.uint8), luma=88.0)

    entries = sorted(os.listdir(session_dir))
    assert "detections.jsonl" in entries
    assert any(f.startswith("frame_000007") and f.endswith(".jpg") for f in entries), entries
    # Both are under the one dated session folder, nothing at the capture root.
    assert sorted(os.listdir(tmp_path)) == [os.path.basename(session_dir)]


def test_set_capture_dir_overrides_base(tmp_path):
    proc = VideoProcessor(make_config(QVS_DUMP_DIR=str(tmp_path / "base")), lambda i: [], lambda p: None)
    assert proc._dump_dir == str(tmp_path / "base")
    proc.set_capture_dir(str(tmp_path / "session"))
    assert proc._dump_dir == str(tmp_path / "session")


# ------------------------------------------------------- ffmpeg log level ----


def test_ffmpeg_log_level_default_and_override():
    assert make_config().ffmpeg_log_level == "error"
    assert make_config(QVS_FFMPEG_LOG_LEVEL="warning").ffmpeg_log_level == "warning"


def test_configure_ffmpeg_logging_sets_threshold():
    import av.logging as av_log

    from questvisionstream.video_processor import configure_ffmpeg_logging

    original = av_log.get_level()
    try:
        configure_ffmpeg_logging("error")
        # ERROR threshold drops the WARNING-level swscaler notice.
        assert av_log.get_level() == av_log.ERROR
        configure_ffmpeg_logging("warning")
        assert av_log.get_level() == av_log.WARNING
        # Unknown value falls back to ERROR rather than raising.
        configure_ffmpeg_logging("nonsense")
        assert av_log.get_level() == av_log.ERROR
    finally:
        if original is not None:
            av_log.set_level(original)
