using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace cs2.ts.util {
    /// <summary>
    /// Formats TypeScript source through the TypeScript 7 native language server (<c>tsc --lsp --stdio</c>).
    /// </summary>
    /// <remarks>
    /// TypeScript 7 ships no JavaScript language-service API; formatting is only exposed through the language
    /// server's <c>textDocument/formatting</c> request, so this class runs a minimal hidden JSON-RPC session:
    /// initialize, open the document as an untitled buffer, request formatting, then shutdown and exit.
    /// </remarks>
    public static class TypeScriptLanguageServerFormatter {
        /// <summary>
        /// The document URI used for the formatted buffer; untitled documents do not make the server watch disk folders.
        /// </summary>
        const string DocumentUri = "untitled:cs2-generated.ts";

        /// <summary>
        /// The time allowed for the server to exit after the exit notification before it is killed.
        /// </summary>
        static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Formats the source with the TypeScript 7 language server using four-space indentation.
        /// </summary>
        /// <param name="executablePath">The TypeScript 7 native executable.</param>
        /// <param name="source">The raw TypeScript source.</param>
        /// <param name="workingDirectory">The working directory for the server process.</param>
        /// <param name="timeout">The maximum time the whole formatting session may take.</param>
        /// <param name="logger">Optional logger that receives the server's stderr when formatting fails.</param>
        /// <returns>The formatted source.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the server cannot be started or formatting fails.</exception>
        public static string Format(string executablePath, string source, string workingDirectory, TimeSpan timeout, Action<string> logger) {
            if (string.IsNullOrWhiteSpace(executablePath)) {
                throw new ArgumentException("The TypeScript 7 executable path is required.", nameof(executablePath));
            }
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            StringBuilder diagnostics = new StringBuilder();
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = executablePath,
                Arguments = "--lsp --stdio",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory)
                    ? Path.GetTempPath()
                    : workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process;
            try {
                process = Process.Start(startInfo);
            } catch (Exception ex) {
                throw new InvalidOperationException($"TypeScript 7 formatter failed to start '{executablePath}': {ex.Message}", ex);
            }
            if (process == null) {
                throw new InvalidOperationException($"TypeScript 7 formatter failed to start '{executablePath}'.");
            }

            using (process) {
                process.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) {
                        AppendDiagnostic(diagnostics, e.Data);
                    }
                };
                process.BeginErrorReadLine();

                Task<string> session = Task.Run(() => RunSession(process, source, diagnostics));
                Exception failure = null;
                try {
                    if (!session.Wait(timeout)) {
                        failure = new TimeoutException($"the language server did not finish within {timeout.TotalSeconds:0} seconds");
                    }
                } catch (AggregateException ex) {
                    failure = ex.InnerException ?? ex;
                }

                if (failure == null) {
                    if (!process.WaitForExit((int)ExitGracePeriod.TotalMilliseconds)) {
                        TryKill(process);
                    }
                    return session.Result;
                }

                TryKill(process);
                process.WaitForExit((int)ExitGracePeriod.TotalMilliseconds);
                string captured;
                lock (diagnostics) {
                    captured = diagnostics.ToString();
                }
                if (!string.IsNullOrWhiteSpace(captured)) {
                    logger?.Invoke(captured.TrimEnd());
                }
                throw new InvalidOperationException($"TypeScript 7 formatter failed ({executablePath}): {failure.Message}", failure);
            }
        }

        /// <summary>
        /// Applies language-server edits to the source in one linear pass.
        /// </summary>
        /// <param name="source">The original source.</param>
        /// <param name="edits">The edits, in any order, addressed with LSP positions.</param>
        /// <returns>The edited source.</returns>
        /// <exception cref="InvalidOperationException">Thrown when edits overlap.</exception>
        public static string ApplyEdits(string source, IReadOnlyList<TypeScriptTextEdit> edits) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }
            if (edits == null || edits.Count == 0) {
                return source;
            }

            List<int> lineStarts = ComputeLineStarts(source);
            (int Start, int End, int Index)[] spans = new (int Start, int End, int Index)[edits.Count];
            int growth = 0;
            for (int i = 0; i < edits.Count; i++) {
                TypeScriptTextEdit edit = edits[i];
                int start = ToOffset(source, lineStarts, edit.StartLine, edit.StartCharacter);
                int end = ToOffset(source, lineStarts, edit.EndLine, edit.EndCharacter);
                if (end < start) {
                    throw new InvalidOperationException($"TypeScript 7 formatter returned an inverted edit range at line {edit.StartLine}.");
                }
                spans[i] = (start, end, i);
                growth += edit.NewText.Length;
            }
            Array.Sort(spans, (left, right) => left.Start != right.Start ? left.Start.CompareTo(right.Start) : left.Index.CompareTo(right.Index));

            StringBuilder builder = new StringBuilder(source.Length + growth);
            int position = 0;
            for (int i = 0; i < spans.Length; i++) {
                (int start, int end, int index) = spans[i];
                if (start < position) {
                    throw new InvalidOperationException($"TypeScript 7 formatter returned overlapping edits at offset {start}.");
                }
                builder.Append(source, position, start - position);
                builder.Append(edits[index].NewText);
                position = end;
            }
            builder.Append(source, position, source.Length - position);
            return builder.ToString();
        }

        /// <summary>
        /// Computes LSP line start offsets, treating CRLF, lone CR and LF as line breaks.
        /// </summary>
        /// <param name="source">The document text.</param>
        /// <returns>The offset where each line starts.</returns>
        static List<int> ComputeLineStarts(string source) {
            List<int> lineStarts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++) {
                char current = source[i];
                if (current == '\r') {
                    if (i + 1 < source.Length && source[i + 1] == '\n') {
                        i++;
                    }
                    lineStarts.Add(i + 1);
                } else if (current == '\n') {
                    lineStarts.Add(i + 1);
                }
            }
            return lineStarts;
        }

        /// <summary>
        /// Converts an LSP position to a source offset, clamping positions past a line or the document end.
        /// </summary>
        /// <param name="source">The document text.</param>
        /// <param name="lineStarts">The line start offsets.</param>
        /// <param name="line">The zero-based line.</param>
        /// <param name="character">The UTF-16 offset within the line.</param>
        /// <returns>The source offset.</returns>
        static int ToOffset(string source, List<int> lineStarts, int line, int character) {
            if (line < 0 || character < 0) {
                throw new InvalidOperationException($"TypeScript 7 formatter returned a negative position ({line}:{character}).");
            }
            if (line >= lineStarts.Count) {
                return source.Length;
            }

            int lineLimit = line + 1 < lineStarts.Count ? lineStarts[line + 1] : source.Length;
            return Math.Min(lineStarts[line] + character, lineLimit);
        }

        /// <summary>
        /// Runs the JSON-RPC conversation and returns the formatted source.
        /// </summary>
        /// <param name="process">The running language server.</param>
        /// <param name="source">The raw TypeScript source.</param>
        /// <param name="diagnostics">Receives error-level server log messages.</param>
        /// <returns>The formatted source.</returns>
        static string RunSession(Process process, string source, StringBuilder diagnostics) {
            Stream input = process.StandardInput.BaseStream;
            BlockingCollection<JsonDocument> messages = new BlockingCollection<JsonDocument>();
            Exception readFailure = null;
            Thread reader = new Thread(() => {
                try {
                    Stream output = new BufferedStream(process.StandardOutput.BaseStream, 1 << 16);
                    while (true) {
                        JsonDocument message = ReadMessage(output);
                        if (message == null) {
                            break;
                        }
                        messages.Add(message);
                    }
                } catch (Exception ex) {
                    readFailure = ex;
                } finally {
                    messages.CompleteAdding();
                }
            }) {
                IsBackground = true,
                Name = "tsgo-lsp-reader"
            };
            reader.Start();

            int nextId = 1;
            int initializeId = nextId++;
            WriteMessage(input, writer => {
                WriteRequestHeader(writer, initializeId, "initialize");
                writer.WriteStartObject("params");
                writer.WriteNumber("processId", Environment.ProcessId);
                writer.WriteNull("rootUri");
                writer.WriteStartObject("capabilities");
                writer.WriteEndObject();
                writer.WriteEndObject();
            });
            WaitForResponse(messages, input, diagnostics, initializeId, "initialize", () => readFailure).Dispose();

            WriteMessage(input, writer => {
                WriteNotificationHeader(writer, "initialized");
                writer.WriteStartObject("params");
                writer.WriteEndObject();
            });
            WriteMessage(input, writer => {
                WriteNotificationHeader(writer, "textDocument/didOpen");
                writer.WriteStartObject("params");
                writer.WriteStartObject("textDocument");
                writer.WriteString("uri", DocumentUri);
                writer.WriteString("languageId", "typescript");
                writer.WriteNumber("version", 1);
                writer.WriteString("text", source);
                writer.WriteEndObject();
                writer.WriteEndObject();
            });

            int formattingId = nextId++;
            WriteMessage(input, writer => {
                WriteRequestHeader(writer, formattingId, "textDocument/formatting");
                writer.WriteStartObject("params");
                writer.WriteStartObject("textDocument");
                writer.WriteString("uri", DocumentUri);
                writer.WriteEndObject();
                writer.WriteStartObject("options");
                writer.WriteNumber("tabSize", 4);
                writer.WriteBoolean("insertSpaces", true);
                writer.WriteEndObject();
                writer.WriteEndObject();
            });

            List<TypeScriptTextEdit> edits;
            using (JsonDocument response = WaitForResponse(messages, input, diagnostics, formattingId, "textDocument/formatting", () => readFailure)) {
                edits = ReadEdits(response.RootElement.GetProperty("result"));
            }

            int shutdownId = nextId++;
            WriteMessage(input, writer => WriteRequestHeader(writer, shutdownId, "shutdown"));
            WaitForResponse(messages, input, diagnostics, shutdownId, "shutdown", () => readFailure).Dispose();
            WriteMessage(input, writer => WriteNotificationHeader(writer, "exit"));
            try {
                input.Close();
            } catch (IOException) {
            }

            return ApplyEdits(source, edits);
        }

        /// <summary>
        /// Waits for the response to a request, answering server requests and recording error logs meanwhile.
        /// </summary>
        /// <param name="messages">The incoming message queue.</param>
        /// <param name="input">The server's stdin.</param>
        /// <param name="diagnostics">Receives error-level server log messages.</param>
        /// <param name="id">The request id to wait for.</param>
        /// <param name="method">The request method, for error messages.</param>
        /// <param name="readFailure">Returns the reader thread's failure, if any.</param>
        /// <returns>The response message; the caller disposes it.</returns>
        static JsonDocument WaitForResponse(BlockingCollection<JsonDocument> messages, Stream input, StringBuilder diagnostics, int id, string method, Func<Exception> readFailure) {
            while (true) {
                if (!messages.TryTake(out JsonDocument message, Timeout.Infinite)) {
                    Exception failure = readFailure();
                    string reason = failure == null ? "the language server closed its output" : failure.Message;
                    throw new InvalidOperationException($"no response to '{method}': {reason}", failure);
                }

                JsonElement root = message.RootElement;
                bool hasId = root.TryGetProperty("id", out JsonElement idElement);
                bool hasMethod = root.TryGetProperty("method", out JsonElement methodElement);
                if (hasId && hasMethod) {
                    AnswerServerRequest(input, idElement, methodElement.GetString(), root);
                    message.Dispose();
                    continue;
                }

                if (!hasId) {
                    RecordLogMessage(diagnostics, methodElement, root);
                    message.Dispose();
                    continue;
                }

                if (idElement.ValueKind != JsonValueKind.Number || idElement.GetInt32() != id) {
                    message.Dispose();
                    continue;
                }

                if (root.TryGetProperty("error", out JsonElement error)) {
                    string errorText = error.TryGetProperty("message", out JsonElement errorMessage) ? errorMessage.GetString() : error.GetRawText();
                    message.Dispose();
                    throw new InvalidOperationException($"'{method}' returned an error: {errorText}");
                }

                return message;
            }
        }

        /// <summary>
        /// Answers a request sent by the server; configuration requests get null settings, everything else a null result.
        /// </summary>
        /// <param name="input">The server's stdin.</param>
        /// <param name="id">The server request id.</param>
        /// <param name="method">The server request method.</param>
        /// <param name="root">The full server request.</param>
        static void AnswerServerRequest(Stream input, JsonElement id, string method, JsonElement root) {
            WriteMessage(input, writer => {
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                id.WriteTo(writer);
                if (method == "workspace/configuration"
                    && root.TryGetProperty("params", out JsonElement parameters)
                    && parameters.TryGetProperty("items", out JsonElement items)
                    && items.ValueKind == JsonValueKind.Array) {
                    writer.WriteStartArray("result");
                    for (int i = 0; i < items.GetArrayLength(); i++) {
                        writer.WriteNullValue();
                    }
                    writer.WriteEndArray();
                } else {
                    writer.WriteNull("result");
                }
            });
        }

        /// <summary>
        /// Records error-level <c>window/logMessage</c> notifications so failures can report them.
        /// </summary>
        /// <param name="diagnostics">The diagnostic buffer.</param>
        /// <param name="method">The notification method.</param>
        /// <param name="root">The full notification.</param>
        static void RecordLogMessage(StringBuilder diagnostics, JsonElement method, JsonElement root) {
            if (method.ValueKind != JsonValueKind.String || method.GetString() != "window/logMessage") {
                return;
            }
            if (!root.TryGetProperty("params", out JsonElement parameters)
                || !parameters.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.Number
                || type.GetInt32() != 1
                || !parameters.TryGetProperty("message", out JsonElement text)) {
                return;
            }
            AppendDiagnostic(diagnostics, "[server error] " + text.GetString());
        }

        /// <summary>
        /// Reads the edits array of a formatting response.
        /// </summary>
        /// <param name="result">The response result, an array of TextEdit or null.</param>
        /// <returns>The parsed edits.</returns>
        static List<TypeScriptTextEdit> ReadEdits(JsonElement result) {
            List<TypeScriptTextEdit> edits = new List<TypeScriptTextEdit>();
            if (result.ValueKind == JsonValueKind.Null) {
                return edits;
            }
            if (result.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException($"'textDocument/formatting' returned {result.ValueKind} instead of an edit array.");
            }

            edits.Capacity = result.GetArrayLength();
            foreach (JsonElement edit in result.EnumerateArray()) {
                JsonElement range = edit.GetProperty("range");
                JsonElement start = range.GetProperty("start");
                JsonElement end = range.GetProperty("end");
                edits.Add(new TypeScriptTextEdit(
                    start.GetProperty("line").GetInt32(),
                    start.GetProperty("character").GetInt32(),
                    end.GetProperty("line").GetInt32(),
                    end.GetProperty("character").GetInt32(),
                    edit.GetProperty("newText").GetString()));
            }
            return edits;
        }

        /// <summary>
        /// Writes the common fields of a request.
        /// </summary>
        /// <param name="writer">The JSON writer positioned inside the message object.</param>
        /// <param name="id">The request id.</param>
        /// <param name="method">The request method.</param>
        static void WriteRequestHeader(Utf8JsonWriter writer, int id, string method) {
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
        }

        /// <summary>
        /// Writes the common fields of a notification.
        /// </summary>
        /// <param name="writer">The JSON writer positioned inside the message object.</param>
        /// <param name="method">The notification method.</param>
        static void WriteNotificationHeader(Utf8JsonWriter writer, string method) {
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
        }

        /// <summary>
        /// Serializes one message object and writes it with its Content-Length header.
        /// </summary>
        /// <param name="input">The server's stdin.</param>
        /// <param name="writeBody">Writes the message properties.</param>
        static void WriteMessage(Stream input, Action<Utf8JsonWriter> writeBody) {
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer)) {
                writer.WriteStartObject();
                writeBody(writer);
                writer.WriteEndObject();
            }

            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + buffer.WrittenCount.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");
            lock (input) {
                input.Write(header, 0, header.Length);
                input.Write(buffer.WrittenSpan);
                input.Flush();
            }
        }

        /// <summary>
        /// Reads one Content-Length framed message from the server's stdout.
        /// </summary>
        /// <param name="output">The buffered stdout stream.</param>
        /// <returns>The parsed message, or null at end of stream.</returns>
        static JsonDocument ReadMessage(Stream output) {
            int contentLength = -1;
            while (true) {
                string line = ReadHeaderLine(output);
                if (line == null) {
                    return null;
                }
                if (line.Length == 0) {
                    break;
                }

                int separator = line.IndexOf(':');
                if (separator > 0 && line.Substring(0, separator).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    contentLength = int.Parse(line.Substring(separator + 1).Trim(), CultureInfo.InvariantCulture);
                }
            }

            if (contentLength < 0) {
                throw new InvalidDataException("language server message is missing its Content-Length header");
            }

            byte[] body = new byte[contentLength];
            int read = 0;
            while (read < contentLength) {
                int count = output.Read(body, read, contentLength - read);
                if (count == 0) {
                    throw new EndOfStreamException("language server output ended inside a message");
                }
                read += count;
            }
            return JsonDocument.Parse(body);
        }

        /// <summary>
        /// Reads one CRLF-terminated ASCII header line.
        /// </summary>
        /// <param name="output">The buffered stdout stream.</param>
        /// <returns>The line without its terminator, or null at end of stream before any byte.</returns>
        static string ReadHeaderLine(Stream output) {
            StringBuilder line = new StringBuilder();
            while (true) {
                int value = output.ReadByte();
                if (value < 0) {
                    if (line.Length == 0) {
                        return null;
                    }
                    throw new EndOfStreamException("language server output ended inside a header");
                }
                if (value == '\n') {
                    if (line.Length > 0 && line[line.Length - 1] == '\r') {
                        line.Length--;
                    }
                    return line.ToString();
                }
                line.Append((char)value);
            }
        }

        /// <summary>
        /// Appends one line to the shared diagnostic buffer.
        /// </summary>
        /// <param name="diagnostics">The diagnostic buffer.</param>
        /// <param name="line">The line to append.</param>
        static void AppendDiagnostic(StringBuilder diagnostics, string line) {
            lock (diagnostics) {
                diagnostics.AppendLine(line);
            }
        }

        /// <summary>
        /// Kills the server process tree, ignoring failures for processes that already exited.
        /// </summary>
        /// <param name="process">The server process.</param>
        static void TryKill(Process process) {
            try {
                if (!process.HasExited) {
                    process.Kill(true);
                }
            } catch {
            }
        }
    }
}
