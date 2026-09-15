/// <summary>Exercises provider strings and fatal guards through actual C# conversion.</summary>
public class StringGate {
    /// <summary>Stores a managed string that must use the selected native storage.</summary>
    public string Name;

    /// <summary>Initializes the string value used by the generated behavior checks.</summary>
    public StringGate(string name) {
        Name = name;
    }

    /// <summary>Exercises managed concatenation across a generated method boundary.</summary>
    public string Join(string suffix) {
        return Name + suffix;
    }

    /// <summary>Raises a fatal failure for a null borrowed value when exceptions are disabled.</summary>
    public void Require(object value) {
        object checkedValue = value ?? throw new System.InvalidOperationException("required");
    }

    /// <summary>Exercises direct exception construction and fatal lowering.</summary>
    public void Fail() {
        throw new System.Exception("failure");
    }

    /// <summary>Exercises interpolation and numeric string conversion.</summary>
    public string Format(int value) {
        return $"{Name}:{value}";
    }

    /// <summary>Exercises string switch lowering without runtime type information.</summary>
    public int Classify(string value) {
        return value switch {
            "x" => 1,
            _ => 0
        };
    }
}
