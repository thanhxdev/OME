import React from 'react';
import {
  GlobalPreset,
  PRESET_CONFIGS,
} from '../hooks/useControlSession';

interface GlobalPresetBarProps {
  activePreset: GlobalPreset;
  onSelectPreset: (preset: Exclude<GlobalPreset, 'custom'>) => void;
}

export const GlobalPresetBar: React.FC<GlobalPresetBarProps> = ({
  activePreset,
  onSelectPreset,
}) => {
  const presetKeys: (keyof typeof PRESET_CONFIGS)[] = [
    'max_quality',
    'balanced',
    'bandwidth_saver',
    'minimal',
  ];

  return (
    <div className="panel-card">
      <div className="panel-header">
        <div className="panel-title">
          <span>Global Codec & Quality Profiles</span>
          {activePreset === 'custom' && (
            <span
              style={{
                background: 'rgba(255, 214, 0, 0.15)',
                color: 'var(--accent-yellow)',
                padding: '2px 8px',
                borderRadius: '4px',
                fontSize: '10px',
                fontFamily: 'var(--font-mono)',
              }}
            >
              CUSTOM OVERRIDE
            </span>
          )}
        </div>
      </div>

      <div className="presets-grid">
        {presetKeys.map((key) => {
          const item = PRESET_CONFIGS[key];
          const isSelected = activePreset === key;

          return (
            <div
              key={key}
              className={`preset-card ${isSelected ? 'active' : ''}`}
              onClick={() => onSelectPreset(key)}
            >
              <div className="preset-name">{item.name}</div>
              <div className="preset-meta">
                {item.codec.toUpperCase()} • {(item.bitrate / 1000).toFixed(1)}M • {item.fps}fps
              </div>
              <div className="preset-desc">{item.desc}</div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
