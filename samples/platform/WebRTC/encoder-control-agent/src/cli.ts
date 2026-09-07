import { Command } from 'commander';
import { defaultConfig, AgentConfig } from './config';
import { EncoderControlAgent } from './agent';
import { CodecMode } from '@webrtc-broadcast/shared';

const program = new Command();

program
  .name('encoder-control-agent')
  .description('Field supervision daemon for stadium encoder process watchdog, hardware telemetry, and GPIO tally')
  .version('1.0.0')
  .option('-c, --camera-id <id>', 'Camera ID (e.g. cam-01)', defaultConfig.cameraId)
  .option('-n, --camera-name <name>', 'Camera Display Name', defaultConfig.cameraName)
  .option('-i, --input-type <type>', 'Input source type (sdi or hdmi)', defaultConfig.inputType)
  .option('-d, --device-index <index>', 'Capture device card index', String(defaultConfig.deviceIndex))
  .option('-m, --codec <mode>', 'Codec mode (h264, h265, throughpass)', defaultConfig.codec)
  .option('-b, --bitrate <kbps>', 'Target bitrate in kbps', String(defaultConfig.bitrate))
  .option('-f, --fps <fps>', 'Framerate (60, 50, 30, 25)', String(defaultConfig.fps))
  .option('-r, --resolution <res>', 'Resolution (1920x1080, etc)', defaultConfig.resolution)
  .option('-s, --signaling-url <url>', 'Signaling Server WebSocket URL', defaultConfig.signalingWsUrl)
  .option('--session-id <id>', 'Broadcast session ID', defaultConfig.sessionId)
  .option('--sfu-host <host>', 'SFU IP or hostname', defaultConfig.sfuHost)
  .option('--sfu-video-port <port>', 'SFU target video ingress UDP port', String(defaultConfig.sfuVideoPort))
  .option('--hw-accel <accel>', 'Hardware acceleration (nvenc, qsv, vaapi, cpu)', defaultConfig.hwAccel)
  .action((options) => {
    const config: AgentConfig = {
      ...defaultConfig,
      cameraId: options.cameraId,
      cameraName: options.cameraName,
      inputType: options.inputType as 'sdi' | 'hdmi',
      deviceIndex: parseInt(options.deviceIndex, 10),
      codec: options.codec as CodecMode,
      bitrate: parseInt(options.bitrate, 10),
      fps: parseInt(options.fps, 10),
      resolution: options.resolution,
      signalingWsUrl: options.signalingUrl,
      sessionId: options.sessionId,
      sfuHost: options.sfuHost,
      sfuVideoPort: parseInt(options.sfuVideoPort, 10),
      hwAccel: options.hwAccel,
    };

    console.log('====================================================');
    console.log(` WebRTC Encoder Supervision Agent [${config.cameraId}]`);
    console.log('====================================================');
    console.log(`Codec / Bitrate:  ${config.codec} @ ${config.bitrate} kbps (${config.fps} fps)`);
    console.log(`Signaling URL:    ${config.signalingWsUrl}`);
    console.log(`SFU Target:       ${config.sfuHost}:${config.sfuVideoPort}`);
    console.log('----------------------------------------------------');

    const agent = new EncoderControlAgent(config);
    agent.start();

    const shutdown = () => {
      console.log(`\n[Agent ${config.cameraId}] Stopping daemon...`);
      agent.stop();
      process.exit(0);
    };

    process.on('SIGINT', shutdown);
    process.on('SIGTERM', shutdown);
  });

program.parse(process.argv);
