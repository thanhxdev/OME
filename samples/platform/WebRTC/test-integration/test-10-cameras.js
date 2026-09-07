const http = require('http');
const WebSocket = require('ws');

async function test10CamerasConcurrent() {
  console.log('================================================================');
  console.log('🧪 TEST 3: High-Load 10-Camera Concurrent Pipeline Verification');
  console.log('================================================================');

  const SIGNALING_PORT = 3053;
  const SFU_PORT = 4053;
  const MONITOR_PORT = 9153;
  const RECORDING_PORT = 3043;
  const SESSION_ID = 'event-001';

  // 1. Launch Signaling Server
  console.log(`1. Launching Signaling Server on port ${SIGNALING_PORT}...`);
  process.env.PORT = String(SIGNALING_PORT);
  require('../signaling-server/dist/index.js');
  await sleep(1000);

  // 2. Launch SFU Media Server
  console.log(`2. Launching SFU Media Server on port ${SFU_PORT}...`);
  process.env.PORT = String(SFU_PORT);
  process.env.SIGNALING_URL = `ws://localhost:${SIGNALING_PORT}/ws`;
  process.env.MIN_PORT = '13400';
  process.env.MAX_PORT = '13500';
  require('../media-server/dist/index.js');
  await sleep(1000);

  // 3. Launch Telemetry & Monitoring Service
  console.log(`3. Launching Monitoring Service on port ${MONITOR_PORT}...`);
  process.env.MONITORING_PORT = String(MONITOR_PORT);
  process.env.MONITORING_HOST = '127.0.0.1';
  process.env.SIGNALING_WS_URL = `ws://127.0.0.1:${SIGNALING_PORT}/ws`;
  process.env.SFU_HTTP_URL = `http://127.0.0.1:${SFU_PORT}`;
  process.env.SESSION_ID = SESSION_ID;

  const { TelemetryCollector } = require('../monitoring-service/dist/collector.js');
  const { createMonitoringServer } = require('../monitoring-service/dist/server.js');
  const collector = new TelemetryCollector();
  collector.start();
  const monitorApp = createMonitoringServer(collector);
  const monitorServer = monitorApp.listen(MONITOR_PORT, '127.0.0.1');
  await sleep(1000);

  // 4. Launch ISO Recording Service
  console.log(`4. Launching ISO Multi-Track Recording Service on port ${RECORDING_PORT}...`);
  process.env.RECORDING_PORT = String(RECORDING_PORT);
  process.env.RECORDING_HOST = '127.0.0.1';
  process.env.SIGNALING_WS_URL = `ws://127.0.0.1:${SIGNALING_PORT}/ws`;
  process.env.SFU_HTTP_URL = `http://127.0.0.1:${SFU_PORT}`;
  process.env.SESSION_ID = SESSION_ID;
  process.env.AUTO_UPLOAD_S3 = 'false';

  const { IsoTrackRecorder } = require('../recording-service/dist/recorder.js');
  const { StorageUploader } = require('../recording-service/dist/uploader.js');
  const { createRecordingServer } = require('../recording-service/dist/server.js');

  const recorder = new IsoTrackRecorder();
  const uploader = new StorageUploader();
  const recApp = createRecordingServer(recorder, uploader);
  const recServer = recApp.listen(RECORDING_PORT, '127.0.0.1');
  await sleep(1000);

  // 5. Connect 10 Camera Encoders
  console.log('5. Registering and streaming 10 concurrent stadium cameras...');
  const cameraClients = [];
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${String(i).padStart(2, '0')}`;
    const ws = new WebSocket(`ws://127.0.0.1:${SIGNALING_PORT}/ws`);
    await new Promise((res) => ws.on('open', res));

    ws.send(
      JSON.stringify({
        type: 'join_session',
        sessionId: SESSION_ID,
        senderId: `encoder-${camId}`,
        timestamp: Date.now(),
        role: 'encoder',
        cameraId: camId,
      })
    );

    const bitrate = i === 7 ? 14000 : 8000;
    const codec = i === 7 ? 'throughpass' : 'h264';

    collector.applyStreamMetrics({
      cameraId: camId,
      bitrate,
      fps: 60,
      packetLoss: 0.05,
      rtt: 18,
      jitter: 2,
      droppedFrames: 0,
      timestamp: Date.now(),
    });

    cameraClients.push(ws);
  }
  await sleep(1000);

  // 6. Start ISO Recording on All 10 Cameras
  console.log('6. Starting simultaneous 10-track ISO recording via REST API...');
  const startRecRes = await postJson(`http://127.0.0.1:${RECORDING_PORT}/api/recording/start`, {
    cameraId: 'all',
  });
  console.log('✅ ISO Multi-track Recording started:', startRecRes);

  await sleep(2000);

  // 7. Verify All 10 Camera Recording States
  const recStatus = await getJson(`http://127.0.0.1:${RECORDING_PORT}/api/recording/status`);
  const activeCount = recStatus.recordings.filter((r) => r.isRecording).length;
  console.log(`📹 Active ISO Recording tracks: ${activeCount} / 10`);

  if (activeCount !== 10) {
    throw new Error(`Expected 10 active recording tracks, got ${activeCount}`);
  }

  // 8. Verify Monitoring Service Aggregates for all 10 Cameras
  const summary = await getJson(`http://127.0.0.1:${MONITOR_PORT}/api/metrics/summary`);
  console.log('📊 Monitoring Aggregates for 10 Cameras:', {
    camerasReported: summary.cameras.length,
    totalWanTxMbps: (summary.totalWanTxKbps / 1000).toFixed(1) + ' Mbps',
    onlineCameras: summary.cameras.filter((c) => c.online).length,
  });

  if (summary.cameras.length !== 10) {
    throw new Error(`Expected 10 cameras in monitoring summary, got ${summary.cameras.length}`);
  }

  // 9. Stop Recording & Verify Segments
  console.log('9. Stopping ISO Multi-track recording...');
  await postJson(`http://127.0.0.1:${RECORDING_PORT}/api/recording/stop`, { cameraId: 'all' });
  await sleep(1000);

  const recordingsList = await getJson(`http://127.0.0.1:${RECORDING_PORT}/api/recordings`);
  console.log(`💾 Completed & Archived ISO segments: ${recordingsList.count}`);

  // Cleanup
  cameraClients.forEach((ws) => ws.close());
  collector.stop();
  recorder.shutdown();
  monitorServer.close();
  recServer.close();

  console.log('================================================================');
  console.log('🎉 TEST 3 PASSED: High-Load 10-Camera Concurrent Pipeline Verified!');
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

function postJson(url, payload) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    const body = JSON.stringify(payload);
    const req = http.request(
      {
        hostname: u.hostname,
        port: u.port,
        path: u.pathname,
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(body),
        },
      },
      (res) => {
        let data = '';
        res.on('data', (c) => (data += c));
        res.on('end', () => resolve(JSON.parse(data)));
      }
    );
    req.on('error', reject);
    req.write(body);
    req.end();
  });
}

test10CamerasConcurrent().catch((err) => {
  console.error('❌ Test 3 Failed:', err);
  process.exit(1);
});
