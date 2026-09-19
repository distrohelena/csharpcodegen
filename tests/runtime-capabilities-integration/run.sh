#!/bin/sh
# Run inside a C++ build environment. EASTL and EABase are caller-owned inputs.
set -eu
if [ "$#" -ne 2 ]; then
    echo 'usage: run.sh OUTPUT_DIRECTORY EASTL_AND_EABASE_PARENT' >&2
    exit 2
fi
fixture=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
runtime="$fixture/../../cs2.cpp/.net.cpp"
output=$1
dependencies=$2
mkdir -p "$output"
cxx=${CXX:-g++}

if "$cxx" -std=c++20 -I"$fixture/missing-provider" -I"$runtime" \
    -c "$fixture/missing-provider.cpp" -o "$output/missing-provider.o" \
    >"$output/missing-provider.log" 2>&1; then
    echo 'Missing provider unexpectedly compiled.' >&2
    exit 1
fi
grep -q 'custom runtime provider' "$output/missing-provider.log"

if "$cxx" -std=c++20 -fno-exceptions -fno-rtti \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    -c "$fixture/unsupported-cast.cpp" -o "$output/unsupported-cast.o" \
    >"$output/unsupported-cast.log" 2>&1; then
    echo 'RTTI-dependent downcast unexpectedly compiled.' >&2
    exit 1
fi
grep -q 'RTTI' "$output/unsupported-cast.log"

if "$cxx" -std=c++20 -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    -c "$fixture/unsupported-helper.cpp" -o "$output/unsupported-helper.o" \
    >"$output/unsupported-helper.log" 2>&1; then
    echo 'Unsupported hosted console service unexpectedly compiled.' >&2
    exit 1
fi
grep -q 'requires HE_CPP_USE_STD_STRING=1' "$output/unsupported-helper.log"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti \
    -DEA_COMPILER_HAS_INTTYPES -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/shared_ptr_smoke.cpp" -o "$output/shared-ptr"
"$output/shared-ptr"

"$cxx" -std=c++20 -DHE_CPP_TEST_HOST -I"$fixture/hosted" -I"$runtime" \
    "$fixture/smoke.cpp" -o "$output/hosted"
"$output/hosted"
result=0
"$output/hosted" fail || result=$?
test "$result" -eq 73

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -I"$fixture/hosted-no-unwind" -I"$runtime" \
    "$fixture/smoke.cpp" -o "$output/hosted-no-unwind"
"$output/hosted-no-unwind"

"$cxx" -std=c++20 -DHE_CPP_TEST_HOST \
    -I"$fixture/hosted" -I"$runtime" \
    "$fixture/services.cpp" -o "$output/hosted-services"
"$output/hosted-services"

"$cxx" -std=c++20 -DHE_CPP_TEST_HOST \
    -I"$fixture/hosted" -I"$runtime" \
    "$fixture/streams.cpp" "$fixture/streams-main.cpp" \
    "$runtime/system/io/memory-stream.cpp" \
    "$runtime/system/io/file-stream.cpp" \
    "$runtime/system/io/binary-reader.cpp" \
    "$runtime/system/io/binary-writer.cpp" \
    -o "$output/hosted-streams"
"$output/hosted-streams"

"$cxx" -std=c++20 -DHE_CPP_TEST_HOST -DHE_CPP_USE_HOSTED_FILE_SYSTEM=0 \
    -I"$fixture/hosted" -I"$runtime" \
    "$fixture/nohost-file-stream.cpp" \
    "$runtime/system/io/file-stream.cpp" \
    -o "$output/host-nohost-file-stream"
"$output/host-nohost-file-stream"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/smoke.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
    "$dependencies/EASTL/source/hashtable.cpp" -o "$output/custom"
"$output/custom"
result=0
"$output/custom" fail >"$output/fatal.stdout" 2>"$output/fatal.stderr" || result=$?
test "$result" -eq 73
grep -q 'expected runtime failure' "$output/fatal.stderr"
result=0
"$output/custom" fail pointer >"$output/fatal-pointer.stdout" 2>"$output/fatal-pointer.stderr" || result=$?
test "$result" -eq 73
grep -q 'expected runtime failure' "$output/fatal-pointer.stderr"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DEA_COMPILER_HAS_INTTYPES -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/number_math_smoke.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" -o "$output/number-math"
"$output/number-math"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/number_parse_smoke.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
    -o "$output/number-parse"
"$output/number-parse"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/debug_fail.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
    -o "$output/debug-fail"
result=0
"$output/debug-fail" >"$output/debug-fail.stdout" 2>"$output/debug-fail.stderr" || result=$?
test "$result" -eq 73
grep -q 'debug failure' "$output/debug-fail.stderr"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DHE_CPP_USE_STD_CHRONO=0 \
    -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/services.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
    "$dependencies/EASTL/source/hashtable.cpp" -o "$output/custom-services"
"$output/custom-services"

