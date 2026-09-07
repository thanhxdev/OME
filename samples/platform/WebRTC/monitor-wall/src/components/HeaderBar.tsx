import React from 'react';

export type GridLayout = '1x1' | '2x2' | '3x3' | '2x5';

interface HeaderBarProps {
  layout: GridLayout;
  onLayoutChange: (layout: GridLayout) => void;
  connected: boolean;
  totalWanTxKbps: number;
  cpuLoad: number;
  onlineCount: number;
  totalCount: number;
  activeSession: string;
}

export const HeaderBar: React.FC<HeaderBarProps> = ({
  layout,
  onLayoutChange,
  connected,
  totalWanTxKbps,
  cpuLoad,
  onlineCount,
  totalCount,
  activeSession,
}) => {
  const mbps = (totalWanTxKbps / 1000).toFixed(1);
  const isOverload = totalWanTxKbps >= 95000;

  return (
    <header className="monitor-header">
      <div className="brand-section">
        <div className="live-badge">
          <span className="live-dot" />
          <span>ON AIR</span>
        </div>
        <div>
          <div className="header-title">10-Camera Multiview Monitor Wall</div>
          <div className="header-subtitle">
            SESSION: {activeSession.toUpperCase()} • {connected ? 'LINKED' : 'STANDBY'}
          </div>
        </div>
      </div>

      <div className="header-stats">
        <div className="stat-pill">
          <span className="label">WAN TX:</span>
          <span className={`value ${isOverload ? 'critical' : ''}`}>{mbps} Mbps</span>
        </div>

        <div className="stat-pill">
          <span className="label">HOST CPU:</span>
          <span className={`value ${cpuLoad > 80 ? 'critical' : ''}`}>{cpuLoad}%</span>
        </div>

        <div className="stat-pill">
          <span className="label">CAMERAS:</span>
          <span className="value">
            {onlineCount}/{totalCount}
          </span>
        </div>

        {/* Layout Switcher */}
        <div className="layout-selector">
          <button
            className={`layout-btn ${layout === '1x1' ? 'active' : ''}`}
            onClick={() => onLayoutChange('1x1')}
            title="Single Camera Focus"
          >
            1x1
          </button>
          <button
            className={`layout-btn ${layout === '2x2' ? 'active' : ''}`}
            onClick={() => onLayoutChange('2x2')}
            title="Quad-Split (4 Cameras)"
          >
            2x2
          </button>
          <button
            className={`layout-btn ${layout === '3x3' ? 'active' : ''}`}
            onClick={() => onLayoutChange('3x3')}
            title="9-Camera Matrix"
          >
            3x3
          </button>
          <button
            className={`layout-btn ${layout === '2x5' ? 'active' : ''}`}
            onClick={() => onLayoutChange('2x5')}
            title="10-Camera Master Multiview (2x5)"
          >
            2x5
          </button>
        </div>
      </div>
    </header>
  );
};
