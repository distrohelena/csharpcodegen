namespace cs2.core {
    public class ConversionNamespace {
        public string Name { get; set; }
        public List<ConversionNamespace> Children { get; set; }
        public List<ConversionClass> Classes { get; set; }

        public ConversionNamespace() {
            Children = new List<ConversionNamespace>();
            Classes = new List<ConversionClass>();
        }

        public override string ToString() {
            return Name;
        }
    }
}