"$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
    -DHE_CPP_USE_HOSTED_FILE_SYSTEM=0 \
    -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
    -DEA_COMPILER_HAS_INTTYPES \
    -I"$fixture/custom" -I"$fixture" -I"$runtime" \
    -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
    "$fixture/streams.cpp" "$fixture/streams-main.cpp" \
    "$runtime/system/io/memory-stream.cpp" \
    "$runtime/system/io/file-stream.cpp" \
    "$runtime/system/io/binary-reader.cpp" \
    "$runtime/system/io/binary-writer.cpp" \
    "$dependencies/EASTL/source/allocator_eastl.cpp" \
    "$dependencies/EASTL/source/hashtable.cpp" -o "$output/custom-streams"
"$output/custom-streams"

# Each storage flag is independent: exercise the six mixed selections too.
for selection in 001 010 011 100 101 110; do
    std_string=$(printf '%s' "$selection" | cut -c1)
    std_vector=$(printf '%s' "$selection" | cut -c2)
    std_map=$(printf '%s' "$selection" | cut -c3)
    "$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
        -DEA_COMPILER_HAS_INTTYPES \
        -DHE_CPP_USE_STD_STRING="$std_string" -DHE_CPP_USE_STD_VECTOR="$std_vector" \
        -DHE_CPP_USE_STD_UNORDERED_MAP="$std_map" \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        "$fixture/smoke.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
        "$dependencies/EASTL/source/hashtable.cpp" -o "$output/mixed-$selection"
    "$output/mixed-$selection"
done

if [ -n "${TARGET_CXX:-}" ]; then
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/smoke.cpp" -o "$output/target.o"
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/shared_ptr_smoke.cpp" -o "$output/shared-ptr-target.o"
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DHE_CPP_USE_STD_CHRONO=0 \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/services.cpp" -o "$output/services-target.o"
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DHE_CPP_USE_HOSTED_FILE_SYSTEM=0 \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/streams.cpp" -o "$output/streams-target.o"
    for stream_source in memory-stream file-stream binary-reader binary-writer; do
        "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
            -DHE_CPP_USE_HOSTED_FILE_SYSTEM=0 \
            -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
            -I"$fixture/custom" -I"$fixture" -I"$runtime" \
            -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
            -c "$runtime/system/io/$stream_source.cpp" -o "$output/$stream_source-target.o"
    done
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/number_parse_smoke.cpp" -o "$output/number-parse-target.o"
    "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/custom" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        -c "$fixture/debug_fail.cpp" -o "$output/debug-fail-target.o"
fi
if [ -n "${GENERATED_OUTPUT:-}" ]; then
    "$cxx" -std=c++20 -fno-exceptions -fno-rtti -DHE_CPP_TEST_HOST \
        -DEA_COMPILER_HAS_INTTYPES -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture" -I"$GENERATED_OUTPUT" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        "$fixture/generated-smoke.cpp" "$GENERATED_OUTPUT/StringGate.cpp" \
        "$dependencies/EASTL/source/allocator_eastl.cpp" \
        "$dependencies/EASTL/source/hashtable.cpp" -o "$output/generated"
    "$output/generated"
    for failure in fail null; do
        result=0
        "$output/generated" "$failure" || result=$?
        test "$result" -eq 73
    done
    if [ -n "${TARGET_CXX:-}" ]; then
        "$TARGET_CXX" -std=c++20 -ffreestanding -fno-exceptions -fno-rtti \
            -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
            -I"$fixture" -I"$GENERATED_OUTPUT" \
            -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
            -c "$GENERATED_OUTPUT/StringGate.cpp" -o "$output/generated-target.o"
    fi
fi
for math_mode in hosted custom; do
    "$cxx" -std=c++20 -DHE_CPP_TEST_HOST -DEA_COMPILER_HAS_INTTYPES \
        -DEASTL_EXCEPTIONS_ENABLED=0 -DEASTL_ASSERT_ENABLED=0 \
        -I"$fixture/$math_mode" -I"$fixture" -I"$runtime" \
        -I"$dependencies/EASTL/include" -I"$dependencies/EABase/include/Common" \
        "$fixture/math_extensions.cpp" "$dependencies/EASTL/source/allocator_eastl.cpp" \
        -o "$output/math-extensions-$math_mode"
    "$output/math-extensions-$math_mode"
    result=0
    "$output/math-extensions-$math_mode" nan || result=$?
    test "$result" -eq 73
done
"$cxx" -std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST \
    -I"$fixture/hosted" -I"$runtime" \
    "$fixture/algorithm_smoke.cpp" -o "$output/algorithm-smoke"
"$output/algorithm-smoke"

freestanding_flags="-std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST"
freestanding_includes="-I$fixture/poison -I$fixture/freestanding -I$fixture -I$runtime"

if "$cxx" $freestanding_flags $freestanding_includes \
    -c "$fixture/hosted_services_guard.cpp" -o "$output/hosted-services-guard.o" \
    >"$output/hosted-services-guard.log" 2>&1; then
    echo 'Hosted threading service unexpectedly compiled under the freestanding runtime.' >&2
    exit 1
fi
grep -q 'hosted threading or OS facilities' "$output/hosted-services-guard.log"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/algorithm_smoke.cpp" -o "$output/freestanding-algorithm-smoke"
"$output/freestanding-algorithm-smoke"
echo 'Runtime capability fixtures passed.'

