using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers runtime-backed managed contract mappings required for native compile validation.
    /// </summary>
    public class CPPManagedRuntimeContractAuditTests {
        /// <summary>
        /// Ensures IDisposable resolves to a runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDisposableBase_UsesRuntimeContractHeader() {
            string source = """
                using System;

                public class Entity : IDisposable {
                    public void Dispose() {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string entityHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));

            Assert.Contains("#include \"runtime/native_disposable.hpp\"", entityHeader);
            Assert.DoesNotContain("#include \"IDisposable.hpp\"", entityHeader, StringComparison.Ordinal);
            Assert.Contains("class Entity : public IDisposable", entityHeader);
            AssertRuntimeRequirement(output.Report, "NativeDisposable");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_disposable.hpp")));
        }

        /// <summary>
        /// Ensures IEquatable resolves to a runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEquatableBase_UsesRuntimeContractHeader() {
            string source = """
                using System;

                public class Vector3 : IEquatable<Vector3> {
                    public bool Equals(Vector3 other) {
                        return true;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string vectorHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Vector3.hpp"));

            Assert.Contains("#include \"runtime/native_equatable.hpp\"", vectorHeader);
            Assert.DoesNotContain("#include \"IEquatable.hpp\"", vectorHeader, StringComparison.Ordinal);
            Assert.Contains("class Vector3 : public IEquatable", vectorHeader);
            AssertRuntimeRequirement(output.Report, "NativeEquatable");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_equatable.hpp")));
        }

        /// <summary>
        /// Ensures managed enum metadata edges resolve to the native enum runtime header instead of a synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEnumBaseReference_UsesRuntimeEnumHeader() {
            string source = """
                using System;

                public enum Keys {
                    None = 0
                }

                public class InputState {
                    public Enum LastType { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "InputState.hpp"));

            Assert.Contains("#include \"runtime/native_enum.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"Enum.hpp\"", headerOutput, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "NativeEnum");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_enum.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.Stream resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoStreamParameter_UsesRuntimeStreamHeader() {
            string source = """
                using System.IO;

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "IContentProcessor_1.hpp"));

            Assert.Contains("#include \"system/io/stream.hpp\"", header);
            Assert.DoesNotContain("#include \"Stream.hpp\"", header, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "Stream");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "stream.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.FileStream resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoFileStreamReturnType_UsesRuntimeFileStreamHeader() {
            string source = """
                using System.IO;

                public class ContentManager {
                    public FileStream Open() {
                        return null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));

            Assert.Contains("#include \"system/io/file-stream.hpp\"", header);
            Assert.DoesNotContain("#include \"FileStream.hpp\"", header, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "FileStream");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "file-stream.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.File resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoFileUsage_UsesRuntimeFileHeader() {
            string source = """
                using System.IO;

                public class ContentManager {
                    public FileStream Open(string fullPath) {
                        return File.OpenRead(fullPath);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));

            Assert.DoesNotContain("#include \"File.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/io/file.hpp\"", sourceOutput);
            Assert.Contains("File::OpenRead(fullPath)", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "File");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "file.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.Path resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoPathUsage_UsesRuntimePathHeader() {
            string source = """
                using System.IO;

                public class ContentManager {
                    public string Normalize(string rootDirectory, string assetPath) {
                        if (Path.IsPathRooted(assetPath)) {
                            return Path.GetFullPath(assetPath);
                        }

                        return Path.Combine(Path.GetFullPath(rootDirectory), Path.GetFileName(assetPath));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));

            Assert.DoesNotContain("#include \"Path.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/io/path.hpp\"", sourceOutput);
            Assert.Contains("Path::IsPathRooted(assetPath)", output.GeneratedText);
            Assert.Contains("Path::GetFullPath(assetPath)", output.GeneratedText);
            Assert.Contains("Path::Combine(Path::GetFullPath(rootDirectory), Path::GetFileName(assetPath))", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "Path");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "path.hpp")));
        }

        /// <summary>
        /// Ensures System.Math and MidpointRounding resolve to the built-in runtime math header instead of missing generated headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemMathUsage_UsesRuntimeMathHeader() {
            string source = """
                using System;

                public class LayoutMath {
                    public int Snap(double value) {
                        int size = Math.Max(1, (int)Math.Ceiling(value));
                        return (int)Math.Round(size / 2.0, MidpointRounding.AwayFromZero);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.cpp"));

            Assert.DoesNotContain("#include \"Math.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"MidpointRounding.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/math.hpp\"", sourceOutput);
            Assert.Contains("Math::Max(1, static_cast<int32_t>(Math::Ceiling(value)))", output.GeneratedText);
            Assert.Contains("MidpointRounding::AwayFromZero", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "Math");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "math.hpp")));
        }

        /// <summary>
        /// Ensures System.Text.Encoding resolves to the lightweight runtime header instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEncodingUsage_UsesRuntimeEncodingHeader() {
            string source = """
                using System.Text;

                public class TextGate {
                    public Encoding GetUtf8() {
                        return Encoding.UTF8;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "TextGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "TextGate.cpp"));

            Assert.Contains("#include \"system/text/encoding.hpp\"", header);
            Assert.DoesNotContain("#include \"Encoding.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("return Encoding::UTF8;", sourceOutput);
            AssertRuntimeRequirement(output.Report, "Encoding");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "text", "encoding.hpp")));
        }

        /// <summary>
        /// Ensures System.Buffers.Binary.BinaryPrimitives resolves to the lightweight runtime header instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBinaryPrimitivesUsage_UsesRuntimeBinaryPrimitivesHeader() {
            string source = """
                using System.Buffers.Binary;

                public class BinaryGate {
                    public ushort Read(byte[] buffer) {
                        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "BinaryGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BinaryGate.cpp"));

            Assert.DoesNotContain("#include \"BinaryPrimitives.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/binary_primitives.hpp\"", sourceOutput);
            Assert.Contains("BinaryPrimitives::ReadUInt16LittleEndian", sourceOutput);
            AssertRuntimeRequirement(output.Report, "BinaryPrimitives");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "binary_primitives.hpp")));
        }

        /// <summary>
        /// Ensures managed argument and operation exceptions resolve through the lightweight runtime exception header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedExceptions_UsesRuntimeExceptionHeader() {
            string source = """
                using System;

                public class GuardGate {
                    public void Validate(object value) {
                        if (value == null) {
                            throw new ArgumentNullException(nameof(value));
                        }

                        throw new InvalidOperationException("bad");
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "GuardGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GuardGate.cpp"));

            Assert.Contains("#include \"runtime/native_exceptions.hpp\"", header);
            Assert.Contains("throw new ArgumentNullException(\"value\");", sourceOutput);
            Assert.Contains("throw new InvalidOperationException(\"bad\");", sourceOutput);
            AssertRuntimeRequirement(output.Report, "NativeExceptions");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_exceptions.hpp")));
        }

        /// <summary>
        /// Ensures MemoryStream resolves to the runtime surface that supports byte-array constructors and ToArray output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemoryStreamArrayCtorAndToArray_UsesRuntimeMemoryStreamSurface() {
            string source = """
                using System.IO;

                public static class MemoryGate {
                    public static byte[] Roundtrip(byte[] data) {
                        using var readStream = new MemoryStream(data, false);
                        using var writeStream = new MemoryStream();
                        return writeStream.ToArray();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "MemoryGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MemoryGate.cpp"));

            Assert.Contains("#include \"system/io/memory-stream.hpp\"", header);
            Assert.Contains("new MemoryStream(data, false)", sourceOutput);
            Assert.Contains("writeStream->ToArray()", sourceOutput);
            AssertRuntimeRequirement(output.Report, "MemoryStream");
        }

        /// <summary>
        /// Ensures System.MathF resolves through the shared runtime math header instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemMathFUsage_UsesRuntimeMathHeader() {
            string source = """
                using System;

                public class LayoutMath {
                    public float Snap(float width, float height) {
                        return MathF.Round(MathF.Min(width, height) * 0.15f);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.cpp"));

            Assert.DoesNotContain("#include \"MathF.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/math.hpp\"", sourceOutput);
            Assert.Contains("MathF::Round(MathF::Min(width, height) * 0.15f)", sourceOutput);
            AssertRuntimeRequirement(output.Report, "Math");
        }

        /// <summary>
        /// Ensures System.IO.MemoryStream resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoMemoryStreamLocal_UsesRuntimeMemoryStreamHeader() {
            string source = """
                using System.IO;

                public static class AssetSerializer {
                    public static byte[] SerializeToBytes() {
                        using var stream = new MemoryStream();
                        return stream.ToArray();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "AssetSerializer.hpp"));

            Assert.Contains("#include \"system/io/memory-stream.hpp\"", header);
            Assert.DoesNotContain("#include \"MemoryStream.hpp\"", header, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "MemoryStream");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "memory-stream.hpp")));
        }

        /// <summary>
        /// Ensures DateTime-backed signatures and expressions lower through the lightweight runtime time contract instead of synthetic generated headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemDateTimeUsage_UsesRuntimeDateTimeHeader() {
            string source = """
                using System;

                public class CursorBlinker {
                    DateTime lastCursorBlink = DateTime.Now;

                    public DateTime GetStamp() {
                        return DateTime.UtcNow;
                    }

                    public bool ShouldBlink() {
                        return (DateTime.Now - lastCursorBlink).TotalMilliseconds > 500;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "CursorBlinker.hpp"));

            Assert.Contains("#include \"runtime/native_datetime.hpp\"", header);
            Assert.DoesNotContain("#include \"DateTime.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("DateTime lastCursorBlink", output.GeneratedText);
            Assert.Contains("DateTime GetStamp()", output.GeneratedText);
            Assert.Contains("DateTime::Now()", output.GeneratedText);
            Assert.Contains("DateTime::UtcNow()", output.GeneratedText);
            Assert.Contains(".TotalMilliseconds > 500", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeDateTime");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_datetime.hpp")));
        }

        /// <summary>
        /// Ensures System.Text.StringBuilder resolves to a lightweight runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemTextStringBuilderUsage_UsesRuntimeStringBuilderHeader() {
            string source = """
                using System.Text;

                public class TextComposer {
                    public string BuildLabel(string name, int count) {
                        StringBuilder builder = new StringBuilder(256);
                        builder.Append('[');
                        builder.Append(name);
                        builder.Append(']');
                        builder.Append(' ');
                        builder.Append(count);
                        return builder.ToString();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "TextComposer.hpp"));

            Assert.Contains("#include \"system/text/string-builder.hpp\"", header);
            Assert.DoesNotContain("#include \"StringBuilder.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("StringBuilder *builder = new StringBuilder(256);", output.GeneratedText);
            Assert.Contains("builder->Append('[')", output.GeneratedText);
            Assert.Contains("builder->Append(count)", output.GeneratedText);
            Assert.Contains("builder->ToString()", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "StringBuilder");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "text", "string-builder.hpp")));
        }

        /// <summary>
        /// Ensures System.Collections.Generic.Stack resolves to the lightweight runtime stack header instead of a missing generated header.
        /// </summary>

        /// <summary>
        /// Ensures StringBuilder string constructors still resolve to the runtime header and preserve pointer-backed invocation semantics.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemTextStringBuilderStringConstructor_UsesRuntimeStringBuilderHeader() {
            string source = """
                using System.Text;

                public static class TextComposer {
                    public static string Compose(string value) {
                        StringBuilder builder = new StringBuilder(value);
                        builder.Append('!');
                        return builder.ToString();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "TextComposer.hpp"));

            Assert.Contains("#include \"system/text/string-builder.hpp\"", header);
            Assert.DoesNotContain("#include \"StringBuilder.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("StringBuilder *builder = new StringBuilder(value);", output.GeneratedText);
            Assert.Contains("builder->Append('!')", output.GeneratedText);
            Assert.Contains("builder->ToString()", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "StringBuilder");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "text", "string-builder.hpp")));
        }

        /// <summary>
        /// Ensures Guid.NewGuid resolves to the runtime Guid header in the generated source and preserves value-style method chaining.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemGuidNewGuidUsage_UsesRuntimeGuidHeader() {
            string source = """
                using System;

                public static class GuidComposer {
                    public static string Create() {
                        return Guid.NewGuid().ToString("N");
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "GuidComposer.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GuidComposer.cpp"));

            Assert.DoesNotContain("#include \"Guid.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/guid.hpp\"", sourceOutput);
            Assert.Contains("Guid::NewGuid()", output.GeneratedText);
            Assert.Contains(".ToString(\"N\")", output.GeneratedText);
            Assert.DoesNotContain("->ToString(\"N\")", output.GeneratedText, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "Guid");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "guid.hpp")));
        }
        [Fact]
        public void WriteOutput_WithGenericStackUsage_UsesRuntimeStackHeader() {
            string source = """
                using System.Collections.Generic;

                public class ShaderConditionalFrame {
                    public bool CurrentIncluded;
                }

                public class StackGate {
                    public bool ReadCurrent() {
                        Stack<ShaderConditionalFrame> frames = new Stack<ShaderConditionalFrame>();
                        frames.Push(new ShaderConditionalFrame());
                        return frames.Peek().CurrentIncluded;
                    }

                    public int GetCount() {
                        Stack<ShaderConditionalFrame> frames = new Stack<ShaderConditionalFrame>();
                        return frames.Count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "StackGate.hpp"));

            Assert.Contains("#include \"runtime/native_stack.hpp\"", header);
            Assert.DoesNotContain("#include \"Stack.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("new Stack<", output.GeneratedText);
            Assert.Contains("frames->Push(", output.GeneratedText);
            Assert.Contains("frames->Peek()", output.GeneratedText);
            Assert.Contains("return frames->Count;", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeStack");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_stack.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.StringReader resolves to the lightweight runtime header and using-block disposal emits valid C++ calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoStringReaderUsage_UsesRuntimeStringReaderHeader() {
            string source = """
                using System.IO;

                public static class ShaderSourceReader {
                    public static void ReadLines(string source) {
                        using (StringReader reader = new StringReader(source)) {
                            string line = reader.ReadLine();
                            while (line != null) {
                                line = reader.ReadLine();
                            }
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ShaderSourceReader.hpp"));

            Assert.Contains("#include \"system/io/string-reader.hpp\"", header);
            Assert.DoesNotContain("#include \"StringReader.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("StringReader *reader = new StringReader(source);", output.GeneratedText);
            Assert.Contains("StringReaderLine line = reader->ReadLine();", output.GeneratedText);
            Assert.Contains("while (!String::IsNullOrEmpty(line))", output.GeneratedText);
            Assert.Contains("reader->Dispose();", output.GeneratedText);
            Assert.DoesNotContain(".dispose()", output.GeneratedText, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "StringReader");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "string-reader.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.StreamReader resolves to the lightweight runtime header, preserves direct-value lifetime semantics, and normalizes Encoding.UTF8 static access for text reads.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoStreamReaderUsage_UsesRuntimeStreamReaderHeader() {
            string source = """
                using System.IO;
                using System.Text;

                public static class TextGate {
                    public static string ReadAll(Stream stream) {
                        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
                        return reader.ReadToEnd();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "TextGate.hpp"));

            Assert.Contains("#include \"system/io/stream-reader.hpp\"", header);
            Assert.DoesNotContain("#include \"StreamReader.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("StreamReader *reader = new StreamReader(stream, Encoding::UTF8, true, 1024, true);", output.GeneratedText);
            Assert.Contains("return reader->ReadToEnd();", output.GeneratedText);
            Assert.DoesNotContain("reader->Dispose();", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("System->Text->Encoding->UTF8", output.GeneratedText, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "StreamReader");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "stream-reader.hpp")));
        }

        /// <summary>
        /// Ensures System.Text.RegularExpressions usage resolves to the lightweight runtime regex header instead of synthetic generated headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRegexMatchesUsage_UsesRuntimeRegexHeader() {
            string source = """
                using System.Text.RegularExpressions;

                public static class RegexGate {
                    static readonly Regex Pattern = new Regex(@"(?<value>\w+)", RegexOptions.Compiled | RegexOptions.Multiline);

                    public static int Count(string source) {
                        MatchCollection matches = Pattern.Matches(source);
                        int total = 0;

                        for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++) {
                            Match match = matches[matchIndex];
                            if (!match.Success) {
                                continue;
                            }

                            string value = match.Groups["value"].Value;
                            total += value.Length;
                        }

                        return total;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "RegexGate.hpp"));

            Assert.Contains("#include \"system/text/regular_expressions/regex.hpp\"", header);
            Assert.DoesNotContain("#include \"Regex.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Match.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"MatchCollection.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("static Regex Pattern;", output.GeneratedText);
            Assert.Contains("MatchCollection matches = Pattern.Matches(source);", output.GeneratedText);
            Assert.Contains("Match match = matches[matchIndex];", output.GeneratedText);
            Assert.Contains("match.Groups[\"value\"].Value", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "Regex");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "text", "regular_expressions", "regex.hpp")));
        }

        /// <summary>
        /// Ensures System.Type resolves to a lightweight runtime token header and typeof expressions lower through the native helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemTypeContract_UsesRuntimeTypeTokenHeader() {
            string source = """
                using System;

                public interface IContentProcessor {
                    Type OutputType { get; }
                }

                public class ContentManager {
                    public Type Resolve() {
                        return typeof(ContentManager);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string contentManagerHeader = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));

            Assert.Contains("#include \"runtime/native_type.hpp\"", contentManagerHeader);
            Assert.DoesNotContain("#include \"Type.hpp\"", contentManagerHeader, StringComparison.Ordinal);
            Assert.Contains("Type* Resolve()", output.GeneratedText);
            Assert.Contains("he_cpp_type_of<ContentManager>(\"ContentManager\")", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeType");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_type.hpp")));
        }

        /// <summary>
        /// Ensures C# event declarations resolve to the lightweight runtime event contract instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithActionEvent_UsesRuntimeEventHeader() {
            string source = """
                using System;

                public interface IInteractable2D {
                    event Action<int, int, int> CursorEvent;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "IInteractable2D.hpp"));

            Assert.Contains("#include \"runtime/native_event.hpp\"", header);
            Assert.DoesNotContain("#include \"Event.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("::Event CursorEvent", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeEvent");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_event.hpp")));
        }

        /// <summary>
        /// Ensures explicit Nullable&lt;T&gt; usage lowers through the lightweight runtime contract instead of leaking a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitNullableValueType_UsesRuntimeNullableHeader() {
            string source = """
                using System;

                public class AnchorData {
                    public Nullable<float> LeftDistance { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "AnchorData.hpp"));

            Assert.Contains("#include \"runtime/native_nullable.hpp\"", header);
            Assert.DoesNotContain("#include \"Nullable.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Nullable<float> LeftDistance", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeNullable");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_nullable.hpp")));
        }

        /// <summary>
        /// Ensures shorthand nullable value syntax does not leak a synthetic primitive nullable header include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullableValueTypeShorthand_DoesNotEmitPrimitiveNullableHeader() {
            string source = """
                public class AnchorData {
                    public float? LeftDistance { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "AnchorData.hpp"));

            Assert.Contains("#include \"runtime/native_nullable.hpp\"", header);
            Assert.DoesNotContain("#include \"float?.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Nullable<float> LeftDistance", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeNullable");
        }

        /// <summary>
        /// Ensures Action delegates resolve to the native system helper header instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemActionCallback_UsesNativeSystemActionHeader() {
            string source = """
                using System;

                public class EngineBinaryWriter {
                    public void WriteArray<T>(T[] values, Action<EngineBinaryWriter, T> writeElement) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "EngineBinaryWriter.hpp"));

            Assert.Contains("#include \"system/action.hpp\"", header);
            Assert.DoesNotContain("#include \"Action.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Action<::EngineBinaryWriter*, T>* writeElement", output.GeneratedText);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "action.hpp")));
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "action.tpp")));
        }

        /// <summary>
        /// Ensures Span-backed buffer parameters resolve to the lightweight runtime span contract instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemSpanBufferParameter_UsesRuntimeSpanHeader() {
            string source = """
                using System;

                public class EngineBinaryReader {
                    protected void ReadRequiredBytes(Span<byte> buffer) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "EngineBinaryReader.hpp"));

            Assert.Contains("#include \"runtime/native_span.hpp\"", header);
            Assert.DoesNotContain("#include \"Span.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Span<uint8_t> buffer", output.GeneratedText);
            Assert.DoesNotContain("Span<uint8_t>* buffer", output.GeneratedText, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "NativeSpan");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_span.hpp")));
        }

        /// <summary>
        /// Ensures Func delegates resolve to the native system helper header instead of a synthetic generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemFuncCallback_UsesNativeSystemFuncHeader() {
            string source = """
                using System;
                using System.IO;

                public class BinaryContentProcessor<T> {
                    readonly Func<Stream, T> Reader;

                    public BinaryContentProcessor(Func<Stream, T> reader) {
                        Reader = reader;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "BinaryContentProcessor_1.hpp"));

            Assert.Contains("#include \"system/func.hpp\"", header);
            Assert.DoesNotContain("#include \"Func.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Func<Stream*, T>* reader", output.GeneratedText);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "func.hpp")));
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-contract-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;
            options.BuildFeatureProfile
                .WithMode(CPPFeatureKind.HostFileSystem, CPPFeatureMode.Enabled)
                .WithMode(CPPFeatureKind.ReflectionLikeRuntime, CPPFeatureMode.Enabled)
                .WithMode(CPPFeatureKind.TextProcessing, CPPFeatureMode.Enabled);

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            string generatedText = ReadGeneratedOutput(outputPath);
            return new ConversionOutput(outputPath, generatedText, report);
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for temporary converter fixtures.
        /// </summary>
        /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
        static string CreateProjectFile() {
            return """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Reads all generated headers and sources from a converter output directory into a single string for assertions.
        /// </summary>
        /// <param name="outputPath">Converter output directory to inspect.</param>
        /// <returns>Concatenated generated text content.</returns>
        static string ReadGeneratedOutput(string outputPath) {
            string[] files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        /// <summary>
        /// Ensures the conversion report registered the expected runtime requirement.
        /// </summary>
        /// <param name="report">Parsed conversion report.</param>
        /// <param name="requirementName">Stable runtime requirement name that must be present.</param>
        static void AssertRuntimeRequirement(JsonDocument report, string requirementName) {
            foreach (JsonElement requirement in report.RootElement.GetProperty("registeredRuntimeRequirements").EnumerateArray()) {
                if (string.Equals(requirement.GetString(), requirementName, StringComparison.Ordinal)) {
                    return;
                }
            }

            Assert.Fail($"Expected runtime requirement '{requirementName}' to be registered.");
        }

        /// <summary>
        /// Represents the generated output artifacts captured for a managed runtime contract fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}

