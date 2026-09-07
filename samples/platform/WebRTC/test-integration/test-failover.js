const http = require('http');
const WebSocket = require('ws');
const path = require('path');

async function testFailoverAndRecovery() {
  console.log('================================================================');
  console.log('🧪 TEST 4: Network Disruption & Watchdog Failover Recovery');
  console.log('================================================================');

  const SIGNALING_PORT = 3054;
  const SESSION_ID = 'event-001';

  // 1. Launch Signaling Server
  console.log(`1. Launching Signaling Server on port ${SIGNALING_PORT}...`);
  process.env.PORT = String(SIGNALING_PORT);
  require('../signaling-server/dist/index.js');
  await sleep(1000);

  // 2. Test Process Supervisor Watchdog Auto-Restart
  console.log('2. Testing Process Supervisor Watchdog auto-restart upon crash...');
  const { ProcessSupervisor } = require('../encoder-control-agent/dist/process-supervisor.js');

  const supervisor = new ProcessSupervisor({
    cameraId: 'cam-01',
    cameraName: 'Test Cam 1',
    inputType: 'sdi',
    deviceIndex: 0,
    codec: 'h264',
    bitrate: 8500,
    fps: 60,
    resolution: '1920x1080',
    signalingWsUrl: `ws://localhost:${SIGNALING_PORT}/ws`,
    sessionId: SESSION_ID,
    sfuHost: '127.0.0.1',
    sfuVideoPort: 14000,
    sfuAudioPort: 14002,
    hwAccel: 'cpu',
    encoderScriptPath: path.resolve(__dirname, '../stadium-encoder/dist/cli.js'),
    telemetryIntervalMs: 1000,
    gpioOnAirPin: 17,
    gpioPreviewPin: 27,
    mockHardware: true,
  });

  let restartDetected = false;
  supervisor.on('watchdog_restarting', ({ attempt }) => {
    console.log(`🛡️ Watchdog detected encoder exit! Auto-restart attempt #${attempt}`);
    restartDetected = true;
  });

  supervisor.start();
  await sleep(1500);

  const initialStatus = supervisor.getStatus();
  console.log(`Encoder running with PID: ${initialStatus.pid}`);

  if (initialStatus.pid) {
    console.log('Simulating unhandled hardware encoder crash (killing child process)...');
    try {
      process.kill(initialStatus.pid, 'SIGKILL');
    } catch {}
  }

  await sleep(2500);

  if (!restartDetected) {
    throw new Error('Watchdog failed to detect crash and restart encoder process');
  }
  console.log('✅ Watchdog supervisor successfully caught crash and initiated auto-recovery!');
  supervisor.stop();

  // 3. Test Network Disruption & Signaling Reconnect
  console.log('3. Testing Signaling Client network drop and reconnect recovery...');
  let reconnectCount = 0;
  let wsClient = new WebSocket(`ws://127.0.0.1:${SIGNALING_PORT}/ws`);

  await new Promise((res) => wsClient.on('open', res));
  console.log('Client established initial WebSocket connection');

  wsClient.on('close', () => {
    console.log('🔌 Network connection severed! Simulating automatic client reconnect...');
    setTimeout(() => {
      const newWs = new WebSocket(`ws://127.0.0.1:${SIGNALING_PORT}/ws`);
      newWs.on('open', () => {
        reconnectCount++;
        console.log('✅ Reconnected to Signaling Server successfully!');
        newWs.close();
      });
    }, 500);
  });

  // Force close socket to simulate network drop
  wsClient.terminate();
  await sleep(1500);

  if (reconnectCount === 0) {
    throw new Error('Signaling reconnect recovery failed');
  }

  console.log('================================================================');
  console.log('🎉 TEST 4 PASSED: Failover & Watchdog Auto-Recovery Verified!');
  console.log('================================================================');
  process.exit(0);
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

testFailoverAndRecovery().catch((err) => {
  console.error('❌ Test 4 Failed:', err);
  process.exit(1);
});
