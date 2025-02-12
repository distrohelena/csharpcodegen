namespace cs2.core {
    public class ConvertedProgram {
        public List<ConvertedClass> Classes { get; private set; }
        public List<KnownClass> Requirements { get; private set; }
        public Dictionary<string, string> TypeMap { get; private set; }
        public ConversionRules Rules { get; private set; }

        public ConvertedProgram(ConversionRules rules) {
            Classes = new List<ConvertedClass>();
            Requirements = new List<KnownClass>();
            TypeMap = new Dictionary<string, string>();
            Rules = rules;
        }
    }
}
