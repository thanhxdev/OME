/// @file WorkerPool.cpp
/// @brief Thread pool with priority task scheduling

#include <openmedia/worker_pool/WorkerPool.h>
#include <openmedia/core/Logger.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <queue>
#include <thread>
#include <vector>

namespace openmedia::worker_pool {

struct TaskEntry {
    TaskId id;
    TaskPriority priority;
    TaskFunction function;

    bool operator>(const TaskEntry& other) const {
        return static_cast<uint32_t>(priority) > static_cast<uint32_t>(other.priority);
    }
};

struct WorkerPool::Impl {
    WorkerPoolConfig config;
    std::vector<std::thread> workers;
    std::atomic<bool> running{false};

    // Priority queue (min-heap: lower priority value = higher priority)
    std::priority_queue<TaskEntry, std::vector<TaskEntry>, std::greater<>> taskQueue;
    std::mutex queueMutex;
    std::condition_variable queueCV;

    // Task tracking
    std::atomic<TaskId> nextTaskId{1};
    std::atomic<uint32_t> activeCount{0};

    // Statistics
    std::atomic<uint64_t> totalSubmitted{0};
    std::atomic<uint64_t> totalCompleted{0};
    std::atomic<uint64_t> totalCancelled{0};
    std::atomic<uint64_t> totalFailed{0};
    std::atomic<uint64_t> totalStolen{0};

    void WorkerFunction(uint32_t threadIndex) {
        std::string threadName = config.namePrefix + "-" + std::to_string(threadIndex);
        core::Logger::SDebug("WorkerPool", "Worker thread {} started", threadName);

        while (running.load()) {
            TaskEntry task;
            {
                std::unique_lock lock(queueMutex);
                queueCV.wait(lock, [this] {
                    return !taskQueue.empty() || !running.load();
                });

                if (!running.load() && taskQueue.empty()) break;
                if (taskQueue.empty()) continue;

                task = taskQueue.top();
                taskQueue.pop();
            }

            activeCount.fetch_add(1);

            try {
                task.function();
                totalCompleted.fetch_add(1);
            } catch (const std::exception& e) {
                totalFailed.fetch_add(1);
                core::Logger::SError("WorkerPool",
                    "Task {} failed in {}: {}", task.id, threadName, e.what());
            } catch (...) {
                totalFailed.fetch_add(1);
                core::Logger::SError("WorkerPool",
                    "Task {} failed in {} with unknown exception",
                    task.id, threadName);
            }

            activeCount.fetch_sub(1);
        }

        core::Logger::SDebug("WorkerPool", "Worker thread {} stopped",
                            config.namePrefix + "-" + std::to_string(threadIndex));
    }
};

WorkerPool::WorkerPool(const WorkerPoolConfig& config)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->config = config;
    if (m_impl->config.threadCount == 0) {
        m_impl->config.threadCount = std::max(2u,
            std::thread::hardware_concurrency());
    }
}

WorkerPool::~WorkerPool() {
    Stop();
}

core::VoidResult WorkerPool::Start() {
    if (m_impl->running.load()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "Worker pool already running"});
    }

    m_impl->running.store(true);
    m_impl->workers.reserve(m_impl->config.threadCount);

    for (uint32_t i = 0; i < m_impl->config.threadCount; ++i) {
        m_impl->workers.emplace_back(
            [this, i] { m_impl->WorkerFunction(i); });
    }

    core::Logger::SInfo("WorkerPool", "Started with {} threads",
                       m_impl->config.threadCount);
    return {};
}

void WorkerPool::Stop() {
    if (!m_impl->running.load()) return;

    m_impl->running.store(false);
    m_impl->queueCV.notify_all();

    for (auto& thread : m_impl->workers) {
        if (thread.joinable()) thread.join();
    }
    m_impl->workers.clear();

    core::Logger::SInfo("WorkerPool", "Stopped. Total: submitted={}, completed={}, failed={}",
        m_impl->totalSubmitted.load(),
        m_impl->totalCompleted.load(),
        m_impl->totalFailed.load());
}

bool WorkerPool::IsRunning() const {
    return m_impl->running.load();
}

TaskId WorkerPool::SubmitTask(TaskPriority priority, TaskFunction task) {
    TaskId id = m_impl->nextTaskId.fetch_add(1);

    {
        std::lock_guard lock(m_impl->queueMutex);
        m_impl->taskQueue.push({id, priority, std::move(task)});
    }
    m_impl->queueCV.notify_one();
    m_impl->totalSubmitted.fetch_add(1);

    return id;
}

bool WorkerPool::Cancel(TaskId /*taskId*/) {
    // Simple implementation: cannot cancel once submitted to queue
    // A more sophisticated version would track task status
    m_impl->totalCancelled.fetch_add(1);
    return false;
}

uint32_t WorkerPool::GetThreadCount() const {
    return m_impl->config.threadCount;
}

uint32_t WorkerPool::GetPendingTaskCount() const {
    std::lock_guard lock(m_impl->queueMutex);
    return static_cast<uint32_t>(m_impl->taskQueue.size());
}

uint32_t WorkerPool::GetActiveTaskCount() const {
    return m_impl->activeCount.load();
}

WorkerPool::Stats WorkerPool::GetStats() const {
    return {
        m_impl->totalSubmitted.load(),
        m_impl->totalCompleted.load(),
        m_impl->totalCancelled.load(),
        m_impl->totalFailed.load(),
        m_impl->totalStolen.load(),
    };
}

} // namespace openmedia::worker_pool
