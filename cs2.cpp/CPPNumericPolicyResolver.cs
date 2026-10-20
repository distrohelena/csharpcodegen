using System.Globalization;

namespace cs2.cpp {
    /// <summary>Normalizes build-specific numeric options without changing ordinary C# primitive mappings.</summary>
    public static class CPPNumericPolicyResolver {
        /// <summary>Resolves and validates the explicit numeric evaluator implemented by native generation.</summary>
        /// <param name="options">Conversion-local options and their selected generic values.</param>
        /// <returns>The immutable native numeric contract.</returns>
        public static CPPRealNumericPolicy Resolve(CPPConversionOptions options) {
            if (options == null) { throw new ArgumentNullException(nameof(options)); }
            else if (options.RealNumericPolicy == null) { throw new ArgumentException("A numeric policy is required.", nameof(options)); }
            CPPRealNumericPolicy policy = new(
                ReadWidth(options.PlatformOptionValues, "real-storage-bits", options.RealNumericPolicy.StorageBits),
                ReadWidth(options.PlatformOptionValues, "real-arithmetic-bits", options.RealNumericPolicy.ArithmeticBits));
            if (policy.ArithmeticBits == 16 && policy.StorageBits != 16 || policy.StorageBits == 16 && policy.ArithmeticBits == 64) {
                throw new NotSupportedException($"Native numeric policy '{policy.Identity}' has no implemented half-precision backend.");
            }
            options.RealNumericPolicy = policy;
            return policy;
        }

        /// <summary>Reads a width without converting malformed explicit input into a default.</summary>
        /// <param name="values">Selected generic options.</param>
        /// <param name="key">Numeric setting identifier.</param>
        /// <param name="defaultValue">Explicit value used only when the key is absent.</param>
        /// <returns>Validated selected width.</returns>
        static int ReadWidth(IReadOnlyDictionary<string, string> values, string key, int defaultValue) {
            int result = defaultValue;
            bool found = false;
            if (values == null) { return result; }
            foreach (KeyValuePair<string, string> value in values) {
                if (!string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (found) { throw new ArgumentException($"Numeric setting '{key}' is duplicated.", nameof(values)); }
                else if (value.Value == null || !int.TryParse(value.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result)
                    || result != 16 && result != 32 && result != 64) {
                    throw new ArgumentException($"Numeric setting '{key}' must be 16, 32 or 64.", nameof(values));
                }
                found = true;
            }
            return result;
        }
    }
}
