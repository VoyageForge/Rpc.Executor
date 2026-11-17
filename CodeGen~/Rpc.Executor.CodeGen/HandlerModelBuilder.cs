using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VoyageForge.Rpc.SourceGen
{
    /// <summary>
    /// 从实现类声明构建处理器模型：
    /// 优先识别 RequestHandlerBase / AsyncRequestHandlerBase；
    /// 也支持直接实现 IRequestHandler&lt;TRequest,TResponse&gt; / IAsyncRequestHandler&lt;...&gt;（一个类可多个）。
    /// </summary>
    internal static class HandlerModelBuilder
    {
        /// <summary>构建模型；不是处理器实现类时返回空。</summary>
        public static ImmutableArray<RpcHandlerModel> Build(GeneratorSyntaxContext context, System.Threading.CancellationToken cancellationToken)
        {
            if (!(context.Node is TypeDeclarationSyntax declaration))
            {
                return ImmutableArray<RpcHandlerModel>.Empty;
            }

            INamedTypeSymbol type = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
            if (type == null || type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
            {
                return ImmutableArray<RpcHandlerModel>.Empty;
            }

            string handlerName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Location location = GetLocation(type);

            // 1) 基类继承
            INamedTypeSymbol baseType = type.BaseType;
            while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                string name = baseType.OriginalDefinition.Name;
                string ns = baseType.OriginalDefinition.ContainingNamespace?.ToDisplayString();
                if (ns == "VoyageForge.Rpc.Runtime.Handlers" &&
                    (name == "RequestHandlerBase" || name == "AsyncRequestHandlerBase") &&
                    baseType.TypeArguments.Length == 2)
                {
                    return ImmutableArray.Create(new RpcHandlerModel(
                        baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        handlerName,
                        name == "AsyncRequestHandlerBase",
                        location));
                }

                baseType = baseType.BaseType;
            }

            // 2) 直接实现接口（一个类可以实现多个请求处理器）
            List<RpcHandlerModel> models = new List<RpcHandlerModel>();
            foreach (INamedTypeSymbol iface in type.Interfaces)
            {
                if (iface.TypeArguments.Length != 2)
                {
                    continue;
                }

                string name = iface.OriginalDefinition.Name;
                string ns = iface.OriginalDefinition.ContainingNamespace?.ToDisplayString();
                bool isAsync = name == "IAsyncRequestHandler";
                if (ns != "VoyageForge.Rpc.Runtime.Handlers" || (!isAsync && name != "IRequestHandler"))
                {
                    continue;
                }

                models.Add(new RpcHandlerModel(
                    iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    handlerName,
                    isAsync,
                    location));
            }

            return models.ToImmutableArray();
        }

        private static Location GetLocation(ISymbol symbol)
        {
            return symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None;
        }
    }
}