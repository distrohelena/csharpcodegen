namespace cs2.cpp.tests {
    /// <summary>Preserves UTF-16 character initializers through preprocessing and native literal emission.</summary>
    public sealed class CPPCharacterInitializerTests {
        /// <summary>Fields, constants and automatic properties accept the same escaped characters as method bodies.</summary>
        [Fact]
        public void WriteOutput_WithCharacterInitializers_PreservesUtf16Values() {
            string source = """
                public class CharacterInitializer {
                    public const char Placeholder = '\u25A1';
                    public char Tab = '\t';
                    public char Quote { get; set; } = '\'';
                    public char Read() { return Placeholder; }
                }
                """;
            CPPCompileValidationRegressionTests.ConversionOutput output = CPPCompileValidationRegressionTests.RunConversion(source);
            Assert.Contains("u'\\x25a1'", output.GeneratedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("u'\\x0009'", output.GeneratedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("u'\\x0027'", output.GeneratedText, StringComparison.OrdinalIgnoreCase);
        }
    }
}