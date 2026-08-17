#pragma once

namespace openmedia {
namespace st2110 {

class PTPClock {
public:
    PTPClock();
    ~PTPClock();

    bool Sync();
    double GetCurrentTime();

private:
    bool synchronized_;
};

} // namespace st2110
} // namespace openmedia
