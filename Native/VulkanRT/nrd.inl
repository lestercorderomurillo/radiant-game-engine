#ifdef RADIANT_NRD
#include "NRD.h"
#include "nrd_composite.spv.h"

static void RgeNrdCheck(nrd::Result Result)
{
    if (Result != nrd::Result::SUCCESS) throw std::runtime_error("NRD API failed: " + std::to_string(uint32_t(Result)));
}

static VkFormat RgeNrdFormat(nrd::Format Format)
{
    switch (Format)
    {
#define RGE_FORMAT(Name, Vulkan) case nrd::Format::Name: return VK_FORMAT_##Vulkan
        RGE_FORMAT(R8_UNORM, R8_UNORM); RGE_FORMAT(R8_UINT, R8_UINT);
        RGE_FORMAT(R16_UINT, R16_UINT); RGE_FORMAT(R16_UNORM, R16_UNORM);
        RGE_FORMAT(R16_SFLOAT, R16_SFLOAT); RGE_FORMAT(R32_SFLOAT, R32_SFLOAT);
        RGE_FORMAT(RG16_SFLOAT, R16G16_SFLOAT); RGE_FORMAT(RG16_UNORM, R16G16_UNORM);
        RGE_FORMAT(RGBA8_UNORM, R8G8B8A8_UNORM); RGE_FORMAT(RGBA16_SFLOAT, R16G16B16A16_SFLOAT);
        RGE_FORMAT(RGBA16_UNORM, R16G16B16A16_UNORM); RGE_FORMAT(R32_UINT, R32_UINT);
        RGE_FORMAT(RG32_UINT, R32G32_UINT); RGE_FORMAT(RGBA32_SFLOAT, R32G32B32A32_SFLOAT);
        RGE_FORMAT(R10_G10_B10_A2_UNORM, A2B10G10R10_UNORM_PACK32);
        RGE_FORMAT(R11_G11_B10_UFLOAT, B10G11R11_UFLOAT_PACK32);
#undef RGE_FORMAT
        default: throw std::runtime_error("Unsupported NRD pool format: " + std::to_string(uint32_t(Format)));
    }
}

struct RgeNrdParameters
{
    float View[16], Projection[16];
    uint32_t Reset;
};

struct RgeNrd
{
    struct PipelineState { VkDescriptorSetLayout Resources = VK_NULL_HANDLE; VkPipelineLayout Layout = VK_NULL_HANDLE; VkPipeline Pipeline = VK_NULL_HANDLE; };
    struct FrameState { VkDescriptorPool Pool = VK_NULL_HANDLE; RgeBuffer Constants; void* Mapped = nullptr; uint64_t LastFrame = UINT64_MAX; };
    MGG_GraphicsDevice* Device;
    uint32_t Width, Height, Mode, FrameIndex = 0;
    nrd::Instance* Instance = nullptr;
    nrd::CommonSettings Common;
    std::vector<RgeImage> Permanent, Transient;
    RgeImage Filtered, Output;
    std::vector<PipelineState> Pipelines;
    PipelineState Composite;

    VkDescriptorSetLayout CommonLayout = VK_NULL_HANDLE;
    VkSampler Samplers[2] = {};
    std::vector<FrameState> Frames;

    VkDeviceSize ConstantStride = 0;
    uint32_t DispatchLimit = 0;

    RgeNrd(RgeTrace* Trace, uint32_t Denoiser) : Device(Trace->Device), Width(Trace->Width), Height(Trace->Height), Mode(Denoiser) {}

    void MakePipeline(PipelineState& State, const void* Code, size_t Size, const char* Entry,
                      const std::vector<VkDescriptorSetLayoutBinding>& Bindings, bool Nrd)
    {
        VkDescriptorSetLayoutCreateInfo SetInfo = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO };
        SetInfo.bindingCount = uint32_t(Bindings.size()); SetInfo.pBindings = Bindings.data();
        RgeCheck(vkCreateDescriptorSetLayout(Device->device, &SetInfo, nullptr, &State.Resources), "Create NRD resources layout");
        VkDescriptorSetLayout Sets[] = { State.Resources, CommonLayout };

