using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace PcrotoGen
{
    internal static class Program
    {
        private static readonly string[] additionalTypes = new[]
        {
            "Elements.ClanDefine/eClanSupportMemberType",
            "Elements.eGachaDrawType",
            "Elements.eSkillLocationCategory",
        };

        private static Protocol ResolveProtocol(Dictionary<string, string> url, List<ApiCall> apis, ModuleDefinition def)
        {
            var added = new HashSet<string>();

            var enums = new List<EnumType>();
            var common = new List<ClassType>();
            var request = new List<ClassType>();
            var response = new List<ClassType>();
            void Resolve(TypeReference reference)
            {
                if (added.Contains(reference.FullName)) return;
                added.Add(reference.FullName);

                var def = reference.Resolve();
                if (def.IsEnum) enums.Add(new EnumType(def));
                else
                {
                    var type = new ClassType();
                    foreach (var @ref in ClassType.FromType(def, type))
                        Resolve(@ref);
                    common.Add(type);
                }
            }

            foreach (var api in apis)
            {
                ClassType req = new(), resp = new();
                foreach (var @ref in ClassType.FromType(def.GetType(api.request), req).Concat(
                             ClassType.FromType(def.GetType(api.response), resp)))
                    Resolve(@ref);
                api.request = req.name;
                api.response = resp.name;
                api.url = url[api.url];
                request.Add(req);
                response.Add(resp);
            }

            foreach (var type in additionalTypes)
            {
                Resolve(def.GetType(type));
            }

            return new Protocol(apis: apis.ToArray(), common: common.ToArray(), enums: enums.ToArray(),
                response: response.ToArray(), request: request.ToArray());
        }

        private static Dictionary<int, string> GetApiUrlDict(ModuleDefinition definition)
        {
            var apiType = definition.GetType("Elements.eApiType");
            return new EnumType(apiType).values.ToDictionary(p => p.Value, p => p.Key);
        }

        private static Dictionary<string, string> ReadApiUrl(ModuleDefinition definition)
        {
            var result = new Dictionary<string, string>();
            var util = definition.GetType("Elements.ApiTypeUtil");
            var cctor = util.Methods.Single(m => m.IsSpecialName && m.Name == ".cctor");
            var typeDict = GetApiUrlDict(definition);

            for (int i = 0; i < cctor.Body.Instructions.Count; ++i)
            {
                var inst = cctor.Body.Instructions[i];
                if (inst.OpCode.Code != OpCodes.Dup.Code) continue;

                var instNext = cctor.Body.Instructions[i + 1];
                var instNext2 = cctor.Body.Instructions[i + 2];

                if (instNext.OpCode.Code is < Code.Ldc_I4_0 or > Code.Ldc_I4) continue;
                if (instNext2.OpCode.Code != Code.Ldstr) continue;

                var val = instNext.OpCode.Code switch
                {
                    Code.Ldc_I4 => (int)instNext.Operand,
                    Code.Ldc_I4_S => (sbyte)instNext.Operand,
                    _ => instNext.OpCode.Code - Code.Ldc_I4_0
                };

                var url = (string)instNext2.Operand;

                result.Add(typeDict[val], url);
            }
            return result;
        }

        private static List<ApiCall> ReadApiCallMono(ModuleDefinition definition)
        {
            var apiMgr = definition.GetType("Elements.ApiManager");
            var result = new List<ApiCall>();
            var addTask = apiMgr.Methods.Single(m => m.Name == "addTask");
            var typeDict = GetApiUrlDict(definition);

            foreach (var method in apiMgr.Methods.OrderBy(m => m.Name))
            {
                if (!method.Name.StartsWith("Add") || !method.Name.EndsWith("PostParam")) continue;

                var addTaskCall =
                    method.Body.Instructions.SingleOrDefault(inst => inst.OpCode.Code == Code.Call && inst.Operand == addTask);
                if (addTaskCall == null) continue;

                var typeName = method.Body.Instructions.Where(inst => inst.OpCode.Code == Code.Newobj)
                    .Select(inst => ((MethodReference)inst.Operand).DeclaringType)
                    .Single(type => type.DeclaringType == apiMgr).FullName;

                var respType = method.Parameters.Select(p => p.ParameterType)
                    .OfType<GenericInstanceType>().SingleOrDefault(p =>
                    p != null && p.GetElementType().FullName == "System.Action`1");

                if (respType == null) continue;

                var valInst = method.Body.Instructions.Take(addTaskCall.Offset).Last(
                    inst => inst.OpCode.Code is >= Code.Ldc_I4_0 and <= Code.Ldc_I4);

                var val = valInst.OpCode.Code switch
                {
                    Code.Ldc_I4 => (int)valInst.Operand,
                    Code.Ldc_I4_S => (sbyte)valInst.Operand,
                    _ => valInst.OpCode.Code - Code.Ldc_I4_0
                };

                result.Add(new()
                {
                    url = typeDict[val],
                    request = typeName,
                    response = respType.GenericArguments[0].FullName
                });
            }

            return result;
        }

        private static Regex reg = new ("(^|_)(.)", RegexOptions.Compiled);

        private static Dictionary<string, string> processNameReplacement(ModuleDefinition def)
        {
            return def.Types.SelectMany(t =>
                    t.Methods.Where(m => m.Parameters.Any(p => p.ParameterType.Name == "JsonData")))
                .Where(m => m.Body != null)
                .SelectMany(m => m.Body.Instructions.Where(inst => inst.OpCode.Code == Code.Ldstr))
                .Select(inst => (string) inst.Operand)
                .Distinct().ToDictionary(val => reg.Replace(val, g => g.Groups[2].Value.ToUpper()), val => val);
        }


        static void Main(string[] args)
        {
            var mono = AssemblyDefinition.ReadAssembly("Assembly-CSharp_mono.dll")!.MainModule!;
            var il2 = AssemblyDefinition.ReadAssembly("Assembly-CSharp_il2cpp.dll")!.MainModule!;

            var url = ReadApiUrl(mono);
            var apis = ReadApiCallMono(mono);

            ClassType.nameReplacementDict = processNameReplacement(mono);

            var protocol = ResolveProtocol(url, apis, mono);

            SavePythonProtocol(protocol);
        }

        private static void SavePythonProtocol(Protocol protocol)
        {
            var reqDict = protocol.apis.ToDictionary(a => a.request, a => a.response);
            var urlDict = protocol.apis.ToDictionary(a => a.request, a => a.url);

            static void writeFile(ClassType[] types, string file, string header, Func<string, string> name, Func<string, string> @base, Func<string, string> classSuffix = null)
            {
                var keywords = new HashSet<string>
                {
                    "def", "break", "from"
                };

                static string fieldToString(FieldType field)
                {
                    static string pythonize(string type)
                    {
                        if (type == "long") return "int";
                        if (type == "string") return "str";
                        if (type == "double") return "float";

                        return type;
                    }

                    if (field.parameters.Length == 0)
                    {
                        return pythonize(field.baseType);
                    }

                    return $"{field.baseType}[{string.Join(", ", field.parameters.Select(fieldToString))}]";
                }

                using var sw = new StreamWriter(File.OpenWrite(file));

                sw.WriteLine(header);
                sw.WriteLine();

                foreach (var type in types)
                {
                    sw.WriteLine($"class {name(type.name)}({@base(type.name)}):");
                    foreach (var field in type.fields)
                    {
                        if (keywords.Contains(field.Key))
                        {
                            sw.WriteLine($"    _{field.Key}: {fieldToString(field.Value)} = Field(alias='{field.Key}')");
                        }
                        else
                            sw.WriteLine($"    {field.Key}: {fieldToString(field.Value)} = None");
                    }

                    if (classSuffix != null)
                    {
                        sw.WriteLine(classSuffix(type.name));
                    }
                    if (classSuffix == null && type.fields.Count == 0) sw.WriteLine("    pass");

                }

            }

            writeFile(protocol.common,
                "common.py", 
                """
                from typing import List
                from .enums import *
                from pydantic import BaseModel, Field
                """,
                x => x, _ => "BaseModel");

            writeFile(protocol.request,
                "requests.py",
                """
                from typing import List
                from .modelbase import Request
                from .responses import *
                from .common import *
                from .enums import *
                from pydantic import Field
                """, 
                x => x[..^9] + "Request", x => $"Request[{reqDict[x][..^12]}Response]",
                x =>
                    $"""
                        @property
                        def url(self) -> str:
                            return "{urlDict[x]}"
                    """);
            writeFile(protocol.response,
                "responses.py",
                """
                from typing import List
                from .modelbase import ResponseBase
                from .common import *
                from .enums import *
                from pydantic import Field
                """,
                x => x[..^12] + "Response", _ => "ResponseBase");

            using var sw = new StreamWriter(File.OpenWrite("enums.py"));

            sw.WriteLine("from enum import Enum");
            sw.WriteLine();

            foreach (var type in protocol.enums)
            {
                sw.WriteLine($"class {type.name}(Enum):");
                foreach (var constant in type.values)
                {
                    sw.WriteLine($"    {constant.Key} = {constant.Value}");
                }
                sw.WriteLine();
            }
        }
    }
}