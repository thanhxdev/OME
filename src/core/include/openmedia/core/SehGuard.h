/// @file SehGuard.h
/// @brief Windows Structured Exception Handling (SEH) Crash Guard for isolating
/// Access Violations (0xC0000005) and hardware/CPU faults in worker tasks and plugins.

#pragma once

#include <cstdint>
#include <functional>
#include <string>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace openmedia::core {

struct SehExecutionResult {
    bool succeeded = true;
    uint32_t exceptionCode = 0;
    std::string errorMessage;

    [[nodiscard]] bool IsFaulted() const noexcept { return !succeeded; }
};

namespace detail {

#ifdef _WIN32
// MSVC C++ exception code (0xE06D7363 -> 'msc')
constexpr DWORD MSVC_CPP_EXCEPTION = 0xE06D7363;

// Isolated function without local C++ unwindable objects to satisfy MSVC C2712 rule
inline uint32_t RunWithSehInternal(const std::function<void()>* fn) {
    __try {
        (*fn)();
        return 0; // Success
    } __except (GetExceptionCode() == MSVC_CPP_EXCEPTION ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
        return GetExceptionCode();
    }
}
#endif

inline std::string FormatExceptionCode(uint32_t code) {
#ifdef _WIN32
    switch (code) {
        case EXCEPTION_ACCESS_VIOLATION:
            return "EXCEPTION_ACCESS_VIOLATION (0xC0000005) - Invalid memory read/write";
        case EXCEPTION_INT_DIVIDE_BY_ZERO:
            return "EXCEPTION_INT_DIVIDE_BY_ZERO (0xC0000094) - Integer division by zero";
        case EXCEPTION_ILLEGAL_INSTRUCTION:
            return "EXCEPTION_ILLEGAL_INSTRUCTION (0xC000001D) - Invalid CPU opcode";
        case EXCEPTION_STACK_OVERFLOW:
            return "EXCEPTION_STACK_OVERFLOW (0xC00000FD) - Thread stack exhausted";
        case EXCEPTION_DATATYPE_MISALIGNMENT:
            return "EXCEPTION_DATATYPE_MISALIGNMENT (0x80000002) - Unaligned memory access";
        case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
            return "EXCEPTION_ARRAY_BOUNDS_EXCEEDED (0xC000008C) - Array bounds exceeded";
        default: {
            char buf[64];
            snprintf(buf, sizeof(buf), "Unknown SEH Exception (0x%08X)", code);
            return std::string(buf);
        }
    }
#else
    return "Non-Windows fault";
#endif
}

} // namespace detail

/// @brief Executes a task function wrapped in an enterprise-grade crash guard.
/// On Windows, catches both C++ exceptions and OS-level SEH faults (Access Violation, etc.).
/// On other platforms, catches std::exception and unknown exceptions.
inline SehExecutionResult ExecuteWithSehGuard(
    const std::function<void()>& fn,
    const char* contextName = "Task") {
    
    SehExecutionResult result;

#ifdef _WIN32
    try {
        uint32_t sehCode = detail::RunWithSehInternal(&fn);
        if (sehCode != 0) {
            result.succeeded = false;
            result.exceptionCode = sehCode;
            result.errorMessage = "[" + std::string(contextName) + "] SEH Crash isolated: " +
                                  detail::FormatExceptionCode(sehCode);
            return result;
        }
    } catch (const std::exception& e) {
        result.succeeded = false;
        result.exceptionCode = 1;
        result.errorMessage = "[" + std::string(contextName) + "] C++ Exception: " + e.what();
        return result;
    } catch (...) {
        result.succeeded = false;
        result.exceptionCode = 2;
        result.errorMessage = "[" + std::string(contextName) + "] Unknown C++ Exception caught";
        return result;
    }
#else
    try {
        fn();
    } catch (const std::exception& e) {
        result.succeeded = false;
        result.exceptionCode = 1;
        result.errorMessage = "[" + std::string(contextName) + "] C++ Exception: " + e.what();
        return result;
    } catch (...) {
        result.succeeded = false;
        result.exceptionCode = 2;
        result.errorMessage = "[" + std::string(contextName) + "] Unknown C++ Exception caught";
        return result;
    }
#endif

    return result;
}

} // namespace openmedia::core
