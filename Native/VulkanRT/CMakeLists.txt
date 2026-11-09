cmake_minimum_required(VERSION 3.24)
project(RadiantVulkanRT LANGUAGES C CXX)
if(NOT WIN32 AND NOT CMAKE_SYSTEM_NAME STREQUAL "Linux")
    message(FATAL_ERROR "Vulkan RT runtime currently supports Windows and Linux")
endif()
find_package(Python3 3.12 REQUIRED COMPONENTS Interpreter)
find_program(GLSLANG_VALIDATOR glslangValidator HINTS "$ENV{VULKAN_SDK}/Bin" REQUIRED)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(SDL_SHARED OFF CACHE BOOL "" FORCE)
set(SDL_STATIC ON CACHE BOOL "" FORCE)
set(SDL_TEST OFF CACHE BOOL "" FORCE)
set(BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(BUILD_UTILS OFF CACHE BOOL "" FORCE)
set(BUILD_SDL3 OFF CACHE BOOL "" FORCE)
add_subdirectory("${RADIANT_SOURCES}/sdl" sdl EXCLUDE_FROM_ALL)
# FAudio normally searches for an installed SDL package. Use this build's copy.
set(SDL2_INCLUDE_DIRS "${RADIANT_SOURCES}/sdl/include")
set(SDL2_LIBRARIES SDL2-static)
set(SDL2_FOUND TRUE)
set(CMAKE_DISABLE_FIND_PACKAGE_SDL2 TRUE)
add_subdirectory("${RADIANT_SOURCES}/faudio" faudio EXCLUDE_FROM_ALL)
set(MONOGAME "${RADIANT_SOURCES}/monogame/native/monogame")
execute_process(COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/patch_runtime.py"
    "${MONOGAME}/vulkan/MGG_Vulkan.cpp" "${RADIANT_GENERATED}/MGG_Vulkan.cpp"
    COMMAND_ERROR_IS_FATAL ANY)
file(GLOB COMMON_SOURCES "${MONOGAME}/common/*.cpp")
add_library(mgruntime SHARED ${COMMON_SOURCES} "${MONOGAME}/sdl/MGP_sdl.cpp"
    "${MONOGAME}/faudio/MGA_faudio.cpp" "${RADIANT_GENERATED}/MGG_Vulkan.cpp")
target_compile_features(mgruntime PRIVATE cxx_std_17)
target_compile_definitions(mgruntime PRIVATE DLL_EXPORT MG_SDL2 MG_VULKAN MG_FAUDIO)
target_include_directories(mgruntime PRIVATE "${MONOGAME}/include" "${RADIANT_SOURCES}/stb"
    "${RADIANT_SOURCES}/volk" "${RADIANT_SOURCES}/vma/include" "${RADIANT_SOURCES}/vulkan/include"
    "${RADIANT_SOURCES}/sdl/include" "${RADIANT_SOURCES}/faudio/include"
    "${RADIANT_GENERATED}" "${CMAKE_CURRENT_SOURCE_DIR}" "${CMAKE_CURRENT_BINARY_DIR}")
target_link_libraries(mgruntime PRIVATE SDL2-static FAudio-static ${CMAKE_DL_LIBS})
if(WIN32)
    target_compile_definitions(mgruntime PRIVATE NOMINMAX)
    target_sources(mgruntime PRIVATE windows_exports.def)
else()
    target_link_libraries(mgruntime PRIVATE pthread m rt)
endif()
add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/path_trace.spv.h"
    COMMAND "${GLSLANG_VALIDATOR}" -V --target-env vulkan1.2
        "${CMAKE_CURRENT_SOURCE_DIR}/path_trace.comp" -o "${CMAKE_CURRENT_BINARY_DIR}/path_trace.spv"
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/embed_spirv.py"
        "${CMAKE_CURRENT_BINARY_DIR}/path_trace.spv" "${CMAKE_CURRENT_BINARY_DIR}/path_trace.spv.h"
    DEPENDS path_trace.comp embed_spirv.py VERBATIM)
target_sources(mgruntime PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/path_trace.spv.h")
install(TARGETS mgruntime LIBRARY DESTINATION . RUNTIME DESTINATION .)
install(FILES "${RADIANT_SOURCES}/monogame/LICENSE.txt" DESTINATION licenses RENAME MonoGame.txt)
install(FILES "${RADIANT_SOURCES}/sdl/LICENSE.txt" DESTINATION licenses RENAME SDL.txt)
install(FILES "${RADIANT_SOURCES}/faudio/LICENSE" DESTINATION licenses RENAME FAudio.txt)
install(FILES "${RADIANT_SOURCES}/volk/LICENSE.md" DESTINATION licenses RENAME Volk.txt)
install(FILES "${RADIANT_SOURCES}/vma/LICENSE.txt" DESTINATION licenses RENAME VMA.txt)
install(FILES "${RADIANT_SOURCES}/stb/LICENSE" DESTINATION licenses RENAME stb.txt)
install(DIRECTORY "${RADIANT_SOURCES}/vulkan/LICENSES/" DESTINATION licenses/Vulkan-Headers)

option(RADIANT_NRD "Build NRD REBLUR and RELAX integration" OFF)
if(RADIANT_NRD)
    set(NRD_STATIC_LIBRARY ON CACHE BOOL "" FORCE)
    set(NRD_NRI OFF CACHE BOOL "" FORCE)
    set(NRD_NORMAL_ENCODING "2" CACHE STRING "" FORCE)
    set(NRD_SUPPORTS_QUAD_INTRINSICS OFF CACHE BOOL "" FORCE)
    set(NRD_EMBEDS_DXBC_SHADERS OFF CACHE BOOL "" FORCE)
    set(NRD_EMBEDS_DXIL_SHADERS OFF CACHE BOOL "" FORCE)
    add_subdirectory("${RADIANT_SOURCES}/nrd" nrd)
    target_link_libraries(mgruntime PRIVATE NRD)
    target_compile_definitions(mgruntime PRIVATE RADIANT_NRD)
    add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/nrd_composite.spv.h"
        COMMAND "${GLSLANG_VALIDATOR}" -V --target-env vulkan1.2
            "${CMAKE_CURRENT_SOURCE_DIR}/nrd_composite.comp" -o "${CMAKE_CURRENT_BINARY_DIR}/nrd_composite.spv"
        COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/embed_spirv.py"
            "${CMAKE_CURRENT_BINARY_DIR}/nrd_composite.spv" "${CMAKE_CURRENT_BINARY_DIR}/nrd_composite.spv.h" RgeNrdCompositeShader
        DEPENDS nrd_composite.comp embed_spirv.py VERBATIM)
    target_sources(mgruntime PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/nrd_composite.spv.h")
    install(FILES "${RADIANT_SOURCES}/nrd/LICENSE.txt" DESTINATION licenses RENAME NRD.txt)
    get_target_property(RADIANT_MATHLIB_SOURCE MathLib SOURCE_DIR)
    get_target_property(RADIANT_SHADERMAKE_SOURCE ShaderMakeBlob SOURCE_DIR)
    install(FILES "${RADIANT_MATHLIB_SOURCE}/LICENSE.txt" DESTINATION licenses RENAME MathLib.txt)
    install(FILES "${RADIANT_SHADERMAKE_SOURCE}/LICENSE.txt" DESTINATION licenses RENAME ShaderMake.txt)
endif()
