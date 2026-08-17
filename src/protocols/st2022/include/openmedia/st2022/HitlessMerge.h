#pragma once

namespace openmedia {
namespace st2022 {

class HitlessMerge {
public:
    HitlessMerge();
    ~HitlessMerge();

    bool Enable(bool enable);
    bool IsEnabled() const;

private:
    bool enabled_;
};

} // namespace st2022
} // namespace openmedia