        VkPushConstantRange Push = { VK_SHADER_STAGE_COMPUTE_BIT, 0, 12 };
        VkPipelineLayoutCreateInfo Layout = { VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
        Layout.setLayoutCount = Nrd ? 2 : 1; Layout.pSetLayouts = Sets;
        if (!Nrd) { Layout.pushConstantRangeCount = 1; Layout.pPushConstantRanges = &Push; }
        RgeCheck(vkCreatePipelineLayout(Device->device, &Layout, nullptr, &State.Layout), "Create NRD pipeline layout");

        VkShaderModuleCreateInfo ModuleInfo = { VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO };
        ModuleInfo.codeSize = Size; ModuleInfo.pCode = static_cast<const uint32_t*>(Code);

        VkShaderModule Module = VK_NULL_HANDLE;
        RgeCheck(vkCreateShaderModule(Device->device, &ModuleInfo, nullptr, &Module), "Create NRD shader");

        VkComputePipelineCreateInfo Info = { VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO };
        Info.layout = State.Layout; Info.stage = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO };
        Info.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; Info.stage.module = Module; Info.stage.pName = Entry;
        auto Result = vkCreateComputePipelines(Device->device, VK_NULL_HANDLE, 1, &Info, nullptr, &State.Pipeline);
        vkDestroyShaderModule(Device->device, Module, nullptr);
        RgeCheck(Result, "Create NRD compute pipeline");
    }

    void Initialize(RgeTrace* Trace)
    {
        if (Width > UINT16_MAX || Height > UINT16_MAX || !Trace->DenoisingSignals || Mode < 1 || Mode > 2)
            throw std::runtime_error("Invalid NRD dimensions, mode or trace signals");
        nrd::DenoiserDesc Denoiser = { 0, Mode == 1 ? nrd::Denoiser::REBLUR_DIFFUSE : nrd::Denoiser::RELAX_DIFFUSE };
        nrd::InstanceCreationDesc Create = {}; Create.denoisers = &Denoiser; Create.denoisersNum = 1;
        RgeNrdCheck(nrd::CreateInstance(Create, Instance));
        const auto& Desc = *nrd::GetInstanceDesc(*Instance);
        const auto& Offsets = nrd::GetLibraryDesc()->spirvBindingOffsets;
        if (Desc.resourcesSpaceIndex != 0 || Desc.constantBufferAndSamplersSpaceIndex != 1)
            throw std::runtime_error("Unexpected NRD binding spaces");
        for (uint32_t Index = 0; Index < 2; Index++)
        {
            VkSamplerCreateInfo Info = { VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO };
            Info.magFilter = Info.minFilter = Index == 0 ? VK_FILTER_NEAREST : VK_FILTER_LINEAR;
            Info.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
            Info.addressModeU = Info.addressModeV = Info.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
            RgeCheck(vkCreateSampler(Device->device, &Info, nullptr, &Samplers[Index]), "Create NRD sampler");
        }

        VkDescriptorSetLayoutBinding CommonBindings[] = {
            { Offsets.samplerOffset, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_COMPUTE_BIT, &Samplers[0] },
            { Offsets.samplerOffset + 1, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_COMPUTE_BIT, &Samplers[1] },
            { Offsets.constantBufferOffset + Desc.constantBufferRegisterIndex, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr }
        };

        VkDescriptorSetLayoutCreateInfo Layout = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO };
        Layout.bindingCount = 3; Layout.pBindings = CommonBindings;
        RgeCheck(vkCreateDescriptorSetLayout(Device->device, &Layout, nullptr, &CommonLayout), "Create NRD common layout");
        Pipelines.resize(Desc.pipelinesNum);
        for (uint32_t Index = 0; Index < Desc.pipelinesNum; Index++)
        {
            const auto& Description = Desc.pipelines[Index];
            std::vector<VkDescriptorSetLayoutBinding> Bindings;
            for (uint32_t Range = 0; Range < Description.resourceRangesNum; Range++)
            {
                const auto& Resources = Description.resourceRanges[Range];
                bool Storage = Resources.descriptorType == nrd::DescriptorType::STORAGE_TEXTURE;
                for (uint32_t Binding = 0; Binding < Resources.descriptorsNum; Binding++)
                    Bindings.push_back({ (Storage ? Offsets.storageTextureAndBufferOffset : Offsets.textureOffset) + Desc.resourcesBaseRegisterIndex + Binding,
                        Storage ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr });
            }

            MakePipeline(Pipelines[Index], Description.computeShaderSPIRV.bytecode, Description.computeShaderSPIRV.size, Desc.shaderEntryPoint, Bindings, true);
        }

        auto MakePool = [&](std::vector<RgeImage>& Pool, const nrd::TextureDesc* Textures, uint32_t Count)
        {
            Pool.resize(Count);
            for (uint32_t Index = 0; Index < Count; Index++)
            {
                auto Divisor = Textures[Index].downsampleFactor;
                Pool[Index].Create(Device, (Width + Divisor - 1) / Divisor, (Height + Divisor - 1) / Divisor, RgeNrdFormat(Textures[Index].format));
            }
        };
        MakePool(Permanent, Desc.permanentPool, Desc.permanentPoolSize);
        MakePool(Transient, Desc.transientPool, Desc.transientPoolSize);
        Filtered.Create(Device, Width, Height, VK_FORMAT_R16G16B16A16_SFLOAT);
        Output.Create(Device, Width, Height, VK_FORMAT_R32G32B32A32_SFLOAT);
        std::vector<VkDescriptorSetLayoutBinding> CompositeBindings;
        for (uint32_t Index = 0; Index < 5; Index++) CompositeBindings.push_back({ Index, Index == 4 ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr });
        MakePipeline(Composite, RgeNrdCompositeShader, sizeof(RgeNrdCompositeShader), "main", CompositeBindings, false);
        DispatchLimit = Desc.descriptorPoolDesc.setsMaxNum + 1;
        auto Alignment = Device->deviceProperties.limits.minUniformBufferOffsetAlignment;
        ConstantStride = (Desc.constantBufferMaxDataSize + Alignment - 1) / Alignment * Alignment;
        Frames.resize(Device->frames.size());
        for (auto& Frame : Frames)
        {
            VkDescriptorPoolSize Sizes[] = {
                { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, DispatchLimit * (Desc.descriptorPoolDesc.perSetTexturesMaxNum + 4) },
                { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, DispatchLimit * (Desc.descriptorPoolDesc.perSetStorageTexturesMaxNum + 1) },
                { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, DispatchLimit }, { VK_DESCRIPTOR_TYPE_SAMPLER, DispatchLimit * 2 }
            };

            VkDescriptorPoolCreateInfo Pool = { VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO };
            Pool.maxSets = DispatchLimit * 2; Pool.poolSizeCount = 4; Pool.pPoolSizes = Sizes;
            RgeCheck(vkCreateDescriptorPool(Device->device, &Pool, nullptr, &Frame.Pool), "Create NRD frame pool");
            std::vector<uint8_t> Zeros(size_t(ConstantStride * DispatchLimit), 0);
            Trace->MakeBuffer(Frame.Constants, Zeros.size(), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, Zeros.data());
            RgeCheck(vkMapMemory(Device->device, Frame.Constants.Memory, 0, Zeros.size(), 0, &Frame.Mapped), "Map NRD constants");
        }
    }

    RgeImage& Resolve(RgeTrace* Trace, const nrd::ResourceDesc& Resource)
    {
        switch (Resource.type)
        {
            case nrd::ResourceType::IN_DIFF_RADIANCE_HITDIST: return Trace->Signals[0];
            case nrd::ResourceType::IN_NORMAL_ROUGHNESS: return Trace->Signals[1];
            case nrd::ResourceType::IN_VIEWZ: return Trace->Signals[2];
            case nrd::ResourceType::IN_MV: return Trace->Signals[5];
            case nrd::ResourceType::OUT_DIFF_RADIANCE_HITDIST: return Filtered;
            case nrd::ResourceType::PERMANENT_POOL: return Permanent.at(Resource.indexInPool);
            case nrd::ResourceType::TRANSIENT_POOL: return Transient.at(Resource.indexInPool);
            default: throw std::runtime_error(std::string("Unexpected NRD input: ") + nrd::GetResourceTypeString(Resource.type));
        }
    }

    VkDescriptorSet Allocate(FrameState& Frame, VkDescriptorSetLayout Layout)
    {
        VkDescriptorSetAllocateInfo Info = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO };
        Info.descriptorPool = Frame.Pool; Info.descriptorSetCount = 1; Info.pSetLayouts = &Layout;

        VkDescriptorSet Set;
        RgeCheck(vkAllocateDescriptorSets(Device->device, &Info, &Set), "Allocate NRD descriptors");
        return Set;
    }

    void Dispatch(RgeTrace* Trace, const RgeNrdParameters& Parameters)
    {
        if (Trace->Width != Width || Trace->Height != Height || !Trace->DenoisingSignals)
            throw std::runtime_error("NRD trace signal dimensions changed");
        auto& GraphicsFrame = Device->frames[Device->frameIndex];

        if (!GraphicsFrame.is_recording) throw std::runtime_error("NRD requires an active graphics frame");

        auto Command = GraphicsFrame.commandBuffer;
        MGVK_EndRenderPass(Device, Command); Device->renderTargetDirty = true;
        auto& Frame = Frames.at(Device->frameIndex);

        // A caller can explicitly render a view twice before Present. Finish its first use before reusing descriptors.
        if (Frame.LastFrame == Device->frame) RgeFlush(Device);
        Frame.LastFrame = Device->frame;
        RgeCheck(vkResetDescriptorPool(Device->device, Frame.Pool, 0), "Reset NRD frame descriptors");
        memcpy(Common.worldToViewMatrixPrev, Common.worldToViewMatrix, 64);
        memcpy(Common.viewToClipMatrixPrev, Common.viewToClipMatrix, 64);
        memcpy(Common.worldToViewMatrix, Parameters.View, 64); memcpy(Common.viewToClipMatrix, Parameters.Projection, 64);
        bool Reset = Parameters.Reset || FrameIndex == 0;
        if (Reset) { memcpy(Common.worldToViewMatrixPrev, Parameters.View, 64); memcpy(Common.viewToClipMatrixPrev, Parameters.Projection, 64); }
        Common.resourceSize[0] = Common.resourceSizePrev[0] = Common.rectSize[0] = Common.rectSizePrev[0] = uint16_t(Width);
        Common.resourceSize[1] = Common.resourceSizePrev[1] = Common.rectSize[1] = Common.rectSizePrev[1] = uint16_t(Height);
        Common.isMotionVectorInWorldSpace = true;
        Common.motionVectorScale[2] = 1;
        Common.frameIndex = FrameIndex++;
        Common.accumulationMode = Reset ? nrd::AccumulationMode::CLEAR_AND_RESTART : nrd::AccumulationMode::CONTINUE;
        RgeNrdCheck(nrd::SetCommonSettings(*Instance, Common));
        nrd::Identifier Identifier = 0;
        const nrd::DispatchDesc* Dispatches = nullptr; uint32_t Count = 0;
        RgeNrdCheck(nrd::GetComputeDispatches(*Instance, &Identifier, 1, Dispatches, Count));

        if (Count >= DispatchLimit) throw std::runtime_error("NRD dispatch budget exceeded");

        const auto& Desc = *nrd::GetInstanceDesc(*Instance);
        const auto& Offsets = nrd::GetLibraryDesc()->spirvBindingOffsets;
        for (uint32_t Index = 0; Index < Count; Index++)
        {
            const auto& Dispatch = Dispatches[Index];
            auto& Pipeline = Pipelines.at(Dispatch.pipelineIndex);
            VkDescriptorSet Sets[] = { Allocate(Frame, Pipeline.Resources), Allocate(Frame, CommonLayout) };
            std::vector<VkDescriptorImageInfo> Images(Dispatch.resourcesNum);
            std::vector<VkWriteDescriptorSet> Writes(Dispatch.resourcesNum);
            uint32_t TextureIndex = 0, StorageIndex = 0;
            for (uint32_t ResourceIndex = 0; ResourceIndex < Dispatch.resourcesNum; ResourceIndex++)
            {
                const auto& Resource = Dispatch.resources[ResourceIndex];
                auto& Image = Resolve(Trace, Resource);
                bool Storage = Resource.descriptorType == nrd::DescriptorType::STORAGE_TEXTURE;
                Image.Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
                Images[ResourceIndex] = { VK_NULL_HANDLE, Image.View, VK_IMAGE_LAYOUT_GENERAL };
                auto& Write = Writes[ResourceIndex]; Write = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET };
                Write.dstSet = Sets[0]; Write.dstBinding = Desc.resourcesBaseRegisterIndex + (Storage ? Offsets.storageTextureAndBufferOffset + StorageIndex++ : Offsets.textureOffset + TextureIndex++);
                Write.descriptorCount = 1; Write.descriptorType = Storage ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
                Write.pImageInfo = &Images[ResourceIndex];
            }

            if (Dispatch.constantBufferDataSize > ConstantStride) throw std::runtime_error("NRD constant buffer overflow");

            if (Dispatch.constantBufferDataSize) memcpy(static_cast<uint8_t*>(Frame.Mapped) + Index * ConstantStride, Dispatch.constantBufferData, Dispatch.constantBufferDataSize);

            VkDescriptorBufferInfo Buffer = { Frame.Constants.Handle, Index * ConstantStride, ConstantStride };
            VkWriteDescriptorSet Constants = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET };
            Constants.dstSet = Sets[1]; Constants.dstBinding = Offsets.constantBufferOffset + Desc.constantBufferRegisterIndex;
            Constants.descriptorCount = 1; Constants.descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER; Constants.pBufferInfo = &Buffer;
            Writes.push_back(Constants);
            vkUpdateDescriptorSets(Device->device, uint32_t(Writes.size()), Writes.data(), 0, nullptr);
            vkCmdBindPipeline(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Pipeline.Pipeline);
            vkCmdBindDescriptorSets(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Pipeline.Layout, 0, 2, Sets, 0, nullptr);
            vkCmdDispatch(Command, Dispatch.gridWidth, Dispatch.gridHeight, 1);
        }

        Filtered.Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
        Trace->Signals[3].Transition(Command, VK_IMAGE_LAYOUT_GENERAL); Trace->Signals[4].Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
        Output.Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
        auto Set = Allocate(Frame, Composite.Resources);
        VkDescriptorImageInfo Images[] = {
            { VK_NULL_HANDLE, Filtered.View, VK_IMAGE_LAYOUT_GENERAL }, { VK_NULL_HANDLE, Trace->Signals[3].View, VK_IMAGE_LAYOUT_GENERAL },
            { VK_NULL_HANDLE, Trace->Signals[4].View, VK_IMAGE_LAYOUT_GENERAL }, { VK_NULL_HANDLE, Trace->ImageView, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL },
            { VK_NULL_HANDLE, Output.View, VK_IMAGE_LAYOUT_GENERAL }
        };
        VkWriteDescriptorSet Writes[5] = {};
        for (uint32_t Index = 0; Index < 5; Index++)
        {
            Writes[Index] = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET }; Writes[Index].dstSet = Set; Writes[Index].dstBinding = Index;
            Writes[Index].descriptorCount = 1; Writes[Index].descriptorType = Index == 4 ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            Writes[Index].pImageInfo = &Images[Index];
        }

        vkUpdateDescriptorSets(Device->device, 5, Writes, 0, nullptr);
        vkCmdBindPipeline(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Composite.Pipeline);
        vkCmdBindDescriptorSets(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Composite.Layout, 0, 1, &Set, 0, nullptr);
        uint32_t Push[] = { Width, Height, Mode };
        vkCmdPushConstants(Command, Composite.Layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(Push), Push);
        vkCmdDispatch(Command, (Width + 7) / 8, (Height + 7) / 8, 1);
        Output.Transition(Command, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    }

    ~RgeNrd()
    {
        for (auto& Frame : Frames)
        {
            if (Frame.Mapped) vkUnmapMemory(Device->device, Frame.Constants.Memory);
            if (Frame.Constants.Handle) vkDestroyBuffer(Device->device, Frame.Constants.Handle, nullptr);
            if (Frame.Constants.Memory) vkFreeMemory(Device->device, Frame.Constants.Memory, nullptr);
            if (Frame.Pool) vkDestroyDescriptorPool(Device->device, Frame.Pool, nullptr);
        }

        auto DestroyPipeline = [&](PipelineState& State) {
            if (State.Pipeline) vkDestroyPipeline(Device->device, State.Pipeline, nullptr);
            if (State.Layout) vkDestroyPipelineLayout(Device->device, State.Layout, nullptr);
            if (State.Resources) vkDestroyDescriptorSetLayout(Device->device, State.Resources, nullptr);
        };
        for (auto& Pipeline : Pipelines) DestroyPipeline(Pipeline);
        DestroyPipeline(Composite);
        if (CommonLayout) vkDestroyDescriptorSetLayout(Device->device, CommonLayout, nullptr);
        for (auto Sampler : Samplers) if (Sampler) vkDestroySampler(Device->device, Sampler, nullptr);
        for (auto& Image : Permanent) Image.Destroy(Device);
        for (auto& Image : Transient) Image.Destroy(Device);
        Filtered.Destroy(Device); Output.Destroy(Device);
        if (Instance) nrd::DestroyInstance(*Instance);
    }
};

