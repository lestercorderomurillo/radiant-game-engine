// Included by the pinned MonoGame Vulkan backend. All entry points are render-thread only.
// Resource changes flush pending commands; steady-state tracing stays in MonoGame's command buffer.
#include <stdexcept>
#include <string>
#include <limits>
#include "path_trace.spv.h"

static thread_local std::string RgeError;

static void RgeCheck(VkResult Result, const char* Operation)
{
    if (Result != VK_SUCCESS)
        throw std::runtime_error(std::string(Operation) + " failed (VkResult " + std::to_string(Result) + ")");
}

static void RgeFlush(MGG_GraphicsDevice* Device)
{
    std::lock_guard Lock(Device->queueMutex);
    auto& Frame = Device->frames[Device->frameIndex];
    if (Frame.is_recording)
    {
        MGVK_FlushCommands(Device, Frame);
        MGVK_BeginCommandBuffer(Frame.commandBuffer);

        // A restarted command buffer has no graphics state.
        Device->pipelineStateDirty = true;
        Device->renderTargetDirty = true;
    }

    RgeCheck(vkDeviceWaitIdle(Device->device), "Wait for Vulkan resources");
}

#include "denoising_image.inl"

struct RgeBuffer
{
    VkBuffer Handle = VK_NULL_HANDLE;
    VkDeviceMemory Memory = VK_NULL_HANDLE;
    VkDeviceAddress Address = 0;
};

struct RgeTraceParameters
{
    float InverseViewProjection[16];
    float Environment[4];
    uint32_t Width, Height, Sequence, Bounces;
    uint32_t StablePrimary, HasCamera, Denoiser, Reserved1;
    float ViewDepth[4];
};
static_assert(sizeof(RgeTraceParameters) == 128);

struct RgeTrace
{
    MGG_GraphicsDevice* Device;
    uint32_t Width, Height;

    VkImage Image = VK_NULL_HANDLE;
    VkDeviceMemory ImageMemory = VK_NULL_HANDLE;
    VkImageView ImageView = VK_NULL_HANDLE;
    VkImageLayout ImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkDescriptorSetLayout DescriptorLayout = VK_NULL_HANDLE;
    VkDescriptorPool DescriptorPool = VK_NULL_HANDLE;
    VkDescriptorSet Descriptors = VK_NULL_HANDLE;
    VkPipelineLayout PipelineLayout = VK_NULL_HANDLE;
    VkPipeline Pipeline = VK_NULL_HANDLE;
    VkQueryPool QueryPool = VK_NULL_HANDLE;
    std::vector<bool> QueryWritten;
    float GpuMilliseconds = -1;
    uint32_t TimestampBits = 0;
    RgeBuffer Triangles, Indices, Instances, BottomStorage, TopStorage;
    RgeImage Signals[6];
    bool DenoisingSignals = false;

    VkAccelerationStructureKHR Bottom = VK_NULL_HANDLE;
    VkAccelerationStructureKHR Top = VK_NULL_HANDLE;
    PFN_vkCreateAccelerationStructureKHR CreateAcceleration;
    PFN_vkDestroyAccelerationStructureKHR DestroyAcceleration;
    PFN_vkGetAccelerationStructureBuildSizesKHR GetBuildSizes;
    PFN_vkCmdBuildAccelerationStructuresKHR BuildAcceleration;
    PFN_vkGetAccelerationStructureDeviceAddressKHR GetAccelerationAddress;
    PFN_vkGetBufferDeviceAddress GetBufferAddress;

