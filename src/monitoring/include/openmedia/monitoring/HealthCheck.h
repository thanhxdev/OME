#pragma once

#include <string>

namespace openmedia {
namespace monitoring {

struct SystemHealth {
    double cpu_usage_percent;
    double memory_usage_mb;
    double gpu_usage_percent;
    double temperature_celsius;
};

class HealthCheck {
public:
    HealthCheck();
    ~HealthCheck();

    SystemHealth GetHealth() const;
};

} // namespace monitoring
} // namespace openmedia
