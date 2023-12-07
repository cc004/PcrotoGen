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
            a.apis.Concat(b.apis).DistinctBy(a => a.url).ToArray(),
            a.common.Concat(b.common).DistinctBy(t => t.name).ToArray(),
            a.enums.Concat(b.enums).DistinctBy(t => t.name).ToArray(),
            a.response.Concat(b.response).DistinctBy(t => t.name).ToArray(),
            a.request.Concat(b.request).DistinctBy(t => t.name).ToArray());
    }
}