    explicit RgeTrace(MGG_GraphicsDevice* GraphicsDevice, uint32_t ImageWidth, uint32_t ImageHeight)
        : Device(GraphicsDevice), Width(ImageWidth), Height(ImageHeight)
    {
        auto Logical = Device->device;
        CreateAcceleration = reinterpret_cast<PFN_vkCreateAccelerationStructureKHR>(vkGetDeviceProcAddr(Logical, "vkCreateAccelerationStructureKHR"));
        DestroyAcceleration = reinterpret_cast<PFN_vkDestroyAccelerationStructureKHR>(vkGetDeviceProcAddr(Logical, "vkDestroyAccelerationStructureKHR"));
        GetBuildSizes = reinterpret_cast<PFN_vkGetAccelerationStructureBuildSizesKHR>(vkGetDeviceProcAddr(Logical, "vkGetAccelerationStructureBuildSizesKHR"));
        BuildAcceleration = reinterpret_cast<PFN_vkCmdBuildAccelerationStructuresKHR>(vkGetDeviceProcAddr(Logical, "vkCmdBuildAccelerationStructuresKHR"));
        GetAccelerationAddress = reinterpret_cast<PFN_vkGetAccelerationStructureDeviceAddressKHR>(vkGetDeviceProcAddr(Logical, "vkGetAccelerationStructureDeviceAddressKHR"));
        GetBufferAddress = reinterpret_cast<PFN_vkGetBufferDeviceAddress>(vkGetDeviceProcAddr(Logical, "vkGetBufferDeviceAddress"));
        if (!CreateAcceleration || !DestroyAcceleration || !GetBuildSizes || !BuildAcceleration || !GetAccelerationAddress || !GetBufferAddress)
            throw std::runtime_error("Vulkan ray query device entry points unavailable");
    }

    uint32_t MemoryType(uint32_t Mask, VkMemoryPropertyFlags Flags)
    {
        VkPhysicalDeviceMemoryProperties Properties;
        vkGetPhysicalDeviceMemoryProperties(Device->physicalDevice, &Properties);
        for (uint32_t Index = 0; Index < Properties.memoryTypeCount; Index++)
            if ((Mask & (1u << Index)) && (Properties.memoryTypes[Index].propertyFlags & Flags) == Flags)
                return Index;
        throw std::runtime_error("No compatible Vulkan memory type");
    }

    void FreeBuffer(RgeBuffer& Buffer)
    {
        if (Buffer.Handle) vkDestroyBuffer(Device->device, Buffer.Handle, nullptr);
        if (Buffer.Memory) vkFreeMemory(Device->device, Buffer.Memory, nullptr);
        Buffer = {};
    }

    void MakeBuffer(RgeBuffer& Buffer, VkDeviceSize Size, VkBufferUsageFlags Usage, const void* Data = nullptr)
    {
        VkBufferCreateInfo Info = { VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
        Info.size = std::max(Size, VkDeviceSize(16));
        Info.usage = Usage | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT;
        RgeCheck(vkCreateBuffer(Device->device, &Info, nullptr, &Buffer.Handle), "Create RT buffer");

        VkMemoryRequirements Requirements;
        vkGetBufferMemoryRequirements(Device->device, Buffer.Handle, &Requirements);

        VkMemoryAllocateFlagsInfo Flags = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_FLAGS_INFO };
        Flags.flags = VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT;

        VkMemoryAllocateInfo Allocation = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, &Flags };
        Allocation.allocationSize = Requirements.size;
        Allocation.memoryTypeIndex = MemoryType(Requirements.memoryTypeBits, Data
            ? VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
            : VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        RgeCheck(vkAllocateMemory(Device->device, &Allocation, nullptr, &Buffer.Memory), "Allocate RT buffer");
        RgeCheck(vkBindBufferMemory(Device->device, Buffer.Handle, Buffer.Memory, 0), "Bind RT buffer");
        if (Data)
        {
            void* Mapped;
            RgeCheck(vkMapMemory(Device->device, Buffer.Memory, 0, Size, 0, &Mapped), "Map RT geometry");
            memcpy(Mapped, Data, static_cast<size_t>(Size));
            vkUnmapMemory(Device->device, Buffer.Memory);
        }

        VkBufferDeviceAddressInfo Address = { VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO };
        Address.buffer = Buffer.Handle;
        Buffer.Address = GetBufferAddress(Device->device, &Address);
    }

