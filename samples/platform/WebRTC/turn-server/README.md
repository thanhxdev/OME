# TURN / STUN Server (coturn) for Broadcast WebRTC

High-throughput, low-latency relay server configuration tailored for professional multi-camera WebRTC production.

---

## Architecture Overview

When direct P2P connections fail due to symmetric NATs, complex stadium firewalls, or 4G/5G bonded uplinks, traffic automatically falls back to this TURN server using UDP or TCP/TLS encapsulation.

```
[Stadium Encoder] ---> (NAT/Firewall) ---> [TURN Server (coturn)] ---> [Media Server (SFU)]
```

---

## Port & Firewall Configuration

| Port | Protocol | Purpose | Access Required |
|------|----------|---------|-----------------|
| `3478` | UDP/TCP | STUN/TURN standard listener | Public Internet |
| `5349` | UDP/TCP | STUN/TURN over TLS/DTLS | Public Internet |
| `49152 - 65535` | UDP | Dynamic Relay Ports (RTP streams) | Public Internet |
| `9641` | TCP | Coturn Prometheus Exporter | Monitoring internal network |

> [!IMPORTANT]
> Ensure UDP port range `49152-65535` is explicitly allowed in your cloud security group (AWS SG, GCP Firewall, Azure NSG, or Linux `iptables/ufw`).

---

## Bandwidth Sizing Guide (10 Cameras)

| Preset Mode | Per-Camera Bitrate | 10 Cameras Aggregate | Recommended Uplink Bandwidth |
|-------------|--------------------|-----------------------|------------------------------|
| **Minimal (720p25 H.265)** | 3.0 Mbps | 30 Mbps | ≥ 50 Mbps |
| **Bandwidth-Saver (1080p30 H.265)** | 6.0 Mbps | 60 Mbps | ≥ 100 Mbps |
| **Balanced (1080p60 H.264)** | 10.0 Mbps | 100 Mbps | ≥ 150 Mbps |
| **Max-Quality (Throughpass)** | 18 - 25 Mbps | 180 - 250 Mbps | ≥ 350 Mbps |

---

## Quick Deployment (Docker)

```bash
# 1. Start coturn and prometheus exporter
docker compose up -d

# 2. Verify health
./scripts/health-check.sh

# 3. Generate short-lived REST credentials
./scripts/generate-credentials.sh 86400 cam-01
```

---

## Prometheus Metrics Export

The included exporter service (`podstream/coturn-prometheus-exporter`) scrapes coturn and exposes metrics at:
`http://<SERVER_IP>:9641/metrics`

Key metrics:
- `coturn_total_traffic_bytes_received`
- `coturn_total_traffic_bytes_sent`
- `coturn_current_allocations`
- `coturn_current_sessions`
