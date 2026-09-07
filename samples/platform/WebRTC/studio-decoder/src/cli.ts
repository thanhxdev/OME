#!/usr/bin/env node
import { Command } from 'commander';
import { Logger, CodecMode } from '@webrtc-broadcast/shared';
import { DecoderOptions, getDefaultDecoderOptions, OutputType } from './config';
import { DecoderRunner } from './pipeline/decoder-runner';
import { DecoderSignalingClient } from './signaling/decoder-signaling';

const logger = new Logger('StudioDecoderCLI');
const program = new Command();

program
  .name('studio-decoder')
  .description('Broadcast Studio WebRTC/RTP Receiver with SDI DeckLink and NDI Output')
  .version('1.0.0')
  .option('--camera-id <id>', 'Target camera feed to decode (e.g. cam-01)', 'cam-01')
  .option('--output <type>', 'Output sink type: sdi | ndi | display | null', 'display')
  .option('--ndi-name <name>', 'NDI Stream Name', 'CAM 01 - Main Wide')
  .option('--sdi-device <index>', 'Blackmagic DeckLink output card index', '0')
  .option('--signaling-url <url>', 'Signaling server WebSocket URL')
  .option('--session-id <id>', 'Session identifier', 'event-001')
  .option('--sfu-url <url>', 'SFU Media Server API URL')
  .option('--buffer <ms>', 'Jitter buffer latency in milliseconds', '60')
  .option('--codec <mode>', 'Initial stream codec: throughpass | h264 | h265', 'h264')
  .option('--listen-video-port <port>', 'Local RTP listening video port')
  .option('--listen-audio-port <port>', 'Local RTP listening audio port')
  .option('--hw-accel <accel>', 'Hardware decode acceleration: nvdec | qsv | cpu', 'cpu')
  .action(async (cmdOpts) => {
    const finalOptions: DecoderOptions = getDefaultDecoderOptions();

    if (cmdOpts.cameraId) finalOptions.cameraId = cmdOpts.cameraId;
    if (cmdOpts.output) finalOptions.output = cmdOpts.output as OutputType;
    if (cmdOpts.ndiName) finalOptions.ndiName = cmdOpts.ndiName;
    if (cmdOpts.sdiDevice) finalOptions.sdiDeviceIndex = parseInt(cmdOpts.sdiDevice, 10);
    if (cmdOpts.signalingUrl) finalOptions.signalingUrl = cmdOpts.signalingUrl;
    if (cmdOpts.sessionId) finalOptions.sessionId = cmdOpts.sessionId;
    if (cmdOpts.sfuUrl) finalOptions.sfuApiUrl = cmdOpts.sfuUrl;
    if (cmdOpts.buffer) finalOptions.bufferMs = parseInt(cmdOpts.buffer, 10);
    if (cmdOpts.codec) finalOptions.activeCodec = cmdOpts.codec as CodecMode;
    if (cmdOpts.hwAccel) finalOptions.hwAccel = cmdOpts.hwAccel;

    // Derive local listening ports automatically from camera ID if not explicitly specified
    if (cmdOpts.listenVideoPort) {
      finalOptions.listenVideoPort = parseInt(cmdOpts.listenVideoPort, 10);
    } else {
      const match = finalOptions.cameraId.match(/\d+/);
      const camNum = match ? parseInt(match[0], 10) : 1;
      finalOptions.listenVideoPort = 20000 + (camNum - 1) * 4;
      finalOptions.listenAudioPort = finalOptions.listenVideoPort + 2;
    }

    logger.info(`====================================================`);
    logger.info(`📺 Starting Studio Decoder for Feed: ${finalOptions.cameraId}`);
    logger.info(`📤 Output Destination: ${finalOptions.output.toUpperCase()}${finalOptions.output === 'ndi' ? ` (${finalOptions.ndiName})` : ''}`);
    logger.info(`🎧 Audio/Video Ingress: RTP Ports UDP ${finalOptions.listenVideoPort} / ${finalOptions.listenAudioPort}`);
    logger.info(`⏱️ Jitter Buffer: ${finalOptions.bufferMs} ms`);
    logger.info(`====================================================`);

    const runner = new DecoderRunner(finalOptions);
    runner.start();

    const signaling = new DecoderSignalingClient(finalOptions, runner);
    await signaling.connect();

    const shutdown = () => {
      logger.info('Shutting down decoder...');
      signaling.close();
      runner.stop();
      process.exit(0);
    };

    process.on('SIGINT', shutdown);
    process.on('SIGTERM', shutdown);
  });

program.parse(process.argv);
