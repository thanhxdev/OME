import { CodecMode, CODEC_PAYLOAD_TYPES, Logger } from '@webrtc-broadcast/shared';

const logger = new Logger('SdpFilter');

/**
 * Filters and manipulates SDP to enforce broadcast codec modes:
 * - 'throughpass': preserves unmodified native SDP
 * - 'h264': enforces H.264 payload types in m=video line
 * - 'h265': injects and prioritizes H.265 RTP payload type 97
 */
export function filterSdpByCodec(sdp: string, codecMode: CodecMode): string {
  if (codecMode === 'throughpass') {
    // Keep unmodified stream
    return sdp;
  }

  const lines = sdp.split('\r\n');
  const videoIndex = lines.findIndex((l) => l.startsWith('m=video'));

  if (videoIndex === -1) {
    return sdp;
  }

  if (codecMode === 'h265') {
    // Inject H.265 payload type 97 if not already present
    const h265Pt = `${CODEC_PAYLOAD_TYPES.H265}`;
    const videoLine = lines[videoIndex];
    const parts = videoLine.split(' ');
    // parts format: m=video <port> <proto> <pt1> <pt2> ...
    if (!parts.includes(h265Pt)) {
      const header = parts.slice(0, 3);
      const payloads = parts.slice(3);
      // Place 97 at the front of payload list
      lines[videoIndex] = [...header, h265Pt, ...payloads].join(' ');

      // Insert H265 attributes right after m=video line
      const h265Attrs = [
        `a=rtpmap:${h265Pt} H265/90000`,
        `a=fmtp:${h265Pt} profile-id=1;tier-flag=0;level-id=120`,
        `a=rtcp-fb:${h265Pt} nack`,
        `a=rtcp-fb:${h265Pt} nack pli`,
        `a=rtcp-fb:${h265Pt} goog-remb`,
        `a=rtcp-fb:${h265Pt} transport-cc`,
      ];
      lines.splice(videoIndex + 1, 0, ...h265Attrs);
      logger.info(`Injected H.265 (PT ${h265Pt}) into SDP negotiation`);
    }
  } else if (codecMode === 'h264') {
    // Ensure H.264 payload types are prioritized in m=video
    const videoLine = lines[videoIndex];
    const parts = videoLine.split(' ');
    const header = parts.slice(0, 3);
    const payloads = parts.slice(3);

    // Find H264 PTs from rtpmap lines
    const h264Pts: string[] = [];
    for (const line of lines) {
      const match = line.match(/^a=rtpmap:(\d+)\s+H264\/90000/i);
      if (match) {
        h264Pts.push(match[1]);
      }
    }

    if (h264Pts.length > 0) {
      const otherPayloads = payloads.filter((pt) => !h264Pts.includes(pt));
      lines[videoIndex] = [...header, ...h264Pts, ...otherPayloads].join(' ');
      logger.info(`Prioritized H.264 PTs [${h264Pts.join(', ')}] in SDP`);
    }
  }

  return lines.join('\r\n');
}
