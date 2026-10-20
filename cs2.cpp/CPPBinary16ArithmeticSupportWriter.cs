namespace cs2.cpp {
    /// <summary>Emits portable binary16 evaluation operators with an explicit rounding boundary after each primitive.</summary>
    public static class CPPBinary16ArithmeticSupportWriter {
        /// <summary>Writes the evaluator only for conversions whose arithmetic policy explicitly selects binary16.</summary>
        /// <param name="outputFolder">Conversion-owned native runtime root.</param>
        /// <param name="policy">Immutable precision selected by this conversion.</param>
        /// <returns>Generated support paths to include in the conversion's output inventory.</returns>
        public static IReadOnlyList<string> Write(string outputFolder, CPPRealNumericPolicy policy) {
            if (string.IsNullOrWhiteSpace(outputFolder)) { throw new ArgumentException("An output folder is required.", nameof(outputFolder)); }
            else if (policy == null) { throw new ArgumentNullException(nameof(policy)); }
            else if (policy.ArithmeticBits != 16) { return Array.Empty<string>(); }
            string runtimeFolder = Path.Combine(outputFolder, "runtime");
            Directory.CreateDirectory(runtimeFolder);
            string path = Path.Combine(runtimeFolder, "binary16_arithmetic.hpp");
            File.WriteAllText(path, BuildHeaderText());
            return new[] { path };
        }

        /// <summary>Builds a software evaluator using exact half loads and float32 primitive operations followed by half rounding.</summary>
        static string BuildHeaderText() {
            return """
                #pragma once
                #include "native_binary16.hpp"
                #include <limits>
                /// <summary>Rounds every primitive result before a following binary16 operation can observe it.</summary>
                inline he_binary16 operator+(he_binary16 a, he_binary16 b) { return he_binary16(static_cast<float>(a) + static_cast<float>(b)); }
                inline he_binary16 operator-(he_binary16 a, he_binary16 b) { return he_binary16(static_cast<float>(a) - static_cast<float>(b)); }
                inline he_binary16 operator*(he_binary16 a, he_binary16 b) { return he_binary16(static_cast<float>(a) * static_cast<float>(b)); }
                inline he_binary16 operator/(he_binary16 a, he_binary16 b) { return he_binary16(static_cast<float>(a) / static_cast<float>(b)); }
                inline he_binary16 operator-(he_binary16 value) { return he_binary16::FromBits(static_cast<std::uint16_t>(value.Bits ^ 0x8000u)); }
                /// <summary>Supplies exact format bounds without consulting unsupported compiler half types.</summary>
                namespace std {
                    template <> class numeric_limits<he_binary16> {
                    public:
                        static he_binary16 max() { return he_binary16::FromBits(0x7bffu); }
                        static he_binary16 infinity() { return he_binary16::FromBits(0x7c00u); }
                    };
                }
                """;
        }
    }
}
