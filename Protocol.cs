using System.Linq;

namespace PcrotoGen;

public class Protocol(ApiCall[] apis, ClassType[] common, EnumType[] enums, ClassType[] response, ClassType[] request)
{
    public ClassType[] request = request, response = response, common = common;
    public EnumType[] enums = enums;
    public ApiCall[] apis = apis;

    public static Protocol operator +(Protocol a, Protocol b)
    {
        return new Protocol(
            a.apis.Concat(b.apis).DistinctBy(a => a.request).ToArray(),
            MergeClass(a.common, b.common),
            a.enums.Concat(b.enums).DistinctBy(t => t.name).ToArray(),
            MergeClass(a.response, b.response),
            MergeClass(a.request, b.request));
    }

    private static ClassType[] MergeClass(ClassType[] a, ClassType[] b)
    {
        var result = a.ToDictionary(c => c.name, c => c);

        foreach (var t in b)
        {
            if (!result.ContainsKey(t.name))
            {
                result[t.name] = t;
                continue;
            }

            var toMerge = result[t.name];
            foreach (var field in t.fields)
            {

                toMerge.fields[field.Key] = field.Value;
                /*
                if (toMerge.fields.TryGetValue(field.Key, out var fieldType))
                {
                    if (fieldType.baseType != field.Value.baseType)
                    {
                        var x = field.Value.baseType;
                        var y = fieldType.baseType;
                        if (x == "long" && y == "int")
                            toMerge.fields[field.Key] = field.Value;
                        else if (x == "int" && y == "long") ;
                        else
                            throw new InvalidOperationException("conflict field type");
                    }
                }
                else
                {
                    toMerge.fields[field.Key] = field.Value;
                }*/
            }
        }

        return result.Values.ToArray();
    }
}