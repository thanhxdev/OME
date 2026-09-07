import React, { useState } from 'react';
import { useSignaling } from './hooks/useSignaling';
import { HeaderBar, GridLayout } from './components/HeaderBar';
import { CameraGrid } from './components/CameraGrid';

export const App: React.FC = () => {
  const [layout, setLayout] = useState<GridLayout>('2x5');
  const { connected, cameras, totalWanTxKbps, cpuLoad, activeSession } = useSignaling();

  const onlineCount = cameras.filter((c) => c.online).length;

  return (
    <>
      <HeaderBar
        layout={layout}
        onLayoutChange={setLayout}
        connected={connected}
        totalWanTxKbps={totalWanTxKbps}
        cpuLoad={cpuLoad}
        onlineCount={onlineCount}
        totalCount={cameras.length}
        activeSession={activeSession}
      />
      <CameraGrid cameras={cameras} layout={layout} />
    </>
  );
};

export default App;
