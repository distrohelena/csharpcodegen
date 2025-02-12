namespace cs2.core {
    public class ConversionRules {
        public List<string> IgnoredNamespaces { get; private set; }
        public List<string> IgnoredClasses { get; private set; }

        public ConversionRules() {
            IgnoredNamespaces = new List<string>();
            IgnoredClasses = new List<string>();
        }
    }
}
