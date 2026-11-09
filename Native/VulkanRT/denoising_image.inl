// Shared render-thread storage for native reconstruction passes.
struct RgeImage
{
    VkImage Handle = VK_NULL_HANDLE;
    VkDeviceMemory Memory = VK_NULL_HANDLE;
    VkImageView View = VK_NULL_HANDLE;
    VkImageLayout Layout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkFormat Format = VK_FORMAT_UNDEFINED;

    void Create(MGG_GraphicsDevice* Device, uint32_t Width, uint32_t Height, VkFormat ImageFormat)
    {
        Format = ImageFormat;

        VkImageCreateInfo Info = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
        Info.imageType = VK_IMAGE_TYPE_2D; Info.format = Format;
        Info.extent = { Width, Height, 1 }; Info.mipLevels = Info.arrayLayers = 1;
        Info.samples = VK_SAMPLE_COUNT_1_BIT; Info.tiling = VK_IMAGE_TILING_OPTIMAL;
        Info.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        RgeCheck(vkCreateImage(Device->device, &Info, nullptr, &Handle), "Create reconstruction image");

        VkMemoryRequirements Requirements;
        vkGetImageMemoryRequirements(Device->device, Handle, &Requirements);

        VkPhysicalDeviceMemoryProperties Properties;
        vkGetPhysicalDeviceMemoryProperties(Device->physicalDevice, &Properties);

        VkMemoryAllocateInfo Allocation = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO };
        Allocation.allocationSize = Requirements.size;
        bool Found = false;
        for (uint32_t Index = 0; Index < Properties.memoryTypeCount; Index++)
            if ((Requirements.memoryTypeBits & (1u << Index)) && (Properties.memoryTypes[Index].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT))
            { Allocation.memoryTypeIndex = Index; Found = true; break; }
        if (!Found) throw std::runtime_error("No reconstruction image memory type");

        RgeCheck(vkAllocateMemory(Device->device, &Allocation, nullptr, &Memory), "Allocate reconstruction image");
        RgeCheck(vkBindImageMemory(Device->device, Handle, Memory, 0), "Bind reconstruction image");

        VkImageViewCreateInfo ViewInfo = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
        ViewInfo.image = Handle; ViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D; ViewInfo.format = Format;
        ViewInfo.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        RgeCheck(vkCreateImageView(Device->device, &ViewInfo, nullptr, &View), "Create reconstruction view");
    }

    void Transition(VkCommandBuffer Command, VkImageLayout Next)
    {
        VkImageMemoryBarrier Barrier = { VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER };
        Barrier.srcAccessMask = Layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_MEMORY_READ_BIT | VK_ACCESS_MEMORY_WRITE_BIT;
        Barrier.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT | VK_ACCESS_MEMORY_WRITE_BIT;
        Barrier.oldLayout = Layout; Barrier.newLayout = Next;
        Barrier.srcQueueFamilyIndex = Barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        Barrier.image = Handle; Barrier.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        vkCmdPipelineBarrier(Command, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0, 0, nullptr, 0, nullptr, 1, &Barrier);
        Layout = Next;
    }

    void Destroy(MGG_GraphicsDevice* Device)
    {
        if (View) vkDestroyImageView(Device->device, View, nullptr);
        if (Handle) vkDestroyImage(Device->device, Handle, nullptr);
        if (Memory) vkFreeMemory(Device->device, Memory, nullptr);
        *this = {};
    }
};
