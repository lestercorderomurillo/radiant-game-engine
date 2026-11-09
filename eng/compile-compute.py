#!/usr/bin/env python3
"""Compile a GLSL compute kernel to SPIR-V for Renderer.CreateComputeShader."""
import argparse
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    compiler = shutil.which("glslangValidator")
    if compiler is None:
        parser.error("Install glslangValidator (glslang or Vulkan SDK) and add it to PATH.")
    if not args.source.is_file():
        parser.error(f"Shader source does not exist: {args.source}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([compiler, "-V", "--target-env", "vulkan1.0", "-S", "comp",
                    str(args.source), "-o", str(args.output)], check=True)


if __name__ == "__main__":
    main()
