import React from 'react';
import { useControlSession } from './hooks/useControlSession';
import { TopBar } from './components/TopBar';
import { GlobalPresetBar } from './components/GlobalPresetBar';
import { BandwidthGauge } from './components/BandwidthGauge';
import { TallyMasterBus } from './components/TallyMasterBus';
import { CameraTable } from './components/CameraTable';
import { IntercomPanel } from './components/IntercomPanel';

export const App: React.FC = () => {
  const {
    connected,
    cameras,
    activePreset,
    programCam,
    previewCam,
    intercomActive,
    intercomTarget,
    totalBitrateKbps,
    setIntercomTarget,
    setIntercomPtt,
    applyGlobalPreset,
    setCameraCodecConfig,
    setCameraTally,
    setProgramCam,
    setPreviewCam,
    executeTake,
    executeCut,
  } = useControlSession();

  return (
    <>
      <TopBar connected={connected} totalBitrateKbps={totalBitrateKbps} />

      <main className="dashboard-content">
        {/* Row 1: Global Presets & WAN Bandwidth Gauge */}
        <section className="top-deck-grid">
          <GlobalPresetBar
            activePreset={activePreset}
            onSelectPreset={applyGlobalPreset}
          />
          <BandwidthGauge totalBitrateKbps={totalBitrateKbps} />
        </section>

        {/* Row 2: Tally Master Switcher */}
        <section>
          <TallyMasterBus
            cameras={cameras}
            programCam={programCam}
            previewCam={previewCam}
            onSelectProgram={setProgramCam}
            onSelectPreview={setPreviewCam}
            onTake={executeTake}
            onCut={() => executeCut(previewCam)}
          />
        </section>

        {/* Row 3: Granular Camera Configuration */}
        <section>
          <CameraTable
            cameras={cameras}
            onCodecChange={setCameraCodecConfig}
            onTallyChange={setCameraTally}
          />
        </section>

        {/* Row 4: Intercom Push-To-Talk */}
        <section>
          <IntercomPanel
            cameras={cameras}
            intercomTarget={intercomTarget}
            intercomActive={intercomActive}
            onTargetChange={setIntercomTarget}
            onPttChange={setIntercomPtt}
          />
        </section>
      </main>
    </>
  );
};

export default App;
