#!/usr/bin/env bash
# Bandwidth throughput estimation script for TURN relay testing
# Uses iperf3 or curl over proxied UDP/TCP

TARGET=${1:-"127.0.0.1"}
DURATION=${2:-10}
PORT=5201

echo "=========================================================="
echo " TURN Relay Bandwidth Sizing Assessment"
echo " Target: ${TARGET} | Duration: ${DURATION}s"
echo "=========================================================="

echo "Expected multi-camera broadcast load calculation:"
echo " 1 Camera  (H.265 @ 1080p30): ~6 Mbps"
echo " 1 Camera  (H.264 @ 1080p60): ~10 Mbps"
echo " 1 Camera  (Throughpass SDI): ~15-25 Mbps"
echo ""
echo " 10 Cameras (H.265 Balanced): ~60 Mbps aggregate throughput"
echo " 10 Cameras (H.264 Broadcast): ~100 Mbps aggregate throughput"
echo " 10 Cameras (Throughpass Max): ~200-250 Mbps aggregate throughput"
echo "=========================================================="

if command -v iperf3 >/dev/null 2>&1; then
  echo "Running iperf3 UDP bandwidth benchmark..."
  iperf3 -c "${TARGET}" -p "${PORT}" -u -b 150M -t "${DURATION}"
else
  echo "iperf3 not found on PATH. Please install iperf3 to run live throughput benchmarks."
fi
