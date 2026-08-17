#pragma once

namespace openmedia {
namespace outputs {
namespace decklink {

class DeckLinkOutput {
public:
    DeckLinkOutput();
    ~DeckLinkOutput();

    bool Start(int device_index, int video_mode);
    void Stop();

private:
    bool started_;
};

} // namespace decklink
} // namespace outputs
} // namespace openmedia
