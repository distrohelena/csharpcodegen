using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Owns backend-emitter substitutions for specific generated function bodies whose implementation must vary by generic platform shape.
    /// </summary>
    public sealed class CPPGeneratedFunctionBodyOverrideCatalog {
        /// <summary>
        /// Writes a specialized generated function body when the active conversion settings require one.
        /// </summary>
        /// <param name="options">Active conversion options.</param>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="writer">Writer that receives the specialized body.</param>
        /// <returns><c>true</c> when a specialized body was emitted; otherwise <c>false</c>.</returns>
        public bool TryWriteOverride(CPPConversionOptions options, ConversionFunction function, TextWriter writer) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            } else if (function == null) {
                throw new ArgumentNullException(nameof(function));
            } else if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            if (options.PlatformProfile.GeneratedMathConvention == CPPGeneratedMathConventionKind.NativeColumnVector &&
                TryWriteNativeColumnVectorMatrixOverride(function, writer)) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Writes one native-column-vector specialization for shared matrix helper methods based on structural function shape rather than caller-owned type names.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="writer">Writer that receives the specialized body.</param>
        /// <returns><c>true</c> when a specialization was emitted; otherwise <c>false</c>.</returns>
        static bool TryWriteNativeColumnVectorMatrixOverride(ConversionFunction function, TextWriter writer) {
            if (IsCreateLookAtOverride(function)) {
                WriteLines(writer, [
                    "::float3 vector = float3::Normalize(cameraPosition - cameraTarget);",
                    "::float3 vector2 = float3::Normalize(float3::Cross(cameraUpVector, vector));",
                    "::float3 vector3 = float3::Cross(vector, vector2);",
                    "result.M11 = vector2.X;",
                    "result.M12 = vector3.X;",
                    "result.M13 = vector.X;",
                    "result.M14 = 0.0f;",
                    "result.M21 = vector2.Y;",
                    "result.M22 = vector3.Y;",
                    "result.M23 = vector.Y;",
                    "result.M24 = 0.0f;",
                    "result.M31 = vector2.Z;",
                    "result.M32 = vector3.Z;",
                    "result.M33 = vector.Z;",
                    "result.M34 = 0.0f;",
                    "result.M41 = -float3::Dot(vector2, cameraPosition);",
                    "result.M42 = -float3::Dot(vector3, cameraPosition);",
                    "result.M43 = -float3::Dot(vector, cameraPosition);",
                    "result.M44 = 1.0f;"
                ]);
                return true;
            } else if (IsCreateOrthographicOffCenterOverride(function)) {
                WriteLines(writer, [
                    "result.M11 = static_cast<float>((2.0 / (static_cast<double>(right) - static_cast<double>(left))));",
                    "result.M12 = 0.0f;",
                    "result.M13 = 0.0f;",
                    "result.M14 = 0.0f;",
                    "result.M21 = 0.0f;",
                    "result.M22 = static_cast<float>((2.0 / (static_cast<double>(top) - static_cast<double>(bottom))));",
                    "result.M23 = 0.0f;",
                    "result.M24 = 0.0f;",
                    "result.M31 = 0.0f;",
                    "result.M32 = 0.0f;",
                    "result.M33 = static_cast<float>((1.0 / (static_cast<double>(zNearPlane) - static_cast<double>(zFarPlane))));",
                    "result.M34 = 0.0f;",
                    "result.M41 = static_cast<float>(((static_cast<double>(left) + static_cast<double>(right)) / (static_cast<double>(left) - static_cast<double>(right))));",
                    "result.M42 = static_cast<float>(((static_cast<double>(top) + static_cast<double>(bottom)) / (static_cast<double>(bottom) - static_cast<double>(top))));",
                    "result.M43 = static_cast<float>((static_cast<double>(zNearPlane) / (static_cast<double>(zNearPlane) - static_cast<double>(zFarPlane))));",
                    "result.M44 = 1.0f;"
                ]);
                return true;
            } else if (IsCreatePerspectiveFieldOfViewOverride(function)) {
                WriteLines(writer, [
                    "if ((fieldOfView <= 0.0f) || (fieldOfView >= 3.141593f))",
                    "{",
                    "throw new ArgumentException(\"fieldOfView <= 0 or >= PI\");",
                    "}",
                    "if (nearPlaneDistance <= 0.0f)",
                    "{",
                    "throw new ArgumentException(\"nearPlaneDistance <= 0\");",
                    "}",
                    "if (farPlaneDistance <= 0.0f)",
                    "{",
                    "throw new ArgumentException(\"farPlaneDistance <= 0\");",
                    "}",
                    "if (nearPlaneDistance >= farPlaneDistance)",
                    "{",
                    "throw new ArgumentException(\"nearPlaneDistance >= farPlaneDistance\");",
                    "}",
                    "float yScale = 1.0f / static_cast<float>(Math::Tan(static_cast<double>(fieldOfView) * 0.5f));",
                    "float xScale = yScale / aspectRatio;",
                    "result.M11 = xScale;",
                    "result.M12 = 0.0f;",
                    "result.M13 = 0.0f;",
                    "result.M14 = 0.0f;",
                    "result.M21 = 0.0f;",
                    "result.M22 = yScale;",
                    "result.M23 = 0.0f;",
                    "result.M24 = 0.0f;",
                    "result.M31 = 0.0f;",
                    "result.M32 = 0.0f;",
                    "result.M33 = Number::IsPositiveInfinity(farPlaneDistance) ? 0.0f : nearPlaneDistance / (nearPlaneDistance - farPlaneDistance);",
                    "result.M34 = nearPlaneDistance * (farPlaneDistance / (nearPlaneDistance - farPlaneDistance));",
                    "result.M41 = 0.0f;",
                    "result.M42 = 0.0f;",
                    "result.M43 = -1.0f;",
                    "result.M44 = 0.0f;"
                ]);
                return true;
            } else if (IsCreateTranslationScalarOverride(function)) {
                WriteLines(writer, [
                    "result.M11 = 1;",
                    "result.M12 = 0;",
                    "result.M13 = 0;",
                    "result.M14 = x;",
                    "result.M21 = 0;",
                    "result.M22 = 1;",
                    "result.M23 = 0;",
                    "result.M24 = y;",
                    "result.M31 = 0;",
                    "result.M32 = 0;",
                    "result.M33 = 1;",
                    "result.M34 = z;",
                    "result.M41 = 0;",
                    "result.M42 = 0;",
                    "result.M43 = 0;",
                    "result.M44 = 1;"
                ]);
                return true;
            } else if (IsCreateTranslationVectorOverride(function)) {
                WriteLines(writer, [
                    "result.M11 = 1;",
                    "result.M12 = 0;",
                    "result.M13 = 0;",
                    "result.M14 = position.X;",
                    "result.M21 = 0;",
                    "result.M22 = 1;",
                    "result.M23 = 0;",
                    "result.M24 = position.Y;",
                    "result.M31 = 0;",
                    "result.M32 = 0;",
                    "result.M33 = 1;",
                    "result.M34 = position.Z;",
                    "result.M41 = 0;",
                    "result.M42 = 0;",
                    "result.M43 = 0;",
                    "result.M44 = 1;"
                ]);
                return true;
            } else if (IsMultiplyOverride(function)) {
                WriteLines(writer, [
                    "float m11 = (((matrix2.M11 * matrix1.M11) + (matrix2.M12 * matrix1.M21)) + (matrix2.M13 * matrix1.M31)) + (matrix2.M14 * matrix1.M41);",
                    "float m12 = (((matrix2.M11 * matrix1.M12) + (matrix2.M12 * matrix1.M22)) + (matrix2.M13 * matrix1.M32)) + (matrix2.M14 * matrix1.M42);",
                    "float m13 = (((matrix2.M11 * matrix1.M13) + (matrix2.M12 * matrix1.M23)) + (matrix2.M13 * matrix1.M33)) + (matrix2.M14 * matrix1.M43);",
                    "float m14 = (((matrix2.M11 * matrix1.M14) + (matrix2.M12 * matrix1.M24)) + (matrix2.M13 * matrix1.M34)) + (matrix2.M14 * matrix1.M44);",
                    "float m21 = (((matrix2.M21 * matrix1.M11) + (matrix2.M22 * matrix1.M21)) + (matrix2.M23 * matrix1.M31)) + (matrix2.M24 * matrix1.M41);",
                    "float m22 = (((matrix2.M21 * matrix1.M12) + (matrix2.M22 * matrix1.M22)) + (matrix2.M23 * matrix1.M32)) + (matrix2.M24 * matrix1.M42);",
                    "float m23 = (((matrix2.M21 * matrix1.M13) + (matrix2.M22 * matrix1.M23)) + (matrix2.M23 * matrix1.M33)) + (matrix2.M24 * matrix1.M43);",
                    "float m24 = (((matrix2.M21 * matrix1.M14) + (matrix2.M22 * matrix1.M24)) + (matrix2.M23 * matrix1.M34)) + (matrix2.M24 * matrix1.M44);",
                    "float m31 = (((matrix2.M31 * matrix1.M11) + (matrix2.M32 * matrix1.M21)) + (matrix2.M33 * matrix1.M31)) + (matrix2.M34 * matrix1.M41);",
                    "float m32 = (((matrix2.M31 * matrix1.M12) + (matrix2.M32 * matrix1.M22)) + (matrix2.M33 * matrix1.M32)) + (matrix2.M34 * matrix1.M42);",
                    "float m33 = (((matrix2.M31 * matrix1.M13) + (matrix2.M32 * matrix1.M23)) + (matrix2.M33 * matrix1.M33)) + (matrix2.M34 * matrix1.M43);",
                    "float m34 = (((matrix2.M31 * matrix1.M14) + (matrix2.M32 * matrix1.M24)) + (matrix2.M33 * matrix1.M34)) + (matrix2.M34 * matrix1.M44);",
                    "float m41 = (((matrix2.M41 * matrix1.M11) + (matrix2.M42 * matrix1.M21)) + (matrix2.M43 * matrix1.M31)) + (matrix2.M44 * matrix1.M41);",
                    "float m42 = (((matrix2.M41 * matrix1.M12) + (matrix2.M42 * matrix1.M22)) + (matrix2.M43 * matrix1.M32)) + (matrix2.M44 * matrix1.M42);",
                    "float m43 = (((matrix2.M41 * matrix1.M13) + (matrix2.M42 * matrix1.M23)) + (matrix2.M43 * matrix1.M33)) + (matrix2.M44 * matrix1.M43);",
                    "float m44 = (((matrix2.M41 * matrix1.M14) + (matrix2.M42 * matrix1.M24)) + (matrix2.M43 * matrix1.M34)) + (matrix2.M44 * matrix1.M44);",
                    "result.M11 = m11;",
                    "result.M12 = m12;",
                    "result.M13 = m13;",
                    "result.M14 = m14;",
                    "result.M21 = m21;",
                    "result.M22 = m22;",
                    "result.M23 = m23;",
                    "result.M24 = m24;",
                    "result.M31 = m31;",
                    "result.M32 = m32;",
                    "result.M33 = m33;",
                    "result.M34 = m34;",
                    "result.M41 = m41;",
                    "result.M42 = m42;",
                    "result.M43 = m43;",
                    "result.M44 = m44;"
                ]);
                return true;
            } else if (IsCreateFromQuaternionOverride(function)) {
                WriteLines(writer, [
                    "float num9 = quaternion.X * quaternion.X;",
                    "float num8 = quaternion.Y * quaternion.Y;",
                    "float num7 = quaternion.Z * quaternion.Z;",
                    "float num6 = quaternion.X * quaternion.Y;",
                    "float num5 = quaternion.Z * quaternion.W;",
                    "float num4 = quaternion.Z * quaternion.X;",
                    "float num3 = quaternion.Y * quaternion.W;",
                    "float num2 = quaternion.Y * quaternion.Z;",
                    "float num = quaternion.X * quaternion.W;",
                    "result.M11 = 1.0f - (2.0f * (num8 + num7));",
                    "result.M12 = 2.0f * (num6 + num5);",
                    "result.M13 = 2.0f * (num4 - num3);",
                    "result.M14 = 0.0f;",
                    "result.M21 = 2.0f * (num6 - num5);",
                    "result.M22 = 1.0f - (2.0f * (num7 + num9));",
                    "result.M23 = 2.0f * (num2 + num);",
                    "result.M24 = 0.0f;",
                    "result.M31 = 2.0f * (num4 + num3);",
                    "result.M32 = 2.0f * (num2 - num);",
                    "result.M33 = 1.0f - (2.0f * (num8 + num9));",
                    "result.M34 = 0.0f;",
                    "result.M41 = 0.0f;",
                    "result.M42 = 0.0f;",
                    "result.M43 = 0.0f;",
                    "result.M44 = 1.0f;"
                ]);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether one generated function matches the structural matrix look-at helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the look-at helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreateLookAtOverride(ConversionFunction function) {
            if (!HasFunctionName(function, "CreateLookAt") ||
                !TryGetOutParameterTypeName(function, 3, out string matrixTypeName)) {
                return false;
            }

            return HasParameter(function, 0, ParameterModifier.Ref, out string vectorTypeName) &&
                HasParameter(function, 1, ParameterModifier.Ref, vectorTypeName) &&
                HasParameter(function, 2, ParameterModifier.Ref, vectorTypeName) &&
                HasParameter(function, 3, ParameterModifier.Out, matrixTypeName);
        }

        /// <summary>
        /// Determines whether one generated function matches the structural matrix orthographic helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the orthographic helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreateOrthographicOffCenterOverride(ConversionFunction function) {
            return HasFunctionName(function, "CreateOrthographicOffCenter") &&
                IsFloatScalarParameter(function, 0) &&
                IsFloatScalarParameter(function, 1) &&
                IsFloatScalarParameter(function, 2) &&
                IsFloatScalarParameter(function, 3) &&
                IsFloatScalarParameter(function, 4) &&
                IsFloatScalarParameter(function, 5) &&
                HasOutParameter(function, 6);
        }

        /// <summary>
        /// Determines whether one generated function matches the structural matrix perspective helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the perspective helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreatePerspectiveFieldOfViewOverride(ConversionFunction function) {
            return HasFunctionName(function, "CreatePerspectiveFieldOfView") &&
                IsFloatScalarParameter(function, 0) &&
                IsFloatScalarParameter(function, 1) &&
                IsFloatScalarParameter(function, 2) &&
                IsFloatScalarParameter(function, 3) &&
                HasOutParameter(function, 4);
        }

        /// <summary>
        /// Determines whether one generated function matches the scalar translation helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the scalar translation helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreateTranslationScalarOverride(ConversionFunction function) {
            return HasFunctionName(function, "CreateTranslation") &&
                IsFloatScalarParameter(function, 0) &&
                IsFloatScalarParameter(function, 1) &&
                IsFloatScalarParameter(function, 2) &&
                HasOutParameter(function, 3);
        }

        /// <summary>
        /// Determines whether one generated function matches the vector translation helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the vector translation helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreateTranslationVectorOverride(ConversionFunction function) {
            return HasFunctionName(function, "CreateTranslation") &&
                HasParameter(function, 0, ParameterModifier.Ref, out _) &&
                HasOutParameter(function, 1);
        }

        /// <summary>
        /// Determines whether one generated function matches the matrix multiply helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the matrix multiply helper shape; otherwise <c>false</c>.</returns>
        static bool IsMultiplyOverride(ConversionFunction function) {
            if (!HasFunctionName(function, "Multiply") ||
                !TryGetOutParameterTypeName(function, 2, out string matrixTypeName)) {
                return false;
            }

            return HasParameter(function, 0, ParameterModifier.Ref, matrixTypeName) &&
                HasParameter(function, 1, ParameterModifier.Ref, matrixTypeName) &&
                HasParameter(function, 2, ParameterModifier.Out, matrixTypeName);
        }

        /// <summary>
        /// Determines whether one generated function matches the quaternion conversion helper contract.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <returns><c>true</c> when the function matches the quaternion conversion helper shape; otherwise <c>false</c>.</returns>
        static bool IsCreateFromQuaternionOverride(ConversionFunction function) {
            return HasFunctionName(function, "CreateFromQuaternion") &&
                HasParameter(function, 0, ParameterModifier.Ref, out _) &&
                HasOutParameter(function, 1);
        }

        /// <summary>
        /// Determines whether one generated function matches the supplied function name and parameter count.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="expectedName">Expected source method name.</param>
        /// <returns><c>true</c> when the function name and required parameters match; otherwise <c>false</c>.</returns>
        static bool HasFunctionName(ConversionFunction function, string expectedName) {
            return function != null &&
                string.Equals(function.Name, expectedName, StringComparison.Ordinal) &&
                function.InParameters != null;
        }

        /// <summary>
        /// Determines whether one parameter is a scalar float input.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <returns><c>true</c> when the parameter is one by-value float input; otherwise <c>false</c>.</returns>
        static bool IsFloatScalarParameter(ConversionFunction function, int index) {
            if (!TryGetParameter(function, index, out ConversionVariable parameter) ||
                parameter.Modifier != ParameterModifier.None) {
                return false;
            }

            string typeName = GetCanonicalTypeName(parameter.VarType);
            return string.Equals(typeName, "float", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Single", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether one parameter exists with the requested modifier and captures its canonical type name.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <param name="modifier">Expected parameter modifier.</param>
        /// <param name="typeName">Resolved canonical type name when the parameter matches.</param>
        /// <returns><c>true</c> when the parameter matches; otherwise <c>false</c>.</returns>
        static bool HasParameter(ConversionFunction function, int index, ParameterModifier modifier, out string typeName) {
            typeName = string.Empty;
            if (!TryGetParameter(function, index, out ConversionVariable parameter) ||
                parameter.Modifier != modifier) {
                return false;
            }

            typeName = GetCanonicalTypeName(parameter.VarType);
            return !string.IsNullOrWhiteSpace(typeName);
        }

        /// <summary>
        /// Determines whether one parameter exists with the requested modifier and canonical type name.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <param name="modifier">Expected parameter modifier.</param>
        /// <param name="expectedTypeName">Expected canonical type name.</param>
        /// <returns><c>true</c> when the parameter matches; otherwise <c>false</c>.</returns>
        static bool HasParameter(ConversionFunction function, int index, ParameterModifier modifier, string expectedTypeName) {
            return HasParameter(function, index, modifier, out string resolvedTypeName) &&
                string.Equals(resolvedTypeName, expectedTypeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether one parameter exists as an out parameter and captures its canonical type name.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <param name="typeName">Resolved canonical type name when the parameter matches.</param>
        /// <returns><c>true</c> when the out parameter matches; otherwise <c>false</c>.</returns>
        static bool TryGetOutParameterTypeName(ConversionFunction function, int index, out string typeName) {
            return HasParameter(function, index, ParameterModifier.Out, out typeName);
        }

        /// <summary>
        /// Determines whether one parameter exists as an out parameter.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <returns><c>true</c> when the out parameter matches; otherwise <c>false</c>.</returns>
        static bool HasOutParameter(ConversionFunction function, int index) {
            return HasParameter(function, index, ParameterModifier.Out, out _);
        }

        /// <summary>
        /// Attempts to resolve one parameter by index.
        /// </summary>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="index">Zero-based parameter index.</param>
        /// <param name="parameter">Resolved parameter when the lookup succeeds.</param>
        /// <returns><c>true</c> when the parameter exists; otherwise <c>false</c>.</returns>
        static bool TryGetParameter(ConversionFunction function, int index, out ConversionVariable parameter) {
            parameter = null;
            if (function?.InParameters == null ||
                index < 0 ||
                index >= function.InParameters.Count) {
                return false;
            }

            parameter = function.InParameters[index];
            return parameter != null && parameter.VarType != null;
        }

        /// <summary>
        /// Resolves one canonical source type name for structural override matching.
        /// </summary>
        /// <param name="type">Type to normalize.</param>
        /// <returns>Canonical qualified type name when available; otherwise the simple source type name.</returns>
        static string GetCanonicalTypeName(VariableType type) {
            if (type == null) {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(type.QualifiedTypeName)) {
                return type.QualifiedTypeName;
            }

            return type.TypeName ?? string.Empty;
        }

        /// <summary>
        /// Writes one sequence of specialized body lines to the destination writer.
        /// </summary>
        /// <param name="writer">Writer that receives the specialized body.</param>
        /// <param name="lines">Lines that compose the specialized function body.</param>
        static void WriteLines(TextWriter writer, IEnumerable<string> lines) {
            foreach (string line in lines) {
                writer.WriteLine(line);
            }
        }
    }
}
