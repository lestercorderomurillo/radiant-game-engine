// General compute on MonoGame's graphics queue; no ray-query feature dependency.
struct RgeCompute
{
    MGG_GraphicsDevice* Device;
    RgeImage Output;
    VkDescriptorSetLayout Bindings = VK_NULL_HANDLE;
    VkDescriptorPool Pool = VK_NULL_HANDLE;
    VkDescriptorSet Set = VK_NULL_HANDLE;
    VkPipelineLayout Layout = VK_NULL_HANDLE;
    VkPipeline Pipeline = VK_NULL_HANDLE;
    uint32_t ConstantBytes;

    RgeCompute(MGG_GraphicsDevice* Device, uint32_t ConstantBytes) : Device(Device), ConstantBytes(ConstantBytes) {}

    void Initialize(const uint32_t* Code, uint32_t Bytes, uint32_t Width, uint32_t Height)
    {
        Output.Create(Device, Width, Height, VK_FORMAT_R32G32B32A32_SFLOAT);
        VkDescriptorSetLayoutBinding Binding = { 0, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr };
        VkDescriptorSetLayoutCreateInfo BindingInfo = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO };
        BindingInfo.bindingCount = 1; BindingInfo.pBindings = &Binding;
        RgeCheck(vkCreateDescriptorSetLayout(Device->device, &BindingInfo, nullptr, &Bindings), "Create compute bindings");
        VkPushConstantRange Constants = { VK_SHADER_STAGE_COMPUTE_BIT, 0, ConstantBytes };
        VkPipelineLayoutCreateInfo LayoutInfo = { VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
        LayoutInfo.setLayoutCount = 1; LayoutInfo.pSetLayouts = &Bindings;
        LayoutInfo.pushConstantRangeCount = ConstantBytes ? 1 : 0; LayoutInfo.pPushConstantRanges = &Constants;
        RgeCheck(vkCreatePipelineLayout(Device->device, &LayoutInfo, nullptr, &Layout), "Create compute layout");
        VkShaderModuleCreateInfo ShaderInfo = { VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO };
        ShaderInfo.codeSize = Bytes; ShaderInfo.pCode = Code;
        VkShaderModule Shader;
        RgeCheck(vkCreateShaderModule(Device->device, &ShaderInfo, nullptr, &Shader), "Create compute shader");
        VkComputePipelineCreateInfo Info = { VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO };
        Info.layout = Layout;
        Info.stage = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO };
        Info.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; Info.stage.module = Shader; Info.stage.pName = "main";
        auto Result = vkCreateComputePipelines(Device->device, VK_NULL_HANDLE, 1, &Info, nullptr, &Pipeline);
        vkDestroyShaderModule(Device->device, Shader, nullptr);
        RgeCheck(Result, "Create compute pipeline");
        VkDescriptorPoolSize Size = { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1 };
        VkDescriptorPoolCreateInfo PoolInfo = { VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO };
        PoolInfo.maxSets = 1; PoolInfo.poolSizeCount = 1; PoolInfo.pPoolSizes = &Size;
        RgeCheck(vkCreateDescriptorPool(Device->device, &PoolInfo, nullptr, &Pool), "Create compute pool");
        VkDescriptorSetAllocateInfo Allocate = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO };
        Allocate.descriptorPool = Pool; Allocate.descriptorSetCount = 1; Allocate.pSetLayouts = &Bindings;
        RgeCheck(vkAllocateDescriptorSets(Device->device, &Allocate, &Set), "Allocate compute bindings");
        VkDescriptorImageInfo Image = { VK_NULL_HANDLE, Output.View, VK_IMAGE_LAYOUT_GENERAL };
        VkWriteDescriptorSet Write = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET };
        Write.dstSet = Set; Write.dstBinding = 0; Write.descriptorCount = 1;
        Write.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE; Write.pImageInfo = &Image;
        vkUpdateDescriptorSets(Device->device, 1, &Write, 0, nullptr);
    }

    void Dispatch(uint32_t X, uint32_t Y, uint32_t Z, const void* Constants)
    {
        const auto& Limits = Device->deviceProperties.limits;
        if (!X || !Y || !Z || X > Limits.maxComputeWorkGroupCount[0] || Y > Limits.maxComputeWorkGroupCount[1] || Z > Limits.maxComputeWorkGroupCount[2])
            throw std::runtime_error("Compute group count exceeds device limits");
        auto& Frame = Device->frames[Device->frameIndex];
        if (!Frame.is_recording) throw std::runtime_error("Compute dispatch requires an active MonoGame frame");
        auto Command = Frame.commandBuffer;
        MGVK_EndRenderPass(Device, Command);
        Device->renderTargetDirty = true;
        Output.Transition(Command, VK_IMAGE_LAYOUT_GENERAL);
        vkCmdBindPipeline(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Pipeline);
        vkCmdBindDescriptorSets(Command, VK_PIPELINE_BIND_POINT_COMPUTE, Layout, 0, 1, &Set, 0, nullptr);
        if (ConstantBytes) vkCmdPushConstants(Command, Layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, ConstantBytes, Constants);
        vkCmdDispatch(Command, X, Y, Z);
        Output.Transition(Command, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    }

    ~RgeCompute()
    {
        if (Pipeline) vkDestroyPipeline(Device->device, Pipeline, nullptr);
        if (Layout) vkDestroyPipelineLayout(Device->device, Layout, nullptr);
        if (Pool) vkDestroyDescriptorPool(Device->device, Pool, nullptr);
        if (Bindings) vkDestroyDescriptorSetLayout(Device->device, Bindings, nullptr);
        Output.Destroy(Device);
    }
};

