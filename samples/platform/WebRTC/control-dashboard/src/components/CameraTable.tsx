import React from 'react';
import { CodecMode, TallyState } from '@webrtc-broadcast/shared';
import { CameraControlState } from '../hooks/useControlSession';

interface CameraTableProps {
  cameras: CameraControlState[];
  onCodecChange: (cameraId: string, codec: CodecMode, bitrate?: number, fps?: number) => void;
  onTallyChange: (cameraId: string, state: TallyState) => void;
}

export const CameraTable: React.FC<CameraTableProps> = ({
  cameras,
  onCodecChange,
  onTallyChange,
}) => {
  return (
    <div className="panel-card">
      <div className="panel-header">
        <div className="panel-title">Per-Camera Granular Stream Configuration</div>
        <div style={{ fontSize: '11px', color: 'var(--text-muted)' }}>
          10 INDEPENDENT ENCODER CHANNELS
        </div>
      </div>

      <div className="camera-table-wrap">
        <table className="camera-table">
          <thead>
            <tr>
              <th>Camera</th>
              <th>Status</th>
              <th>Codec Mode</th>
              <th>Bitrate (Target)</th>
              <th>Framerate</th>
              <th>Tally State</th>
              <th>Loss / RTT</th>
            </tr>
          </thead>
          <tbody>
            {cameras.map((cam) => {
              const bitrateDisplay =
                cam.currentBitrate >= 1000
                  ? `${(cam.currentBitrate / 1000).toFixed(1)}M`
                  : `${cam.currentBitrate}k`;

              return (
                <tr key={cam.id}>
                  {/* Camera ID & Name */}
                  <td>
                    <div className="cam-id-badge">{cam.id.toUpperCase()}</div>
                    <div className="cam-name-sub">{cam.name || `Camera ${cam.id}`}</div>
                  </td>

                  {/* Online status */}
                  <td>
                    <span
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '6px',
                        fontFamily: 'var(--font-mono)',
                        fontSize: '11px',
                        color: cam.online ? 'var(--accent-green)' : 'var(--text-muted)',
                      }}
                    >
                      <span
                        style={{
                          width: '6px',
                          height: '6px',
                          borderRadius: '50%',
                          background: cam.online ? 'var(--accent-green)' : 'var(--text-muted)',
                        }}
                      />
                      {cam.online ? 'ACTIVE' : 'OFFLINE'}
                    </span>
                  </td>

                  {/* Codec dropdown */}
                  <td>
                    <select
                      className="ctrl-select"
                      value={cam.codec}
                      onChange={(e) =>
                        onCodecChange(cam.id, e.target.value as CodecMode, cam.currentBitrate, cam.fps)
                      }
                    >
                      <option value="throughpass">Throughpass (SDI direct)</option>
                      <option value="h264">H.264 (AVC Broadcast)</option>
                      <option value="h265">H.265 (HEVC Low-Bandwidth)</option>
                    </select>
                  </td>

                  {/* Bitrate slider & display */}
                  <td>
                    <div className="bitrate-slider-group">
                      <input
                        type="range"
                        className="bitrate-slider"
                        min="1000"
                        max="20000"
                        step="500"
                        value={cam.currentBitrate}
                        onChange={(e) =>
                          onCodecChange(cam.id, cam.codec, parseInt(e.target.value, 10), cam.fps)
                        }
                      />
                      <span className="bitrate-val-text">{bitrateDisplay}</span>
                    </div>
                  </td>

                  {/* FPS dropdown */}
                  <td>
                    <select
                      className="ctrl-select"
                      value={cam.fps}
                      onChange={(e) =>
                        onCodecChange(
                          cam.id,
                          cam.codec,
                          cam.currentBitrate,
                          parseInt(e.target.value, 10)
                        )
                      }
                    >
                      <option value="60">60 fps (Full)</option>
                      <option value="50">50 fps (PAL)</option>
                      <option value="30">30 fps (Half)</option>
                      <option value="25">25 fps (Cinematic)</option>
                    </select>
                  </td>

                  {/* Tally state selector buttons */}
                  <td>
                    <div style={{ display: 'flex', gap: '4px' }}>
                      <button
                        className={`tally-pill-btn ${cam.tally === 'off' ? 'active' : ''}`}
                        onClick={() => onTallyChange(cam.id, 'off')}
                      >
                        OFF
                      </button>
                      <button
                        className={`tally-pill-btn ${cam.tally === 'preview' ? 'preview' : ''}`}
                        onClick={() => onTallyChange(cam.id, 'preview')}
                      >
                        PVW
                      </button>
                      <button
                        className={`tally-pill-btn ${cam.tally === 'on-air' ? 'on-air' : ''}`}
                        onClick={() => onTallyChange(cam.id, 'on-air')}
                      >
                        PGM
                      </button>
                    </div>
                  </td>

                  {/* Loss / RTT */}
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: '11px' }}>
                    <span
                      style={{
                        color:
                          cam.packetLoss > 2
                            ? 'var(--accent-red)'
                            : cam.packetLoss > 0.5
                            ? 'var(--accent-yellow)'
                            : 'var(--text-secondary)',
                      }}
                    >
                      {cam.packetLoss.toFixed(1)}%
                    </span>{' '}
                    / <span style={{ color: 'var(--text-muted)' }}>{cam.rtt}ms</span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};
