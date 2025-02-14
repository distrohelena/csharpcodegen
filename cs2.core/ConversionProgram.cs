namespace cs2.core {
    public class ConversionProgram {
        public List<ConversionClass> Classes { get; private set; }
        public Dictionary<string, string> TypeMap { get; private set; }
        public ConversionRules Rules { get; private set; }

        public ConversionProgram(ConversionRules rules) {
            Classes = new List<ConversionClass>();
            TypeMap = new Dictionary<string, string>();
            Rules = rules;
        }
    }
}
