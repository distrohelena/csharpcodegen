class Program {
    static void Main() {
        string filePath = "testdata.bin";

        if (File.Exists(filePath)) {
            File.Delete(filePath);
        }

        // write binary data
        using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create))) {
            writer.Write(42);// int
            writer.Write(3.14f);// float
            writer.Write(123456789012345L);// long
            writer.Write("Hello, world!");// string
        }

        // read binary data
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open))) {
            int intValue = reader.ReadInt32();
            float floatValue = reader.ReadSingle();
            long longValue = reader.ReadInt64();
            string stringValue = reader.ReadString();

            Console.WriteLine($"Int: {intValue}");
            Console.WriteLine($"Float: {floatValue}");
            Console.WriteLine($"Long: {longValue}");
            Console.WriteLine($"String: {stringValue}");
        }

#if !TYPESCRIPT
        Console.ReadLine();
#endif
    }
}
