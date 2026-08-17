#pragma once
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <vector>
#include <string>

namespace openmedia::playlist {

struct PlaylistItem {
    std::string sourceUri;
    long long inPoint;
    long long outPoint;
};

class Playlist {
public:
    Playlist();
    ~Playlist();

    void AddItem(const PlaylistItem& item);
    void RemoveItem(int index);
    
    PlaylistItem GetCurrentItem() const;
    void Next();
    void Previous();

private:
    std::vector<PlaylistItem> m_items;
    int m_currentIndex;
};

} // namespace openmedia::playlist
