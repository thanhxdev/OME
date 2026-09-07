import React, { useEffect, useState } from 'react';

interface VUMeterProps {
  active?: boolean;
}

export const VUMeter: React.FC<VUMeterProps> = ({ active = true }) => {
  const [levels, setLevels] = useState({ left: 72, right: 68, peakL: 82, peakR: 79 });

  useEffect(() => {
    if (!active) {
      setLevels({ left: 0, right: 0, peakL: 0, peakR: 0 });
      return;
    }

    const interval = setInterval(() => {
      // Simulate natural speech / broadcast stadium crowd audio envelope
      const base = 55 + Math.sin(Date.now() / 400) * 20;
      const noiseL = Math.random() * 20;
      const noiseR = Math.random() * 20;

      const left = Math.min(100, Math.max(0, Math.round(base + noiseL)));
      const right = Math.min(100, Math.max(0, Math.round(base + noiseR)));

      setLevels((prev) => ({
        left,
        right,
        peakL: Math.max(left, prev.peakL - 2),
        peakR: Math.max(right, prev.peakR - 2),
      }));
    }, 80);

    return () => clearInterval(interval);
  }, [active]);

  return (
    <div className="vu-container">
      <div className="vu-channel">
        <span className="vu-ch-label">L</span>
        <div className="vu-track">
          <div className="vu-bar" style={{ width: `${levels.left}%` }} />
          <div className="vu-peak" style={{ left: `${Math.max(0, levels.peakL - 2)}%` }} />
        </div>
      </div>
      <div className="vu-channel">
        <span className="vu-ch-label">R</span>
        <div className="vu-track">
          <div className="vu-bar" style={{ width: `${levels.right}%` }} />
          <div className="vu-peak" style={{ left: `${Math.max(0, levels.peakR - 2)}%` }} />
        </div>
      </div>
    </div>
  );
};