    void Initialize()
    {
        VkImageCreateInfo Info = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
        Info.imageType = VK_IMAGE_TYPE_2D;
        Info.format = VK_FORMAT_R32G32B32A32_SFLOAT;
        Info.extent = { Width, Height, 1 };
        Info.mipLevels = Info.arrayLayers = 1;
        Info.samples = VK_SAMPLE_COUNT_1_BIT;
        Info.tiling = VK_IMAGE_TILING_OPTIMAL;
        Info.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        RgeCheck(vkCreateImage(Device->device, &Info, nullptr, &Image), "Create trace output");

        VkMemoryRequirements Requirements;
        vkGetImageMemoryRequirements(Device->device, Image, &Requirements);

        VkMemoryAllocateInfo Allocation = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO };
        Allocation.allocationSize = Requirements.size;
        Allocation.memoryTypeIndex = MemoryType(Requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        RgeCheck(vkAllocateMemory(Device->device, &Allocation, nullptr, &ImageMemory), "Allocate trace output");
        RgeCheck(vkBindImageMemory(Device->device, Image, ImageMemory, 0), "Bind trace output");

        VkImageViewCreateInfo View = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
        View.image = Image; View.viewType = VK_IMAGE_VIEW_TYPE_2D; View.format = Info.format;
        View.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        RgeCheck(vkCreateImageView(Device->device, &View, nullptr, &ImageView), "Create trace image view");

        for (auto& Signal : Signals) Signal.Create(Device, DenoisingSignals ? Width : 1, DenoisingSignals ? Height : 1, VK_FORMAT_R32G32B32A32_SFLOAT);
        VkDescriptorSetLayoutBinding Bindings[] = {
            { 0, VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 1, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 2, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 3, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 4, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 5, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 6, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 7, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
            { 8, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr }
        };

        VkDescriptorSetLayoutCreateInfo Layout = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO };
        Layout.bindingCount = 9; Layout.pBindings = Bindings;
        RgeCheck(vkCreateDescriptorSetLayout(Device->device, &Layout, nullptr, &DescriptorLayout), "Create trace descriptor layout");
        VkDescriptorPoolSize Sizes[] = {
            { VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR, 1 },
            { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 7 }, { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1 }
        };

        VkDescriptorPoolCreateInfo Pool = { VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO };
        Pool.maxSets = 1; Pool.poolSizeCount = 3; Pool.pPoolSizes = Sizes;
        RgeCheck(vkCreateDescriptorPool(Device->device, &Pool, nullptr, &DescriptorPool), "Create trace descriptor pool");

        VkDescriptorSetAllocateInfo Set = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO };
        Set.descriptorPool = DescriptorPool; Set.descriptorSetCount = 1; Set.pSetLayouts = &DescriptorLayout;
        RgeCheck(vkAllocateDescriptorSets(Device->device, &Set, &Descriptors), "Allocate trace descriptors");

        VkPushConstantRange Constants = { VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(RgeTraceParameters) };
        VkPipelineLayoutCreateInfo PipelineInfo = { VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
        PipelineInfo.setLayoutCount = 1; PipelineInfo.pSetLayouts = &DescriptorLayout;
        PipelineInfo.pushConstantRangeCount = 1; PipelineInfo.pPushConstantRanges = &Constants;
        RgeCheck(vkCreatePipelineLayout(Device->device, &PipelineInfo, nullptr, &PipelineLayout), "Create trace pipeline layout");

        VkShaderModule Shader = VK_NULL_HANDLE;
        VkShaderModuleCreateInfo Module = { VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO };
        Module.codeSize = sizeof(RgeTraceShader); Module.pCode = RgeTraceShader;
        RgeCheck(vkCreateShaderModule(Device->device, &Module, nullptr, &Shader), "Create ray query shader");

        VkComputePipelineCreateInfo Compute = { VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO };
        Compute.layout = PipelineLayout;
        Compute.stage = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO };
        Compute.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; Compute.stage.module = Shader; Compute.stage.pName = "main";

