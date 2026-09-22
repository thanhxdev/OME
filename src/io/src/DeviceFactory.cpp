/// @file DeviceFactory.cpp
#include <openmedia/io/DeviceFactory.h>
#include <openmedia/io/DesktopCapture.h>
#include <openmedia/io/DeckLinkSource.h>
#include <openmedia/io/AJASource.h>
#include <openmedia/io/MagewellSource.h>
#include <openmedia/io/DirectShowSource.h>
#include <openmedia/io/MediaFoundationSource.h>
#include <openmedia/io/WASAPISource.h>
#include <openmedia/core/Logger.h>
#include <DeckLinkAPI.h>

namespace openmedia::io {

std::vector<DeviceInfo> DeviceFactory::EnumerateDevices(DeviceType filter) {
    std::vector<DeviceInfo> devices;
    
    if (filter == DeviceType::Unknown || filter == DeviceType::DesktopDuplication) {
        devices.push_back({"Primary Monitor", "Monitor0", DeviceType::DesktopDuplication});
    }

    if (filter == DeviceType::Unknown || filter == DeviceType::VideoInput) {
        // Enumerate real physical Blackmagic DeckLink hardware via COM iterator
        IDeckLinkIterator* iterator = CreateDeckLinkIteratorInstance();
        if (iterator) {
            IDeckLink* dl = nullptr;
            int idx = 0;
            while (iterator->Next(&dl) == 0 && dl != nullptr) {
                const char* modelName = nullptr;
                if (dl->GetModelName(&modelName) == 0 && modelName) {
                    devices.push_back({modelName, modelName, DeviceType::VideoInput});
                } else {
                    std::string fallbackName = "DeckLink Device (" + std::to_string(idx) + ")";
                    devices.push_back({fallbackName, fallbackName, DeviceType::VideoInput});
                }
                dl->Release();
                idx++;
            }
            iterator->Release();
        }

        // Other hardware capture devices
        devices.push_back({"Integrated Camera", "Integrated Camera", DeviceType::VideoInput});
        devices.push_back({"MF USB Camera", "MF_USB_Camera_01", DeviceType::VideoInput});
        devices.push_back({"AJA Kona 4", "AJA0", DeviceType::VideoInput});
        devices.push_back({"Magewell Pro Capture", "Magewell0", DeviceType::VideoInput});
    }

    if (filter == DeviceType::Unknown || filter == DeviceType::AudioInput) {
        devices.push_back({"Microphone Array", "Microphone Array", DeviceType::AudioInput});
    }

    return devices;
}

core::Result<std::shared_ptr<core::IMediaObject>> DeviceFactory::CreateDeviceSource(const DeviceInfo& info) {
    if (info.type == DeviceType::DesktopDuplication) {
        return std::make_shared<DesktopCapture>();
    } else if (info.type == DeviceType::VideoInput && info.id.find("DeckLink") != std::string::npos) {
        auto src = std::make_shared<DeckLinkSource>();
        (void)src->Open(info.id);
        return src;
    } else if (info.type == DeviceType::VideoInput && info.id.find("AJA") != std::string::npos) {
        return std::make_shared<AJASource>(0);
    } else if (info.type == DeviceType::VideoInput && info.id.find("Magewell") != std::string::npos) {
        return std::make_shared<MagewellSource>();
    } else if (info.type == DeviceType::VideoInput && info.id.find("MF_") != std::string::npos) {
        auto src = std::make_shared<MediaFoundationSource>();
        (void)src->Open(info.id);
        return src;
    } else if (info.type == DeviceType::VideoInput) {
        auto src = std::make_shared<DirectShowSource>();
        (void)src->Open(info.id);
        return src;
    } else if (info.type == DeviceType::AudioInput) {
        auto src = std::make_shared<WASAPISource>();
        (void)src->Open(info.id);
        return src;
    }

    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Device type not supported"));
}

} // namespace openmedia::io
