#pragma once

/// @file WorkerPool.h
/// @brief Configurable thread pool with priority task scheduling
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>

#include <chrono>
#include <cstdint>
#include <functional>
#include <future>
#include <memory>
#include <string>

namespace openmedia::worker_pool {

/// @brief Task priority levels
enum class TaskPriority : uint32_t {
    High = 0,       ///< Real-time tasks (decode, encode, frame processing)
    Normal = 1,     ///< Pipeline management, filter application
    Low = 2,        ///< Metrics, logging, health check
};

/// @brief Task status
enum class TaskStatus : uint32_t {
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
    Timeout,
};

/// @brief Task identifier
using TaskId = uint64_t;

/// @brief Task function type
using TaskFunction = std::function<void()>;

/// @brief Worker pool configuration
struct WorkerPoolConfig {
    uint32_t threadCount = 0;           ///< 0 = auto (hardware_concurrency)
    bool enableTaskStealing = true;     ///< Work-stealing between queues
    bool enableThreadAffinity = false;  ///< NUMA-aware thread affinity
    std::string namePrefix = "OME-Worker";  ///< Thread name prefix
};

/// @brief Configurable thread pool with priority-based task scheduling
///
/// Manages a pool of worker threads that execute tasks from priority queues.
/// Supports task cancellation, timeout, progress tracking, and work-stealing.
///
/// @code
/// WorkerPool pool({.threadCount = 4});
/// pool.Start();
///
/// auto future = pool.Submit(TaskPriority::High, [] {
///     // decode frame
/// });
///
/// future.get();  // wait for completion
/// pool.Stop();
/// @endcode
class WorkerPool {
public:
    explicit WorkerPool(const WorkerPoolConfig& config = {});
    ~WorkerPool();

    WorkerPool(const WorkerPool&) = delete;
    WorkerPool& operator=(const WorkerPool&) = delete;

    // --- Lifecycle ---

    /// @brief Start the worker threads
    [[nodiscard]] core::VoidResult Start();

    /// @brief Stop all workers (waits for running tasks)
    void Stop();

    /// @brief Check if pool is running
    [[nodiscard]] bool IsRunning() const;

    // --- Task Submission ---

    /// @brief Submit a task with priority
    /// @return Future that completes when the task finishes
    template <typename F>
    auto Submit(TaskPriority priority, F&& task)
        -> std::future<decltype(task())>;

    /// @brief Submit a void task
    TaskId SubmitTask(TaskPriority priority, TaskFunction task);

    /// @brief Cancel a task
    /// @return true if task was cancelled, false if already running/completed
    bool Cancel(TaskId taskId);

    // --- Info ---

    /// @brief Get number of worker threads
    [[nodiscard]] uint32_t GetThreadCount() const;

    /// @brief Get number of pending tasks
    [[nodiscard]] uint32_t GetPendingTaskCount() const;

    /// @brief Get number of active (running) tasks
    [[nodiscard]] uint32_t GetActiveTaskCount() const;

    /// @brief Pool statistics
    struct Stats {
        uint64_t totalSubmitted = 0;
        uint64_t totalCompleted = 0;
        uint64_t totalCancelled = 0;
        uint64_t totalFailed = 0;
        uint64_t totalStolen = 0;       ///< Tasks stolen from other queues
    };
    [[nodiscard]] Stats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

// Template implementation
template <typename F>
auto WorkerPool::Submit(TaskPriority priority, F&& task)
    -> std::future<decltype(task())> {
    using ReturnType = decltype(task());
    auto packagedTask = std::make_shared<std::packaged_task<ReturnType()>>(
        std::forward<F>(task));
    auto future = packagedTask->get_future();
    SubmitTask(priority, [packagedTask] { (*packagedTask)(); });
    return future;
}

} // namespace openmedia::worker_pool
