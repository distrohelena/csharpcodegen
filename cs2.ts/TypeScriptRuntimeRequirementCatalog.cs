using System.Collections.Generic;

namespace cs2.ts {
    /// <summary>
    /// Catalog of runtime requirement definitions for the TypeScript backend.
    /// </summary>
    public static class TypeScriptRuntimeRequirementCatalog {
        /// <summary>
        /// Gets the shared runtime requirement definitions.
        /// </summary>
        public static IReadOnlyList<TypeScriptRuntimeRequirementDefinition> BaseRequirements { get; } = new List<TypeScriptRuntimeRequirementDefinition> {
            // system
            new TypeScriptRuntimeRequirementDefinition("InvalidOperationException", "./system/invalid-operation.exception"),
            new TypeScriptRuntimeRequirementDefinition("NotSupportedException", "./system/not-supported.exception"),
            new TypeScriptRuntimeRequirementDefinition("NotImplementedException", "./system/not-implemented.exception"),
            new TypeScriptRuntimeRequirementDefinition("ArgumentException", "./system/argument.exception"),
            new TypeScriptRuntimeRequirementDefinition("ArgumentNullException", "./system/argument-null.exception"),
            new TypeScriptRuntimeRequirementDefinition("ArgumentOutOfRangeException", "./system/argument-out-of-range.exception"),
            TypeScriptRuntimeRequirementDefinition.CreateGeneric(17, 0, false, "Action", "./system/action"),
            TypeScriptRuntimeRequirementDefinition.CreateGeneric(17, 1, false, "Func", "./system/func"),
            new TypeScriptRuntimeRequirementDefinition("ConsoleColor", "./system/console-color"),
            new TypeScriptRuntimeRequirementDefinition("DateTime", "./system/date-time"),
            new TypeScriptRuntimeRequirementDefinition("TimeSpan", "./system/time-span"),
            new TypeScriptRuntimeRequirementDefinition("Exception", "./system/exception"),
            new TypeScriptRuntimeRequirementDefinition("Random", "./system/random"),
            new TypeScriptRuntimeRequirementDefinition("Attribute", "./system/attribute"),
            new TypeScriptRuntimeRequirementDefinition("Version", "./system/version"),
            new TypeScriptRuntimeRequirementDefinition("Tuple", "./system/tuple"),
            new TypeScriptRuntimeRequirementDefinition("Console", "./system/console"),
            new TypeScriptRuntimeRequirementDefinition("IDisposable", "./system/disposable.interface"),
            new TypeScriptRuntimeRequirementDefinition("Guid", "./system/guid"),
            new TypeScriptRuntimeRequirementDefinition("NativeArrayUtil", "./system/util/nat-array-util"),
            new TypeScriptRuntimeRequirementDefinition("NativeStringUtil", "./system/util/nat-string-util"),
            new TypeScriptRuntimeRequirementDefinition("Convert", "./system/convert"),
            new TypeScriptRuntimeRequirementDefinition("Environment", "./system/environment"),
            new TypeScriptRuntimeRequirementDefinition("AppDomain", "./system/app-domain"),
            new TypeScriptRuntimeRequirementDefinition("StringComparer", "./system/string-comparer"),
            new TypeScriptRuntimeRequirementDefinition("StringComparison", "./system/string-comparison"),

            // system.collection.concurrent
            new TypeScriptRuntimeRequirementDefinition("ConcurrentDictionary", "./system/collections/concurrent/concurrent-dictionary"),

            // system.collection.generic
            new TypeScriptRuntimeRequirementDefinition("IDictionary", "./system/collections/generic/dictionary.interface"),
            new TypeScriptRuntimeRequirementDefinition("Dictionary", "./system/collections/generic/dictionary"),
            new TypeScriptRuntimeRequirementDefinition("ICollection", "./system/collections/generic/icollection"),
            new TypeScriptRuntimeRequirementDefinition("IEqualityComparer", "./system/collections/generic/iequalitycomparer"),
            new TypeScriptRuntimeRequirementDefinition("IEnumerable", "./system/collections/generic/ienumerable"),
            new TypeScriptRuntimeRequirementDefinition("IReadOnlyDictionary", "./system/collections/generic/ireadonlydictionary"),
            new TypeScriptRuntimeRequirementDefinition("IReadOnlyList", "./system/collections/generic/ireadonlylist"),
            new TypeScriptRuntimeRequirementDefinition("List", "./system/collections/generic/list"),
            new TypeScriptRuntimeRequirementDefinition("KeyValuePair", "./system/collections/generic/key-value-pair"),
            new TypeScriptRuntimeRequirementDefinition("SortedList", "./system/collections/generic/sorted-list"),
            new TypeScriptRuntimeRequirementDefinition("Queue", "./system/collections/generic/queue"),
            new TypeScriptRuntimeRequirementDefinition("ReadOnlyCollection", "./system/collections/objectmodel/read-only-collection"),

            // system.drawing
            new TypeScriptRuntimeRequirementDefinition("Point", "./system/drawing/point"),
            new TypeScriptRuntimeRequirementDefinition("Rectangle", "./system/drawing/rectangle"),
            new TypeScriptRuntimeRequirementDefinition("Size", "./system/drawing/size"),

            // system.diagnostics
            new TypeScriptRuntimeRequirementDefinition("Debug", "./system/diagnostics/debug"),
            new TypeScriptRuntimeRequirementDefinition("Stopwatch", "./system/diagnostics/stopwatch"),

            // system.io
            new TypeScriptRuntimeRequirementDefinition("SeekOrigin", "./system/io/seek-origin"),
            new TypeScriptRuntimeRequirementDefinition("Stream", "./system/io/stream"),
            new TypeScriptRuntimeRequirementDefinition("MemoryStream", "./system/io/memory-stream"),
            new TypeScriptRuntimeRequirementDefinition("StreamWriter", "./system/io/stream-writer"),
            new TypeScriptRuntimeRequirementDefinition("BinaryReader", "./system/io/binary-reader"),
            new TypeScriptRuntimeRequirementDefinition("BinaryWriter", "./system/io/binary-writer"),
            new TypeScriptRuntimeRequirementDefinition("DirectoryInfo", "./system/io/directory-info"),
            new TypeScriptRuntimeRequirementDefinition("FileInfo", "./system/io/file-info"),
            new TypeScriptRuntimeRequirementDefinition("FileMode", "./system/io/file-mode"),
            new TypeScriptRuntimeRequirementDefinition("FileAccess", "./system/io/file-access"),
            new TypeScriptRuntimeRequirementDefinition("FileShare", "./system/io/file-share"),
            new TypeScriptRuntimeRequirementDefinition("FileStream", "./system/io/file-stream"),
            new TypeScriptRuntimeRequirementDefinition("Path", "./system/io/path"),
            new TypeScriptRuntimeRequirementDefinition("SearchOption", "./system/io/search-option"),
            new TypeScriptRuntimeRequirementDefinition("StreamReader", "./system/io/stream-reader"),
            new TypeScriptRuntimeRequirementDefinition("TextWriter", "./system/io/text-writer"),
            new TypeScriptRuntimeRequirementDefinition("DriveInfo", "./system/io/drive-info"),

            // system.net.sockets
            new TypeScriptRuntimeRequirementDefinition("TcpClient", "./system/net/sockets/tcp-client"),
            new TypeScriptRuntimeRequirementDefinition("TcpListener", "./system/net/sockets/tcp-listener"),

            // system.reflection
            new TypeScriptRuntimeRequirementDefinition("Type", "./src/reflection"),
            new TypeScriptRuntimeRequirementDefinition("BindingFlags", "./src/reflection"),
            new TypeScriptRuntimeRequirementDefinition("Activator", "./system/reflection/activator"),
            new TypeScriptRuntimeRequirementDefinition("PropertyInfo", "./src/reflection"),
            new TypeScriptRuntimeRequirementDefinition("Assembly", "./system/reflection/assembly"),
            new TypeScriptRuntimeRequirementDefinition("AssemblyName", "./system/reflection/assembly-name"),

            // system.security.cryptography
            new TypeScriptRuntimeRequirementDefinition("SHA256", "./system/security/cryptography/sha256"),
            new TypeScriptRuntimeRequirementDefinition("MD5", "./system/security/cryptography/md5"),
            new TypeScriptRuntimeRequirementDefinition("AesGcm", "./system/security/cryptography/aes-gcm"),
            new TypeScriptRuntimeRequirementDefinition("HashAlgorithm", "./system/security/cryptography/hash-algorithm"),
            new TypeScriptRuntimeRequirementDefinition("HMACSHA256", "./system/security/cryptography/hmac-sha256"),
            new TypeScriptRuntimeRequirementDefinition("Rfc2898DeriveBytes", "./system/security/cryptography/rfc-2898-derive-bytes"),
            new TypeScriptRuntimeRequirementDefinition("HashAlgorithmName", "./system/security/cryptography/hash-algorithm-name"),
            new TypeScriptRuntimeRequirementDefinition("RandomNumberGenerator", "./system/security/cryptography/random-number-generator"),
            new TypeScriptRuntimeRequirementDefinition("WebCryptoUtil", "./system/security/cryptography/web-crypto"),
            new TypeScriptRuntimeRequirementDefinition("ECParameters", "./system/security/cryptography/ec-parameters"),
            new TypeScriptRuntimeRequirementDefinition("ECDiffieHellman", "./system/security/cryptography/ec-diffie-helman"),
            new TypeScriptRuntimeRequirementDefinition("ECDiffieHellmanPublicKey", "./system/security/cryptography/ec-diffie-helman-public-key"),
            new TypeScriptRuntimeRequirementDefinition("ECCurve", "./system/security/cryptography/ec-curve"),
            new TypeScriptRuntimeRequirementDefinition("ECCurveType", "./system/security/cryptography/ec-curve-type"),
            new TypeScriptRuntimeRequirementDefinition("ECPoint", "./system/security/cryptography/ec-point"),
            new TypeScriptRuntimeRequirementDefinition("ECDsa", "./system/security/cryptography/ecdsa"),
            new TypeScriptRuntimeRequirementDefinition("CryptographicException", "./system/security/cryptography/cryptographic-exception"),

            // system.text
            new TypeScriptRuntimeRequirementDefinition("Encoding", "./system/text/encoding"),
            new TypeScriptRuntimeRequirementDefinition("Regex", "./system/text/regular-expressions/regex"),
            new TypeScriptRuntimeRequirementDefinition("RegexOptions", "./system/text/regular-expressions/regex-options"),
            new TypeScriptRuntimeRequirementDefinition("JsonSerializer", "./system/text/json/json-serializer"),
            new TypeScriptRuntimeRequirementDefinition("JsonSerializerOptions", "./system/text/json/json-serializer-options"),
            new TypeScriptRuntimeRequirementDefinition("JsonDocument", "./system/text/json/json-document"),
            new TypeScriptRuntimeRequirementDefinition("JsonDocumentOptions", "./system/text/json/json-document-options"),
            new TypeScriptRuntimeRequirementDefinition("JsonElement", "./system/text/json/json-element"),
            new TypeScriptRuntimeRequirementDefinition("JsonProperty", "./system/text/json/json-property"),
            new TypeScriptRuntimeRequirementDefinition("JsonValueKind", "./system/text/json/json-value-kind"),
            new TypeScriptRuntimeRequirementDefinition("Utf8JsonReader", "./system/text/json/utf8-json-reader"),
            new TypeScriptRuntimeRequirementDefinition("Utf8JsonWriter", "./system/text/json/utf8-json-writer"),
            new TypeScriptRuntimeRequirementDefinition("JsonWriterOptions", "./system/text/json/json-writer-options"),
            new TypeScriptRuntimeRequirementDefinition("JsonNumberHandling", "./system/text/json/json-number-handling"),
            new TypeScriptRuntimeRequirementDefinition("JsonIgnoreCondition", "./system/text/json/json-ignore-condition"),
            new TypeScriptRuntimeRequirementDefinition("JsonConverter", "./system/text/json/serialization/json-converter"),
            new TypeScriptRuntimeRequirementDefinition("JsonStringEnumConverter", "./system/text/json/serialization/json-string-enum-converter"),

            // system.threading
            new TypeScriptRuntimeRequirementDefinition("Thread", "./system/threading/thread"),
            new TypeScriptRuntimeRequirementDefinition("AutoResetEvent", "./system/threading/auto-reset-event"),
            new TypeScriptRuntimeRequirementDefinition("SynchronizationContext", "./system/threading/synchronization-context", "", true),
            new TypeScriptRuntimeRequirementDefinition("SendOrPostCallback", "./system/threading/send-or-post-callback"),

            // system.threading.tasks
            new TypeScriptRuntimeRequirementDefinition("Task", "./system/threading/tasks/task", "", true),

            // WebSocketSharp
            new TypeScriptRuntimeRequirementDefinition("WebSocketWS", "./websocketsharp/websocket"),
            new TypeScriptRuntimeRequirementDefinition("WebSocketState", "./websocketsharp/websocket-state"),
            new TypeScriptRuntimeRequirementDefinition("ErrorEventArgs", "./websocketsharp/error-event-args"),
            new TypeScriptRuntimeRequirementDefinition("MessageEventArgs", "./websocketsharp/message-event-args"),

            // Blake2B
            new TypeScriptRuntimeRequirementDefinition("Blake2b", "./blake2fast/blake2b"),

            // Newtonsoft.Json
            new TypeScriptRuntimeRequirementDefinition("JsonConvert", "./newtonsoft.json/jsonconvert")
        };

