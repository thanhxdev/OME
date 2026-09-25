#!/usr/bin/env node
import fs from 'fs';
import { Command } from 'commander';
import { Logger, CodecMode, InputType } from '@webrtc-broadcast/shared';
import { EncoderOptions, getDefaultOptions } from './config';
import { PipelineRunner } from './pipeline/pipeline-runner';
import { EncoderSignalingClient } from './signaling/encoder-signaling';

const logger = new Logger('StadiumEncoderCLI');
const program = new Command();

program
  .name('stadium-encoder')
  .description('Professional SDI/HDMI Video Capture and WebRTC/RTP Broadcast Encoder')
  .version('2.0.0')
  .option('-c, --config <path>', 'JSON configuration file path')
  .option('--camera-id <id>', 'Unique camera identifier (e.g. cam-01)', 'cam-01')
  .option('--camera-name <name>', 'Descriptive camera name (e.g. "Main Wide")', 'Main Wide')
  .option('--input <type>', 'Input source type: sdi | hdmi | testpattern', 'testpattern')
  .option('--device <index>', 'Capture device index number', '0')
  .option('--codec <mode>', 'Codec mode: throughpass | h264 | h265', 'h264')
  .option('--bitrate <kbps>', 'Target video bitrate in kbps', '10000')
  .option('--resolution <res>', 'Video resolution (e.g. 1920x1080)', '1920x1080')
  .option('--fps <rate>', 'Target framerate (e.g. 60, 50, 30)', '60')
  .option('--signaling-url <url>', 'Signaling server WebSocket URL')
  .option('--session-id <id>', 'Session identifier', 'event-001')
  .option('--sfu-host <host>', 'SFU Media Server IP or Hostname', '127.0.0.1')
  .option('--sfu-video-port <port>', 'SFU ingress video RTP port')
  .option('--sfu-audio-port <port>', 'SFU ingress audio RTP port')
  .option('--hw-accel <accel>', 'Hardware acceleration: nvenc | qsv | cpu', 'cpu')
  .action((cmdOpts) => {
    let finalOptions: EncoderOptions = getDefaultOptions();

    // Load JSON config if specified
    if (cmdOpts.config) {
      if (fs.existsSync(cmdOpts.config)) {
        logger.info(`Loading configuration from ${cmdOpts.config}`);
        const fileData = JSON.parse(fs.readFileSync(cmdOpts.config, 'utf8'));
        finalOptions = { ...finalOptions, ...fileData };
      } else {
        logger.error(`Config file ${cmdOpts.config} not found!`);
        process.exit(1);
      }
    }

    // Merge CLI flags (they take precedence)
    if (cmdOpts.cameraId) finalOptions.cameraId = cmdOpts.cameraId;
    if (cmdOpts.cameraName) finalOptions.cameraName = cmdOpts.cameraName;
    if (cmdOpts.input) finalOptions.inputType = cmdOpts.input as InputType | 'testpattern';
    if (cmdOpts.device) finalOptions.deviceIndex = parseInt(cmdOpts.device, 10);
    if (cmdOpts.codec) finalOptions.codec = cmdOpts.codec as CodecMode;
    if (cmdOpts.bitrate) finalOptions.bitrate = parseInt(cmdOpts.bitrate, 10);
    if (cmdOpts.resolution) finalOptions.resolution = cmdOpts.resolution;
    if (cmdOpts.fps) finalOptions.fps = parseInt(cmdOpts.fps, 10);
    if (cmdOpts.signalingUrl) finalOptions.signalingUrl = cmdOpts.signalingUrl;
    if (cmdOpts.sessionId) finalOptions.sessionId = cmdOpts.sessionId;
    if (cmdOpts.sfuHost) finalOptions.sfuHost = cmdOpts.sfuHost;
    if (cmdOpts.hwAccel) finalOptions.hwAccel = cmdOpts.hwAccel;

    // Derive SFU Ports automatically based on camera ID if not explicitly specified
    if (cmdOpts.sfuVideoPort) {
      finalOptions.sfuVideoPort = parseInt(cmdOpts.sfuVideoPort, 10);
    } else {
      const match = finalOptions.cameraId.match(/\d+/);
      const camNum = match ? parseInt(match[0], 10) : 1;
      finalOptions.sfuVideoPort = 10000 + (camNum - 1) * 4;
      finalOptions.sfuAudioPort = finalOptions.sfuVideoPort + 2;
    }

    logger.info(`====================================================`);
    logger.info(`🎥 Starting Stadium Encoder: ${finalOptions.cameraName} (${finalOptions.cameraId})`);
    logger.info(`📥 Input: ${finalOptions.inputType.toUpperCase()} (device ${finalOptions.deviceIndex})`);
    logger.info(`⚙️ Codec: ${finalOptions.codec.toUpperCase()} @ ${finalOptions.bitrate} kbps (${finalOptions.resolution} @ ${finalOptions.fps}fps)`);
    logger.info(`📡 SFU Target: rtp://${finalOptions.sfuHost}:${finalOptions.sfuVideoPort}`);
    logger.info(`====================================================`);

    // Launch pipeline runner
    const runner = new PipelineRunner(finalOptions);
    runner.start();

    // Connect to signaling server
    const signaling = new EncoderSignalingClient(finalOptions, runner);
    signaling.connect();

    const shutdown = () => {
      logger.info('Shutting down encoder...');
      signaling.close();
      runner.stop();
      process.exit(0);
    };

    process.on('SIGINT', shutdown);
    process.on('SIGTERM', shutdown);
  });

program.parse(process.argv);
