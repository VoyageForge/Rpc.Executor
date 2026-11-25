using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VoyageForge.Rpc.CodeGen
{
    public sealed partial class RpcPartialEmitter
    {
        private const string ContractAttr = "VoyageForge.Rpc.Transport.RpcContractAttribute";
        private const string SessionAttr = "VoyageForge.Rpc.Transport.RpcSessionAttribute";
        private const string ApiAttr = "VoyageForge.Rpc.Transport.RpcApiAttribute";
        private const string RpcMethodAttr = "VoyageForge.Rpc.Transport.RpcMethodAttribute";
        private const string RpcControllerAttr = "VoyageForge.Rpc.Transport.RpcControllerAttribute";
        private const string ControllerBase = "VoyageForge.Rpc.Transport.RpcControllerBase";
        private const string CancellationTokenName = "System.Threading.CancellationToken";
        private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;
        private static readonly SymbolDisplayFormat ProtocolFqn = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        private static bool ValidateMethod(IMethodSymbol method, out List<IParameterSymbol> args,
            out IParameterSymbol cancellation, out ITypeSymbol result, out bool hasResult, out string error)
        {
            args = new List<IParameterSymbol>(); cancellation = null; result = null; hasResult = false; error = null;
            if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.IsGenericMethod)
            { error = "只支持非泛型实例方法。"; return false; }
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind != RefKind.None) { error = "不支持 ref/out/in 参数。"; return false; }
                if (parameter.Type.TypeKind == TypeKind.Pointer || parameter.Type.TypeKind == TypeKind.FunctionPointer || parameter.Type.IsRefLikeType)
                { error = "不支持指针、函数指针或 ref struct 参数。"; return false; }
                if (parameter.Type.ToDisplayString() == CancellationTokenName)
                {
                    if (cancellation != null || !SymbolEqualityComparer.Default.Equals(parameter, method.Parameters.Last()))
                    { error = "CancellationToken 最多一个，且必须是最后一个参数。"; return false; }
                    cancellation = parameter;
                }
                else args.Add(parameter);
            }
            if (args.Count > 7) { error = "最多支持 7 个业务参数。"; return false; }
            INamedTypeSymbol task = method.ReturnType as INamedTypeSymbol;
            if (task == null || task.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks" || task.Name != "Task")
            { error = "只支持 Task 或 Task<T> 返回类型。"; return false; }
            hasResult = task.Arity == 1;
            if (task.Arity > 1) { error = "不支持该 Task 返回类型。"; return false; }
            if (hasResult)
            {
                result = task.TypeArguments[0];
                if (result.TypeKind == TypeKind.Pointer || result.TypeKind == TypeKind.FunctionPointer || result.IsRefLikeType)
                { error = "不支持指针、函数指针或 ref struct 返回类型。"; return false; }
            }
            return true;
        }

        private static IEnumerable<IMethodSymbol> Methods(INamedTypeSymbol contract)
        {
            List<INamedTypeSymbol> all = contract.AllInterfaces.ToList(); all.Add(contract);
            return all.SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
                .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public)
                .GroupBy(MethodIdentity, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(MethodIdentity, StringComparer.Ordinal);
        }
        private static string MethodIdentity(IMethodSymbol method) => method.Name + "(" +
            string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(ProtocolFqn))) + ")->" + method.ReturnType.ToDisplayString(ProtocolFqn);
        private static string Signature(List<IParameterSymbol> args) => "(" + string.Join(", ", args.Select(a => a.Type.ToDisplayString(ProtocolFqn))) + ")";
        private static string CanonicalKey(string name, string method, List<IParameterSymbol> args) =>
            "v2\u001F" + name + "\u001F" + method + "\u001F" + string.Join("\u001E", args.Select(a => a.Type.ToDisplayString(ProtocolFqn)));
        private static ulong Hash(string value)
        {
            const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
            ulong hash = offset; byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes) { hash ^= b; hash = unchecked(hash * prime); } return hash;
        }
        private static string ControllerName(INamedTypeSymbol contract)
        {
            AttributeData a = Attribute(contract, ContractAttr);
            string name = a != null && a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null;
            if (!string.IsNullOrWhiteSpace(name)) return name;

            string fallback = contract.Name.StartsWith("I", StringComparison.Ordinal) && contract.Name.Length > 1
                ? contract.Name.Substring(1) : contract.Name;
            if (fallback.EndsWith("Api", StringComparison.Ordinal) && fallback.Length > 3)
                fallback = fallback.Substring(0, fallback.Length - 3);
            return fallback;
        }
        private static AttributeData Attribute(ISymbol symbol, string fullName) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == fullName);
        private static bool Derives(INamedTypeSymbol type, string fullName)
        { for (INamedTypeSymbol t = type; t != null; t = t.BaseType) if (t.ToDisplayString() == fullName) return true; return false; }
        private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().Any(s => s.Modifiers.Any(SyntaxKind.PartialKeyword));
        private static string Id(string value) => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None ? "@" + value : value;
        private static string Lit(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        private static string SafeName(string value)
        {
            string safe = new string((value ?? "").Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (safe.Length == 0) return "_Assembly";
            if (!char.IsLetter(safe[0]) && safe[0] != '_') safe = "_" + safe;
            return safe;
        }
    }
}
