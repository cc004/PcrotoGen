using Mono.Cecil;

namespace PcrotoGen;

public class ClassType
{
    public string name;
    public Dictionary<string, FieldType> fields;

    public static Dictionary<string, string> nameReplacementDict = new ();

    public static IEnumerable<TypeReference> FromType(TypeDefinition def, ClassType result)
    {
        result.name = def.Name;
        result.fields = new Dictionary<string, FieldType>();
        foreach (var (type, name) in def.Properties.Where(p => !p.GetMethod.IsStatic && p.GetMethod.IsPublic)
                         .Select(p => (p.PropertyType, p.Name)))
        {
            var field = new FieldType();
            foreach (var child in FieldType.FromType(type, field))
                yield return child;
            result.fields[nameReplacementDict.TryGetValue(name, out var val) ? val : name] = field;
        }
    }
}