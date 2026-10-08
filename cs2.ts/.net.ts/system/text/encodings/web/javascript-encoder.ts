// @ts-nocheck

/**
 * Browser JSON uses JavaScript's native string escaping. Its default output leaves HTML-significant
 * characters unescaped, which matches System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping.
 */
export class JavaScriptEncoder {
    private constructor() {
    }

    public static readonly UnsafeRelaxedJsonEscaping = new JavaScriptEncoder();
}
