import React from 'react';
import { CameraControlState } from '../hooks/useControlSession';

interface IntercomPanelProps {
  cameras: CameraControlState[];
  intercomTarget: string;
  intercomActive: boolean;
  onTargetChange: (target: string) => void;
  onPttChange: (active: boolean) => void;
}

export const IntercomPanel: React.FC<IntercomPanelProps> = ({
  cameras,
  intercomTarget,
  intercomActive,
  onTargetChange,
  onPttChange,
}) => {
  return (
    <div className="intercom-deck">
      <div className="intercom-left">
        <div>
          <div className="intercom-title">Director Intercom & Talkback</div>
          <div className="intercom-desc">
            Low-latency Opus bidirectional audio channel to camera operators
          </div>
        </div>

        <div>
          <select
            className="ctrl-select"
            value={intercomTarget}
            onChange={(e) => onTargetChange(e.target.value)}
            style={{ padding: '8px 12px' }}
          >
            <option value="all">BROADCAST TO ALL OPERATORS</option>
            {cameras.map((c) => (
              <option key={`target-${c.id}`} value={c.id}>
                {c.id.toUpperCase()} - {c.name || `Camera ${c.id}`}
              </option>
            ))}
          </select>
        </div>
      </div>

      <button
        className={`ptt-button ${intercomActive ? 'active' : ''}`}
        onMouseDown={() => onPttChange(true)}
        onMouseUp={() => onPttChange(false)}
        onTouchStart={() => onPttChange(true)}
        onTouchEnd={() => onPttChange(false)}
      >
        <span className="wave-dot" />
        <span>{intercomActive ? 'TRANSMITTING...' : 'PUSH TO TALK'}</span>
      </button>
    </div>
  );
};
