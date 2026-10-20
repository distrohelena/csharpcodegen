#!/usr/bin/env python3
"""Execute the GCC module-lifetime regression with explicit, reusable output paths.

Use -fno-use-cxa-atexit as on PS1. Disabling GNU unique symbols lets dlclose
retire the library, matching the PS1 loader's explicit module retirement.
"""
import argparse
import json
import pathlib
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=pathlib.Path)
parser.add_argument("--compiler", default="g++")
parser.add_argument("--runtime", type=pathlib.Path,
                    default=pathlib.Path(__file__).resolve().parents[3] / "cs2.cpp/.net.cpp/runtime")
args = parser.parse_args()
source = pathlib.Path(__file__).resolve().parent
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
library = output / "deferred-static-module.so"
program = output / "deferred-static-module-test"
flags = [args.compiler, "-std=c++20", "-O2", "-fno-use-cxa-atexit", "-fno-gnu-unique",
         "-I" + str(args.runtime.resolve()), "-I" + str(source)]
commands = [flags + ["-shared", "-fPIC", str(source / "DeferredStaticModule.cpp"), "-o", str(library)],
            flags + ["-rdynamic", str(source / "DeferredStaticModuleMain.cpp"), "-ldl", "-o", str(program)],
            [str(program), str(library)]]
for command in commands:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode:
        print((result.stdout + result.stderr)[-8000:])
        raise SystemExit(result.returncode)
print(json.dumps({"moduleReloads": 2, "exitCode": result.returncode, "runtimeOutput": result.stdout}))
