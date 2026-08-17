#include <gtest/gtest.h>
#include "openmedia/srt/SRTEngine.h"
#include "openmedia/srt/SRTSource.h"
#include "openmedia/srt/SRTOutput.h"
#include <thread>
#include <chrono>

using namespace openmedia::srt;

TEST(SRTEngineTest, InitializeAndShutdown) {
    SRTEngine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST(SRTEngineTest, ConnectCallerListener) {
    SRTEngine engine;
    ASSERT_TRUE(engine.Initialize());

    SRTOutput listener;
    SRTSource caller;

    // Start a thread to listen
    std::thread listenerThread([&]() {
        // Use a different port
        EXPECT_TRUE(listener.Start("srt://127.0.0.1:19091?mode=listener"));
    });

    // Give the listener a moment to bind
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    EXPECT_TRUE(caller.Connect("srt://127.0.0.1:19091?mode=caller"));

    caller.Disconnect();
    listener.Stop();

    if (listenerThread.joinable()) {
        listenerThread.join();
    }
}
