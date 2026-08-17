/// @file CommandDispatcher.cpp
/// @brief Command routing and dispatching implementation

#include <openmedia/command_dispatcher/CommandDispatcher.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <chrono>
#include <mutex>
#include <unordered_map>

namespace openmedia::command_dispatcher {

/// @brief Wrapper to store either a handler object or function
struct HandlerEntry {
    std::shared_ptr<ICommandHandler> handler;
    CommandHandlerFn function;

    core::Result<std::vector<uint8_t>> Invoke(uint32_t clientId,
                                               const std::vector<uint8_t>& payload) {
        if (handler) return handler->Handle(clientId, payload);
        if (function) return function(clientId, payload);
        return std::unexpected(core::Error{
            core::ErrorCode::NotFound, "No handler"});
    }
};

struct CommandDispatcher::Impl {
    std::unordered_map<ipc::CommandType, HandlerEntry> handlers;
    mutable std::mutex handlersMutex;

    std::atomic<uint64_t> totalDispatched{0};
    std::atomic<uint64_t> totalSuccess{0};
    std::atomic<uint64_t> totalErrors{0};
    std::atomic<uint64_t> totalTimeout{0};
};

CommandDispatcher::CommandDispatcher()
    : m_impl(std::make_unique<Impl>()) {}

CommandDispatcher::~CommandDispatcher() = default;

void CommandDispatcher::Register(ipc::CommandType type,
                                  std::shared_ptr<ICommandHandler> handler) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->handlers[type] = HandlerEntry{std::move(handler), nullptr};
    core::Logger::SDebug("CommandDispatcher", "Registered handler for command 0x{:04X}",
                        static_cast<uint32_t>(type));
}

void CommandDispatcher::Register(ipc::CommandType type, CommandHandlerFn handler) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->handlers[type] = HandlerEntry{nullptr, std::move(handler)};
    core::Logger::SDebug("CommandDispatcher", "Registered function handler for command 0x{:04X}",
                        static_cast<uint32_t>(type));
}

void CommandDispatcher::Unregister(ipc::CommandType type) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->handlers.erase(type);
}

bool CommandDispatcher::HasHandler(ipc::CommandType type) const {
    std::lock_guard lock(m_impl->handlersMutex);
    return m_impl->handlers.contains(type);
}

core::Result<std::vector<uint8_t>> CommandDispatcher::Dispatch(
    uint32_t clientId,
    ipc::CommandType type,
    const std::vector<uint8_t>& payload) {

    m_impl->totalDispatched.fetch_add(1);
    auto startTime = std::chrono::steady_clock::now();

    HandlerEntry entry;
    {
        std::lock_guard lock(m_impl->handlersMutex);
        auto it = m_impl->handlers.find(type);
        if (it == m_impl->handlers.end()) {
            m_impl->totalErrors.fetch_add(1);
            core::Logger::SWarn("CommandDispatcher",
                "No handler for command {} (0x{:04X}) from client {}",
                ipc::CommandTypeToString(type),
                static_cast<uint32_t>(type), clientId);
            return std::unexpected(core::Error{
                core::ErrorCode::NotFound,
                "No handler for command: " +
                std::string(ipc::CommandTypeToString(type))});
        }
        entry = it->second;
    }

    auto result = entry.Invoke(clientId, payload);

    auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now() - startTime);

    if (result) {
        m_impl->totalSuccess.fetch_add(1);
        core::Logger::SDebug("CommandDispatcher",
            "Command {} from client {} completed in {}us",
            ipc::CommandTypeToString(type), clientId, elapsed.count());
    } else {
        m_impl->totalErrors.fetch_add(1);
        core::Logger::SError("CommandDispatcher",
            "Command {} from client {} failed: {}",
            ipc::CommandTypeToString(type), clientId,
            result.error().message);
    }

    return result;
}

size_t CommandDispatcher::GetHandlerCount() const {
    std::lock_guard lock(m_impl->handlersMutex);
    return m_impl->handlers.size();
}

CommandDispatcher::Stats CommandDispatcher::GetStats() const {
    return {
        m_impl->totalDispatched.load(),
        m_impl->totalSuccess.load(),
        m_impl->totalErrors.load(),
        m_impl->totalTimeout.load(),
    };
}

} // namespace openmedia::command_dispatcher
