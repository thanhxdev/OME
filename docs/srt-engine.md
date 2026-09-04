# SRT Engine Remaining Tasks

## Overview
This plan covers the remaining tasks for the SRT (Secure Reliable Transport) Engine in the OpenMedia SDK (Tasks 5.1.4 - 5.1.8). The goal is to make the SRT implementation production-ready by adding encryption, latency tuning, real-time statistics, and comprehensive unit tests.

## Project Type
BACKEND

## Success Criteria
- SRT connections can be encrypted with AES-128/AES-256 using passphrases.
- Configurable latency and bandwidth limits for both Caller and Listener modes.
- Real-time statistics (RTT, packet loss, bitrate) are extractable from active sessions.
- Unit tests cover the full SRT lifecycle including edge cases (wrong passphrase, network simulated drops).

## Tech Stack
- **C++20** (Core SDK)
- **libsrt** (Underlying SRT protocol library)
- **GTest** (Unit testing framework)

## File Structure
Modifications will primarily occur in:
- `src/protocols/srt/include/openmedia/srt/` (Headers)
- `src/protocols/srt/src/` (Implementations)
- `tests/unit/protocols/srt/` (Tests)

---

## Task Breakdown

### 1. SRT Encryption Implementation
- **Task ID:** SRT-1
- **Agent:** `backend-specialist`
- **Skills:** `cpp-networking`, `clean-code`
- **Priority:** P1
- **Dependencies:** None
- **INPUT → OUTPUT → VERIFY:**
  - **Input:** Passphrase, key length (16/24/32 bytes for AES-128/192/256).
  - **Output:** `SRT_PASSPHRASE` and `SRT_PBKEYLEN` socket options applied before connection in both `SRTSource` and `SRTOutput`.
  - **Verify:** Connect a caller and listener with matching passphrases (success) and mismatched passphrases (failure/rejection).

### 2. Latency & Bandwidth Tuning
- **Task ID:** SRT-2
- **Agent:** `backend-specialist`
- **Skills:** `cpp-networking`
- **Priority:** P1
- **Dependencies:** None
- **INPUT → OUTPUT → VERIFY:**
  - **Input:** Latency (ms), max bandwidth (bps).
  - **Output:** Apply `SRT_LATENCY` / `SRT_RCVLATENCY` / `SRT_PEERLATENCY` and `SRT_MAXBW` socket options. Add these configurations to the connection settings struct.
  - **Verify:** Socket options can be queried and match the provided settings after configuration.

### 3. Statistics & Metrics Retrieval
- **Task ID:** SRT-3
- **Agent:** `backend-specialist`
- **Skills:** `cpp-networking`
- **Priority:** P2
- **Dependencies:** None
- **INPUT → OUTPUT → VERIFY:**
  - **Input:** Active SRT socket.
  - **Output:** Method `GetStatistics()` returning a struct with RTT, packet loss, bitrate, and retransmit count (using `srt_bistats()`).
  - **Verify:** Call `GetStatistics()` during active transmission and verify non-zero payload/bitrate metrics.

### 4. Unit Testing Suite
- **Task ID:** SRT-4
- **Agent:** `test-engineer`
- **Skills:** `testing-patterns`, `tdd-workflow`
- **Priority:** P1
- **Dependencies:** SRT-1, SRT-2, SRT-3
- **INPUT → OUTPUT → VERIFY:**
  - **Input:** Complete SRT classes.
  - **Output:** `test_srt_engine.cpp` with tests for: Engine init/cleanup, Caller/Listener connection, Encryption mismatch, Latency config, and Stats polling.
  - **Verify:** `ctest -R test_srt` passes 100%.

---

## Phase X: Verification

- [x] Lint & Type Check: `cmake --build build --target clang-tidy`
- [x] Security: Verify passphrases are not logged in plaintext.
- [x] Build: `cmake --build build --config Release`
- [x] Test: `ctest -R test_srt_engine`

## ✅ PHASE X COMPLETE
- Lint: ✅ Pass
- Security: ✅ No critical issues
- Build: ✅ Success
- Date: 2026-07-26
