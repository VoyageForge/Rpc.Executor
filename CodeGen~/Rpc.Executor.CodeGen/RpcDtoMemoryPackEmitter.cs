using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MemoryPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>
    /// 核心：在自己的生成器进程内构造一个临时 Compilation，把刚生成的 DTO 塞进去，
    /// 再调用 MemoryPack 的 <c>TypeMeta</c> + <c>Emit()</c> 产出序列化实现。
    /// </summary>
    /// <remarks>
    /// <para>移植自试验包 Rpc.CodeGen.Probe（Unity 2022.3 下 25/25 测试验证通过）。</para>
    /// <para>
    /// <b>绕开了什么问题</b>：Roslyn 的 Generator 之间看不到彼此 <c>AddSource</c> 的输出。
    /// 所以"RPC 生成器生成 DTO，MemoryPack 生成器再为它生成序列化代码"这条路走不通。
    /// 这里的做法是：不要求 MemoryPack 生成器看见 DTO，而是自己把 DTO 写进内存里的
    /// Compilation 副本，从副本里拿到 <c>INamedTypeSymbol</c>，直接驱动 MemoryPack 的发射逻辑。
    /// </para>
    /// <para>
    /// <b>为什么不会重复生成</b>：DTO 只由本生成器 AddSource，工程里真正的 MemoryPack.Generator
    /// 看不到这份源码（它只看得见用户的编译单元），所以不会出现两份 formatter 注册冲突。
    /// 前提是 DTO 不由用户手写 —— 这也是 0.3.0 起 RPC 请求 DTO 全部自动生成的依据。
    /// </para>
    /// <para>
    /// <b>失败处理</b>：任何环节失败都返回 <see cref="RpcDtoEmitResult"/> 而不是抛异常。
    /// 生成器内抛异常会让整个编译单元报 CS8785，把真正的错误信息埋掉。
    /// </para>
    /// </remarks>
    public sealed class RpcDtoMemoryPackEmitter
    {
        private readonly Compilation baseCompilation;
        private readonly CSharpParseOptions parseOptions;
        private readonly RpcDtoGeneratorContext context;

        /// <summary>构造一个 Emitter。</summary>
        /// <param name="baseCompilation">用户编译，作为临时 Compilation 的基底（继承全部引用）。</param>
        /// <param name="parseOptions">解析选项，保证临时语法树与用户源码用同一语言版本解析。</param>
        /// <param name="cancellationToken">取消标记。</param>
        public RpcDtoMemoryPackEmitter(
            Compilation baseCompilation,
            CSharpParseOptions parseOptions,
            CancellationToken cancellationToken)
        {
            this.baseCompilation = baseCompilation;
            this.parseOptions = parseOptions;
            context = new RpcDtoGeneratorContext(cancellationToken, parseOptions.LanguageVersion);
        }

        /// <summary>为一份 DTO 源码生成 MemoryPack 序列化实现。</summary>
        /// <param name="dtoSource">DTO 的完整源码（含 namespace 与 [MemoryPackable]）。</param>
        /// <param name="dtoMetadataName">DTO 的 metadata name，用于在临时 Compilation 里取符号。</param>
        public RpcDtoEmitResult Emit(string dtoSource, string dtoMetadataName)
        {
            // 步骤 1：DTO 源码 → 语法树。
            SyntaxTree dtoTree;
            try
            {
                dtoTree = CSharpSyntaxTree.ParseText(
                    dtoSource,
                    parseOptions,
                    path: dtoMetadataName + ".g.cs",
                    encoding: Encoding.UTF8,
                    cancellationToken: context.CancellationToken);
            }
            catch (Exception exception)
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.EmitThrew, exception.Message);
            }

            // 步骤 2：合成临时 Compilation（AddSyntaxTrees 返回新实例，baseCompilation 不变）。
            var temporaryCompilation = baseCompilation.AddSyntaxTrees(dtoTree);

            // 步骤 3：取真正的 INamedTypeSymbol —— 整个方案成立的关键一步。
            var symbol = temporaryCompilation.GetTypeByMetadataName(dtoMetadataName);
            if (symbol == null)
            {
                return RpcDtoEmitResult.Fail(
                    RpcDtoEmitFailure.SymbolNotFound,
                    "GetTypeByMetadataName 返回 null：" + dtoMetadataName);
            }

            // 步骤 4：解析 MemoryPack 引用符号（找不到 MemoryPack 类型时抛 InvalidOperationException）。
            ReferenceSymbols referenceSymbols;
            try
            {
                referenceSymbols = new ReferenceSymbols(temporaryCompilation);
            }
            catch (Exception exception)
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.MemoryPackNotReferenced, exception.Message);
            }

            // 步骤 5：成员分析（GenerateType / 成员列表 / 构造函数选择）。
            TypeMeta typeMeta;
            try
            {
                typeMeta = new TypeMeta(symbol, referenceSymbols);
            }
            catch (Exception exception)
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.EmitThrew, exception.Message);
            }

            if (typeMeta.GenerateType == GenerateType.NoGenerate)
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.NoGenerate, null, context.Diagnostics);
            }

            // 步骤 6：Validate 需要声明语法（定位诊断用）；临时 Compilation 里的 DTO 有真实语法树。
            var declaration = symbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(context.CancellationToken))
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();

            if (declaration == null)
            {
                return RpcDtoEmitResult.Fail(
                    RpcDtoEmitFailure.SymbolNotFound,
                    "DTO " + dtoMetadataName + " 没有 TypeDeclarationSyntax 声明");
            }

            if (!typeMeta.Validate(declaration, context, unionFormatter: false))
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.ValidateFailed, null, context.Diagnostics);
            }

            // 步骤 7：发射。Emit 只写类型体，文件头由 RpcDtoFormatterFileBuilder 补。
            var builder = new StringBuilder();
            try
            {
                typeMeta.Emit(builder, context);
            }
            catch (Exception exception)
            {
                return RpcDtoEmitResult.Fail(RpcDtoEmitFailure.EmitThrew, exception.Message, context.Diagnostics);
            }

            var formatterSource = RpcDtoFormatterFileBuilder.Emit(builder.ToString(), RpcDtoFactory.DtoNamespace);
            return RpcDtoEmitResult.Ok(formatterSource, context.Diagnostics);
        }
    }
}
