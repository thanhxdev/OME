#include <gtest/gtest.h>
#include <openmedia/ipc/IPCServer.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/CommandTypes.h>
#include <thread>
#include <chrono>

using namespace openmedia::ipc;

TEST(IPCIntegration, ClientServerHandshake) {
    IPCServerConfig serverConfig;
    serverConfig.pipeConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_Integration_Pipe";
    
    IPCServer server(serverConfig);
    ASSERT_TRUE(server.Start().has_value());
    
    // Wait for server listener to start
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    
    server.RegisterHandler(CommandType::Handshake, [](uint32_t clientId, const std::vector<uint8_t>& payload) {
        std::string response = "OK";
        return std::vector<uint8_t>(response.begin(), response.end());
    });
    
    IPCClientConfig clientConfig;
    clientConfig.pipeConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_Integration_Pipe";
    clientConfig.autoLaunchServer = false;
    
    IPCClient client(clientConfig);
    ASSERT_TRUE(client.Connect().has_value());
    
    auto result = client.SendCommand(CommandType::Handshake);
    ASSERT_TRUE(result.has_value());
    
    std::string responseStr(result.value().begin(), result.value().end());
    EXPECT_EQ(responseStr, "OK");
    
    client.Disconnect();
    server.Stop();
}
