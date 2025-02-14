class MemoryStreamTest {
    public static void Main() {
        // write binary data
        using (MemoryStream stream = new MemoryStream()) {
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(42);// int
            writer.Write(3.14f);// float
            writer.Write(123456789L);// long
            writer.Write("Hello, world!");// string

            stream.Position = 0;

            using BinaryReader reader = new BinaryReader(stream);
            int intValue = reader.ReadInt32();
            float floatValue = reader.ReadSingle();
            long longValue = reader.ReadInt64();
            string stringValue = reader.ReadString();

            Console.WriteLine($"Int: {intValue}");
            Console.WriteLine($"Float: {floatValue}");
            Console.WriteLine($"Long: {longValue}");
            Console.WriteLine($"String: {stringValue}");
        }
    }
}