extern "C" {
MG_EXPORT int RGE_Compute_Version() { return 1; }
MG_EXPORT int RGE_Compute_Supported(VkDevice Logical)
{
    auto Found = RgeDevices.find(Logical);
    if (Found == RgeDevices.end()) return 0;
    auto Device = Found->second;
    uint32_t Count = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(Device->physicalDevice, &Count, nullptr);
    std::vector<VkQueueFamilyProperties> Families(Count);
    vkGetPhysicalDeviceQueueFamilyProperties(Device->physicalDevice, &Count, Families.data());
    VkFormatProperties Format;
    vkGetPhysicalDeviceFormatProperties(Device->physicalDevice, VK_FORMAT_R32G32B32A32_SFLOAT, &Format);
    VkFormatFeatureFlags Required = VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT | VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT;
    return (Families[Device->graphicsQueueFamily].queueFlags & VK_QUEUE_COMPUTE_BIT) && (Format.optimalTilingFeatures & Required) == Required;
}
MG_EXPORT RgeCompute* RGE_Compute_Create(VkDevice Logical, const uint32_t* Code, uint32_t Bytes, uint32_t Width, uint32_t Height, uint32_t Constants)
{
    RgeCompute* Session = nullptr;
    try
    {
        if (!RGE_Compute_Supported(Logical)) throw std::runtime_error("Compute or RGBA32F storage images unavailable");
        auto Device = RgeDevices.at(Logical);
        if (!Code || Bytes < 20 || Bytes % 4 || Code[0] != 0x07230203) throw std::runtime_error("Invalid SPIR-V header");
        if (!Width || !Height || Width > Device->deviceProperties.limits.maxImageDimension2D || Height > Device->deviceProperties.limits.maxImageDimension2D)
            throw std::runtime_error("Invalid compute image dimensions");
        if (Constants % 4 || Constants > Device->deviceProperties.limits.maxPushConstantsSize) throw std::runtime_error("Invalid compute push constant size");
        Session = new RgeCompute(Device, Constants);
        Session->Initialize(Code, Bytes, Width, Height);
        return Session;
    }
    catch (const std::exception& Error) { RgeError = Error.what(); delete Session; return nullptr; }
}
MG_EXPORT VkImage RGE_Compute_Image(RgeCompute* Session) { return Session->Output.Handle; }
MG_EXPORT int RGE_Compute_Dispatch(RgeCompute* Session, uint32_t X, uint32_t Y, uint32_t Z, const void* Constants)
{
    try { Session->Dispatch(X, Y, Z, Constants); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}
MG_EXPORT int RGE_Compute_Flush(RgeCompute* Session)
{
    try { RgeFlush(Session->Device); return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}
MG_EXPORT int RGE_Compute_Destroy(RgeCompute* Session)
{
    try { if (Session) { RgeFlush(Session->Device); delete Session; } return 1; }
    catch (const std::exception& Error) { RgeError = Error.what(); return 0; }
}
}
