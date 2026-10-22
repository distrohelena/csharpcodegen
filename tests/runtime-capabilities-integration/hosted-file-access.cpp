#include "system/io/file-stream.hpp"
#include <cstdio>
#include <cstdint>
#include <iostream>
#include <stdexcept>
#include <string>
#if defined(_MSC_VER)
#include <crtdbg.h>
#include <cstdlib>
#include <windows.h>
#endif

/// <summary>Exercises actual hosted FileStream access and ordered shared writes without starting an engine or touching application input.</summary>
class FileStreamAccessTests {
    /// <summary>Requires a concrete contract result and preserves a readable failure in headless test output.</summary>
    static void Require(bool condition, const char* message) {
        if (!condition) { throw std::runtime_error(message); }
    }

    /// <summary>Seeds a fixed owned file with distinguishable bytes using independent stdio before testing the runtime adapter.</summary>
    static void Seed(const std::string& path) {
        std::FILE* file = std::fopen(path.c_str(), "wb");
        Require(file != nullptr, "Cannot seed owned filesystem fixture.");
        uint8_t bytes[256];
        for (size_t index = 0; index < sizeof(bytes); ++index) { bytes[index] = static_cast<uint8_t>(index ^ 0xa5); }
        const bool written = std::fwrite(bytes, 1, sizeof(bytes), file) == sizeof(bytes);
        const bool closed = std::fclose(file) == 0;
        Require(written && closed, "Cannot complete owned filesystem fixture seed.");
    }

    /// <summary>Checks independent on-disk length and all untouched bytes after rejected constructors or access operations.</summary>
    static void Original(const std::string& path) {
        std::FILE* file = std::fopen(path.c_str(), "rb");
        Require(file != nullptr, "Owned filesystem fixture disappeared.");
        uint8_t bytes[257] = {};
        const size_t count = std::fread(bytes, 1, sizeof(bytes), file);
        const bool closed = std::fclose(file) == 0;
        Require(count == 256 && closed, "An access operation changed the existing fixture length.");
        for (size_t index = 0; index < count; ++index) { Require(bytes[index] == static_cast<uint8_t>(index ^ 0xa5), "An invalid access operation changed existing bytes."); }
    }

    /// <summary>Serializes an eight-byte command stamp through the actual native byte-write path used by tray input.</summary>
    static void WriteStamp(FileStream& stream, uint64_t value) {
        for (int index = 0; index < 8; ++index) { stream.WriteByte(static_cast<uint8_t>(value >> (index * 8))); }
    }

    /// <summary>Reads one exact little-endian command stamp from a separately opened native stream.</summary>
    static uint64_t ReadStamp(FileStream& stream) {
        uint64_t value = 0;
        for (int index = 0; index < 8; ++index) {
            const int next = stream.ReadByte(); Require(next >= 0, "Native command stamp was incomplete.");
            value |= static_cast<uint64_t>(next) << (index * 8);
        }
        return value;
    }

    /// <summary>Reproduces Open plus ReadWrite and checks ordered publication without truncating, appending or replacing the file.</summary>
    static void ExistingReadWrite(const std::string& path) {
        Seed(path);
        {
            FileStream writer(path.c_str(), FileMode::Open, FileAccess::ReadWrite, FileShare::ReadWrite);
            Require(writer.CanRead() && writer.CanWrite() && writer.Length() == 256, "Open ReadWrite capabilities or original length are wrong.");
            writer.Seek(248, SeekOrigin::Begin); WriteStamp(writer, 0); writer.Flush();
            writer.Seek(64, SeekOrigin::Begin); WriteStamp(writer, UINT64_C(0x123456789abcdef)); writer.Flush();
            writer.Seek(248, SeekOrigin::Begin); WriteStamp(writer, 1); writer.Flush();
            FileStream reader(path.c_str(), FileMode::Open, FileAccess::Read, FileShare::ReadWrite);
            Require(reader.Length() == 256, "Shared publication grew the bounded existing file.");
            reader.Seek(248, SeekOrigin::Begin); Require(ReadStamp(reader) == 1, "Shared reader missed the committed sequence.");
            reader.Seek(64, SeekOrigin::Begin); Require(ReadStamp(reader) == UINT64_C(0x123456789abcdef), "Shared reader missed the committed payload.");
            writer.Seek(0, SeekOrigin::Begin); uint8_t bytes[256] = {};
            Require(writer.Read(bytes, 0, sizeof(bytes)) == sizeof(bytes), "Read after write did not preserve update-stream positioning.");
            for (size_t index = 0; index < sizeof(bytes); ++index) {
                if ((index < 64 || index >= 72) && index < 248) { Require(bytes[index] == static_cast<uint8_t>(index ^ 0xa5), "Shared command writes changed unrelated bytes."); }
            }
        }
    }

