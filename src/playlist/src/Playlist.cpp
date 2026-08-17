#include <openmedia/playlist/Playlist.h>

namespace openmedia::playlist {

Playlist::Playlist() : m_currentIndex(0) {}
Playlist::~Playlist() {}

void Playlist::AddItem(const PlaylistItem& item) {
    m_items.push_back(item);
}

void Playlist::RemoveItem(int index) {
    if (index >= 0 && index < m_items.size()) {
        m_items.erase(m_items.begin() + index);
    }
}

PlaylistItem Playlist::GetCurrentItem() const {
    if (m_currentIndex >= 0 && m_currentIndex < m_items.size()) {
        return m_items[m_currentIndex];
    }
    return PlaylistItem{};
}

void Playlist::Next() {
    if (m_currentIndex < m_items.size() - 1) {
        m_currentIndex++;
    }
}

void Playlist::Previous() {
    if (m_currentIndex > 0) {
        m_currentIndex--;
    }
}

} // namespace openmedia::playlist
