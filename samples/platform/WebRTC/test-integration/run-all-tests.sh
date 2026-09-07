#!/usr/bin/env bash
set -e

echo "================================================================================"
echo " 🚀 EXECUTING PHASE 4 MASTER INTEGRATION TEST SUITE (10-CAMERA WEBRTC)"
echo "================================================================================"

export NODE_PATH="../monitoring-service/node_modules:../signaling-server/node_modules:../recording-service/node_modules"

TESTS=(
    "test-1-camera-h264.js"
    "test-codec-switching.js"
    "test-10-cameras.js"
    "test-failover.js"
    "test-bandwidth.js"
)

PASSED=0
FAILED=0

for TEST in "${TESTS[@]}"; do
    echo ""
    echo ">>> Running ${TEST}..."
    if node "${TEST}"; then
        echo ">>> [PASS] ${TEST}"
        ((PASSED++))
    else
        echo ">>> [FAIL] ${TEST}"
        ((FAILED++))
    fi
    sleep 1
done

echo ""
echo "================================================================================"
echo " 🏁 TEST SUITE SUMMARY: ${PASSED} PASSED | ${FAILED} FAILED"
echo "================================================================================"

if [ "${FAILED}" -gt 0 ]; then
    exit 1
fi
exit 0
