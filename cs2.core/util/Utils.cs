using System.Text.RegularExpressions;

namespace cs2.core {
    public class Utils {
        public static bool IsNumber(string str) {
            switch (str) {
                case "int":
                case "uint":
                case "double":
                case "float":
                case "Int32":
                case "UInt32":
                    return true;
            }
            return false;
        }

        public static bool IsBoolean(string str) {
            return str == "bool";
        }

        public static bool IsNativeType(string str) {
            switch (str) {
                case "uint":
                case "double":
                case "float":
                    return true;
            }
            return false;
        }

        public static string ReplacePlaceholders(string input, Dictionary<string, string> replacements) {
            // Define the regex pattern for placeholders
            string pattern = @"\$\w+\$";

            // Use Regex to replace placeholders
            return Regex.Replace(input, pattern, match => {
                // Extract the placeholder (e.g., $ASSEMBLY_NAME$)
                string key = match.Value;
                key = key.Substring(1, key.Length - 2);

                // Check if the placeholder exists in the replacements dictionary
                if (replacements.TryGetValue(key, out string? value)) {
                    return value; // Replace with the corresponding value
                }

                // If no replacement is found, return the placeholder as is
                return key;
            });
        }

    }
}
