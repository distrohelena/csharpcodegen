"""Check that each MIPS translation unit calls shared deferred-static cleanup instead of expanding it."""
import argparse
import pathlib
import subprocess


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=pathlib.Path)
parser.add_argument("--generated-core", required=True, type=pathlib.Path)
parser.add_argument("--sdk", required=True, type=pathlib.Path)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[3]
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
source = output / "deferred-static-mips.cpp"
obj = output / "deferred-static-mips.o"
source.write_text('#include "runtime/native_string.hpp"\nextern "C" unsigned empty_length() { return String::get_Empty().size(); }\n')
includes = [repository / "cs2.cpp/.net.cpp", args.generated_core,
            args.sdk.parent.parent / "src", args.sdk.parent.parent / "src/platform/ps1", args.sdk,
            args.sdk / "third_party/EASTL/include", args.sdk / "third_party/EABase/include/Common"]
command = ["mipsel-none-elf-g++", "-std=c++20", "-march=mips1", "-mabi=32", "-msoft-float",
           "-mno-gpopt", "-fno-exceptions", "-fno-use-cxa-atexit", "-ffreestanding",
           "-fno-builtin", "-ffunction-sections", "-fdata-sections", "-Os",
           "-include", str(args.generated_core / "helcpp_config.hpp"),
           *["-I" + str(p) for p in includes], "-c", str(source), "-o", str(obj)]
subprocess.run(command, check=True)
symbols = subprocess.check_output(["mipsel-none-elf-nm", "-S", "--defined-only", str(obj)], text=True)
cleanup = sum(int(parts[1], 16) for line in symbols.splitlines()
              if len(parts := line.split()) == 4 and parts[3].startswith("_GLOBAL__sub_D_"))
print("MIPS translation-unit static cleanup bytes:", cleanup)
assert cleanup <= 80, "Deferred string cleanup is duplicated into each translation unit"
print("PASS shared deferred-static cleanup budget")