    /// <summary>Requires explicit read-only access to reject a byte write before calling the CRT.</summary>
    static void ReadOnly(const std::string& path) {
        Seed(path);
        {
            FileStream stream(path.c_str(), FileMode::Open, FileAccess::Read, FileShare::ReadWrite);
            Require(stream.CanRead() && !stream.CanWrite(), "Read-only capabilities permit writing.");
            bool rejected = false;
            try { stream.WriteByte(1); } catch (const std::exception&) { rejected = true; }
            Require(rejected, "Read-only Write must throw before stdio.");
            rejected = false;
            try { stream.SetLength(0); } catch (const std::exception&) { rejected = true; }
            Require(rejected, "Read-only SetLength must throw before stdio.");
        }
        Original(path);
    }

    /// <summary>Requires explicit write-only access to reject reads and to preserve all bytes outside its intended update.</summary>
    static void WriteOnly(const std::string& path) {
        Seed(path);
        {
            FileStream stream(path.c_str(), FileMode::Open, FileAccess::Write, FileShare::ReadWrite);
            Require(!stream.CanRead() && stream.CanWrite(), "Write-only capabilities permit reading.");
            bool rejected = false;
            try { stream.ReadByte(); } catch (const std::exception&) { rejected = true; }
            Require(rejected, "Write-only Read must throw before stdio.");
            stream.Seek(15, SeekOrigin::Begin); stream.WriteByte(static_cast<uint8_t>(15 ^ 0xa5)); stream.Flush();
        }
        Original(path);
    }

    /// <summary>Requires Open to reject a missing path rather than silently creating a replacement transport.</summary>
    static void MissingOpen(const std::string& path) {
        const std::string missing = path + ".missing"; std::remove(missing.c_str());
        bool rejected = false;
        try { FileStream stream(missing.c_str(), FileMode::Open, FileAccess::ReadWrite, FileShare::ReadWrite); }
        catch (const std::exception&) { rejected = true; }
        std::FILE* created = std::fopen(missing.c_str(), "rb");
        const bool absent = created == nullptr;
        if (created != nullptr) { std::fclose(created); std::remove(missing.c_str()); }
        Require(rejected && absent, "Open on a missing path created a file.");
    }

    /// <summary>Checks invalid mode/access pairs before a constructor can truncate or create any file.</summary>
    static void InvalidCombinations(const std::string& path) {
        const FileMode modes[] = { FileMode::Create, FileMode::Truncate, FileMode::Append };
        for (FileMode mode : modes) {
            Seed(path); bool rejected = false;
            try { FileStream stream(path.c_str(), mode, FileAccess::Read, FileShare::ReadWrite); }
            catch (const std::exception&) { rejected = true; }
            Require(rejected, "Invalid read-only mode combination was accepted."); Original(path);
        }
        Seed(path); bool rejected = false;
        try { FileStream stream(path.c_str(), FileMode::Create, static_cast<FileAccess>(255), FileShare::ReadWrite); }
        catch (const std::exception&) { rejected = true; }
        Require(rejected, "Invalid FileAccess value was accepted."); Original(path);
        rejected = false;
        try { FileStream stream(path.c_str(), FileMode::Append, FileAccess::ReadWrite, FileShare::ReadWrite); }
        catch (const std::exception&) { rejected = true; }
        Require(rejected, "Append ReadWrite must be rejected before opening."); Original(path);
        const std::string absent = path + ".invalid-create-new"; std::remove(absent.c_str());
        rejected = false;
        try { FileStream stream(absent.c_str(), FileMode::CreateNew, FileAccess::Read, FileShare::ReadWrite); }
        catch (const std::exception&) { rejected = true; }
        std::FILE* created = std::fopen(absent.c_str(), "rb"); const bool missing = created == nullptr;
        if (created != nullptr) { std::fclose(created); std::remove(absent.c_str()); }
        Require(rejected && missing, "Invalid CreateNew Read created an absent file.");
    }

