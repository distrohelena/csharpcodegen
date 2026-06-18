using System.Text.Json;
using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

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
            Assert.Contains("IEquatable", vectorHeader);
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
        /// Ensures System.Buffer resolves to a runtime header that exposes the static MemoryCopy surface used by generated unsafe code.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemBufferMemoryCopyUsage_UsesRuntimeBufferHeader() {
            string source = """
                using System;

                public unsafe class Copier {
                    public void Copy(void* source, void* destination, int byteCount) {
                        Buffer.MemoryCopy(source, destination, byteCount, byteCount);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Copier.cpp"));
            string bufferRuntimePath = Path.Combine(output.OutputPath, "system", "buffer.hpp");
            string bufferRuntime = File.ReadAllText(bufferRuntimePath);

            Assert.Contains("#include \"system/buffer.hpp\"", sourceOutput);
            Assert.DoesNotContain("#include \"Buffer.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Buffer::MemoryCopy(source, destination, byteCount, byteCount)", sourceOutput);
            Assert.Contains("class Buffer", bufferRuntime, StringComparison.Ordinal);
            Assert.Contains("static void MemoryCopy", bufferRuntime, StringComparison.Ordinal);
            Assert.True(File.Exists(bufferRuntimePath));
        }

        /// <summary>
        /// Ensures the portable intrinsics runtime exposes the compare and mask helpers required by generated BEPU bundle indexing code.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEmptyClass_CopiesPortableIntrinsicsCompareAndMaskSurface() {
            string source = """
                public class RuntimeSeed {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string avxRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "x86", "avx.hpp"));
            string sseRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "x86", "sse.hpp"));
            string vector128Runtime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector128.hpp"));
            string vector256Runtime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector256.hpp"));
            string funcRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "func.hpp"));
            string spanRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_span.hpp"));
            string nullableRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_nullable.hpp"));

            Assert.Contains("static Vector256<T> CompareGreaterThan", avxRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector256<T> CompareLessThanOrEqual", avxRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector256<T> Reciprocal", avxRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector256<T> ReciprocalSqrt", avxRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector128_1<T> CompareGreaterThan", sseRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector128_1<T> CompareLessThanOrEqual", sseRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector128_1<T> Reciprocal", sseRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector128_1<T> ReciprocalSqrt", sseRuntime, StringComparison.Ordinal);
            Assert.Contains("static int32_t MoveMask", sseRuntime, StringComparison.Ordinal);
            Assert.Contains("static Vector128_1<T> As(const Vector128_1<T>& value)", vector128Runtime, StringComparison.Ordinal);
            Assert.Contains("static Vector256<T> As(const Vector256<T>& value)", vector256Runtime, StringComparison.Ordinal);
            Assert.Contains("class Func<TResult>", funcRuntime, StringComparison.Ordinal);
            Assert.Contains("TResult operator()() const", funcRuntime, StringComparison.Ordinal);
            Assert.Contains("Array<T>* ToArray() const", spanRuntime, StringComparison.Ordinal);
            Assert.Contains("bool operator==(std::nullptr_t) const", nullableRuntime, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the shared native runtime exposes the array resizing, ranged copy, debug fail, and message-bearing NotImplementedException contracts required by generated BEPU output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEmptyClass_CopiesNativeRuntimeHelperContracts() {
            string source = """
                public class RuntimeSeed {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string arrayRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "array.hpp"));
            string debugRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "diagnostics", "debug.hpp"));
            string notImplementedRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "system", "not_implemented_exception.hpp"));

            Assert.Contains("static void Resize(Array<T>*& array, int32_t newLength)", arrayRuntime, StringComparison.Ordinal);
            Assert.Contains("static void Copy(const Array<T>* source, int32_t sourceIndex, Array<T>* destination, int32_t destinationIndex, int32_t length)", arrayRuntime, StringComparison.Ordinal);
            Assert.Contains("static void Fail(const std::string& message)", debugRuntime, StringComparison.Ordinal);
            Assert.Contains("explicit NotImplementedException(const std::string& message)", notImplementedRuntime, StringComparison.Ordinal);
            Assert.Contains("explicit NotImplementedException(const char* message)", notImplementedRuntime, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures caller-provided qualified type remaps can redirect System.Numerics references onto generated project-owned math types.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConfiguredSystemNumericsRemap_UsesGeneratedMathHeaders() {
            string source = """
                namespace ExampleMath {
                    public struct float3 {
                    }

                    public struct float4 {
                    }
                }

                public class Pose {
                    public System.Numerics.Quaternion Orientation;
                    public System.Numerics.Vector3 Position;
                }
                """;

            ConversionOutput output = RunConversion(
                source,
                options => options.TypeRemaps = new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector3"] = "ExampleMath.float3",
                    ["System.Numerics.Quaternion"] = "ExampleMath.float4"
                });
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Pose.hpp"));

            Assert.Contains("#include \"examplemath_float4.hpp\"", headerOutput);
            Assert.Contains("#include \"examplemath_float3.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"Quaternion.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Vector3.hpp\"", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures System.Threading.AutoResetEvent resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAutoResetEventField_UsesRuntimeAutoResetEventHeader() {
            string source = """
                using System.Threading;

                public class WorkerState {
                    public AutoResetEvent Signal;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "WorkerState.hpp"));

            Assert.Contains("#include \"system/threading/auto_reset_event.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"AutoResetEvent.hpp\"", headerOutput, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "AutoResetEvent");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "auto_reset_event.hpp")));
        }

        /// <summary>
        /// Ensures the shared Span runtime exposes indexer helpers and buffer-backed constructors required by generated BEPU buffer/span interop.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSpanUsage_UsesRuntimeSpanBufferInteropSurface() {
            string source = """
                public class SpanOwner {
                    public int First(Span<int> values) {
                        return values[0];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_span.hpp"));

            Assert.Contains("T& get_Item(int32_t index)", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("const T& get_Item(int32_t index) const", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("Span(TBuffer buffer)", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("ReadOnlySpan(TBuffer buffer)", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("Data(buffer.Memory)", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("buffer.get_Length()", runtimeHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures Vector256 and x86 intrinsic helpers resolve to portable runtime headers instead of missing synthetic generated includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemRuntimeIntrinsicsVector256Usage_UsesRuntimeIntrinsicsHeaders() {
            string source = """
                using System.Runtime.Intrinsics;
                using System.Runtime.Intrinsics.X86;

                public unsafe class WideIntrinsics {
                    public static Vector256<float> Load(float* source) {
                        return Avx.IsSupported ? Avx.LoadAlignedVector256(source) : Vector256<float>.Zero;
                    }

                    public static bool SupportsSse() {
                        return Sse.IsSupported;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "WideIntrinsics.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "WideIntrinsics.cpp"));

            Assert.Contains("#include \"system/runtime/intrinsics/vector256.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"system/runtime/intrinsics/x86/avx.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("#include \"system/runtime/intrinsics/x86/sse.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Vector256.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Avx.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Sse.hpp\"", sourceOutput, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "NativeVector256");
            AssertRuntimeRequirement(output.Report, "Avx");
            AssertRuntimeRequirement(output.Report, "Sse");
        }

        /// <summary>
        /// Ensures the portable vector runtime exposes the bridge helpers BEPU uses between Vector, Vector128, and Vector256.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemNumericsVectorIntrinsicBridgeUsage_ProvidesPortableBridgeHelpers() {
            string source = """
                using System.Numerics;
                using System.Runtime.Intrinsics.X86;

                public static class WideIntrinsics {
                    public static Vector<float> RoundTrip(Vector<float> value) {
                        return Avx.IsSupported ? value.AsVector256().AsVector() : value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string vectorHeader = File.ReadAllText(Path.Combine(output.OutputPath, "system", "numerics", "vector.hpp"));
            string vector256Header = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector256.hpp"));

            Assert.Contains("Vector256<T> AsVector256() const;", vectorHeader, StringComparison.Ordinal);
            Assert.Contains("template <typename TTo>\n    Vector256<TTo> AsVector256() const;", vectorHeader, StringComparison.Ordinal);
            Assert.Contains("Vector_1<T> AsVector() const", vector256Header, StringComparison.Ordinal);
            Assert.Contains("Vector256<int32_t> AsInt32() const", vector256Header, StringComparison.Ordinal);
            Assert.Contains("Vector128_1<TTo> GetLower() const", vector256Header, StringComparison.Ordinal);
            Assert.Contains("Vector128_1<TTo> GetUpper() const", vector256Header, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the portable Vector256 runtime preserves .NET AndNot operand order used by BEPU integration scheduling.
        /// </summary>
        [Fact]
        public void WriteOutput_WithVector256AndNotUsage_PreservesManagedOperandOrder() {
            string source = """
                using System.Runtime.Intrinsics;

                public static class WideIntrinsics {
                    public static Vector256<uint> Mask(Vector256<uint> left, Vector256<uint> right) {
                        return Vector256.AndNot(left, right);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string vector256Header = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector256.hpp"));

            Assert.Contains("leftBits & (~rightBits)", vector256Header, StringComparison.Ordinal);
            Assert.DoesNotContain("(~leftBits) & rightBits", vector256Header, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures Vector128 exposes a portable raw-value bridge for remapped engine value types such as float3.
        /// </summary>
        [Fact]
        public void WriteOutput_WithVector128RawValueBridgeUsage_ProvidesPortableValueOverload() {
            string source = """
                using System.Numerics;
                using System.Runtime.Intrinsics;

                namespace ExampleMath {
                    public struct float3 {
                        public float X;
                        public float Y;
                        public float Z;
                    }
                }

                public static class BoundsGate {
                    public static Vector128<float> Promote(ExampleMath.float3 value) {
                        return Vector128.AsVector128(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(
                source,
                options => options.TypeRemaps = new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector3"] = "ExampleMath.float3"
                });
            string vector128Header = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector128.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BoundsGate.cpp"));

            Assert.Contains("template <typename TValue>\n    static Vector128_1<float> AsVector128(const TValue& value)", vector128Header, StringComparison.Ordinal);
            Assert.Contains("Vector128::AsVector128(value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures caller-provided qualified type remaps also cover source files that reference the mapped type through a using directive.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUsingImportedSystemNumericsRemap_UsesGeneratedMathHeaders() {
            string source = """
                using System.Numerics;

                namespace ExampleMath {
                    public struct float3 {
                    }
                }

                public class Pose {
                    public Vector3 Position;
                }
                """;

            ConversionOutput output = RunConversion(
                source,
                options => options.TypeRemaps = new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector3"] = "ExampleMath.float3"
                });
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Pose.hpp"));

            Assert.Contains("#include \"examplemath_float3.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"Vector3.hpp\"", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures caller-provided qualified type remaps also cover unqualified static/member usages collected from method bodies.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUsingImportedSystemNumericsBodyUsageRemap_UsesGeneratedMathHeaders() {
            string source = """
                using System.Numerics;

                namespace ExampleMath {
                    public struct float3 {
                        public static float Dot(float3 first, float3 second) {
                            return 0f;
                        }
                    }
                }

                public class Pose {
                    public float Magnitude(Vector3 value) {
                        return Vector3.Dot(value, value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(
                source,
                options => options.TypeRemaps = new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector3"] = "ExampleMath.float3"
                });
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Pose.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Pose.cpp"));

            Assert.Contains("#include \"examplemath_float3.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"Vector3.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.Contains("float3::Dot(value, value)", sourceOutput);
        }

        /// <summary>
        /// Ensures native-sized integer aliases lower to built-in pointer-width types without synthetic generated includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeSizedIntegerAliases_UsesPointerWidthNativeTypes() {
            string source = """
                public class WindowHost {
                    public void Attach(nint handle, nuint token) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "WindowHost.hpp"));

            Assert.Contains("void Attach(intptr_t handle, uintptr_t token);", headerOutput);
            Assert.DoesNotContain("#include \"nint.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"nuint.hpp\"", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed array length access lowers through the lightweight runtime getter contract.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedArrayLengthAccess_UsesRuntimeArrayGetterContract() {
            string source = """
                public class BufferMetrics {
                    public int Count(byte[] values) {
                        return values.Length;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BufferMetrics.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "array.hpp"));

            Assert.Contains("values->get_Length()", sourceOutput);
            Assert.Contains("int32_t get_Length() const", runtimeHeader);
            AssertRuntimeRequirement(output.Report, "NativeArray");
        }

        /// <summary>
        /// Ensures nullable wrappers and TimeSpan expose managed getter members expected by emitted native code.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullableAndTimeSpanPropertyAccess_UsesManagedGetterContracts() {
            string source = """
                using System;

                public class RuntimeValueReader {
                    public bool Has(float? value) {
                        return value.HasValue;
                    }

                    public float Read(float? value) {
                        return value.Value;
                    }

                    public double Milliseconds(TimeSpan elapsed) {
                        return elapsed.TotalMilliseconds;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RuntimeValueReader.cpp"));
            string nullableHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_nullable.hpp"));
            string dateTimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_datetime.hpp"));

            Assert.Contains("value.get_HasValue()", sourceOutput);
            Assert.Contains("value.get_Value()", sourceOutput);
            Assert.Contains("elapsed.get_TotalMilliseconds()", sourceOutput);
            Assert.Contains("bool get_HasValue() const", nullableHeader);
            Assert.Contains("const T& get_Value() const", nullableHeader);
            Assert.Contains("double get_TotalMilliseconds() const", dateTimeHeader);
            AssertRuntimeRequirement(output.Report, "NativeNullable");
            AssertRuntimeRequirement(output.Report, "NativeDateTime");
        }

        /// <summary>
        /// Ensures managed StringComparer property access lowers through the lightweight runtime getter contract.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringComparerPropertyAccess_UsesManagedGetterContract() {
            string source = """
                using System;

                public class ComparerProvider {
                    public StringComparer Create() {
                        return StringComparer.OrdinalIgnoreCase;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ComparerProvider.cpp"));
            string comparerHeader = File.ReadAllText(Path.Combine(output.OutputPath, "system", "string_comparer.hpp"));

            Assert.Contains("StringComparer::get_OrdinalIgnoreCase()", sourceOutput);
            Assert.Contains("static const StringComparer& get_OrdinalIgnoreCase()", comparerHeader);
            Assert.Contains("static const StringComparer& get_Ordinal()", comparerHeader);
            AssertRuntimeRequirement(output.Report, "StringComparer");
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
        /// Ensures NotImplementedException resolves through the dedicated runtime include instead of a missing synthetic type header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNotImplementedException_UsesRuntimeNotImplementedExceptionHeader() {
            string source = """
                using System;

                public static class GuardGate {
                    public static void Fail() {
                        throw new NotImplementedException();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GuardGate.cpp"));

            Assert.Contains("#include \"system/not_implemented_exception.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"NotImplementedException.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("throw new NotImplementedException()", sourceOutput, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "NotImplementedException");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "not_implemented_exception.hpp")));
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
            Assert.Contains(".get_TotalMilliseconds() > 500", output.GeneratedText);
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
            Assert.Contains("return frames->get_Count();", output.GeneratedText);
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
            Assert.Contains("reader->Dispose();", output.GeneratedText);
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
            Assert.Contains("match.get_Groups()[\"value\"].get_Value()", output.GeneratedText);
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
    /// Ensures instance-method event subscriptions lower through an explicit bound runtime helper instead of emitting unusable unbound member pointers.
    /// </summary>
    [Fact]
    public void WriteOutput_WithInstanceEventSubscription_UsesBoundRuntimeEventHelper() {
        string source = """
                using System;

                public class InteractableComponent {
                    public event Action<int, int, int> CursorEvent;
                }

                public class NintendoDsReturnOverlayComponent {
                    InteractableComponent BoundInteractable;

                    public void Bind() {
                        BoundInteractable.CursorEvent += HandleCursorEvent;
                    }

                    public void Unbind() {
                        BoundInteractable.CursorEvent -= HandleCursorEvent;
                    }

                    void HandleCursorEvent(int relativePosition, int delta, int interaction) {
                    }
                }
                """;

        ConversionOutput output = RunConversion(source);
        string sourceText = File.ReadAllText(Path.Combine(output.OutputPath, "NintendoDsReturnOverlayComponent.cpp"));

        Assert.Contains("CursorEvent += Event::Bind(this, static_cast<void (NintendoDsReturnOverlayComponent::*)(int32_t, int32_t, int32_t)>(&NintendoDsReturnOverlayComponent::HandleCursorEvent))", sourceText, StringComparison.Ordinal);
        Assert.Contains("CursorEvent -= Event::Bind(this, static_cast<void (NintendoDsReturnOverlayComponent::*)(int32_t, int32_t, int32_t)>(&NintendoDsReturnOverlayComponent::HandleCursorEvent))", sourceText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures custom delegate declarations lower to callable runtime aliases instead of empty synthetic delegate classes.
    /// </summary>
    [Fact]
        public void WriteOutput_WithCustomDelegateDeclaration_UsesRuntimeDelegateAlias() {
            string source = """
                public delegate void RefinementScheduler(int frameIndex, out int rootRefinementSize, out bool usePriorityQueue);

                public static class BroadPhase {
                    public static void Default(int frameIndex, out int rootRefinementSize, out bool usePriorityQueue) {
                        rootRefinementSize = frameIndex;
                        usePriorityQueue = frameIndex > 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string delegateHeader = File.ReadAllText(Path.Combine(output.OutputPath, "RefinementScheduler.hpp"));

            Assert.Contains("#include \"system/delegate.hpp\"", delegateHeader, StringComparison.Ordinal);
            Assert.Contains("using RefinementScheduler = Delegate<void, int32_t, int32_t&, bool&>;", delegateHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("class RefinementScheduler", delegateHeader, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "Delegate");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "delegate.hpp")));
        }

        /// <summary>
        /// Ensures delegate invocation preserves inline out-variable declarations by hoisting them before the call expression.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDelegateInvocationOutVarDeclarations_HoistsArgumentsBeforeCall() {
            string source = """
                public delegate void RefinementScheduler(int frameIndex, out int rootRefinementSize, out bool usePriorityQueue);

                public static class BroadPhase {
                    public static RefinementScheduler ActiveRefinementSchedule { get; set; }

                    public static void Default(int frameIndex, out int rootRefinementSize, out bool usePriorityQueue) {
                        rootRefinementSize = frameIndex;
                        usePriorityQueue = frameIndex > 0;
                    }

                    public static void Initialize() {
                        ActiveRefinementSchedule = Default;
                    }

                    public static void Update() {
                        ActiveRefinementSchedule(1, out var rootRefinementSize, out var usePriorityQueue);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BroadPhase.cpp"));

            Assert.Contains("int32_t rootRefinementSize;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("bool usePriorityQueue;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("(*BroadPhase::get_ActiveRefinementSchedule())(1, rootRefinementSize, usePriorityQueue);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("BroadPhase::set_ActiveRefinementSchedule(new RefinementScheduler(&BroadPhase::Default__out1_out2));", sourceOutput, StringComparison.Ordinal);
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
        /// Ensures System.Numerics.Vector&lt;T&gt; resolves to one generic runtime header instead of a synthetic generated header and preserves the emitted static helper surface used by BEPU.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemNumericsVectorUsage_UsesRuntimeVectorHeader() {
            string source = """
                using System.Numerics;

                public static class VectorGate {
                    public static Vector<float> Select(Vector<int> mask, Vector<float> left, Vector<float> right) {
                        return Vector.ConditionalSelect(mask, left, right);
                    }

                    public static int Width() {
                        return Vector<float>.Count;
                    }

                    public static Vector<int> Compare(Vector<float> left, Vector<float> right) {
                        return Vector.LessThan(left, right);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "VectorGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "VectorGate.cpp"));

            Assert.Contains("#include \"system/numerics/vector.hpp\"", header);
            Assert.DoesNotContain("#include \"Vector.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Vector_1<float>", output.GeneratedText);
            Assert.Contains("Vector_1<int32_t>", output.GeneratedText);
            Assert.Contains("Vector::ConditionalSelect", sourceOutput);
            Assert.Contains("Vector::LessThan", sourceOutput);
            Assert.Contains("Vector_1<float>::get_Count()", sourceOutput);
            AssertRuntimeRequirement(output.Report, "NativeVector");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "numerics", "vector.hpp")));
        }

        /// <summary>
        /// Ensures System.Numerics.Vector&lt;T&gt; constructors preserve span-backed initialization required by BEPU bundle setup.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemNumericsVectorSpanConstructor_UsesRuntimeVectorSpanSurface() {
            string source = """
                using System;
                using System.Numerics;

                public static class VectorGate {
                    public static Vector<float> Read(Span<float> values) {
                        return new Vector<float>(values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "VectorGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "VectorGate.cpp"));

            Assert.Contains("#include \"system/numerics/vector.hpp\"", header);
            Assert.Contains("#include \"runtime/native_span.hpp\"", header);
            Assert.Contains("return Vector_1<float>(values);", sourceOutput);
            AssertRuntimeRequirement(output.Report, "NativeVector");
            AssertRuntimeRequirement(output.Report, "NativeSpan");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "numerics", "vector.hpp")));
        }

        /// <summary>
        /// Ensures nested runtime generic rendering preserves the portable System.Numerics.Vector runtime type even when it appears inside another generic container.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedSystemNumericsVectorUsage_PreservesRuntimeGenericTypeInsideOuterGeneric() {
            string source = """
                using System.Numerics;

                public struct Buffer<T> {
                }

                public class VectorBucket {
                    public Buffer<Vector<int>> Masks { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "VectorBucket.hpp"));

            Assert.Contains("#include \"system/numerics/vector.hpp\"", header);
            Assert.Contains("Buffer_1<Vector_1<int32_t>>", output.GeneratedText);
            Assert.DoesNotContain("Buffer_1<Vector<int32_t>>", output.GeneratedText, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "NativeVector");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "numerics", "vector.hpp")));
        }

        /// <summary>
        /// Ensures System.Collections.Generic.KeyValuePair&lt;TKey, TValue&gt; resolves to a shared runtime header instead of a missing synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemCollectionsGenericKeyValuePairUsage_UsesRuntimeHeader() {
            string source = """
                using System.Collections.Generic;

                public static class PairGate {
                    public static KeyValuePair<int, string> Make(int key, string value) {
                        return new KeyValuePair<int, string>(key, value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "PairGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PairGate.cpp"));

            Assert.Contains("#include \"system/collections/generic/key_value_pair.hpp\"", header);
            Assert.DoesNotContain("#include \"KeyValuePair.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("KeyValuePair<int32_t, std::string>", output.GeneratedText);
            Assert.Contains("return KeyValuePair<int32_t, std::string>(key, value);", sourceOutput);
            AssertRuntimeRequirement(output.Report, "NativeKeyValuePair");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "collections", "generic", "key_value_pair.hpp")));
        }

        /// <summary>
        /// Ensures System.Runtime.Intrinsics.Vector128 and Sse41 resolve to portable runtime headers instead of synthetic generated includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemRuntimeIntrinsicsVector128Usage_UsesRuntimeIntrinsicsHeaders() {
            string source = """
                using System.Runtime.Intrinsics;
                using System.Runtime.Intrinsics.X86;

                public static class IntrinsicsGate {
                    public static Vector128<float> Blend(Vector128<float> left, Vector128<float> right) {
                        if (Sse41.IsSupported) {
                            return Sse41.Blend(left, right, 0b1000);
                        }

                        return Vector128.ConditionalSelect(Vector128.Create(-1, -1, -1, 0).As<int, float>(), left, right);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "IntrinsicsGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "IntrinsicsGate.cpp"));

            Assert.Contains("#include \"system/runtime/intrinsics/vector128.hpp\"", header);
            Assert.Contains("#include \"system/runtime/intrinsics/x86/sse41.hpp\"", header);
            Assert.DoesNotContain("#include \"Vector128.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Sse41.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("Vector128_1<float>", output.GeneratedText);
            Assert.Contains("Vector128::ConditionalSelect", sourceOutput);
            Assert.Contains("Sse41::Blend", sourceOutput);
            AssertRuntimeRequirement(output.Report, "NativeVector128");
            AssertRuntimeRequirement(output.Report, "Sse41");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector128.hpp")));
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "x86", "sse41.hpp")));
        }

        /// <summary>
        /// Ensures generic IEnumerator contracts resolve to the shared runtime header instead of a missing synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemCollectionsGenericIEnumeratorUsage_UsesRuntimeEnumeratorHeader() {
            string source = """
                using System.Collections.Generic;

                public struct Counter : IEnumerator<int> {
                    int value;

                    public int Current => value;

                    object System.Collections.IEnumerator.Current => value;

                    public void Dispose() {
                    }

                    public bool MoveNext() {
                        value++;
                        return value < 4;
                    }

                    public void Reset() {
                        value = 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Counter.hpp"));

            Assert.Contains("#include \"system/collections/generic/ienumerator.hpp\"", header);
            Assert.DoesNotContain("#include \"IEnumerator.hpp\"", header, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "collections", "generic", "ienumerator.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeIEnumerator");
        }

        /// <summary>
        /// Ensures generic IEnumerable contracts resolve to the shared runtime header instead of relying on missing synthetic generated includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemCollectionsGenericIEnumerableUsage_UsesRuntimeEnumerableHeader() {
            string source = """
                using System.Collections;
                using System.Collections.Generic;

                public class CounterEnumerable : IEnumerable<int> {
                    public Counter GetEnumerator() {
                        return new Counter();
                    }

                    IEnumerator IEnumerable.GetEnumerator() {
                        return GetEnumerator();
                    }

                    IEnumerator<int> IEnumerable<int>.GetEnumerator() {
                        return GetEnumerator();
                    }
                }

                public struct Counter : IEnumerator<int> {
                    int value;

                    public int Current => value;

                    object IEnumerator.Current => value;

                    public void Dispose() {
                    }

                    public bool MoveNext() {
                        value++;
                        return value < 4;
                    }

                    public void Reset() {
                        value = 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "CounterEnumerable.hpp"));

            Assert.Contains("#include \"system/collections/generic/ienumerable.hpp\"", header);
            Assert.DoesNotContain("#include \"IEnumerable.hpp\"", header, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "collections", "generic", "ienumerable.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeIEnumerable");
        }

        /// <summary>
        /// Ensures System.Collections.Generic.List&lt;T&gt; runtime output exposes managed indexer helpers required by converted element-access call sites.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemCollectionsGenericListIndexerUsage_EmitsRuntimeIndexerSurface() {
            string source = """
                using System.Collections.Generic;

                public class Inventory {
                    public int Read(List<int> values, int index) {
                        return values[index];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_list.hpp"));

            Assert.Contains("#include \"runtime/native_list.hpp\"", output.GeneratedText);
            Assert.Contains("T& get_Item(int32_t index)", runtimeHeader);
            Assert.Contains("const T& get_Item(int32_t index) const", runtimeHeader);
            Assert.Contains("void set_Item(int32_t index, const T& value)", runtimeHeader);
        }

        /// <summary>
        /// Ensures System.Collections.Generic.Dictionary&lt;TKey, TValue&gt; runtime output exposes managed indexer helpers required by converted element-access call sites.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemCollectionsGenericDictionaryIndexerUsage_EmitsRuntimeIndexerSurface() {
            string source = """
                using System.Collections.Generic;

                public class Inventory {
                    public int Read(Dictionary<string, int> values, string key) {
                        return values[key];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_dictionary.hpp"));

            Assert.Contains("#include \"runtime/native_dictionary.hpp\"", output.GeneratedText);
            Assert.Contains("TValue& get_Item(const TKey& key)", runtimeHeader);
            Assert.Contains("const TValue& get_Item(const TKey& key) const", runtimeHeader);
            Assert.Contains("void set_Item(const TKey& key, const TValue& value)", runtimeHeader);
        }

        /// <summary>
        /// Ensures System.Threading.SpinLock resolves to the shared runtime header instead of a missing synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemThreadingSpinLockUsage_UsesRuntimeHeader() {
            string source = """
                using System.Threading;

                public class Gate {
                    SpinLock sync;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.hpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "system", "threading", "spin_lock.hpp"));

            Assert.Contains("#include \"system/threading/spin_lock.hpp\"", header);
            Assert.DoesNotContain("#include \"SpinLock.hpp\"", header, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "SpinLock");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "spin_lock.hpp")));
            Assert.Contains("std::atomic<bool>", runtimeHeader);
            Assert.DoesNotContain("atomic_flag", runtimeHeader, StringComparison.Ordinal);
        }

        /// <summary>

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
        /// Ensures unmanaged function-pointer signatures lower to the portable runtime wrapper instead of degrading to object pointers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFunctionPointerField_UsesRuntimeFunctionPointerHeader() {
            string source = """
                public unsafe interface IThreadDispatcher {
                }

                public unsafe struct Task {
                    public delegate*<long, void*, int, IThreadDispatcher, void> Function;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Task.hpp"));

            Assert.Contains("#include \"runtime/function_pointer.hpp\"", header);
            Assert.DoesNotContain("object* Function", output.GeneratedText, StringComparison.Ordinal);
            Assert.Contains("FunctionPointer<void, int64_t, void*, int32_t, ::IThreadDispatcher*> Function;", output.GeneratedText);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "function_pointer.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeFunctionPointer");
        }

        /// <summary>
        /// Ensures unmanaged function-pointer initializers emit one qualified address-of expression for generic static methods.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericStaticFunctionPointerInitializer_EmitsSingleQualifiedAddressOf() {
            string source = """
                public unsafe class ThunkHolder<T> {
                    public static readonly delegate*<int, void> Handler = &Handle;

                    static void Handle(int value) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ThunkHolder_1.cpp"));

            Assert.Contains("FunctionPointer<void, int32_t> ThunkHolder_1<T>::Handler =", sourceOutput);
            Assert.Contains("&ThunkHolder_1<T>::Handle", sourceOutput);
            Assert.DoesNotContain("&&ThunkHolder_1", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unmanaged function-pointer wrappers preserve ref and out parameter shapes in their generic argument lists.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFunctionPointerByRefParameters_PreservesReferenceGenericArguments() {
            string source = """
                public unsafe struct ChildEnumerator {
                }

                public unsafe struct Triangle {
                }

                public unsafe class MeshReductionThunks {
                    public static readonly delegate*<void*, int, int, ref ChildEnumerator, void> FindLocalOverlaps = &FindLocalOverlapsImpl;
                    public static readonly delegate*<void*, int, out Triangle, void> GetLocalChild = &GetLocalChildImpl;

                    static void FindLocalOverlapsImpl(void* mesh, int min, int max, ref ChildEnumerator enumerator) {
                    }

                    static void GetLocalChildImpl(void* mesh, int childIndex, out Triangle triangle) {
                        triangle = default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MeshReductionThunks.cpp"));

            Assert.Contains("FunctionPointer<void, void*, int32_t, int32_t, ::ChildEnumerator&> MeshReductionThunks::FindLocalOverlaps", sourceOutput);
            Assert.Contains("FunctionPointer<void, void*, int32_t, ::Triangle&> MeshReductionThunks::GetLocalChild", sourceOutput);
            Assert.Contains("static_cast<void (*)(void*, int32_t, int32_t, ChildEnumerator&)>(&MeshReductionThunks::FindLocalOverlapsImpl__ref3)", sourceOutput);
            Assert.Contains("static_cast<void (*)(void*, int32_t, Triangle&)>(&MeshReductionThunks::GetLocalChildImpl__out2)", sourceOutput);
        }

        /// <summary>
        /// Ensures System.Threading.Interlocked resolves to the portable runtime helper header instead of a missing generated type include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemThreadingInterlockedUsage_UsesRuntimeInterlockedHeader() {
            string source = """
                using System.Threading;

                public class Counter {
                    int Value;

                    public int Next() {
                        return Interlocked.Increment(ref Value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            Assert.Contains("#include \"system/threading/interlocked.hpp\"", output.GeneratedText);
            Assert.DoesNotContain("#include \"Interlocked.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "interlocked.hpp")));
            AssertRuntimeRequirement(output.Report, "Interlocked");
        }

        /// <summary>
        /// Ensures pointer-only pointee types used exclusively inside method bodies stay as forward declarations in headers to avoid circular value-type includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerPointeeUsedOnlyInMethodBody_KeepsHeaderOnForwardDeclaration() {
            string source = """
                public struct Task {
                    public int Value;
                }

                public unsafe struct TaskContinuation {
                    public Task OnCompleted;
                }

                public unsafe struct ContinuationHandle {
                    TaskContinuation* continuation;

                    public int Read() {
                        return continuation->OnCompleted.Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ContinuationHandle.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContinuationHandle.cpp"));

            Assert.Contains("class TaskContinuation;", header);
            Assert.DoesNotContain("#include \"TaskContinuation.hpp\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Task.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"TaskContinuation.hpp\"", sourceOutput);
        }

        /// <summary>
        /// Ensures System.Threading.SpinWait resolves to the portable runtime helper header instead of a missing generated type include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemThreadingSpinWaitUsage_UsesRuntimeSpinWaitHeader() {
            string source = """
                using System.Threading;

                public class Waiter {
                    public void Wait() {
                        var waiter = new SpinWait();
                        waiter.SpinOnce(-1);
                        waiter.Reset();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("#include \"system/threading/spin_wait.hpp\"", output.GeneratedText);
            Assert.DoesNotContain("#include \"SpinWait.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "spin_wait.hpp")));
            AssertRuntimeRequirement(output.Report, "SpinWait");
        }

        /// <summary>
        /// Ensures System.Threading.Volatile resolves to the portable runtime helper header instead of a missing generated type include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemThreadingVolatileUsage_UsesRuntimeVolatileHeader() {
            string source = """
                using System.Threading;

                public static class Fixture {
                    public static int Run(ref int syncIndex, ref int completedWorkBlockCount) {
                        Volatile.Write(ref syncIndex, 1);
                        return Volatile.Read(ref completedWorkBlockCount);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"system/threading/volatile.hpp\"", output.GeneratedText);
            Assert.DoesNotContain("#include \"Volatile.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.Contains("Volatile::Write(syncIndex, 1)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Volatile::Read(completedWorkBlockCount)", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "volatile.hpp")));
            AssertRuntimeRequirement(output.Report, "Volatile");
        }

        /// <summary>
        /// Ensures dependent generic null comparisons resolve through the portable runtime helper header so value-type instantiations do not emit invalid nullptr operators.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentGenericNullComparison_UsesRuntimeNullHelperHeader() {
            string source = """
                public class Fixture<TComparer> {
                    public bool HasComparer(TComparer comparer) {
                        return comparer != null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("#include \"runtime/native_null.hpp\"", output.GeneratedText);
            Assert.Contains("return !he_cpp_is_null(comparer);", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_null.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeNullComparison");
        }

        /// <summary>
        /// Ensures the portable Volatile runtime surface accepts convertible scalar literals without template deduction conflicts.
        /// </summary>
        [Fact]
        public void WriteOutput_CopiesVolatileRuntimeTemplateWithConvertibleWriteOverload() {
            string source = """
                public class EmptyGate {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string volatileHeader = File.ReadAllText(Path.Combine(output.OutputPath, "system", "threading", "volatile.hpp"));

            Assert.Contains("template <typename T, typename TValue>", volatileHeader, StringComparison.Ordinal);
            Assert.Contains("atomicLocation.store(static_cast<T>(value), std::memory_order_release);", volatileHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dependent generic hash-code calls resolve through the portable runtime helper header instead of leaking invalid direct member syntax for primitive instantiations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentGenericGetHashCode_UsesRuntimeHashHelperHeader() {
            string source = """
                public class Fixture<TValue> {
                    public int Hash(TValue value) {
                        return value.GetHashCode();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("#include \"runtime/native_hash.hpp\"", output.GeneratedText);
            Assert.Contains("return he_cpp_get_hash_code(value);", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_hash.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeHashCode");
        }

        /// <summary>
        /// Ensures System.Random resolves to the portable runtime helper header instead of a missing generated type include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemRandomUsage_UsesRuntimeRandomHeader() {
            string source = """
                using System;

                public abstract class RandomGate {
                    public abstract int Pick(Random random, int maxValue);
                }

                public class RandomGateImpl : RandomGate {
                    public override int Pick(Random random, int maxValue) {
                        return random.Next(maxValue);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RandomGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RandomGateImpl.cpp"));

            Assert.Contains("#include \"system/random.hpp\"", headerOutput);
            Assert.DoesNotContain("#include \"Random.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.Contains("return random->Next(maxValue);", sourceOutput);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "random.hpp")));
            AssertRuntimeRequirement(output.Report, "Random");
        }

        /// <summary>
        /// Ensures MemoryMarshal resolves to the portable runtime helper instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemoryMarshalSpanCast_UsesRuntimeMemoryMarshalHeader() {
            string source = """
                using System;
                using System.Runtime.InteropServices;

                public struct Handle {
                    public int Value;

                    public Handle(int value) {
                        Value = value;
                    }
                }

                public class Fixture {
                    public int Run(Span<Handle> handles) {
                        return MemoryMarshal.Cast<Handle, int>(handles).Length;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"system/runtime/interopservices/memory_marshal.hpp\"", sourceOutput);
            Assert.Contains("MemoryMarshal::Cast<Handle,int32_t>(handles)", sourceOutput);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "runtime", "interopservices", "memory_marshal.hpp")));
            AssertRuntimeRequirement(output.Report, "MemoryMarshal");
        }

        /// <summary>
        /// Ensures pointer-based span construction stays a raw unmanaged view instead of allocating and element-converting composite source structs.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerBasedSpanConstruction_UsesRawPointerViewRuntimeSurface() {
            string source = """
                public class RuntimeSeed {
                    public int Value;

                    public int Run() {
                        return Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_span.hpp"));

            Assert.Contains("Data(reinterpret_cast<T*>(data))", runtimeHeader, StringComparison.Ordinal);
            Assert.Contains("Data(reinterpret_cast<const T*>(data))", runtimeHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("InitializeOwnedBuffer(data, length);", runtimeHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures Vector512 resolves to the portable runtime helper surface instead of an undeclared intrinsic type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithVector512Usage_UsesRuntimeVector512Header() {
            string source = """
                using System;
                using System.Runtime.Intrinsics;

                public class Fixture {
                    public ulong Run() {
                        var zero = Vector512<ulong>.Zero;
                        var mask = Vector512.Equals(zero, Vector512<ulong>.AllBitsSet);
                        return Vector512.ExtractMostSignificantBits(mask);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"system/runtime/intrinsics/vector512.hpp\"", sourceOutput);
            Assert.Contains("Vector512Runtime::Equals<uint64_t>", sourceOutput);
            Assert.Contains("Vector512Runtime::ExtractMostSignificantBits<uint64_t>", sourceOutput);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "runtime", "intrinsics", "vector512.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeVector512");
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            return RunConversion(source, null);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project with optional option mutation.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="configureOptions">Optional callback that can refine the converter options before conversion.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source, Action<CPPConversionOptions> configureOptions) {
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
                .WithMode("host_file_system", CPPFeatureMode.Enabled)
                .WithMode("reflection_like_runtime", CPPFeatureMode.Enabled)
                .WithMode("text_processing", CPPFeatureMode.Enabled);
            options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();
            configureOptions?.Invoke(options);

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

