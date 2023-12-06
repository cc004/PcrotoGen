using Mono.Cecil;

namespace PcrotoGen;

public class EnumType
{
    public string name;
    public Dictionary<string, int> values;

    public EnumType(TypeDefinition def)
    {
        name = def.Name;
        values = def.Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => (int)f.Constant);
    }
}