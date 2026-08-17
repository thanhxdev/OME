#pragma once

namespace openmedia {
namespace outputs {
namespace decklink {

class DeckLinkSource {
public:
    DeckLinkSource();
    ~DeckLinkSource();

    bool Start(int device_index, int video_mode);
    void Stop();

private:
    bool started_;
};

} // namespace decklink
} // namespace outputs
} // namespace openmedia
