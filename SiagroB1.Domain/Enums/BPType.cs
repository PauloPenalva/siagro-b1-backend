namespace SiagroB1.Domain.Enums;

public class BPType
{
    private BPType(string value) { Value = value; }
    
    public string Value { get; private set; }
    
    public static BPType Customer => new("C");
    public static BPType Vendor => new("S");

    public override string ToString()
    {
        return Value;
    }
}