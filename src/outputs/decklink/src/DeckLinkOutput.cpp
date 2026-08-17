#include "openmedia/outputs/decklink/DeckLinkOutput.h"

namespace openmedia {
namespace outputs {
namespace decklink {

DeckLinkOutput::DeckLinkOutput() : started_(false) {
}

DeckLinkOutput::~DeckLinkOutput() {
    Stop();
}

bool DeckLinkOutput::Start(int device_index, int video_mode) {
    if (started_) return true;
    
    // TODO: Initialize Blackmagic DeckLink API
    // Get IDeckLinkIterator, find device, set IDeckLinkOutput
    
    started_ = true;
    return true;
}

void DeckLinkOutput::Stop() {
    if (!started_) return;
    
    // TODO: Release DeckLink resources
    
    started_ = false;
}

} // namespace decklink
} // namespace outputs
} // namespace openmedia
