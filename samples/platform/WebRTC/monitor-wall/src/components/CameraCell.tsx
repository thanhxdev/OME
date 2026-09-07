import React from 'react';
import { CameraStreamState } from '../hooks/useSignaling';
import { VideoCanvas } from './VideoCanvas';
import { VUMeter } from './VUMeter';

interface CameraCellProps {
  camera: CameraStreamState;
  onSelect?: (cameraId: string) => void;
}

export const CameraCell: React.FC<CameraCellProps> = ({ camera, onSelect }) => {
  const tallyClass =
    camera.tally === 'on-air'
      ? 'tally-on-air'
      : camera.tally === 'preview'
      ? 'tally-preview'
      : '';

  const bitrateFormatted =
    camera.currentBitrate >= 1000
      ? `${(camera.currentBitrate / 1000).toFixed(1)}M`
      : `${camera.currentBitrate}k`;

  return (
    <div
      className={`camera-cell ${tallyClass}`}
      onClick={() => onSelect && onSelect(camera.id)}
    >
      {/* 60fps Video Simulation / Stream canvas */}
      <VideoCanvas
        cameraId={camera.id}
        cameraName={camera.name || camera.id}
        online={camera.online}
        codec={camera.codec}
        fps={camera.fps}
      />

      {/* Top Banner: Camera Label & Tally state */}
      <div className="overlay-top">
        <div className="camera-tag">
          <span className="camera-num">{camera.id.toUpperCase()}</span>
          <span className="camera-name">{camera.name || `Camera ${camera.id}`}</span>
        </div>

        {camera.tally !== 'off' && (
          <div className={`tally-badge ${camera.tally}`}>
            {camera.tally === 'on-air' ? 'ON AIR' : 'PREVIEW'}
          </div>
        )}
      </div>

      {/* Bottom Banner: Real-time Telemetry & Audio VU meter */}
      <div className="overlay-bottom">
        <div className="telemetry-strip">
          <div className="telemetry-item">
            <span className="label">CODEC:</span>
            <span className="val">{camera.codec.toUpperCase()}</span>
          </div>
          <div className="telemetry-item">
            <span className="label">BR:</span>
            <span className="val">{bitrateFormatted}</span>
          </div>
          <div className="telemetry-item">
            <span className="label">FPS:</span>
            <span className={`val ${camera.fps < 50 && camera.online ? 'warn' : ''}`}>
              {camera.fps}
            </span>
          </div>
          <div className="telemetry-item">
            <span className="label">LOSS:</span>
            <span
              className={`val ${
                camera.packetLoss > 2 ? 'bad' : camera.packetLoss > 0.5 ? 'warn' : ''
              }`}
            >
              {camera.packetLoss.toFixed(1)}%
            </span>
          </div>
          <div className="telemetry-item">
            <span className="label">RTT:</span>
            <span className="val">{camera.rtt}ms</span>
          </div>
        </div>

        {/* Stereophonic VU Meter */}
        <VUMeter active={camera.online} />
      </div>

      {/* Offline Overlay if camera is disconnected */}
      {!camera.online && (
        <div className="camera-offline-veil">
          <div className="offline-text">STREAM OFFLINE</div>
        </div>
      )}
    </div>
  );
};
