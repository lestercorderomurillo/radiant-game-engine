#!/usr/bin/env python3
"""Build the optional MonoGame 3.8.6 Vulkan runtime with Radiant ray queries.

Requires Python 3.12+, CMake, a C/C++ compiler, .NET 8+, and glslangValidator
(Vulkan SDK on Windows). Downloads are pinned to upstream commits.
"""
import argparse
import concurrent.futures
import os
import platform
from pathlib import Path
import subprocess
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / "artifacts/native"
SOURCES = WORK / "sources"
DEPENDENCIES = {
    "monogame": ("MonoGame/MonoGame", "c610a529667336f63da323c6057274258727664b"),
    "sdl": ("libsdl-org/SDL", "5d249570393f7a37e037abf22cd6012a4cc56a71"),
    "faudio": ("FNA-XNA/FAudio", "8a87fdc9242a7b507cf5c96c539334a1760c1ec7"),
    "vma": ("GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator", "0e89587db3ebee4d463f191bd296374c5fafc8ea"),
    "volk": ("zeux/volk", "3a8068a57417940cf2bf9d837a7bb60d015ca2f1"),
    "vulkan": ("KhronosGroup/Vulkan-Headers", "eaa319dade959cb61ed2229c8ea42e307cc8f8b3"),
    "stb": ("nothings/stb", "f58f558c120e9b32c217290b80bad1a0729fbb2c"),
}


def fetch(entry):
    name, (repo, commit) = entry
    destination = SOURCES / name

    if (destination / ".radiant-source").exists():
        if (destination / ".radiant-source").read_text() != commit:
            raise RuntimeError(f"Source version mismatch: {destination}")

        return name, destination

    archive = SOURCES / f"{name}-{commit}.tar.gz"
    print(f"Fetching {repo}@{commit}", flush=True)

    if not archive.exists():
        temporary = archive.with_suffix(".download")
        urllib.request.urlretrieve(f"https://codeload.github.com/{repo}/tar.gz/{commit}", temporary)
        temporary.replace(archive)

    with tarfile.open(archive) as bundle:
        prefix = bundle.getmembers()[0].name.split("/")[0]
        bundle.extractall(SOURCES, filter="data")

    if destination.exists():
        raise RuntimeError(f"Incomplete source directory; remove and retry: {destination}")

    (SOURCES / prefix).rename(destination)
    (destination / ".radiant-source").write_text(commit)
    return name, destination


def run(*args):
    subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jobs", type=int, default=min(8, os.cpu_count() or 2))
    parser.add_argument("--nrd", action="store_true", help="Include official NRD REBLUR/RELAX (requires CMake 3.30+)")
    args = parser.parse_args()

    if args.nrd:
        DEPENDENCIES["nrd"] = ("NVIDIA-RTX/NRD", "1b6c1865cb59cd14081f2e2eecd02e37b8ca6853")

    if os.name != "nt" and os.uname().sysname != "Linux":
        parser.error("This optional runtime supports Windows and Linux.")

    SOURCES.mkdir(parents=True, exist_ok=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        sources = dict(executor.map(fetch, DEPENDENCIES.items()))

    tools = WORK / "tools"
    compiler = tools / ("mgfxc.exe" if os.name == "nt" else "mgfxc")

    if not compiler.exists():
        run("dotnet", "tool", "install", "dotnet-mgfxc", "--tool-path", tools, "--version", "3.8.6")

    generated = WORK / "generated"
    generated.mkdir(exist_ok=True)
    effects = sources["monogame"] / "MonoGame.Framework/Platform/Graphics/Effect/Resources"

    for name in ("AlphaTestEffect", "BasicEffect", "DualTextureEffect", "EnvironmentMapEffect", "SkinnedEffect", "SpriteEffect"):
        output = generated / f"{name}.vk.mgfxo.h"

        if not output.exists():
            run(compiler, effects / f"{name}.fx", output, "/Profile:Vulkan")

    run("cmake", "-S", ROOT / "Native/VulkanRT", "-B", WORK / "build",
        f"-DRADIANT_SOURCES={SOURCES}", f"-DRADIANT_GENERATED={generated}",
        "-DCMAKE_BUILD_TYPE=Release", f"-DRADIANT_NRD={'ON' if args.nrd else 'OFF'}")
    run("cmake", "--build", WORK / "build", "--config", "Release", "--parallel", args.jobs)
    run("cmake", "--install", WORK / "build", "--config", "Release", "--prefix", WORK / "installed")
    architecture = {"x86_64": "x64", "amd64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(platform.machine().lower())

    if architecture is None:
        raise RuntimeError(f"Unsupported build architecture: {platform.machine()}")

    (WORK / "installed/runtime-id.txt").write_text(("win" if os.name == "nt" else "linux") + "-" + architecture)
    print("Built optional Vulkan RT runtime. Build Radiant with -p:RadiantVulkanRT=true.")


if __name__ == "__main__":
    main()
