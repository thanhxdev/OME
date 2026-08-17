#pragma once

/// @file CommandDispatcher.h
/// @brief Routes IPC commands to appropriate handlers
/// @since 1.0.0

#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/core/ErrorCodes.h>

#include <functional>
#include <memory>
#include <vector>

namespace openmedia::command_dispatcher {

/// @brief Command handler interface
class ICommandHandler {
public:
    virtual ~ICommandHandler() = default;

    /// @brief Handle a command
    /// @param clientId ID of the requesting client
    /// @param payload Command payload data
    /// @return Response payload or error
    [[nodiscard]] virtual core::Result<std::vector<uint8_t>> Handle(
        uint32_t clientId,
        const std::vector<uint8_t>& payload) = 0;

    /// @brief Get the command type this handler handles
    [[nodiscard]] virtual ipc::CommandType GetCommandType() const = 0;

    /// @brief Get handler name for logging
    [[nodiscard]] virtual std::string GetName() const = 0;
};

/// @brief Command handler function type (for lambdas / non-class handlers)
using CommandHandlerFn = std::function<core::Result<std::vector<uint8_t>>(
    uint32_t clientId,
    const std::vector<uint8_t>& payload)>;

/// @brief Routes incoming IPC commands to registered handlers
///
/// Central routing hub for the server process. Receives commands from
/// IPCServer, validates them, looks up the handler, submits to WorkerPool.
///
/// @code
/// CommandDispatcher dispatcher;
/// dispatcher.Register(CommandType::CreatePipeline, pipelineHandler);
/// dispatcher.Register(CommandType::GetStatus, [](auto id, auto& p) {
///     return std::vector<uint8_t>{1, 2, 3};
/// });
///
/// auto result = dispatcher.Dispatch(clientId, commandType, payload);
/// @endcode
class CommandDispatcher {
public:
    CommandDispatcher();
    ~CommandDispatcher();

    CommandDispatcher(const CommandDispatcher&) = delete;
    CommandDispatcher& operator=(const CommandDispatcher&) = delete;

    /// @brief Register a handler object for a command type
    void Register(ipc::CommandType type, std::shared_ptr<ICommandHandler> handler);

    /// @brief Register a handler function for a command type
    void Register(ipc::CommandType type, CommandHandlerFn handler);

    /// @brief Unregister a handler
    void Unregister(ipc::CommandType type);

    /// @brief Check if a handler is registered
    [[nodiscard]] bool HasHandler(ipc::CommandType type) const;

    /// @brief Dispatch a command to its handler (synchronous)
    [[nodiscard]] core::Result<std::vector<uint8_t>> Dispatch(
        uint32_t clientId,
        ipc::CommandType type,
        const std::vector<uint8_t>& payload);

    /// @brief Get number of registered handlers
    [[nodiscard]] size_t GetHandlerCount() const;

    /// @brief Get command dispatch statistics
    struct Stats {
        uint64_t totalDispatched = 0;
        uint64_t totalSuccess = 0;
        uint64_t totalErrors = 0;
        uint64_t totalTimeout = 0;
    };
    [[nodiscard]] Stats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::command_dispatcher
