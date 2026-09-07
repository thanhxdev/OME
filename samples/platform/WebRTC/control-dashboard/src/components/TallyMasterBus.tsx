import React from 'react';
import { CameraControlState } from '../hooks/useControlSession';

interface TallyMasterBusProps {
  cameras: CameraControlState[];
  programCam: string;
  previewCam: string;
  onSelectProgram: (camId: string) => void;
  onSelectPreview: (camId: string) => void;
  onTake: () => void;
  onCut: () => void;
}

export const TallyMasterBus: React.FC<TallyMasterBusProps> = ({
  cameras,
  programCam,
  previewCam,
  onSelectProgram,
  onSelectPreview,
  onTake,
  onCut,
}) => {
  return (
    <div className="panel-card">
      <div className="panel-header">
        <div className="panel-title">Tally Master Bus & Video Switcher</div>
        <div style={{ fontFamily: 'var(--font-mono)', fontSize: '11px', color: 'var(--text-muted)' }}>
          PGM: <strong style={{ color: 'var(--tally-program)' }}>{programCam.toUpperCase()}</strong> | PVW:{' '}
          <strong style={{ color: 'var(--tally-preview)' }}>{previewCam.toUpperCase()}</strong>
        </div>
      </div>

      <div className="tally-bus-container">
        {/* PROGRAM BUS */}
        <div className="bus-row">
          <div className="bus-label program">Program (On-Air)</div>
          <div className="bus-buttons">
            {cameras.map((c, idx) => {
              const isSelected = programCam === c.id;
              return (
                <button
                  key={`pgm-${c.id}`}
                  className={`bus-btn ${isSelected ? 'active-program' : ''}`}
                  onClick={() => onSelectProgram(c.id)}
                  title={`${c.name || c.id} - Set as Program`}
                >
                  {idx + 1}
                </button>
              );
            })}
          </div>
          <div className="bus-actions">
            <button className="action-btn-take" onClick={onTake} title="Swap Program and Preview">
              TAKE
            </button>
          </div>
        </div>

        {/* PREVIEW BUS */}
        <div className="bus-row">
          <div className="bus-label preview">Preview (Next)</div>
          <div className="bus-buttons">
            {cameras.map((c, idx) => {
              const isSelected = previewCam === c.id;
              return (
                <button
                  key={`pvw-${c.id}`}
                  className={`bus-btn ${isSelected ? 'active-preview' : ''}`}
                  onClick={() => onSelectPreview(c.id)}
                  title={`${c.name || c.id} - Set as Preview`}
                >
                  {idx + 1}
                </button>
              );
            })}
          </div>
          <div className="bus-actions">
            <button
              className="action-btn-cut"
              onClick={onCut}
              title="Immediate cut to selected preview camera"
            >
              CUT
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
