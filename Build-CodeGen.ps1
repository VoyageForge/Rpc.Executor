#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$NoRestore,
    [switch]$Clean,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$PackageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$CodeGenRoot = Join-Path $PackageRoot 'CodeGen~'
$Solution = Join-Path $CodeGenRoot 'Rpc.Executor.CodeGen.sln'
$ProjectRoot = Join-Path $CodeGenRoot 'Rpc.Executor.CodeGen'
$BuiltDll = Join-Path $ProjectRoot 'bin\Release\netstandard2.0\Rpc.Executor.CodeGen.dll'
$MetaTemplate = Join-Path $CodeGenRoot 'Templates\Rpc.Executor.CodeGen.dll.meta.template'
$AnalyzerDirectory = Join-Path $PackageRoot 'Runtime\Analyzers'
$TargetDll = Join-Path $AnalyzerDirectory 'Rpc.Executor.CodeGen.dll'
$TargetMeta = $TargetDll + '.meta'
$Failure = $null
$Succeeded = $false

function Write-BuildStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    Write-Host $Message -ForegroundColor $Color
}

try {
    if ($Clean -and $NoRestore) { throw '-Clean 与 -NoRestore 不能同时使用。' }

    Write-BuildStatus '============================================================' Cyan
    Write-BuildStatus 'Rpc.Executor Session Source Generator 构建开始' Cyan
    Write-BuildStatus '[说明] 构建输出完整显示在当前终端，不创建日志文件。' DarkGray

    Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Status '检查构建环境' -PercentComplete 10
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '未找到 dotnet SDK。请安装可构建 netstandard2.0 项目的 .NET SDK。'
    }
    if (-not (Test-Path -LiteralPath $Solution)) { throw "找不到解决方案：$Solution" }
    if (-not (Test-Path -LiteralPath $MetaTemplate)) { throw "找不到 Unity DLL 元数据模板：$MetaTemplate" }

    if ($Clean) {
        Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Status '清理旧的 bin/obj' -PercentComplete 20
        Write-BuildStatus '[清理] 删除 CodeGen 项目的 bin/obj 缓存。' Yellow
        $foldersToClean = @(
            (Join-Path $ProjectRoot 'bin'),
            (Join-Path $ProjectRoot 'obj')
        )
        foreach ($folder in $foldersToClean) {
            if (Test-Path -LiteralPath $folder) {
                Remove-Item -LiteralPath $folder -Recurse -Force
                Write-BuildStatus ("已删除：{0}" -f $folder) DarkGray
            }
        }
    }

    $buildArgs = @('build', $Solution, '--configuration', 'Release', '--verbosity', 'normal', '--nologo')
    if ($NoRestore) { $buildArgs += '--no-restore' }
    Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Status '正在执行 dotnet build' -PercentComplete 35
    Write-BuildStatus ('[构建] dotnet ' + ($buildArgs -join ' ')) Cyan
    Write-BuildStatus '[构建输出开始]' DarkGray

    # 不使用 2>&1 管道、不捕获 stdout/stderr：让 dotnet 原样输出到当前 CMD/PowerShell。
    # PowerShell 5.1 下临时设置 Continue，避免 native stderr 被 ErrorActionPreference=Stop 提前截断。
    $previousErrorActionPreference = $ErrorActionPreference
    $dotnetExitCode = -1
    try {
        $ErrorActionPreference = 'Continue'
        & dotnet @buildArgs
        $dotnetExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($dotnetExitCode -eq 0) {
        Write-BuildStatus ("[dotnet ExitCode] {0}" -f $dotnetExitCode) Green
    }
    else {
        Write-BuildStatus ("[dotnet ExitCode] {0}" -f $dotnetExitCode) Red
    }
    if ($dotnetExitCode -ne 0) {
        throw "Rpc.Executor Source Generator 编译失败，dotnet exit code: $dotnetExitCode。请查看上方完整构建输出。"
    }

    Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Status '检查编译产物' -PercentComplete 78
    if (-not (Test-Path -LiteralPath $BuiltDll)) {
        throw "dotnet build 返回成功，但没有找到预期 DLL：$BuiltDll"
    }
    Write-BuildStatus ("[产物] DLL 已生成：{0}" -f $BuiltDll) Green

    Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Status '复制 DLL 和 Unity Analyzer 元数据' -PercentComplete 90
    New-Item -ItemType Directory -Path $AnalyzerDirectory -Force | Out-Null
    Copy-Item -LiteralPath $BuiltDll -Destination $TargetDll -Force
    Copy-Item -LiteralPath $MetaTemplate -Destination $TargetMeta -Force
    if (-not (Test-Path -LiteralPath $TargetDll) -or -not (Test-Path -LiteralPath $TargetMeta)) {
        throw 'Analyzer DLL 或 .meta 文件复制失败。'
    }

    Write-BuildStatus ("[成功] DLL：{0}" -f $TargetDll) Green
    Write-BuildStatus ("[成功] META：{0}" -f $TargetMeta) Green
    Write-BuildStatus '请在 Unity Inspector 中确认 DLL 的 Asset Label 为 RoslynAnalyzer。' Yellow
    $Succeeded = $true
}
catch {
    $Failure = $_
    Write-BuildStatus ("[失败] {0}" -f $_.Exception.Message) Red
    if ($_.ScriptStackTrace) { Write-BuildStatus $_.ScriptStackTrace DarkRed }
}
finally {
    Write-Progress -Activity '构建 Rpc.Executor Source Generator' -Completed
    if ($Succeeded) {
        Write-BuildStatus 'Rpc.Executor Source Generator 构建完成。' Green
    }
    else {
        Write-BuildStatus 'Rpc.Executor Source Generator 构建未完成，请根据上方第一个错误排查。' Red
    }
    Write-BuildStatus '============================================================' Cyan
    if (-not $NoPause) {
        Write-Host ''
        [void](Read-Host '按 Enter 键关闭窗口')
    }
}

if ($Failure) { throw $Failure }
