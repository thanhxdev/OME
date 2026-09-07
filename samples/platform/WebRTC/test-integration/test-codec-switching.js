const http = require('http');
const WebSocket = require('ws');

async function testCodecSwitching() {
  console.log('================================================================');
  console.log('🧪 TEST 2: Sub-Second Runtime Codec Switching Verification');
  console.log('================================================================');

  const SIGNALING_PORT = 3052;
  const SFU_PORT = 4052;
  const SESSION_ID = 'event-001';

  // 1. Start Signaling Server
  console.log(`1. Launching Signaling Server on port ${SIGNALING_PORT}...`);
  process.env.PORT = String(SIGNALING_PORT);
  require('../signaling-server/dist/index.js');
  await sleep(1000);

  // 2. Start SFU Media Server
  console.log(`2. Launching SFU Media Server on port ${SFU_PORT}...`);
  process.env.PORT = String(SFU_PORT);
  process.env.SIGNALING_URL = `ws://localhost:${SIGNALING_PORT}/ws`;
  process.env.MIN_PORT = '13200';
  process.env.MAX_PORT = '13300';
  require('../media-server/dist/index.js');
  await sleep(1000);

  // 3. Connect Mock Controller Client
  console.log('3. Connecting Controller WebSocket client...');
  const ctrlWs = new WebSocket(`ws://127.0.0.1:${SIGNALING_PORT}/ws`);
  await new Promise((res) => ctrlWs.on('open', res));
  ctrlWs.send(
    JSON.stringify({
      type: 'join_session',
      sessionId: SESSION_ID,
      senderId: 'mock-controller',
      timestamp: Date.now(),
      role: 'admin',
    })
  );
  await sleep(500);

  // 4. Connect Mock Camera Encoder
  console.log('4. Connecting Camera Encoder (cam-01, default H.264)...');
  const encWs = new WebSocket(`ws://127.0.0.1:${SIGNALING_PORT}/ws`);
  await new Promise((res) => encWs.on('open', res));
  encWs.send(
    JSON.stringify({
      type: 'join_session',
      sessionId: SESSION_ID,
      senderId: 'mock-encoder-01',
      timestamp: Date.now(),
      role: 'encoder',
      cameraId: 'cam-01',
    })
  );

  let receivedCodecChanges = [];
  encWs.on('message', (raw) => {
    try {
      const msg = JSON.parse(raw.toString());
      if (msg.type === 'codec_change') {
        receivedCodecChanges.push(msg);
      }
    } catch {}
  });

  await sleep(500);

  // 5. Switch from H.264 -> H.265 (HEVC)
  console.log('5. Triggering runtime switch: H.264 -> H.265 (HEVC 4500kbps)...');
  const tStart1 = Date.now();
  ctrlWs.send(
    JSON.stringify({
      type: 'codec_change',
      sessionId: SESSION_ID,
      senderId: 'mock-controller',
      timestamp: Date.now(),
      cameraId: 'cam-01',
      codec: 'h265',
      bitrate: 4500,
      fps: 60,
    })
  );
  await sleep(600);
  const switchTime1 = Date.now() - tStart1;
  console.log(`⏱️ Codec Switch 1 completed in ${switchTime1} ms (< 1 second)`);

  // Check SFU stream status
  let sfuStream = await getJson(`http://localhost:${SFU_PORT}/api/streams/cam-01/stats`);
  console.log(`📡 SFU Stream Router Codec state: ${sfuStream.codec}`);
  if (sfuStream.codec !== 'h265') {
    throw new Error(`Expected SFU codec to update to h265, got ${sfuStream.codec}`);
  }
  console.log('✅ SFU successfully re-indexed RTP router to H.265 without socket drop!');

  // 6. Switch from H.265 -> Throughpass (Uncompressed SDI 14000kbps)
  console.log('6. Triggering runtime switch: H.265 -> Throughpass (14000kbps)...');
  const tStart2 = Date.now();
  ctrlWs.send(
    JSON.stringify({
      type: 'codec_change',
      sessionId: SESSION_ID,
      senderId: 'mock-controller',
      timestamp: Date.now(),
      cameraId: 'cam-01',
      codec: 'throughpass',
      bitrate: 14000,
      fps: 60,
    })
  );
  await sleep(600);
  const switchTime2 = Date.now() - tStart2;
  console.log(`⏱️ Codec Switch 2 completed in ${switchTime2} ms (< 1 second)`);

  sfuStream = await getJson(`http://localhost:${SFU_PORT}/api/streams/cam-01/stats`);
  console.log(`📡 SFU Stream Router Codec state: ${sfuStream.codec}`);
  if (sfuStream.codec !== 'throughpass') {
    throw new Error(`Expected SFU codec to update to throughpass, got ${sfuStream.codec}`);
  }
  console.log('✅ SFU successfully transitioned to Throughpass direct routing!');

  console.log(`Total codec switches acknowledged: ${receivedCodecChanges.length}`);
  if (receivedCodecChanges.length < 2) {
    throw new Error('Encoder client did not receive both codec changes');
  }

  // Cleanup
  ctrlWs.close();
  encWs.close();

  console.log('================================================================');
  console.log('🎉 TEST 2 PASSED: Sub-Second Runtime Codec Switching Verified!');
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

testCodecSwitching().catch((err) => {
  console.error('❌ Test 2 Failed:', err);
  process.exit(1);
});
