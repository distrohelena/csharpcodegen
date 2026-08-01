using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Decides whether one converted managed type owns runtime C++ source rather than authoring-only metadata.
    /// </summary>
    public static class CPPGeneratedTypeEmissionPolicy {
        /// <summary>
        /// Stores exact managed symbol identities that the native runtime consumes only as conversion-time metadata.
        /// </summary>
        static readonly HashSet<string> ExcludedTypeNames = new HashSet<string>(StringComparer.Ordinal) {
            "cs2.attributes.CodeGenRenameAttribute",
            "NativeBorrowedReturnAttribute",
            "NativeFreeFunctionAttribute",
            "NativeNoEscapeAttribute",
            "NativeOwnedMemberAttribute",
            "NativeOwnedReturnAttribute",
            "NativeRetainsBorrowAttribute",
            "NativeTakesOwnershipAttribute",
            "cs2.attributes.NativeBorrowedReturnAttribute",
            "cs2.attributes.NativeOwnedMemberAttribute",
            "cs2.attributes.NativeOwnedReturnAttribute",
            "cs2.attributes.NativeRetainsBorrowAttribute",
            "cs2.attributes.NativeTakesOwnershipAttribute",
            "helengine.EditorPropertyDisplayNameAttribute",
            "helengine.EditorPropertyHiddenAttribute",
            "helengine.EditorPropertyOrderAttribute",
            "helengine.NativeFreeFunctionAttribute",
            "helengine.NativeMigrationRequiredAttribute",
            "helengine.NativeNoEscapeAttribute",
            "helengine.NativeBorrowedReturnAttribute",
            "helengine.NativeOwnedMemberAttribute",
            "helengine.NativeOwnedReturnAttribute",
            "helengine.NativeRetainsBorrowAttribute",
            "helengine.NativeTakesOwnershipAttribute",
            "helengine.ScenePersistenceAppendAttribute",
            "helengine.ScenePersistenceIgnoreAttribute"
        };

        /// <summary>
        /// Returns whether the supplied converted type should emit standalone runtime-native source files.
        /// </summary>
        /// <param name="conversionClass">Converted type whose managed symbol identity should be evaluated.</param>
        /// <returns><c>true</c> for runtime types and unrelated same-leaf user types; otherwise, <c>false</c>.</returns>
        public static bool ShouldEmit(ConversionClass conversionClass) {
            if (conversionClass == null) {
                return false;
            }

            if (conversionClass.TypeSymbol == null) {
                return true;
            }

            string managedTypeName = conversionClass.TypeSymbol.ToDisplayString();
            return !ExcludedTypeNames.Contains(managedTypeName);
        }
    }
}
