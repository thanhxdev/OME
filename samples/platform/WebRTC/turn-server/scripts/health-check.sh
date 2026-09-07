#!/usr/bin/env bash
# Quick STUN/TURN listener probe
HOST=${TURN_HOST:-"127.0.0.1"}
PORT=${TURN_PORT:-3478}

echo "Probing STUN/TURN on ${HOST}:${PORT}..."

# Test UDP listening via netcat
if nc -z -u -w 2 "${HOST}" "${PORT}" 2>/dev/null; then
  echo "✅ TURN UDP port ${PORT} is open and responding."
else
  echo "⚠️ Warning: TURN UDP probe timed out or port closed on ${HOST}:${PORT}."
fi

# Test TCP listening via netcat
if nc -z -w 2 "${HOST}" "${PORT}" 2>/dev/null; then
  echo "✅ TURN TCP port ${PORT} is open and listening."
else
  echo "❌ Error: TURN TCP port ${PORT} is NOT reachable."
  exit 1
fi

echo "TURN server is alive."
exit 0
