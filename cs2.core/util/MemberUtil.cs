using Microsoft.CodeAnalysis;

namespace cs2.core {
    public static class MemberUtil {
        public static bool IsAsync(SyntaxTokenList modifiers) {
            return modifiers.Any(c => c.ValueText == "async");
        }

        public static void GetModifiers(
            SyntaxTokenList modifiers,
            out bool isStatic,
            out bool isOverride,
            out MemberAccessType access,
            out MemberDeclarationType type) {
            access = MemberAccessType.Private;
            isStatic = false;
            isOverride = false;
            type = MemberDeclarationType.Class;

            foreach (SyntaxToken modifier in modifiers) {
                string value = (string)modifier.Value;

                switch (value) {
                    case "static": {
                            isStatic = true;
                    }
                        break;
                    case "private": {
                            access = MemberAccessType.Private;
                    }
                        break;
                    case "public": {
                            access = MemberAccessType.Public;
                    }
                        break;
                    case "protected": {
                            access = MemberAccessType.Protected;
                    }
                        break;
                    case "override": {
                            isOverride = true;
                    }
                        break;
                    case "abstract": {
                            type = MemberDeclarationType.Abstract;
                    }
                        break;
                    case "virtual": {
                            type = MemberDeclarationType.Virtual;
                    }
                        break;
                    case "const": {
                            isStatic = true;
                    }
                        break;
                }
            }
        }
    }
}
