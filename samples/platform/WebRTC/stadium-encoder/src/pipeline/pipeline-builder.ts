import { EncoderOptions } from '../config';
import { CODEC_PAYLOAD_TYPES } from '@webrtc-broadcast/shared';

export class PipelineBuilder {
  /**
   * Builds the media pipeline arguments based on input device and active codec mode.
   */
  public static buildFfmpegArgs(opts: EncoderOptions): string[] {
    const args: string[] = ['-hide_banner', '-loglevel', 'warning'];

    // 1. Configure Video Input
    if (opts.inputType === 'sdi') {
      args.push('-f', 'decklink', '-i', `DeckLink SDI (${opts.deviceIndex})`);
    } else if (opts.inputType === 'hdmi') {
      const isWin = process.platform === 'win32';
      if (isWin) {
        args.push('-f', 'dshow', '-i', `video=HDMI Video Capture ${opts.deviceIndex}:audio=Digital Audio Interface`);
      } else {
        args.push('-f', 'v4l2', '-i', `/dev/video${opts.deviceIndex}`);
      }
    } else {
      // Test pattern generator: -re forces real-time broadcast playback
      args.push(
        '-re',
        '-f',
        'lavfi',
        '-i',
        `testsrc=size=${opts.resolution}:rate=${opts.fps}`,
        '-f',
        'lavfi',
        '-i',
        'sine=frequency=1000:sample_rate=48000'
      );
    }

    const preset =
      opts.hwAccel === 'nvenc' ? 'p1' : opts.hwAccel === 'qsv' ? 'veryfast' : 'ultrafast';

    // 2. Configure Video Codec
    if (opts.codec === 'throughpass') {
      args.push('-map', '0:v', '-c:v', 'copy', '-payload_type', `${CODEC_PAYLOAD_TYPES.H264}`);
    } else if (opts.codec === 'h265') {
      const videoEncoder =
        opts.hwAccel === 'nvenc'
          ? 'hevc_nvenc'
          : opts.hwAccel === 'qsv'
          ? 'hevc_qsv'
          : 'libx265';

      args.push(
        '-map',
        '0:v',
        '-pix_fmt',
        'yuv420p',
        '-c:v',
        videoEncoder,
        '-preset',
        preset,
        '-tune',
        'zerolatency',
        '-b:v',
        `${opts.bitrate}k`,
        '-maxrate',
        `${opts.bitrate}k`,
        '-bufsize',
        `${Math.round(opts.bitrate / 2)}k`,
        '-g',
        `${opts.fps * 2}`,
        '-payload_type',
        `${CODEC_PAYLOAD_TYPES.H265}`
      );
    } else {
      // H.264 mode
      const videoEncoder =
        opts.hwAccel === 'nvenc'
          ? 'h264_nvenc'
          : opts.hwAccel === 'qsv'
          ? 'h264_qsv'
          : 'libx264';

      args.push(
        '-map',
        '0:v',
        '-pix_fmt',
        'yuv420p',
        '-c:v',
        videoEncoder,
        '-profile:v',
        'high',
        '-preset',
        preset,
        '-tune',
        'zerolatency',
        '-b:v',
        `${opts.bitrate}k`,
        '-maxrate',
        `${opts.bitrate}k`,
        '-bufsize',
        `${Math.round(opts.bitrate / 2)}k`,
        '-g',
        `${opts.fps * 2}`,
        '-payload_type',
        `${CODEC_PAYLOAD_TYPES.H264}`
      );
    }

    // Output 1: Video RTP
    args.push('-an', '-f', 'rtp', `rtp://${opts.sfuHost}:${opts.sfuVideoPort}`);

    // Output 2: Audio Opus RTP (Input 1 is audio)
    args.push(
      '-map',
      '1:a',
      '-c:a',
      'libopus',
      '-b:a',
      '128k',
      '-ar',
      '48000',
      '-ac',
      '2',
      '-payload_type',
      `${CODEC_PAYLOAD_TYPES.OPUS}`,
      '-vn',
      '-f',
      'rtp',
      `rtp://${opts.sfuHost}:${opts.sfuAudioPort}`
    );

    return args;
  }
}
