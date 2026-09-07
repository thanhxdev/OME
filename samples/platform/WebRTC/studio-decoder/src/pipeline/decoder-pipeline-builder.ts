import fs from 'fs';
import path from 'path';
import { DecoderOptions } from '../config';
import { CODEC_PAYLOAD_TYPES } from '@webrtc-broadcast/shared';

export class DecoderPipelineBuilder {
  /**
   * Generates a temporary SDP file required by ffmpeg to receive raw RTP streams.
   */
  public static createRtpSdpFile(opts: DecoderOptions): string {
    const sdpDir = path.join(process.cwd(), 'temp_sdp');
    if (!fs.existsSync(sdpDir)) {
      fs.mkdirSync(sdpDir, { recursive: true });
    }

    const sdpPath = path.join(sdpDir, `${opts.cameraId}_ingress.sdp`);
    const isH265 = opts.activeCodec === 'h265';
    const pt = isH265 ? CODEC_PAYLOAD_TYPES.H265 : CODEC_PAYLOAD_TYPES.H264;
    const codecName = isH265 ? 'H265' : 'H264';

    const sdpContent = [
      'v=0',
      'o=- 0 0 IN IP4 127.0.0.1',
      's=StudioDecoderStream',
      'c=IN IP4 127.0.0.1',
      't=0 0',
      `m=video ${opts.listenVideoPort} RTP/AVP ${pt}`,
      `a=rtpmap:${pt} ${codecName}/90000`,
      isH265 ? `a=fmtp:${pt} profile-id=1` : `a=fmtp:${pt} packetization-mode=1`,
      `m=audio ${opts.listenAudioPort} RTP/AVP ${CODEC_PAYLOAD_TYPES.OPUS}`,
      `a=rtpmap:${CODEC_PAYLOAD_TYPES.OPUS} opus/48000/2`,
    ].join('\r\n');

    fs.writeFileSync(sdpPath, sdpContent, 'utf8');
    return sdpPath;
  }

  /**
   * Builds FFmpeg decode & output arguments.
   */
  public static buildFfmpegArgs(opts: DecoderOptions, sdpPath: string): string[] {
    const args: string[] = [
      '-hide_banner',
      '-loglevel',
      'warning',
      '-protocol_whitelist',
      'file,udp,rtp',
      '-i',
      sdpPath,
    ];

    // Hardware accelerated video decoder selection
    if (opts.hwAccel === 'nvdec') {
      const decoder = opts.activeCodec === 'h265' ? 'hevc_cuvid' : 'h264_cuvid';
      args.push('-c:v', decoder);
    } else if (opts.hwAccel === 'qsv') {
      const decoder = opts.activeCodec === 'h265' ? 'hevc_qsv' : 'h264_qsv';
      args.push('-c:v', decoder);
    }

    // Configure Audio Decoder (Opus -> 48kHz PCM)
    args.push('-c:a', 'pcm_s16le', '-ar', '48000');

    // Configure Video Output Destination
    if (opts.output === 'sdi') {
      // Blackmagic DeckLink SDI Output
      args.push('-f', 'decklink', '-preroll', '0.4', `DeckLink SDI (${opts.sdiDeviceIndex || 0})`);
    } else if (opts.output === 'ndi') {
      // NDI Output (via NDI plugin / external pipe)
      args.push('-f', 'null', '-');
    } else if (opts.output === 'null') {
      // Benchmark / headless testing mode
      args.push('-f', 'null', '-');
    } else {
      // Display window output
      args.push('-f', 'sdl', `${opts.ndiName || opts.cameraId}`);
    }

    return args;
  }
}
