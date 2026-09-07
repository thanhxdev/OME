import React, { useState } from 'react';
import { CameraStreamState } from '../hooks/useSignaling';
import { GridLayout } from './HeaderBar';
import { CameraCell } from './CameraCell';

interface CameraGridProps {
  cameras: CameraStreamState[];
  layout: GridLayout;
}

export const CameraGrid: React.FC<CameraGridProps> = ({ cameras, layout }) => {
  const [selectedCamId, setSelectedCamId] = useState<string>('cam-01');

  // Determine which cameras to display
  let displayedCameras: CameraStreamState[] = [];

  switch (layout) {
    case '1x1': {
      const found = cameras.find((c) => c.id === selectedCamId) || cameras[0];
      displayedCameras = found ? [found] : [];
      break;
    }
    case '2x2':
      displayedCameras = cameras.slice(0, 4);
      break;
    case '3x3':
      displayedCameras = cameras.slice(0, 9);
      break;
    case '2x5':
    default:
      displayedCameras = cameras.slice(0, 10);
      break;
  }

  const gridClass = `grid-container grid-layout-${layout}`;

  return (
    <main className={gridClass}>
      {displayedCameras.map((cam) => (
        <CameraCell
          key={cam.id}
          camera={cam}
          onSelect={(id) => setSelectedCamId(id)}
        />
      ))}
    </main>
  );
};
