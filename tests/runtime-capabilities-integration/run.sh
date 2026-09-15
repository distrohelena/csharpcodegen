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
    echo 'Unsupported hosted event storage unexpectedly compiled.' >&2
    exit 1
fi
grep -q 'requires HE_CPP_USE_STD_VECTOR=1' "$output/unsupported-helper.log"

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
echo 'Runtime capability fixtures passed.'