        VkResult Result = vkCreateComputePipelines(Device->device, VK_NULL_HANDLE, 1, &Compute, nullptr, &Pipeline);
        vkDestroyShaderModule(Device->device, Shader, nullptr);
        RgeCheck(Result, "Create ray query pipeline");

        uint32_t QueueCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(Device->physicalDevice, &QueueCount, nullptr);
        std::vector<VkQueueFamilyProperties> Queues(QueueCount);
        vkGetPhysicalDeviceQueueFamilyProperties(Device->physicalDevice, &QueueCount, Queues.data());
        TimestampBits = Queues[Device->graphicsQueueFamily].timestampValidBits;
        if (TimestampBits)
        {
            VkQueryPoolCreateInfo Query = { VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO };
            Query.queryType = VK_QUERY_TYPE_TIMESTAMP;
            Query.queryCount = static_cast<uint32_t>(Device->frames.size()) * 2;
            RgeCheck(vkCreateQueryPool(Device->device, &Query, nullptr, &QueryPool), "Create trace timing queries");
            QueryWritten.resize(Device->frames.size(), false);
        }
    }

    void FreeScene()
    {
        if (Top) DestroyAcceleration(Device->device, Top, nullptr);
        if (Bottom) DestroyAcceleration(Device->device, Bottom, nullptr);
        Top = Bottom = VK_NULL_HANDLE;
        FreeBuffer(TopStorage); FreeBuffer(BottomStorage); FreeBuffer(Instances); FreeBuffer(Indices); FreeBuffer(Triangles);
    }

    void BuildStructure(VkCommandBuffer Command, VkAccelerationStructureTypeKHR Type,
                        VkAccelerationStructureGeometryKHR& Geometry, uint32_t PrimitiveCount,
                        RgeBuffer& Storage, VkAccelerationStructureKHR& Structure, RgeBuffer& Scratch)
    {
        VkAccelerationStructureBuildGeometryInfoKHR Build = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR };
        Build.type = Type; Build.flags = VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR;
        Build.mode = VK_BUILD_ACCELERATION_STRUCTURE_MODE_BUILD_KHR;
        Build.geometryCount = 1; Build.pGeometries = &Geometry;

        VkAccelerationStructureBuildSizesInfoKHR Sizes = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR };
        GetBuildSizes(Device->device, VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR, &Build, &PrimitiveCount, &Sizes);
        MakeBuffer(Storage, Sizes.accelerationStructureSize, VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR);

        VkAccelerationStructureCreateInfoKHR Create = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR };
        Create.buffer = Storage.Handle; Create.size = Sizes.accelerationStructureSize; Create.type = Type;
        RgeCheck(CreateAcceleration(Device->device, &Create, nullptr, &Structure), "Create acceleration structure");

        VkPhysicalDeviceAccelerationStructurePropertiesKHR Properties = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_PROPERTIES_KHR };
        VkPhysicalDeviceProperties2 DeviceProperties = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2, &Properties };
        vkGetPhysicalDeviceProperties2(Device->physicalDevice, &DeviceProperties);

        VkDeviceSize Alignment = Properties.minAccelerationStructureScratchOffsetAlignment;
        MakeBuffer(Scratch, Sizes.buildScratchSize + Alignment, VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);
        Build.scratchData.deviceAddress = (Scratch.Address + Alignment - 1) & ~(Alignment - 1);
        Build.dstAccelerationStructure = Structure;

        VkAccelerationStructureBuildRangeInfoKHR Range = { PrimitiveCount, 0, 0, 0 };
        const VkAccelerationStructureBuildRangeInfoKHR* Ranges[] = { &Range };
        BuildAcceleration(Command, 1, &Build, Ranges);

        VkMemoryBarrier Barrier = { VK_STRUCTURE_TYPE_MEMORY_BARRIER };
        Barrier.srcAccessMask = VK_ACCESS_ACCELERATION_STRUCTURE_WRITE_BIT_KHR;
        Barrier.dstAccessMask = VK_ACCESS_ACCELERATION_STRUCTURE_READ_BIT_KHR;
        vkCmdPipelineBarrier(Command, VK_PIPELINE_STAGE_ACCELERATION_STRUCTURE_BUILD_BIT_KHR,
            VK_PIPELINE_STAGE_ACCELERATION_STRUCTURE_BUILD_BIT_KHR | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &Barrier, 0, nullptr, 0, nullptr);
    }

    void Upload(const float* PackedTriangles, uint32_t TriangleCount)
    {
        RgeFlush(Device);
        FreeScene();
        float Empty[20] = {};
        uint32_t Count = std::max(1u, TriangleCount);
        if (uint64_t(Count) * 80 > Device->deviceProperties.limits.maxStorageBufferRange)
            throw std::runtime_error("Trace geometry exceeds Vulkan storage buffer range");
        MakeBuffer(Triangles, VkDeviceSize(Count) * 80,
            VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR,
            TriangleCount ? PackedTriangles : Empty);
        std::vector<uint32_t> IndexData(size_t(Count) * 3);
        for (uint32_t Triangle = 0; Triangle < Count; Triangle++)
            for (uint32_t Corner = 0; Corner < 3; Corner++) IndexData[Triangle * 3 + Corner] = Triangle * 5 + Corner;
        MakeBuffer(Indices, IndexData.size() * sizeof(uint32_t), VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR, IndexData.data());

        VkCommandBuffer Command = VK_NULL_HANDLE;
        RgeBuffer BottomScratch, TopScratch;
        try
        {
            Command = MGVK_BeginNewCommandBuffer(Device, Device->cmdPool);

            VkAccelerationStructureGeometryKHR Geometry = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR };
            Geometry.geometryType = VK_GEOMETRY_TYPE_TRIANGLES_KHR;
            Geometry.flags = VK_GEOMETRY_OPAQUE_BIT_KHR;
            auto& Vertices = Geometry.geometry.triangles;
            Vertices.sType = VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_TRIANGLES_DATA_KHR;
            Vertices.vertexFormat = VK_FORMAT_R32G32B32_SFLOAT;
            Vertices.vertexData.deviceAddress = Triangles.Address; Vertices.vertexStride = 16;
            Vertices.maxVertex = Count * 5 - 3;
            Vertices.indexType = VK_INDEX_TYPE_UINT32; Vertices.indexData.deviceAddress = Indices.Address;
            BuildStructure(Command, VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR, Geometry, TriangleCount, BottomStorage, Bottom, BottomScratch);

            VkAccelerationStructureDeviceAddressInfoKHR Address = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_DEVICE_ADDRESS_INFO_KHR };
            Address.accelerationStructure = Bottom;

            VkAccelerationStructureInstanceKHR Instance = {};
            Instance.transform.matrix[0][0] = Instance.transform.matrix[1][1] = Instance.transform.matrix[2][2] = 1;
            Instance.mask = 255; Instance.flags = VK_GEOMETRY_INSTANCE_TRIANGLE_FACING_CULL_DISABLE_BIT_KHR;
            Instance.accelerationStructureReference = GetAccelerationAddress(Device->device, &Address);
            MakeBuffer(Instances, sizeof(Instance), VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR, &Instance);
            Geometry = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR };
            Geometry.geometryType = VK_GEOMETRY_TYPE_INSTANCES_KHR;
            Geometry.geometry.instances.sType = VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_INSTANCES_DATA_KHR;
            Geometry.geometry.instances.data.deviceAddress = Instances.Address;
            BuildStructure(Command, VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR, Geometry, 1, TopStorage, Top, TopScratch);
            MGVK_ExecuteAndFreeCommandBuffer(Device, Command, Device->cmdPool);
            Command = VK_NULL_HANDLE;
        }
        catch (...)
        {
            if (Command) vkFreeCommandBuffers(Device->device, Device->cmdPool, 1, &Command);
            FreeBuffer(BottomScratch); FreeBuffer(TopScratch);
            throw;
        }

        FreeBuffer(BottomScratch); FreeBuffer(TopScratch);

        VkWriteDescriptorSetAccelerationStructureKHR Acceleration = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET_ACCELERATION_STRUCTURE_KHR };
        Acceleration.accelerationStructureCount = 1; Acceleration.pAccelerationStructures = &Top;

        VkDescriptorImageInfo Output = { VK_NULL_HANDLE, ImageView, VK_IMAGE_LAYOUT_GENERAL };
        VkDescriptorBufferInfo Materials = { Triangles.Handle, 0, VkDeviceSize(Count) * 80 };
        VkWriteDescriptorSet Writes[3] = {};
        for (uint32_t Binding = 0; Binding < 3; Binding++)
        {
            Writes[Binding].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            Writes[Binding].dstSet = Descriptors; Writes[Binding].dstBinding = Binding; Writes[Binding].descriptorCount = 1;
        }

        Writes[0].descriptorType = VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR; Writes[0].pNext = &Acceleration;
        Writes[1].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE; Writes[1].pImageInfo = &Output;
        Writes[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; Writes[2].pBufferInfo = &Materials;
        vkUpdateDescriptorSets(Device->device, 3, Writes, 0, nullptr);
        for (uint32_t Index = 0; Index < 6; Index++)
        {
            VkDescriptorImageInfo Signal = { VK_NULL_HANDLE, Signals[Index].View, VK_IMAGE_LAYOUT_GENERAL };
            VkWriteDescriptorSet Write = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET };
            Write.dstSet = Descriptors; Write.dstBinding = Index + 3; Write.descriptorCount = 1;
            Write.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE; Write.pImageInfo = &Signal;
            vkUpdateDescriptorSets(Device->device, 1, &Write, 0, nullptr);
        }
    }

    void Dispatch(const RgeTraceParameters& Parameters)
    {
        if (!Top || Parameters.Width != Width || Parameters.Height != Height)
            throw std::runtime_error("Trace geometry or image size is not initialized");
        auto& Frame = Device->frames[Device->frameIndex];

        if (!Frame.is_recording) throw std::runtime_error("Ray tracing must run inside a MonoGame frame");

        auto Command = Frame.commandBuffer;
        MGVK_EndRenderPass(Device, Command);
        Device->renderTargetDirty = true;
        uint32_t QueryIndex = static_cast<uint32_t>(Device->frameIndex) * 2;
        bool Timing = QueryPool && Device->frameIndex < QueryWritten.size();
        if (Timing)
        {
            if (QueryWritten[Device->frameIndex])
            {
                uint64_t Timestamps[2] = {};
                if (vkGetQueryPoolResults(Device->device, QueryPool, QueryIndex, 2, sizeof(Timestamps), Timestamps,
                    sizeof(uint64_t), VK_QUERY_RESULT_64_BIT) == VK_SUCCESS)
                {
                    uint64_t Mask = TimestampBits == 64 ? UINT64_MAX : ((uint64_t(1) << TimestampBits) - 1);
                    GpuMilliseconds = float((Timestamps[1] - Timestamps[0]) & Mask) * Device->deviceProperties.limits.timestampPeriod / 1000000.0f;
                }
            }

            vkCmdResetQueryPool(Command, QueryPool, QueryIndex, 2);
            vkCmdWriteTimestamp(Command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, QueryPool, QueryIndex);
        }

        VkImageMemoryBarrier Barrier = { VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER };
        Barrier.srcAccessMask = ImageLayout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_SHADER_READ_BIT;
        Barrier.dstAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        Barrier.oldLayout = ImageLayout; Barrier.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        Barrier.srcQueueFamilyIndex = Barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        Barrier.image = Image; Barrier.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        vkCmdPipelineBarrier(Command, ImageLayout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &Barrier);
        for (auto& Signal : Signals) Signal.Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
        vkCmdBindPipeline(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Pipeline);
        vkCmdBindDescriptorSets(Command, VK_PIPELINE_BIND_POINT_COMPUTE, PipelineLayout, 0, 1, &Descriptors, 0, nullptr);
        vkCmdPushConstants(Command, PipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(Parameters), &Parameters);
        vkCmdDispatch(Command, (Width + 7) / 8, (Height + 7) / 8, 1);
        Barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; Barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        Barrier.oldLayout = VK_IMAGE_LAYOUT_GENERAL; Barrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        vkCmdPipelineBarrier(Command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
            0, 0, nullptr, 0, nullptr, 1, &Barrier);
        ImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        if (Timing)
        {
            vkCmdWriteTimestamp(Command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, QueryPool, QueryIndex + 1);
            QueryWritten[Device->frameIndex] = true;
        }
    }

    ~RgeTrace()
    {
        FreeScene();
        for (auto& Signal : Signals) Signal.Destroy(Device);
        if (QueryPool) vkDestroyQueryPool(Device->device, QueryPool, nullptr);
        if (Pipeline) vkDestroyPipeline(Device->device, Pipeline, nullptr);
        if (PipelineLayout) vkDestroyPipelineLayout(Device->device, PipelineLayout, nullptr);
        if (DescriptorPool) vkDestroyDescriptorPool(Device->device, DescriptorPool, nullptr);
        if (DescriptorLayout) vkDestroyDescriptorSetLayout(Device->device, DescriptorLayout, nullptr);
        if (ImageView) vkDestroyImageView(Device->device, ImageView, nullptr);
        if (Image) vkDestroyImage(Device->device, Image, nullptr);
        if (ImageMemory) vkFreeMemory(Device->device, ImageMemory, nullptr);
    }
};

