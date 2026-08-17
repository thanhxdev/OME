#include "openmedia/outputs/decklink/DeckLinkSource.h"

namespace openmedia {
namespace outputs {
namespace decklink {

DeckLinkSource::DeckLinkSource() : started_(false) {
}

DeckLinkSource::~DeckLinkSource() {
    Stop();
}

bool DeckLinkSource::Start(int device_index, int video_mode) {
    if (started_) return true;
    
    // TODO: Initialize Blackmagic DeckLink API
    // Get IDeckLinkIterator, find device, set IDeckLinkInput
    
    started_ = true;
    return true;
}

void DeckLinkSource::Stop() {
    if (!started_) return;
    
    // TODO: Release DeckLink resources
    
    started_ = false;
}

} // namespace decklink
} // namespace outputs
} // namespace openmedia
