$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path (Join-Path $repo 'obj') ('S7T03ScriptCheck-' + [guid]::NewGuid().ToString('N'))
if (-not [IO.Path]::GetFullPath($fixture).StartsWith(
    [IO.Path]::GetFullPath((Join-Path $repo 'obj')) + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Fixture escaped the workspace obj directory.'
}
$docs = Join-Path $fixture 'docs'
$runtime = Join-Path $fixture 'local\StoreExpiryInspector'
$data = Join-Path $runtime 'data'
$exe = Join-Path $fixture 'src\StoreExpiryInspector\bin\Release\net10.0-windows\StoreExpiryInspector.exe'
foreach ($path in @($docs, $data, (Split-Path $exe), (Join-Path $runtime 'backups'))) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}
[IO.File]::WriteAllBytes($exe, @())
$db = Join-Path $data 'app.db'
[IO.File]::WriteAllBytes($db, [byte[]]::new(299008))
$hash = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $runtime 'backups\historical.txt') -Value 'preserve original backup'
$source = Get-Content -LiteralPath (Join-Path $repo 'docs\S7-T03-GUI.ps1') -Raw
$runtimeAssignment = '$runtime = Join-Path ([Environment]::GetFolderPath(''LocalApplicationData'')) ''StoreExpiryInspector'''
if (-not $source.Contains($runtimeAssignment) -or
    -not $source.Contains('F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522')) {
    throw 'Fixture substitution no longer matches the helper. Never run the unmodified helper here.'
}
$source = $source.Replace(
    $runtimeAssignment,
    ('$runtime = ''' + $runtime.Replace("'", "''") + "'"))
$source = $source.Replace('F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522', $hash)
# Replace the two registry boundaries in the isolated copy only. This check
# must never read or change the actual user's Run key.
$sourceTokens = $null; $sourceErrors = $null
$sourceAst = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$sourceTokens, [ref]$sourceErrors)
$registryFunctions = @($sourceAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -in @('Read-AutoStart', 'Restore-AutoStart')
}, $true))
if ($sourceErrors.Count -or $registryFunctions.Count -ne 2) { throw 'Registry fixture boundaries changed.' }
$registryMocks = @{
    'Read-AutoStart' = 'function Read-AutoStart { Get-Content -LiteralPath (Join-Path $repo ''fake-autostart.json'') -Raw | ConvertFrom-Json }'
    'Restore-AutoStart' = 'function Restore-AutoStart($Original) { $Original | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo ''fake-autostart.json'') -Encoding UTF8 }'
}
foreach ($function in ($registryFunctions | Sort-Object { $_.Extent.StartOffset } -Descending)) {
    $source = $source.Substring(0, $function.Extent.StartOffset) + $registryMocks[$function.Name] +
        $source.Substring($function.Extent.EndOffset)
}
if ($source.Contains('[Microsoft.Win32.Registry]')) { throw 'Unmocked registry access in fixture.' }
$fakeAutoStart = Join-Path $fixture 'fake-autostart.json'
@{ Exists = $true; Kind = 'String'; Value = 'original-test-command' } |
    ConvertTo-Json | Set-Content -LiteralPath $fakeAutoStart -Encoding UTF8
$script = Join-Path $docs 'S7-T03-GUI.ps1'
Set-Content -LiteralPath $script -Value $source -Encoding UTF8
$tokens = $null; $parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }

function Expect-Failure([scriptblock]$Operation) {
    $failed = $false
    try { & $Operation } catch { $failed = $true }
    if (-not $failed) { throw 'Expected safety refusal did not occur.' }
}

# Wrong formal hash must fail without moving anything.
[IO.File]::WriteAllText($db, 'wrong baseline')
Expect-Failure { & $script -Action Prepare }
if (Test-Path -LiteralPath ($runtime + '.s7t03-original')) { throw 'Wrong baseline was moved.' }
[IO.File]::WriteAllBytes($db, [byte[]]::new(299008))
& $script -Action Prepare
Expect-Failure { & $script -Action Prepare }
@{ Exists = $false; Kind = 'String'; Value = $null } |
    ConvertTo-Json | Set-Content -LiteralPath $fakeAutoStart -Encoding UTF8
if (-not (Test-Path -LiteralPath ($runtime + '.s7t03-original\backups\historical.txt'))) {
    throw 'Historical original backup was not preserved.'
}
$protectedDb = $runtime + '.s7t03-original\data\app.db'
[IO.File]::WriteAllText($protectedDb, 'damaged protection')
Expect-Failure { & $script -Action Finish }
if (-not (Test-Path -LiteralPath (Join-Path $runtime '.s7t03-isolated.json'))) {
    throw 'Unsafe Finish changed the isolated runtime.'
}
[IO.File]::WriteAllBytes($protectedDb, [byte[]]::new(299008))

# Simulate already-restored bytes. No SQLite or WPF is executed by this filesystem test.
New-Item -ItemType Directory -Path (Join-Path $runtime 'data'), (Join-Path $runtime 'backups') | Out-Null
$restored = [byte[]]::new(4096); $restored[0] = 1
[IO.File]::WriteAllBytes($db, $restored)
$restoredHash = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash
foreach ($name in @('backup-test.db', 'pre-restore-test.db')) {
    $path = Join-Path $runtime ('backups\' + $name)
    [IO.File]::WriteAllBytes($path, $restored)
    @{ ValidationResult = 'verified'; FileName = $name; FileSize = 4096; Sha256 = $restoredHash } |
        ConvertTo-Json | Set-Content -LiteralPath ($path + '.metadata.json') -Encoding UTF8
}
Expect-Failure { & $script -Action VerifyRestore -BackupFileName '..\app.db' }
& $script -Action VerifyRestore -BackupFileName 'backup-test.db'
[IO.File]::WriteAllText(($db + '-wal'), 'unexpected')
Expect-Failure { & $script -Action VerifyRestore -BackupFileName 'backup-test.db' }
Remove-Item -LiteralPath ($db + '-wal')

$marker = Join-Path $runtime '.s7t03-isolated.json'
$savedMarker = Get-Content -LiteralPath $marker -Raw
Set-Content -LiteralPath $marker -Value '{"Token":"wrong"}'
Expect-Failure { & $script -Action Finish }
Set-Content -LiteralPath $marker -Value $savedMarker
# Simulate an interrupted Finish after parking the isolated runtime.
Move-Item -LiteralPath $runtime -Destination ($runtime + '.s7t03-isolated')
& $script -Action Finish
& $script -Action Finish
if ((Get-Content -LiteralPath $fakeAutoStart -Raw | ConvertFrom-Json).Value -cne 'original-test-command') {
    throw 'Original autostart snapshot was not passed to the restoration boundary.'
}
if ((Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash -ne $hash -or
    -not (Test-Path -LiteralPath (Join-Path $runtime 'backups\historical.txt')) -or
    (Test-Path -LiteralPath ($runtime + '.s7t03-isolated')) -or
    (Test-Path -LiteralPath ($runtime + '.s7t03-original'))) { throw 'Final restoration failed.' }
$resolvedFixture = (Resolve-Path -LiteralPath $fixture).ProviderPath
if (-not $resolvedFixture.StartsWith(
    [IO.Path]::GetFullPath((Join-Path $repo 'obj')) + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing fixture cleanup outside the workspace obj directory.'
}
Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
Write-Output 'SCRIPT_FILESYSTEM_CHECK_PASS: refusals, byte verification, original preservation, interrupted Finish and repeated Finish.'
Write-Output 'Synthetic fixture cleaned. No WPF or formal runtime was used.'
