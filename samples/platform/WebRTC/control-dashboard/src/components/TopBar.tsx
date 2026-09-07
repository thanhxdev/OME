import React from 'react';

interface TopBarProps {
  connected: boolean;
  totalBitrateKbps: number;
}

export const TopBar: React.FC<TopBarProps> = ({ connected, totalBitrateKbps }) => {
  const mbps = (totalBitrateKbps / 1000).toFixed(1);

  return (
    <header className="control-topbar">
      <div className="brand-wrap">
        <div className="brand-icon">M</div>
        <div>
          <h1 className="brand-title">WebRTC Master Control & Engineering</h1>
          <div className="brand-subtitle">
            BROADCAST PRODUCTION SUITE • SESSION: BROADCAST-MAIN
          </div>
        </div>
      </div>

      <div className="topbar-actions">
        <div className="conn-pill">
          <span className={`conn-dot ${connected ? 'online' : 'offline'}`} />
          <span>{connected ? 'SIGNALING ACTIVE' : 'RECONNECTING'}</span>
        </div>

        <div className="conn-pill">
          <span style={{ color: 'var(--text-muted)' }}>TOTAL UPLINK:</span>
          <span style={{ color: 'var(--accent-cyan)' }}>{mbps} Mbps</span>
        </div>
      </div>
    </header>
  );
};
