namespace cs2.cpp.tests;

/// <summary>
/// Verifies runtime support contracts are authored directly in the C++ template sources instead of being repaired after generation.
/// </summary>
public sealed class CPPRuntimeTemplateContractTests {
    /// <summary>
    /// Verifies the shared path runtime template avoids heavyweight <c>std::filesystem</c> dependencies.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_path_runtime_avoids_std_filesystem() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "path.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::filesystem", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared directory runtime template avoids heavyweight <c>std::filesystem</c> dependencies.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_directory_runtime_avoids_std_filesystem() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "directory.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::filesystem", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared file runtime template avoids stream-based existence probing.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_file_runtime_avoids_ifstream_existence_probe() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "file.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::ifstream", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared guid runtime template avoids locale-heavy stream formatting.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_guid_runtime_avoids_ostringstream() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "guid.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::ostringstream", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared vector runtime template avoids locale-heavy stream formatting.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_vector_runtime_avoids_ostringstream() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "numerics",
            "vector.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::ostringstream", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::snprintf", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::to_string", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared stopwatch runtime template uses a monotonic native clock directly instead of routing through DateTime wall-clock helpers.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_stopwatch_runtime_avoids_datetime_wall_clock_dependency() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "diagnostics",
            "stopwatch.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("DateTime::Now()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime/native_datetime.hpp", source, StringComparison.Ordinal);
        Assert.Contains("runtime/native_timespan.hpp", source, StringComparison.Ordinal);
        Assert.Contains("std::chrono::steady_clock", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared DateTime runtime template delegates duration support to the narrower TimeSpan runtime template.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_datetime_runtime_delegates_timespan_support() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_datetime.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("#include \"native_timespan.hpp\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class TimeSpan", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared exception runtime template accepts literal messages without requiring caller-side <c>std::string</c> construction.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_exceptions_support_literal_message_overloads() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_exceptions.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("explicit Exception(const char* message)", source, StringComparison.Ordinal);
        Assert.Contains("explicit InvalidOperationException(const char* message)", source, StringComparison.Ordinal);
        Assert.Contains("explicit ArgumentNullException(const char* parameterName)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared exception runtime template honors the generic compact-message config switch instead of always constructing heavyweight standard-library exception payloads.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_exceptions_honor_compact_message_config_switch() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_exceptions.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("#include \"helcpp_config.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES", source, StringComparison.Ordinal);
        Assert.Contains("class Exception : public std::exception", source, StringComparison.Ordinal);
        Assert.Contains("const char* what() const noexcept override", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared native string runtime avoids heavyweight standard integer formatting helpers.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_string_runtime_avoids_std_to_string() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_string.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::to_string", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared native string runtime keeps the bool join path separate from the arithmetic append helpers.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_string_runtime_special_cases_bool_join_formatting() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_string.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("if constexpr (std::is_same_v<TValue, bool>) {", source, StringComparison.Ordinal);
        Assert.Contains("return value ? \"True\" : \"False\";", source, StringComparison.Ordinal);
        Assert.Contains("} else {", source, StringComparison.Ordinal);
        Assert.Contains("AppendArithmeticToString(builder, value);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared string builder runtime avoids heavyweight standard integer formatting helpers.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_string_builder_runtime_avoids_std_to_string() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "text",
            "string-builder.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("std::to_string", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared bit-converter runtime exposes the managed float byte-packing helper used by generated material defaults.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_bit_converter_runtime_declares_float_get_bytes() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "bit_converter.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("static Array<uint8_t>* GetBytes(float value)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared console runtime template avoids heavyweight iostream dependencies.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_console_runtime_avoids_iostream() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "console.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("<iostream>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::getline", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cin", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the shared stream runtime template avoids unused fstream includes.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_stream_runtime_avoids_fstream_include() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "stream.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.DoesNotContain("<fstream>", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native dictionary runtime template declares the managed-style <c>Clear()</c> surface directly.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_dictionary_declares_managed_clear_surface_directly() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_dictionary.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("void Clear()", source, StringComparison.Ordinal);
        Assert.Contains("this->clear();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native dictionary runtime can use generated value-type equality and hash members for struct keys.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_dictionary_hashes_generated_value_type_keys() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_dictionary.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("class NativeDictionaryHash", source, StringComparison.Ordinal);
        Assert.Contains("class NativeDictionaryEqual", source, StringComparison.Ordinal);
        Assert.Contains("value.GetHashCode()", source, StringComparison.Ordinal);
        Assert.Contains("value.Equals(right)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native list runtime can use generated value-type equality members for Contains and Remove.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_list_compares_generated_value_type_items() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_list.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("class NativeListEqual", source, StringComparison.Ordinal);
        Assert.Contains("value.Equals(right)", source, StringComparison.Ordinal);
        Assert.Contains("std::find_if", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native event runtime stores and invokes free or static subscribers instead of discarding all event traffic.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_event_invokes_static_subscribers() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_event.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("std::vector<Subscriber> Subscribers", source, StringComparison.Ordinal);
        Assert.Contains("Event& operator+=(void (*handler)(TArgs...))", source, StringComparison.Ordinal);
        Assert.Contains("std::array<void*, sizeof...(TArgs)> argumentPointers", source, StringComparison.Ordinal);
        Assert.Contains("subscriber.Invoke(argumentPointers.data());", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native event runtime declares an explicit bound-instance helper instead of silently discarding member-method subscriptions.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_event_declares_bound_instance_subscription_support() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_event.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("static auto Bind(TInstance* instance, void (TInstance::*method)(TArgs...))", source, StringComparison.Ordinal);
        Assert.Contains("Event& operator+=(BoundHandler<TInstance, TArgs...> handler)", source, StringComparison.Ordinal);
        Assert.Contains("Event& operator-=(BoundHandler<TInstance, TArgs...> handler)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the number runtime template declares the finite-check helper surface directly.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_number_declares_finite_helpers_directly() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "number.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("static bool IsNaN(float value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsNaN(double value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsInfinity(float value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsInfinity(double value)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the file-stream runtime template owns the PS2 `cdrom0:` direct-read path instead of requiring downstream generated-code rewrites.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_file_stream_owns_ps2_cdrom_direct_read_support() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "file-stream.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("#if HE_CPP_PLATFORM_PS2", source, StringComparison.Ordinal);
        Assert.Contains("FileStreamSupportStartsWithPs2CdromPrefix", source, StringComparison.Ordinal);
        Assert.Contains("ReadPs2DiscFile", source, StringComparison.Ordinal);
        Assert.Contains("mode == FileMode::Open && FileStreamSupportStartsWithPs2CdromPrefix", source, StringComparison.Ordinal);
        Assert.Contains("memoryBuffer = ReadPs2DiscFile", source, StringComparison.Ordinal);
        Assert.Contains("ownsMemoryBuffer = true;", source, StringComparison.Ordinal);
        Assert.Contains("writable = false;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the file-stream runtime template owns the generic custom native file-system handoff instead of requiring downstream generated-code rewrites.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_file_stream_owns_generic_custom_file_system_handoff() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "io",
            "file-stream.cpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM", source, StringComparison.Ordinal);
        Assert.Contains("#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER", source, StringComparison.Ordinal);
        Assert.Contains("mode == FileMode::Open && HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(path)", source, StringComparison.Ordinal);
        Assert.Contains("std::unique_ptr<FileStream> customStream(HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(path));", source, StringComparison.Ordinal);
        Assert.Contains("memoryBuffer.swap(customStream->memoryBuffer);", source, StringComparison.Ordinal);
        Assert.Contains("customStream->file = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("customStream->ownsMemoryBuffer = false;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the csharpcodegen repository root from the current test assembly location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRootPath() {
        string currentPath = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(currentPath)) {
            string rootMarkerPath = Path.Combine(currentPath, "cs2.cpp", "cs2.cpp.csproj");
            if (File.Exists(rootMarkerPath)) {
                return currentPath;
            }

            DirectoryInfo parentDirectory = Directory.GetParent(currentPath);
            if (parentDirectory == null) {
                break;
            }

            currentPath = parentDirectory.FullName;
        }

        throw new InvalidOperationException("Could not resolve the csharpcodegen repository root from the current test assembly location.");
    }
}
