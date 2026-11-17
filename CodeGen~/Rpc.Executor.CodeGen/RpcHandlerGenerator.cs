using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VoyageForge.Rpc.SourceGen
{
    /// <summary>
    /// 处理器生成器入口（编译期运行，产物不手改）：
    /// <list type="bullet">
    /// <item>扫描继承 <c>RequestHandlerBase</c> / <c>AsyncRequestHandlerBase</c> 的类；</item>
    /// <item>也扫描直接实现 <c>IRequestHandler&lt;TRequest,TResponse&gt;</c> / <c>IAsyncRequestHandler&lt;...&gt;</c> 的类（一个类可多个）；</item>
    /// <item>为每个程序集生成 <c>VoyageForge.Rpc.Generated.RpcHandlerCatalog</c>，</item>
    /// <item>重复处理器在编译期报 <c>RPCHANDLER001</c>。</item>
    /// </list>
    /// 运行时由 <c>RpcHandlerCatalogCache</c> 找到该目录，<c>RpcExecutor</c> 再按请求类型分发。
    /// </summary>
    [Generator]
    public sealed class RpcHandlerGenerator : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<RpcHandlerModel> handlers = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => HandlerModelBuilder.Build(syntaxContext, cancellationToken))
                .SelectMany(static (models, _) => models);

            context.RegisterSourceOutput(handlers.Collect(), static (productionContext, models) =>
                RpcHandlerSourceWriter.Write(productionContext, models));
        }
    }
}