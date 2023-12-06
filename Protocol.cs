namespace PcrotoGen;

public class Protocol(ApiCall[] apis, ClassType[] common, EnumType[] enums, ClassType[] response, ClassType[] request)
{
    public ClassType[] request = request, response = response, common = common;
    public EnumType[] enums = enums;
    public ApiCall[] apis = apis;
}