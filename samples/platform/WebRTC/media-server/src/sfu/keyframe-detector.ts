/**
 * Ultra-fast NAL unit inspector for RTP packets containing H.264 / H.265 video.
 * Analyzes the RTP payload following standard 12-byte RTP header.
 */
export class KeyframeDetector {
  /**
   * Checks if an RTP packet contains an H.264 Keyframe (IDR slice NAL type 5 or SPS/PPS).
   */
  public static isH264Keyframe(rtpPacket: Buffer): boolean {
    if (rtpPacket.length <= 12) return false;
    const payload = rtpPacket.subarray(12);
    if (payload.length === 0) return false;

    const firstByte = payload[0];
    const nalType = firstByte & 0x1f;

    // Single NAL unit packet
    if (nalType === 5 || nalType === 7 || nalType === 8) {
      return true;
    }

    // Fragmentation Unit A (FU-A) packet (nalType 28)
    if (nalType === 28 && payload.length > 1) {
      const fuHeader = payload[1];
      const isStart = (fuHeader & 0x80) !== 0;
      const originalNalType = fuHeader & 0x1f;
      if (isStart && (originalNalType === 5 || originalNalType === 7 || originalNalType === 8)) {
        return true;
      }
    }

    return false;
  }

  /**
   * Checks if an RTP packet contains an H.265 (HEVC) Keyframe (IDR NAL types 19, 20 or VPS 32, SPS 33, PPS 34).
   */
  public static isH265Keyframe(rtpPacket: Buffer): boolean {
    if (rtpPacket.length <= 12) return false;
    const payload = rtpPacket.subarray(12);
    if (payload.length === 0) return false;

    // HEVC NAL header is 2 bytes: Type is in bits 1-6 of byte 0: ((byte0 >> 1) & 0x3F)
    const nalType = (payload[0] >> 1) & 0x3f;

    // Single NAL unit
    if (nalType === 19 || nalType === 20 || nalType === 32 || nalType === 33 || nalType === 34) {
      return true;
    }

    // Fragmentation Unit (FU) packet in RFC 7798 is type 49
    if (nalType === 49 && payload.length > 2) {
      const fuHeader = payload[2];
      const isStart = (fuHeader & 0x80) !== 0;
      const originalNalType = fuHeader & 0x3f;
      if (isStart && (originalNalType === 19 || originalNalType === 20 || originalNalType === 32 || originalNalType === 33 || originalNalType === 34)) {
        return true;
      }
    }

    return false;
  }
}
