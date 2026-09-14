$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src\StoreExpiryInspector\StoreExpiryInspector.csproj'
$tests = Join-Path $repoRoot 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'
$instructions = Join-Path $PSScriptRoot 'S22-T02-GUI验收步骤.md'
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$workbook = Join-Path $dataRoot 'S22-T02-请导入此文件.xlsx'
$userPackages = Join-Path $env:USERPROFILE '.nuget\packages'

if (-not (Test-Path -LiteralPath $userPackages -PathType Container)) { throw "未找到当前用户 NuGet 缓存：$userPackages" }
$env:NUGET_PACKAGES = $userPackages
& dotnet restore $tests --packages $userPackages --ignore-failed-sources --force-evaluate -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw '当前用户依赖资产刷新失败。' }

try {
    $env:S22_T02_GUI_ROOT = $dataRoot
    & dotnet test $tests -c Release --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName~S22T02ImportDifferenceSummaryTests.SeedsRequestedTemporaryGuiFixture' --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw '隔离验收数据生成失败。' }
}
finally {
    Remove-Item Env:S22_T02_GUI_ROOT -ErrorAction SilentlyContinue
}

& dotnet build $project -c Release --no-restore -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Release App 构建失败。' }

$executable = Join-Path $repoRoot 'src\StoreExpiryInspector\bin\Release\net10.0-windows\StoreExpiryInspector.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw '未找到验收程序。' }
if (-not (Test-Path -LiteralPath $workbook -PathType Leaf)) { throw '未找到验收 Excel。' }

Set-Clipboard -Value $workbook
Start-Process -FilePath 'notepad.exe' -ArgumentList ('"{0}"' -f $instructions)
Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable -Parent) -ArgumentList ('--data-root "{0}" --allow-existing-isolated-data-root' -f $dataRoot)

Write-Host ''
Write-Host 'S22-T02 GUI 验收已启动。' -ForegroundColor Green
Write-Host "隔离数据根：$dataRoot"
Write-Host "验收 Excel：$workbook"
Write-Host 'Excel 路径已复制到剪贴板；请按已打开的验收步骤操作。'
Write-Host '本入口不会读取或修改正式数据库，也不会自动删除本次隔离证据。'
Read-Host '完成后按 Enter 关闭此窗口'
