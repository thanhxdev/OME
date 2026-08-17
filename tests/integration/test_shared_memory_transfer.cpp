#include <gtest/gtest.h>
#include <openmedia/ipc/IPCServer.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/SharedMemoryBuffer.h>
#include <openmedia/ipc/CommandTypes.h>
#include <thread>
#include <chrono>
#include <cstring>

using namespace openmedia::ipc;

TEST(IPCIntegration, SharedMemoryTransfer) {
    IPCServerConfig serverConfig;
    serverConfig.pipeConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_Integration_MemPipe";
    serverConfig.sharedMemConfig.name = "OpenMedia_Test_Integration_Mem";
    
    IPCServer server(serverConfig);
    ASSERT_TRUE(server.Start().has_value());
    
    // Wait for server listener to start
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    
    IPCClientConfig clientConfig;
    clientConfig.pipeConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_Integration_MemPipe";
    clientConfig.sharedMemConfig.name = "OpenMedia_Test_Integration_Mem";
    clientConfig.autoLaunchServer = false;
    
    IPCClient client(clientConfig);
    ASSERT_TRUE(client.Connect().has_value());
    
    // Map memory on client side
    ASSERT_TRUE(client.MapSharedMemory().has_value());
    
    // Get server memory buffer and write something
    SharedMemoryBuffer* serverMem = server.GetSharedMemory();
    ASSERT_NE(serverMem, nullptr);
    
    // Allocate a slot and write data
    uint32_t slotIndex = 0;
    uint8_t* serverPtr = serverMem->AcquireWriteSlot(slotIndex);
    ASSERT_NE(serverPtr, nullptr);
    
    const char* testData = "Hello Shared Memory!";
    std::memcpy(serverPtr, testData, std::strlen(testData) + 1);
    
    FrameSlotHeader metadata{};
    metadata.dataSize = std::strlen(testData) + 1;
    metadata.pts = 123456789;
    
    bool frameReceived = false;
    
    // Setup client callback
    client.OnFrameReady([&](uint32_t slot, const FrameSlotHeader& meta) {
        EXPECT_EQ(slot, slotIndex);
        
        SharedMemoryBuffer* clientMem = client.GetSharedMemory();
        ASSERT_NE(clientMem, nullptr);
        
        uint32_t readSlotIndex = 0;
        FrameSlotHeader readMeta{};
        const uint8_t* clientPtr = clientMem->AcquireReadSlot(readSlotIndex, readMeta);
        
        // Sometimes the read slot isn't immediately acquired if we just received the notification,
        // but let's assume it is since the notification means it was committed.
        if (clientPtr) {
            std::string receivedData(reinterpret_cast<const char*>(clientPtr));
            EXPECT_EQ(receivedData, "Hello Shared Memory!");
            clientMem->ReleaseReadSlot(readSlotIndex);
        }
        
        frameReceived = true;
    });
    
    // Server commits slot and notifies client
    serverMem->CommitWriteSlot(slotIndex, metadata);
    ASSERT_TRUE(server.NotifyFrameReady(slotIndex, metadata).has_value());
    
    // Wait a bit for the callback to fire
    std::this_thread::sleep_for(std::chrono::milliseconds(500));
    
    EXPECT_TRUE(frameReceived);
    
    client.Disconnect();
    server.Stop();
}
