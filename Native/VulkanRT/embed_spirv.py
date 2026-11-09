import struct
import sys
from pathlib import Path

data = Path(sys.argv[1]).read_bytes()
words = struct.unpack(f"<{len(data) // 4}I", data)
Path(sys.argv[2]).write_text("static const uint32_t " + (sys.argv[3] if len(sys.argv) > 3 else "RgeTraceShader") + "[] = {\n" +
    ",\n".join(",".join(f"0x{word:08x}" for word in words[offset:offset + 8])
                for offset in range(0, len(words), 8)) + "\n};\n")
