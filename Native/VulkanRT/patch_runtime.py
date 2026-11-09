"""Apply small, checked hooks to the pinned upstream runtime; never edit the download."""
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text()


def replace(before, after):
    global source

    if source.count(before) != 1:
        raise RuntimeError(f"MonoGame 3.8.6 patch anchor changed: {before!r}")

    source = source.replace(before, after)


replace("bool customBorderColorSupported = false;", "bool customBorderColorSupported = false;\n\tbool radiantRayQuery = false;")
replace("bool MGVK_ValidationEnabled = false;", "bool MGVK_ValidationEnabled = false;\nstatic uint32_t RgeApiVersion = VK_API_VERSION_1_0;")
replace("device->graphicsQueueFamily = 0;", "device->graphicsQueueFamily = 0;\n\tbool radiantComputeQueue = false;")
replace("device->graphicsQueueFamily = i;", "device->graphicsQueueFamily = i;\n\t\t\t\tradiantComputeQueue = (queueFamilyProps[i].queueFlags & VK_QUEUE_COMPUTE_BIT) != 0;")
replace("struct MGG_Texture\n{", "static std::map<VkDevice, MGG_GraphicsDevice*> RgeDevices;\n\nstruct MGG_Texture\n{")
# Mid-frame resource uploads/readbacks must consume the acquired-image semaphore once.
# The upstream flush submits the frame without waiting; Present then waits unconditionally.
replace("bool is_recording = false;", "bool is_recording = false;\n\tbool radiantAcquirePending = false;")
replace("\t\t\tframe.imageAcquiredSemaphore, VK_NULL_HANDLE, &frame.image_index);",
        "\t\t\tframe.imageAcquiredSemaphore, VK_NULL_HANDLE, &frame.image_index);\n\t\tframe.radiantAcquirePending = true;")
replace("\tsubmitInfo.pCommandBuffers = &frame.commandBuffer;\n\t{", """\tsubmitInfo.pCommandBuffers = &frame.commandBuffer;
    VkPipelineStageFlags radiantWaitStage = VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
    submitInfo.waitSemaphoreCount = frame.radiantAcquirePending ? 1 : 0;
    submitInfo.pWaitSemaphores = &frame.imageAcquiredSemaphore;
    submitInfo.pWaitDstStageMask = &radiantWaitStage;
    frame.radiantAcquirePending = false;
\t{""")
replace("\t\tsubmitInfo.waitSemaphoreCount = 1;", "\t\tsubmitInfo.waitSemaphoreCount = frame.radiantAcquirePending ? 1 : 0;\n\t\tframe.radiantAcquirePending = false;")
replace("VkPipelineStageFlags waitFlags[] = { VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT };",
        "VkPipelineStageFlags waitFlags[] = { VK_PIPELINE_STAGE_ALL_COMMANDS_BIT };")
replace("\tflushSubmitInfo.waitSemaphoreCount = 1;",
        "\tflushSubmitInfo.waitSemaphoreCount = frame.radiantAcquirePending ? 1 : 0;\n\tframe.radiantAcquirePending = false;")
replace("\tflushSubmitInfo.signalSemaphoreCount = 1;", "\tflushSubmitInfo.signalSemaphoreCount = 0;")
replace("VkPipelineStageFlags waitStages[] = { VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT };",
        "VkPipelineStageFlags waitStages[] = { VK_PIPELINE_STAGE_ALL_COMMANDS_BIT };")
replace("    submitInfo.pCommandBuffers = &frame.commandBuffer;\n\t{", """    submitInfo.pCommandBuffers = &frame.commandBuffer;
    VkPipelineStageFlags radiantWaitStage = VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
    submitInfo.waitSemaphoreCount = frame.radiantAcquirePending ? 1 : 0;
    submitInfo.pWaitSemaphores = &frame.imageAcquiredSemaphore;
    submitInfo.pWaitDstStageMask = &radiantWaitStage;
    frame.radiantAcquirePending = false;
\t{""")
replace("app_info.apiVersion = VK_API_VERSION_1_0;", """uint32_t radiantLoaderVersion = VK_API_VERSION_1_0;
    if (vkEnumerateInstanceVersion) vkEnumerateInstanceVersion(&radiantLoaderVersion);
    app_info.apiVersion = std::min(radiantLoaderVersion, uint32_t(VK_API_VERSION_1_2));
    RgeApiVersion = app_info.apiVersion;""")
replace("\tVkPhysicalDeviceFeatures2 deviceFeatures2 = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 };", """
    // Ray queries are optional. Unsupported devices keep the stock raster/software path.
    VkPhysicalDeviceBufferDeviceAddressFeatures rgeAddress = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES };
    VkPhysicalDeviceAccelerationStructureFeaturesKHR rgeAcceleration = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_FEATURES_KHR };
    VkPhysicalDeviceRayQueryFeaturesKHR rgeQuery = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_RAY_QUERY_FEATURES_KHR };
    if (RgeApiVersion >= VK_API_VERSION_1_2 && radiantComputeQueue && device->deviceProperties.apiVersion >= VK_API_VERSION_1_2 &&
        SupportsExtension(deviceExtensions, VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME) &&
        SupportsExtension(deviceExtensions, VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME) &&
        SupportsExtension(deviceExtensions, VK_KHR_RAY_QUERY_EXTENSION_NAME))
    {
        VkPhysicalDeviceFeatures2 supported = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 };
        supported.pNext = &rgeAddress;
        rgeAddress.pNext = &rgeAcceleration;
        rgeAcceleration.pNext = &rgeQuery;
        vkGetPhysicalDeviceFeatures2(device->physicalDevice, &supported);
        device->radiantRayQuery = rgeAddress.bufferDeviceAddress && rgeAcceleration.accelerationStructure && rgeQuery.rayQuery;
        if (device->radiantRayQuery)
        {
            // Enable only the features this integration uses, not all queried features.
            rgeAddress = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES, &rgeAcceleration, VK_TRUE };
            rgeAcceleration = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_FEATURES_KHR, &rgeQuery, VK_TRUE };
            rgeQuery = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_RAY_QUERY_FEATURES_KHR, lastFeature, VK_TRUE };
            lastFeature = &rgeAddress;
            extensions.push_back(VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME);
            extensions.push_back(VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME);
            extensions.push_back(VK_KHR_RAY_QUERY_EXTENSION_NAME);
        }
    }
    VkPhysicalDeviceFeatures2 deviceFeatures2 = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 };""")
replace("\tvkGetDeviceQueue(device->device, device->graphicsQueueFamily, 0, &device->queue);",
        "\tvkGetDeviceQueue(device->device, device->graphicsQueueFamily, 0, &device->queue);\n\tRgeDevices[device->device] = device;")
replace("void MGG_GraphicsDevice_Destroy(MGG_GraphicsDevice* device)\n{",
        "void MGG_GraphicsDevice_Destroy(MGG_GraphicsDevice* device)\n{\n\tRgeDevices.erase(device->device);")
replace("VkPhysicalDeviceFeatures enabledFeatures = {};", "VkPhysicalDeviceFeatures enabledFeatures = {};\n    enabledFeatures.shaderStorageImageWriteWithoutFormat = device->deviceFeatures.shaderStorageImageWriteWithoutFormat;\n    enabledFeatures.shaderStorageImageReadWithoutFormat = device->deviceFeatures.shaderStorageImageReadWithoutFormat;")
source += '\n#include "ray_tracing.inl"\n#include "nrd.inl"\n#include "compute.inl"\n'
output = Path(sys.argv[2])

if not output.exists() or output.read_text() != source:
    output.write_text(source)