    /// <summary>Checks valid explicit creation/update access and closed capabilities without broadening legacy creation policies.</summary>
    static void CreationCapabilities(const std::string& path) {
        const FileMode modes[] = { FileMode::Create, FileMode::CreateNew, FileMode::Truncate };
        const FileAccess accessModes[] = { FileAccess::Write, FileAccess::ReadWrite };
        for (FileMode mode : modes) {
            for (FileAccess access : accessModes) {
                Seed(path);
                if (mode == FileMode::CreateNew) { Require(std::remove(path.c_str()) == 0, "Cannot prepare absent owned CreateNew fixture."); }
                FileStream stream(path.c_str(), mode, access, FileShare::ReadWrite);
                Require(stream.CanWrite() && stream.CanRead() == (access == FileAccess::ReadWrite) && stream.Length() == 0,
                    "Explicit creation mode did not honor its requested access.");
                stream.WriteByte(0x33); stream.Flush();
                if (access == FileAccess::ReadWrite) { stream.Seek(0, SeekOrigin::Begin); Require(stream.ReadByte() == 0x33, "Creation update stream could not read its own byte."); }
                stream.Close(); Require(!stream.CanRead() && !stream.CanWrite() && !stream.CanSeek(), "Closed stream still reports access capabilities.");
            }
        }
    }

    /// <summary>Preserves legacy open strings while requiring their capabilities to describe the actual descriptor mode.</summary>
    static void LegacyCapabilities(const std::string& path) {
        Seed(path);
        { FileStream stream(path.c_str(), FileMode::Open); Require(stream.CanRead() && !stream.CanWrite(), "Legacy rb mode must report read-only access."); }
        { FileStream stream(path.c_str(), FileMode::Truncate); Require(!stream.CanRead() && stream.CanWrite(), "Legacy wb mode must report write-only access."); }
        Seed(path);
        { FileStream stream(path.c_str(), FileMode::OpenOrCreate); Require(stream.CanRead() && stream.CanWrite(), "Legacy update mode lost read/write access."); }
    }

public:
    /// <summary>Runs one requested scenario or the complete focused native access regression set.</summary>
    static void Run(const std::string& path, const std::string& scenario) {
        if (scenario == "all" || scenario == "readwrite") { ExistingReadWrite(path); }
        if (scenario == "all" || scenario == "readonly") { ReadOnly(path); }
        if (scenario == "all" || scenario == "writeonly") { WriteOnly(path); }
        if (scenario == "all" || scenario == "missing") { MissingOpen(path); }
        if (scenario == "all" || scenario == "invalid") { InvalidCombinations(path); }
        if (scenario == "all" || scenario == "legacy") { LegacyCapabilities(path); }
        if (scenario == "all" || scenario == "creation") { CreationCapabilities(path); }
    }
};

/// <summary>Routes debug CRT reports to stderr only in this headless fixture, retaining assertions and the real failing native write.</summary>
int main(int argc, char** argv) {
#if defined(_MSC_VER)
    _set_error_mode(_OUT_TO_STDERR);
    _set_abort_behavior(0, _CALL_REPORTFAULT);
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
#if defined(_DEBUG)
    _CrtSetReportMode(_CRT_WARN, _CRTDBG_MODE_FILE); _CrtSetReportFile(_CRT_WARN, _CRTDBG_FILE_STDERR);
    _CrtSetReportMode(_CRT_ERROR, _CRTDBG_MODE_FILE); _CrtSetReportFile(_CRT_ERROR, _CRTDBG_FILE_STDERR);
    _CrtSetReportMode(_CRT_ASSERT, _CRTDBG_MODE_FILE); _CrtSetReportFile(_CRT_ASSERT, _CRTDBG_FILE_STDERR);
#endif
#endif
    if (argc < 2 || argc > 3) { std::cerr << "Supply an owned fixture directory and optional scenario.\n"; return 2; }
    const std::string path = std::string(argv[1]) + "/native-file-access.fixture";
    try {
        FileStreamAccessTests::Run(path, argc == 3 ? argv[2] : "all"); std::remove(path.c_str());
        std::cout << "Native hosted FileStream access, ordered shared writes and existing-file preservation passed.\n"; return 0;
    } catch (const std::exception& error) {
        std::remove(path.c_str()); std::cerr << error.what() << '\n'; return 1;
    }
}