extern "C" {
MG_EXPORT int RGE_RT_Version() { return 2; }
MG_EXPORT const char* RGE_RT_Error() { return RgeError.c_str(); }
MG_EXPORT int RGE_RT_Supported(VkDevice Logical)
{
    auto Found = RgeDevices.find(Logical);
    return Found != RgeDevices.end() && Found->second->radiantRayQuery;
}

MG_EXPORT RgeTrace* RGE_RT_Create(VkDevice Logical, uint32_t Width, uint32_t Height, int Signals)
{
    RgeTrace* Trace = nullptr;
    try
    {
        if (!RGE_RT_Supported(Logical)) throw std::runtime_error("Vulkan ray queries are not enabled on this device");

        auto Device = RgeDevices.at(Logical);
        if (!Width || !Height || Width > Device->deviceProperties.limits.maxImageDimension2D || Height > Device->deviceProperties.limits.maxImageDimension2D)
            throw std::runtime_error("Invalid ray tracing image dimensions");
        Trace = new RgeTrace(Device, Width, Height);
        Trace->DenoisingSignals = Signals != 0;
        Trace->Initialize();
        return Trace;
    }
    catch (const std::exception& Error) { RgeError = Error.what(); delete Trace; return nullptr; }
}

MG_EXPORT VkImage RGE_RT_Image(RgeTrace* Trace) { return Trace->Image; }
MG_EXPORT int RGE_RT_Upload(RgeTrace* Trace, const float* Triangles, uint32_t Count)
{
    try { Trace->Upload(Triangles, Count); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}

MG_EXPORT int RGE_RT_Dispatch(RgeTrace* Trace, const RgeTraceParameters* Parameters)
{
    try { Trace->Dispatch(*Parameters); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}

MG_EXPORT float RGE_RT_GpuMilliseconds(RgeTrace* Trace) { return Trace->GpuMilliseconds; }
MG_EXPORT int RGE_RT_Flush(RgeTrace* Trace)
{
    try { RgeFlush(Trace->Device); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}

MG_EXPORT int RGE_RT_Destroy(RgeTrace* Trace)
{
    try { if (Trace) { RgeFlush(Trace->Device); delete Trace; } return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}
}
