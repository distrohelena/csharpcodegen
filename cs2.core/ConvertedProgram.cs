namespace cs2.core {
    public class ConvertedProgram {
        public List<ConvertedClass> Classes { get; private set; }
        public Dictionary<string, string> TypeMap { get; private set; }
        public ConversionRules Rules { get; private set; }

        public ConvertedProgram(ConversionRules rules) {
            Classes = new List<ConvertedClass>();
            TypeMap = new Dictionary<string, string>();
            Rules = rules;
        }
    }
}
