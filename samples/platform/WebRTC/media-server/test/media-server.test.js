const http = require('http');

async function testMediaServer() {
  console.log('--- Testing Media Server (SFU) ---');
  process.env.PORT = '4010';
  require('../dist/index.js');

  await new Promise((r) => setTimeout(r, 1000));

  // 1. Check health
  const healthRes = await getJson('http://localhost:4010/api/health');
  if (healthRes.status !== 'healthy' || healthRes.activeProducers !== 10) {
    throw new Error(`Unexpected health: ${JSON.stringify(healthRes)}`);
  }
  console.log('✅ /api/health passed. 10 producers pre-registered.');

  // 2. List streams
  const streams = await getJson('http://localhost:4010/api/streams');
  if (!Array.isArray(streams) || streams.length !== 10) {
    throw new Error(`Streams count mismatch: ${streams.length}`);
  }
  console.log(`✅ /api/streams passed. Found ${streams.length} camera channels.`);

  // 3. Register subscriber
  const subRes = await postJson('http://localhost:4010/api/streams/cam-01/subscribe', {
    id: 'studio-decoder-cam01',
    role: 'decoder',
    ip: '127.0.0.1',
    videoPort: 20000,
    audioPort: 20002,
  });
  if (!subRes.success) {
    throw new Error(`Subscribe failed: ${JSON.stringify(subRes)}`);
  }
  console.log('✅ Subscriber registered to cam-01:', subRes);

  console.log('🎉 ALL MEDIA SERVER TESTS PASSED!');
  process.exit(0);
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

testMediaServer().catch((err) => {
  console.error('❌ Test failed:', err);
  process.exit(1);
});
