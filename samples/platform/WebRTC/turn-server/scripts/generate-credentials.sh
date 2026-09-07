#!/usr/bin/env bash
# Generate ephemeral TURN credentials via HMAC-SHA1 shared secret
# Usage: ./generate-credentials.sh [duration_seconds] [username_prefix]

SECRET=${TURN_SECRET:-"broadcast_shared_turn_secret_change_in_production"}
DURATION=${1:-86400} # Default 24 hours
PREFIX=${2:-"cam"}

EXPIRY=$(($(date +%s) + DURATION))
USERNAME="${EXPIRY}:${PREFIX}"
PASSWORD=$(echo -n "${USERNAME}" | openssl dgst -sha1 -hmac "${SECRET}" -binary | base64)

echo "=== Generated Short-Lived TURN Credentials ==="
echo "Username: ${USERNAME}"
echo "Password: ${PASSWORD}"
echo "Expires at: $(date -d @${EXPIRY} 2>/dev/null || date -r ${EXPIRY})"
echo "==============================================="

# Output JSON for API / automation integration
cat <<EOF
{
  "username": "${USERNAME}",
  "password": "${PASSWORD}",
  "ttl": ${DURATION},
  "expiresAt": ${EXPIRY}
}
EOF
