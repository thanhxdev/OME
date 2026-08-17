#include "openmedia/monitoring/HealthCheck.h"

namespace openmedia {
namespace monitoring {

HealthCheck::HealthCheck() {
}

HealthCheck::~HealthCheck() {
}

SystemHealth HealthCheck::GetHealth() const {
    // TODO: Implement cross-platform hardware querying
    SystemHealth health = {0};
    health.cpu_usage_percent = 15.0; // dummy
    health.memory_usage_mb = 1024.0; // dummy
    return health;
}

} // namespace monitoring
} // namespace openmedia