        /// <summary>
        /// Gets the Node.js-specific runtime requirement definitions.
        /// </summary>
        public static IReadOnlyList<TypeScriptRuntimeRequirementDefinition> NodeRequirements { get; } = new List<TypeScriptRuntimeRequirementDefinition> {
            new TypeScriptRuntimeRequirementDefinition("NodeDirectory", "./system/io/node-directory", "Directory"),
            new TypeScriptRuntimeRequirementDefinition("File", "./system/io/node-file"),
            new TypeScriptRuntimeRequirementDefinition("FileStream", "./system/io/node-file-stream")
        };

        /// <summary>
        /// Gets the web-specific runtime requirement definitions.
        /// </summary>
        public static IReadOnlyList<TypeScriptRuntimeRequirementDefinition> WebRequirements { get; } = new List<TypeScriptRuntimeRequirementDefinition> {
            new TypeScriptRuntimeRequirementDefinition("WebDirectory", "./system/io/web-directory", "Directory"),
            new TypeScriptRuntimeRequirementDefinition("File", "./system/io/file"),
            new TypeScriptRuntimeRequirementDefinition("FileStream", "./system/io/file-stream")
        };

        /// <summary>
        /// Returns the combined requirement definitions for the given environment.
        /// </summary>
        /// <param name="env">The target TypeScript runtime environment.</param>
        /// <returns>The ordered requirement definitions.</returns>
        public static IEnumerable<TypeScriptRuntimeRequirementDefinition> GetRequirements(TypeScriptEnvironment env) {
            for (int i = 0; i < BaseRequirements.Count; i++) {
                yield return BaseRequirements[i];
            }

            if (env == TypeScriptEnvironment.NodeJS) {
                for (int i = 0; i < NodeRequirements.Count; i++) {
                    yield return NodeRequirements[i];
                }
            } else if (env == TypeScriptEnvironment.Web) {
                for (int i = 0; i < WebRequirements.Count; i++) {
                    yield return WebRequirements[i];
                }
            }
        }
    }
}