extern "C" {
MG_EXPORT RgeNrd* RGE_NRD_Create(RgeTrace* Trace, uint32_t Mode)
{
    RgeNrd* Session = nullptr;
    try { Session = new RgeNrd(Trace, Mode); Session->Initialize(Trace); return Session; }
    catch (const std::exception& Error) { RgeError = Error.what(); delete Session; return nullptr; }
}

MG_EXPORT VkImage RGE_NRD_Image(RgeNrd* Session) { return Session->Output.Handle; }
MG_EXPORT int RGE_NRD_Dispatch(RgeNrd* Session, RgeTrace* Trace, const RgeNrdParameters* Parameters)
{
    try { Session->Dispatch(Trace, *Parameters); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}

MG_EXPORT int RGE_NRD_Flush(RgeNrd* Session)
{
    try { RgeFlush(Session->Device); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}

MG_EXPORT int RGE_NRD_Destroy(RgeNrd* Session)
{
    try { if (Session) { RgeFlush(Session->Device); delete Session; } return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}
}
#endif
extern "C" MG_EXPORT int RGE_NRD_Supported(VkDevice Logical)
{
#ifdef RADIANT_NRD
    auto Found = RgeDevices.find(Logical);

    if (Found == RgeDevices.end() || !Found->second->radiantRayQuery) return 0;

    VkPhysicalDeviceFeatures Features;
    vkGetPhysicalDeviceFeatures(Found->second->physicalDevice, &Features);
    return Features.shaderStorageImageWriteWithoutFormat && Features.shaderStorageImageReadWithoutFormat;
#else
    return 0;
#endif
}
