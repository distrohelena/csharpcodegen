namespace cs2.cpp {
    /// <summary>
    /// Supplies profiling-dependent replacement text for copied generated C++ runtime templates.
    /// </summary>
    public static class CPPGeneratedRuntimeProfilingTemplateReplacements {
        /// <summary>
        /// Adds runtime template replacements that either emit direct Tracy support or leave the copied runtime uninstrumented.
        /// </summary>
        /// <param name="replacements">Replacement table used while copying the native runtime templates.</param>
        /// <param name="enabled">Whether generated C++ profiling was requested for the conversion.</param>
        public static void Add(IDictionary<string, string> replacements, bool enabled) {
            if (replacements == null) {
                throw new ArgumentNullException(nameof(replacements));
            }

            replacements["GENERATED_FUNCTION_PROFILING_NATIVE_MEMORY_INCLUDE"] = enabled
                ? "#include \"../../../runtime/generated_profiler.hpp\""
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_NATIVE_MEMORY_ALLOCATE"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_ALLOCATE(alignedAllocation, alignedByteCount, \"NativeMemory::AlignedAlloc\");"
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_NATIVE_MEMORY_FREE"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_FREE(value, \"NativeMemory::AlignedFree\");"
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_SPIN_LOCK_INCLUDE"] = enabled
                ? "#include <mutex>\n#include \"../../runtime/generated_profiler.hpp\""
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_SPIN_LOCK_FIELD"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_LOCKABLE(std::mutex, ProfileLock, \"SpinLock\");"
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_SPIN_LOCK_BEFORE_ENTER"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_BEFORE_LOCK(ProfileLock, profileLockQueued);"
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_SPIN_LOCK_AFTER_ENTER"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_AFTER_LOCK(ProfileLock, profileLockQueued);"
                : string.Empty;
            replacements["GENERATED_FUNCTION_PROFILING_SPIN_LOCK_AFTER_EXIT"] = enabled
                ? "HE_CPP_GENERATED_PROFILE_AFTER_UNLOCK(ProfileLock);"
                : string.Empty;
        }
    }
}
