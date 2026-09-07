import { config } from './config';
import { TelemetryCollector } from './collector';
import { createMonitoringServer } from './server';

async function main() {
  console.log('====================================================');
  console.log(' WebRTC 10-Camera Telemetry & Monitoring Service');
  console.log('====================================================');
  console.log(`Port:            ${config.port}`);
  console.log(`Signaling URL:   ${config.signalingWsUrl}`);
  console.log(`SFU HTTP URL:    ${config.sfuHttpUrl}`);
  console.log(`Session ID:      ${config.sessionId}`);
  console.log(`Scrape Interval: ${config.scrapeIntervalMs} ms`);
  console.log('----------------------------------------------------');

  const collector = new TelemetryCollector();
  collector.start();

  const app = createMonitoringServer(collector);

  const server = app.listen(config.port, config.host, () => {
    console.log(`[Monitoring Service] Prometheus Exporter listening on http://${config.host}:${config.port}/metrics`);
    console.log(`[Monitoring Service] Summary API listening on http://${config.host}:${config.port}/api/metrics/summary`);
  });

  const shutdown = () => {
    console.log('\n[Monitoring Service] Graceful shutdown initiated...');
    collector.stop();
    server.close(() => {
      console.log('[Monitoring Service] Server closed.');
      process.exit(0);
    });
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

if (require.main === module) {
  main().catch((err) => {
    console.error('[Monitoring Service] Fatal initialization error:', err);
    process.exit(1);
  });
}
