#requires -Version 5.1
[CmdletBinding()]
param(
    # 可指定 Unity 当前实际使用的 Rpc.Executor 包目录；不指定时清理本包目录。
    [string]$PackageRoot,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$Failure = $null
try {
    if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
        $PackageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    else {
        $PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
    }

    $LegacyTcpDirectory = Join-Path $PackageRoot 'Runtime\Transports\Tcp'
    $LegacyTcpMeta = $LegacyTcpDirectory + '.meta'
    $LegacyParent = Join-Path $PackageRoot 'Runtime\Transports'

    Write-Host '[检查] Rpc.Executor/Runtime/Transports/Tcp 旧源码……' -ForegroundColor Cyan
    if (Test-Path -LiteralPath $LegacyTcpDirectory) {
        $legacyFiles = @(Get-ChildItem -LiteralPath $LegacyTcpDirectory -Recurse -File -ErrorAction SilentlyContinue)
        Write-Host ("[发现] 旧 TCP 源码目录：{0}" -f $LegacyTcpDirectory) -ForegroundColor Yellow
        foreach ($file in $legacyFiles) {
            Write-Host ("  删除：{0}" -f $file.FullName) -ForegroundColor DarkGray
        }
        Remove-Item -LiteralPath $LegacyTcpDirectory -Recurse -Force
        Write-Host '[完成] 旧 TCP 源码目录已删除。' -ForegroundColor Green
    }
    else {
        Write-Host '[通过] 未发现旧版 TCP 源码目录，无需清理。' -ForegroundColor Green
    }

    if (Test-Path -LiteralPath $LegacyTcpMeta) {
        Remove-Item -LiteralPath $LegacyTcpMeta -Force
        Write-Host '[清理] 已删除旧 TCP 目录 .meta 文件。' -ForegroundColor DarkGray
    }
    if (Test-Path -LiteralPath $LegacyParent) {
        $remaining = @(Get-ChildItem -LiteralPath $LegacyParent -Force -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) {
            Remove-Item -LiteralPath $LegacyParent -Force
            if (Test-Path -LiteralPath ($LegacyParent + '.meta')) {
                Remove-Item -LiteralPath ($LegacyParent + '.meta') -Force
            }
            Write-Host '[清理] 已移除空的 Runtime/Transports 目录。' -ForegroundColor DarkGray
        }
    }
}
catch {
    $Failure = $_
    Write-Host ("[清理失败] {0}" -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        Write-Host ''
        [void](Read-Host '按 Enter 键关闭窗口')
    }
}
if ($Failure) { throw $Failure }
