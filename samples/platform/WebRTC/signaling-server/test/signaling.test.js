const http = require('http');
const WebSocket = require('ws');

async function runTests() {
  console.log('--- Starting Signaling Server Self-Test ---');

  // Start the compiled signaling server in-process
  process.env.PORT = '3010';
  require('../dist/index.js');

  // Wait 1 second for server to listen
  await new Promise((r) => setTimeout(r, 1000));

  // 1. Test HTTP /api/health
  console.log('1. Testing GET /api/health...');
  const healthRes = await makeRequest('http://localhost:3010/api/health');
  const healthData = JSON.parse(healthRes);
  if (healthData.status !== 'healthy') {
    throw new Error(`Unexpected health status: ${healthData.status}`);
  }
  console.log('✅ /api/health passed:', healthData);

  // 2. Test GET /api/sessions
  console.log('2. Testing GET /api/sessions...');
  const sessionsRes = await makeRequest('http://localhost:3010/api/sessions');
  const sessions = JSON.parse(sessionsRes);
  if (!Array.isArray(sessions) || sessions.length === 0) {
    throw new Error('No default session found');
  }
  console.log('✅ /api/sessions passed. Default session:', sessions[0].id);

  // 3. Test WebSocket connection & join_session
  console.log('3. Testing WebSocket /ws join_session...');
  const ws = new WebSocket('ws://localhost:3010/ws');

  await new Promise((resolve, reject) => {
    ws.on('open', () => {
      console.log('WebSocket connected. Sending join_session...');
      ws.send(
        JSON.stringify({
          type: 'join_session',
          sessionId: 'event-001',
          senderId: 'test-encoder-cam01',
          role: 'encoder',
          cameraId: 'cam-01',
          timestamp: Date.now(),
        })
      );
    });

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      console.log('Received WebSocket message type:', msg.type);
      if (msg.type === 'joined_session') {
        console.log('✅ joined_session received. Registered cameras:', msg.cameras.length);
        ws.close();
        resolve();
      }
    });

    ws.on('error', reject);
    setTimeout(() => reject(new Error('WebSocket test timeout')), 5000);
  });

  console.log('🎉 ALL SIGNALING SERVER TESTS PASSED SUCCESSFULLY!');
  process.exit(0);
}

function makeRequest(url) {
  return new Promise((resolve, reject) => {
    http
      .get(url, (res) => {
        let data = '';
        res.on('data', (chunk) => (data += chunk));
        res.on('end', () => resolve(data));
      })
      .on('error', reject);
  });
}

runTests().catch((err) => {
  console.error('❌ Test failed:', err);
  process.exit(1);
});
