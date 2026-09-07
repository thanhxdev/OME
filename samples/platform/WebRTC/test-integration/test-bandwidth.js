const http = require('http');

async function testBandwidthProfiles() {
  console.log('================================================================');
  console.log('🧪 TEST 5: Codec Bandwidth Comparison & WAN Threshold Verification');
  console.log('================================================================');

  const MONITOR_PORT = 9155;
  process.env.MONITORING_PORT = String(MONITOR_PORT);
  process.env.MONITORING_HOST = '127.0.0.1';
  process.env.WAN_BANDWIDTH_LIMIT_KBPS = '120000'; // 120 Mbps limit
  process.env.WAN_BANDWIDTH_WARNING_KBPS = '95000'; // 95 Mbps warning

  const { TelemetryCollector } = require('../monitoring-service/dist/collector.js');
  const { createMonitoringServer } = require('../monitoring-service/dist/server.js');

  const collector = new TelemetryCollector();
  const app = createMonitoringServer(collector);
  const server = app.listen(MONITOR_PORT, '127.0.0.1');
  await sleep(600);

  // Profile 1: Minimal Uplink (H.265 @ 2,500 kbps each = 25,000 kbps / 25 Mbps)
  console.log('1. Evaluating Minimal Uplink Profile (H.265 @ 2.5 Mbps)...');
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${String(i).padStart(2, '0')}`;
    collector.applyStreamMetrics({
      cameraId: camId,
      bitrate: 2500,
      fps: 30,
      packetLoss: 0.0,
      rtt: 15,
      timestamp: Date.now(),
    });
  }

  let summary = await getJson(`http://127.0.0.1:${MONITOR_PORT}/api/metrics/summary`);
  console.log(`- Total WAN TX: ${(summary.totalWanTxKbps / 1000).toFixed(1)} Mbps`);
  console.log(`- Is Overloaded: ${summary.isWanOverloaded ? 'YES' : 'NO'}`);
  if (summary.totalWanTxKbps !== 25000 || summary.isWanOverloaded) {
    throw new Error('Minimal profile bandwidth evaluation failed');
  }

  // Profile 2: Bandwidth Saver (H.265 @ 4,500 kbps each = 45,000 kbps / 45 Mbps)
  console.log('2. Evaluating Bandwidth Saver Profile (H.265 @ 4.5 Mbps)...');
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${String(i).padStart(2, '0')}`;
    collector.applyStreamMetrics({
      cameraId: camId,
      bitrate: 4500,
      fps: 60,
      packetLoss: 0.0,
      rtt: 17,
      timestamp: Date.now(),
    });
  }

  summary = await getJson(`http://127.0.0.1:${MONITOR_PORT}/api/metrics/summary`);
  console.log(`- Total WAN TX: ${(summary.totalWanTxKbps / 1000).toFixed(1)} Mbps`);
  console.log(`- Is Overloaded: ${summary.isWanOverloaded ? 'YES' : 'NO'}`);
  if (summary.totalWanTxKbps !== 45000 || summary.isWanOverloaded) {
    throw new Error('Bandwidth Saver profile evaluation failed');
  }

  // Profile 3: Balanced Broadcast (H.264 @ 8,500 kbps each = 85,000 kbps / 85 Mbps)
  console.log('3. Evaluating Balanced Broadcast Profile (H.264 @ 8.5 Mbps)...');
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${String(i).padStart(2, '0')}`;
    collector.applyStreamMetrics({
      cameraId: camId,
      bitrate: 8500,
      fps: 60,
      packetLoss: 0.1,
      rtt: 19,
      timestamp: Date.now(),
    });
  }

  summary = await getJson(`http://127.0.0.1:${MONITOR_PORT}/api/metrics/summary`);
  console.log(`- Total WAN TX: ${(summary.totalWanTxKbps / 1000).toFixed(1)} Mbps`);
  console.log(`- Is Overloaded: ${summary.isWanOverloaded ? 'YES' : 'NO'}`);
  if (summary.totalWanTxKbps !== 85000 || summary.isWanOverloaded) {
    throw new Error('Balanced profile evaluation failed');
  }

  // Profile 4: Max Quality (Throughpass / High-bitrate @ 14,000 kbps each = 140,000 kbps / 140 Mbps)
  console.log('4. Evaluating Max Quality Overload Profile (Throughpass @ 14 Mbps)...');
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${String(i).padStart(2, '0')}`;
    collector.applyStreamMetrics({
      cameraId: camId,
      bitrate: 14000,
      fps: 60,
      packetLoss: 0.2,
      rtt: 22,
      timestamp: Date.now(),
    });
  }

  summary = await getJson(`http://127.0.0.1:${MONITOR_PORT}/api/metrics/summary`);
  console.log(`- Total WAN TX: ${(summary.totalWanTxKbps / 1000).toFixed(1)} Mbps`);
  console.log(`- Is Overloaded: ${summary.isWanOverloaded ? 'YES (OVERLOAD THRESHOLD TRIGGERED)' : 'NO'}`);
  if (summary.totalWanTxKbps !== 140000 || !summary.isWanOverloaded) {
    throw new Error('Overload profile failed to trigger warning threshold');
  }
  console.log('✅ Overload alert successfully engaged when aggregate bandwidth exceeded 95 Mbps!');

  // Cleanup
  collector.stop();
  server.close();

  console.log('================================================================');
  console.log('🎉 TEST 5 PASSED: Bandwidth Profiles & Thresholds Verified!');
  console.log('================================================================');
  process.exit(0);
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function getJson(url) {
  return new Promise((resolve, reject) => {
    http
      .get(url, (res) => {
        let data = '';
        res.on('data', (c) => (data += c));
        res.on('end', () => resolve(JSON.parse(data)));
      })
      .on('error', reject);
  });
}

testBandwidthProfiles().catch((err) => {
  console.error('❌ Test 5 Failed:', err);
  process.exit(1);
});
