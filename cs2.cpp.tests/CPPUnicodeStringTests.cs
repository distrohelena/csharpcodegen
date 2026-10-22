using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Verifies that generated managed characters and string operations preserve UTF-16 code units.</summary>
public sealed class CPPUnicodeStringTests {
    /// <summary>Generates Unicode character, atlas-key and string operations without narrowing them to UTF-8 bytes.</summary>
    [Fact]
    public void WriteOutput_UnicodeCharactersAndStrings_PreserveManagedCodeUnits() {
        string fixture = Path.Combine(Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_TEMP_ROOT") ?? Path.GetTempPath(), "unicode-codegen");
        string output = Path.Combine(fixture, "generated");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            using System.Collections.Generic;
            using System.Text;
            /// <summary>Exercises managed Unicode semantics through the production C++ conversion pipeline.</summary>
            public class UnicodeFixture {
                /// <summary>Contains accented, East Asian and supplementary characters in one managed constant.</summary>
                public const string Title = "Ação — 漢字 😀";
                /// <summary>Preserves the sixteen-bit atlas key read from an unsigned binary value.</summary>
                public static char AtlasKey(ushort value) { return (char)value; }
                /// <summary>Returns the managed UTF-16 length of a runtime string.</summary>
                public static int Length(string value) { return value.Length; }
                /// <summary>Reads a single managed UTF-16 code unit at a supplied index.</summary>
                public static char At(string value, int index) { return value[index]; }
                /// <summary>Visits every managed code unit through string enumeration.</summary>
                public static int Sum(string value) { int sum = 0; foreach (char character in value) sum += character; return sum; }
                /// <summary>Slices strings at managed UTF-16 code unit boundaries.</summary>
                public static string Slice(string value, int index, int length) { return value.Substring(index, length); }
                /// <summary>Converts one managed code unit to a string without narrowing.</summary>
                public static string Character(char value) { return value.ToString(); }
                /// <summary>Constructs text from independently supplied managed characters.</summary>
                public static string Construct(char[] value) { return new string(value); }
                /// <summary>Repeats a managed code unit with the managed character-first constructor contract.</summary>
                public static string Repeat(char value, int count) { return new string(value, count); }
                /// <summary>Constructs text from a selected range of managed UTF-16 units.</summary>
                public static string ConstructSlice(char[] value, int start, int length) { return new string(value, start, length); }
                /// <summary>Constructs a character range when source arguments name parameters in a different order.</summary>
                public static string ConstructSliceNamed(char[] value, int start, int length) { return new string(length: length, value: value, startIndex: start); }
                /// <summary>Records constructor argument evaluation order across named and positional calls.</summary>
                public static int Trace;
                /// <summary>Produces a character while recording its evaluation as the first argument kind.</summary>
                static char MarkChar() { Trace = Trace * 10 + 1; return (char)0x0100; }
                /// <summary>Produces a count while recording its evaluation as the second argument kind.</summary>
                static int MarkCount() { Trace = Trace * 10 + 2; return 3; }
                /// <summary>Checks single evaluation and left-to-right source ordering for both constructor call forms.</summary>
                public static bool RepeatOrder() { Trace = 0; string value = new string(MarkChar(), MarkCount()); if (Trace != 12 || value.Length != 3) return false; Trace = 0; value = new string(count: MarkCount(), c: MarkChar()); return Trace == 21 && value.Length == 3; }
                /// <summary>Preserves a surrogate character literal without requiring a Unicode scalar value.</summary>
                public static char Surrogate() { return '\uD800'; }
                /// <summary>Copies an owned inline character array without retaining its native allocation.</summary>
                public static string InlineArray() { return new string(new[] { (char)0x0100, (char)0x6F22 }); }
                /// <summary>Copies an owned inline character range and must release its source when validation throws.</summary>
                public static string InlineSlice(int start, int length) { return new string(new[] { (char)0x0100, (char)0x6F22 }, start, length); }
                /// <summary>Replaces an accented character with a character outside Latin-1.</summary>
                public static string Replace(string value) { return value.Replace('ã', '漢'); }
                /// <summary>Combines Unicode text and an interpolated managed character.</summary>
                public static string Interpolate(char value) { return $"Ação:{value}"; }
                /// <summary>Preserves Unicode optional string and character defaults.</summary>
                public static string Optional(string value = "Ação", char separator = '漢') { return value + separator.ToString(); }
                /// <summary>Exercises generated optional argument expansion.</summary>
                public static string CallOptional() { return Optional(); }
                /// <summary>Returns an owned copy of managed character units.</summary>
                public static char[] Characters(string value) { return value.ToCharArray(); }
                /// <summary>Preserves an embedded null and an isolated surrogate in a literal.</summary>
                public static string Embedded() { return "A\0F\uD800"; }
                /// <summary>Combines characters and supplementary text in a string builder.</summary>
                public static string Build() { var text = new StringBuilder(); text.Append('ã'); text.Append('漢'); text.Append("😀"); return text.ToString(); }
                /// <summary>Searches for an East Asian character using managed indices.</summary>
                public static int Find(string value) { return value.IndexOf('漢'); }
                /// <summary>Splits on a sixteen-bit character and preserves empty entries.</summary>
                public static int Split(string value) { return value.Split('漢').Length; }

                /// <summary>Compares a fixed native UTF-8 byte sequence with the matching managed literal.</summary>
                public static bool Decode() { byte[] bytes = new byte[] { 0x41, 0xC3, 0xA7, 0xC3, 0xA3, 0x6F, 0x20, 0xE2, 0x80, 0x94, 0x20, 0xE6, 0xBC, 0xA2, 0xE5, 0xAD, 0x97, 0x20, 0xF0, 0x9F, 0x98, 0x80 }; return Encoding.UTF8.GetString(bytes) == Title; }
                /// <summary>Preserves surrogate halves and managed indexing in the builder.</summary>
                public static bool BuilderUnits() { var text = new StringBuilder(); text.Append((char)0xD83D); text.Append((char)0xDE00); return text.Length == 2 && text[0] == (char)0xD83D && text[1] == (char)0xDE00; }
                /// <summary>Keeps dictionary character keys distinct from their truncated byte values.</summary>
                public static bool Atlas() { var atlas = new Dictionary<char, int>(); atlas.Add((char)0x6F22, 7); return atlas[(char)0x6F22] == 7 && !atlas.ContainsKey((char)0x22); }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.RuntimeProfile.UseExceptions = true;
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        converter.WriteOutput(output);
        Assert.False(converter.Report.HasErrors);
        string header = File.ReadAllText(Path.Combine(output, "UnicodeFixture.hpp"));
        string implementation = File.ReadAllText(Path.Combine(output, "UnicodeFixture.cpp"));
        Assert.Contains("char16_t AtlasKey(uint16_t value)", header);
        Assert.Contains("he_cpp_string_length(", implementation);
        Assert.Contains("he_cpp_string_char_at(", implementation);
        Assert.Contains("he_cpp_string_chars(", implementation);
        Assert.Contains("he_cpp_string_from_utf16(", implementation);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output).Run("""
            static_assert(sizeof(decltype(UnicodeFixture::AtlasKey(0x6F22))) == 2);
            if (UnicodeFixture::AtlasKey(0x6F22) != 0x6F22 || UnicodeFixture::AtlasKey(0x0100) != 0x0100 || !UnicodeFixture::Atlas()) return 1;
            HeCppString title = UnicodeFixture::Title();
            if (UnicodeFixture::Length(title) != 12) return 2;
            if (UnicodeFixture::At(title, 1) != 0xE7 || UnicodeFixture::At(title, 10) != 0xD83D || UnicodeFixture::At(title, 11) != 0xDE00) return 3;
            if (UnicodeFixture::Sum(HeCppString(u"\u00E3\u6F22\U0001F600")) != 0xE3 + 0x6F22 + 0xD83D + 0xDE00) return 4;
            if (UnicodeFixture::Slice(title, 7, 1) != HeCppString(u"\u6F22")) return 5;
            if (UnicodeFixture::Character(0x6F22) != HeCppString(u"\u6F22")) return 6;
            Array<char16_t> characters({u'\u00E3', u'\u6F22', static_cast<char16_t>(0xD83D), static_cast<char16_t>(0xDE00)});
            if (UnicodeFixture::Construct(&characters) != HeCppString(u"\u00E3\u6F22\U0001F600")) return 7;
            if (UnicodeFixture::Replace(HeCppString(u"a\u00E7\u00E3o")) != HeCppString(u"a\u00E7\u6F22o")) return 8;
            if (UnicodeFixture::Interpolate(0x6F22) != HeCppString(u"A\u00E7\u00E3o:\u6F22")) return 9;
            if (UnicodeFixture::Build() != HeCppString(u"\u00E3\u6F22\U0001F600")) return 10;
            if (UnicodeFixture::Find(HeCppString(u"\u00E3\u6F22")) != 1 || UnicodeFixture::Split(HeCppString(u"\u00E3\u6F22\u6F22")) != 3) return 11;

            auto result = UnicodeFixture::Characters(HeCppString(u"\u00E3\u6F22\U0001F600"));
            if (result->Length != 4 || (*result)[1] != 0x6F22 || (*result)[3] != 0xDE00) return 12;
            delete result;
            auto embedded = UnicodeFixture::Embedded();
            if (he_cpp_string_length(embedded) != 4 || he_cpp_string_char_at(embedded, 1) != 0 || he_cpp_string_char_at(embedded, 3) != 0xD800) return 13;
            if (UnicodeFixture::CallOptional() != HeCppString(u"A\u00E7\u00E3o\u6F22")) return 14;
            if (!UnicodeFixture::Decode()) return 15;
            if (!UnicodeFixture::BuilderUnits()) return 16;
            if (UnicodeFixture::Repeat(0x0100, 3) != HeCppString(u"\u0100\u0100\u0100")) return 17;
            if (UnicodeFixture::ConstructSlice(&characters, 1, 3) != HeCppString(u"\u6F22\U0001F600")) return 18;
            if (UnicodeFixture::ConstructSliceNamed(&characters, 1, 3) != HeCppString(u"\u6F22\U0001F600")) return 19;
            if (!UnicodeFixture::RepeatOrder() || UnicodeFixture::Surrogate() != 0xD800) return 20;
            try { UnicodeFixture::Repeat(0x0100, -1); return 21; } catch (const ArgumentOutOfRangeException&) {}
            if (UnicodeFixture::Construct(nullptr) != HeCppString("")) return 22;
            try { UnicodeFixture::ConstructSlice(nullptr, 0, 0); return 23; } catch (const ArgumentNullException&) {}
            const int arraysBeforeInlineConstruction = UnicodeOutstandingArrays;
            { auto copied = UnicodeFixture::InlineArray(); if (copied != HeCppString(u"\u0100\u6F22")) return 24; }
            if (UnicodeOutstandingArrays != arraysBeforeInlineConstruction) return 25;
            { auto copied = UnicodeFixture::InlineSlice(1, 1); if (copied != HeCppString(u"\u6F22")) return 26; }
            if (UnicodeOutstandingArrays != arraysBeforeInlineConstruction) return 27;
            try { UnicodeFixture::InlineSlice(3, 1); return 28; } catch (const ArgumentOutOfRangeException&) {}
            if (UnicodeOutstandingArrays != arraysBeforeInlineConstruction) return 29;
            """, """
            #include <cstdlib>
            #include <new>
            // Count array payload lifetimes independently of the string object's scalar allocations.
            inline int UnicodeOutstandingArrays = 0;
            void* operator new[](std::size_t bytes) {
                void* allocation = std::malloc(bytes == 0 ? 1 : bytes);
                if (allocation == nullptr) throw std::bad_alloc();
                ++UnicodeOutstandingArrays;
                return allocation;
            }
            void operator delete[](void* allocation) noexcept {
                if (allocation != nullptr) { --UnicodeOutstandingArrays; std::free(allocation); }
            }
            void operator delete[](void* allocation, std::size_t) noexcept { ::operator delete[](allocation); }
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Program returned {result.ProgramExitCode}: {result.ProgramOutput}");
    }
}
