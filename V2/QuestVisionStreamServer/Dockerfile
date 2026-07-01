# CPU fallback image (YOLO detector, headless). For lowest latency on Apple
# Silicon run natively instead (see DEPLOY_LOCAL_MAC.md) — Docker Desktop on macOS
# has no MPS GPU and no host networking.
FROM python:3.11-slim

WORKDIR /app

# libglib2.0-0 + libgomp1 cover OpenCV-headless / numpy / torch runtime needs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt requirements-yolo.txt ./
RUN pip install --no-cache-dir -r requirements.txt -r requirements-yolo.txt

COPY questvisionstream ./questvisionstream

ENV QVS_ENABLE_DISPLAY=false \
    QVS_HOST=0.0.0.0 \
    QVS_PORT=3000 \
    QVS_HEALTH_PORT=8080

EXPOSE 3000 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8080/').getcode()==200 else 1)"

CMD ["python", "-m", "questvisionstream"]
