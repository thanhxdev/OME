import React from 'react';

interface BandwidthGaugeProps {
  totalBitrateKbps: number;
}

export const BandwidthGauge: React.FC<BandwidthGaugeProps> = ({ totalBitrateKbps }) => {
  const limitKbps = 120000; // 120 Mbps capacity
  const warningKbps = 95000; // 95 Mbps safe ceiling

  const currentMbps = (totalBitrateKbps / 1000).toFixed(1);
  const percent = Math.min(100, Math.round((totalBitrateKbps / limitKbps) * 100));

  let color = 'var(--accent-green)';
  let statusText = 'NORMAL / OPTIMAL';

  if (totalBitrateKbps >= limitKbps) {
    color = 'var(--accent-red)';
    statusText = 'OVERLOAD LIMIT REACHED';
  } else if (totalBitrateKbps >= warningKbps) {
    color = 'var(--accent-yellow)';
    statusText = 'HIGH LOAD WARNING';
  }

  return (
    <div className="panel-card">
      <div className="panel-header">
        <div className="panel-title">WAN Uplink Throughput</div>
        <span
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: '11px',
            color: color,
            fontWeight: 700,
          }}
        >
          {statusText}
        </span>
      </div>

      <div className="bandwidth-gauge-wrap">
        <div className="gauge-stats-row">
          <div className="gauge-big-num" style={{ color }}>
            {currentMbps} <span style={{ fontSize: '14px', color: 'var(--text-muted)' }}>Mbps</span>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div style={{ color: 'var(--text-secondary)' }}>Capacity: 120.0 Mbps</div>
            <div style={{ color: 'var(--text-muted)' }}>Load: {percent}%</div>
          </div>
        </div>

        <div className="gauge-meter-bar">
          <div
            className="gauge-meter-fill"
            style={{
              width: `${percent}%`,
              backgroundColor: color,
              boxShadow: `0 0 10px ${color}`,
            }}
          />
        </div>
      </div>
    </div>
  );
};
