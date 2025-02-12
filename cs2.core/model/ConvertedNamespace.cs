namespace cs2.core {
    public class ConvertedNamespace {
        public string Name { get; set; }
        public List<ConvertedNamespace> Children { get; set; }
        public List<ConvertedClass> Classes { get; set; }

        public ConvertedNamespace() {
            Children = new List<ConvertedNamespace>();
            Classes = new List<ConvertedClass>();
        }

        public override string ToString() {
            return Name;
        }
    }
}
