# S8-T06 final-regression reuse map

This is a run map, not fresh S8-T06 evidence. Every high-scale entry creates
its own explicit TEMP/GUID root; do not supply a database, backup, or Excel
path. First obtain the Release assembly; every command below uses it and never
restores or builds implicitly.

```powershell
dotnet build StoreExpiryInspector.slnx -c Release --no-restore -p:NuGetAudit=false
```

## Isolation preflight

```powershell
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~S8T02IsolationTests"
```

Expected: 9 passed. This is the default-factory/loader fail-closed gate; it
does not measure high-scale work.

## Fresh high-scale evidence

```powershell
$env:S8_T01_PERF = '1'
$env:S8_T01_COMMIT = (git rev-parse HEAD)
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~S8T01PerformanceBaselineTests.MeasuresIsolated100kBatch300kInspectionBaseline" --logger "console;verbosity=normal"
Remove-Item Env:S8_T01_PERF,Env:S8_T01_COMMIT
```

The console prints the TEMP `S8-T01-baseline.json`, which contains three
samples, median/max, captured SQL plus `EXPLAIN QUERY PLAN`, counts, logical
fingerprints, integrity/FK/migration, and blocker diagnostics.

```powershell
$env:S8_T03_RUN_HIGH_SCALE = '1'
$env:S8_T03_ROWS = '100000'
$env:S8_T03_COMMIT = (git rev-parse HEAD)
$env:S8_T03_EVIDENCE_KIND = 's8-t06-final'
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~S8T03ImportPerformanceTests.High_scale_real_import_requires_explicit_gate&DisplayName~100000" --logger "console;verbosity=normal"
Remove-Item Env:S8_T03_RUN_HIGH_SCALE,Env:S8_T03_ROWS,Env:S8_T03_COMMIT,Env:S8_T03_EVIDENCE_KIND
```

The `DisplayName~100000` conjunction selects only the 100,000-row theory
case; the FQN alone discovers all three rows and two return early. Its TEMP
evidence JSON records parse/validate/plan/snapshot/write/post timings,
workbook/DB size, business counts, excluded categories, integrity and FK.

```powershell
$env:S8_T03_COMMIT = (git rev-parse HEAD)
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~Controlled_real_write_failures_roll_back_every_business_table&DisplayName~product_part'
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~Controlled_real_write_failures_roll_back_every_business_table&DisplayName~post_middle'
Remove-Item Env:S8_T03_COMMIT
```

These are the representative write-middle and cross-250-command
post-orchestration failures. They preserve complete table/BLOB fingerprints,
integrity and FK=0 without mechanically repeating the 100k failure case.

## Crash smoke and recovery

`80b2c57..f981211` changes no Import, inspection, or inventory transaction
source; it changes only restore/startup code and tests. Thus the existing
S8-T04 representative smoke is sufficient unless Sol's final diff review
finds a later relevant transaction change. Run exactly these six pre/post
points; every selected theory case loops three times. `DisplayName` filtering
was checked with `--list-tests` against the current assembly and selects one
parameterized case, not the whole theory.

```powershell
$project = 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~inspection&DisplayName~precommit'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~inspection&DisplayName~postcommit'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~inventory&DisplayName~precommit'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~inventory&DisplayName~postcommit'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~Import_10k_process_kill_commit_boundaries_are_atomic&DisplayName~precommit'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~Import_10k_process_kill_commit_boundaries_are_atomic&DisplayName~postcommit'
```

TEMP evidence JSON records PID, pre/reference/post complete fingerprints,
reopen/read-write, integrity, FK and migration. The default 100k-import theory
returns early unless `S8_T04_RUN_100K=1`; do not count it as evidence.
Historical 48/48 remains historical evidence, not this smoke's result.

```powershell
$env:S8_T05_RUN_ID = [guid]::NewGuid().ToString('N')
$project = 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~TamperedBackupIsRejectedBeforeProtectionOrReplacement'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~CorruptCurrentDatabaseFailsClosedBeforeStagingOrReplacement'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~HealthyBackupRestoresRepresentativeFingerprintAfterProtection'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~InjectedRestoreFailurePreservesOrRollsBackTheOriginalDatabase&DisplayName~final-header'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~RestoredDatabaseSupportsAuthoritativeBusinessReads'
dotnet test $project -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'DisplayName~LargeS8T01SeedBackupRestoreRecordsActualSizeAndElapsedTime'
Remove-Item Env:S8_T05_RUN_ID
```

This exact representative subset covers bad-backup rejection, corrupt-current
protection block, healthy restore, final-validation rollback, authority reads,
and the large 100k/300k seed backup/restore sample. These cases write one
small JSON under their TEMP/GUID root with the supplied `runId`; locate the
run's `S8-T05-*.json` and extract `backupMilliseconds`, `restoreMilliseconds`,
`finalValidationMilliseconds`, `databaseBytes`, and `finalFingerprint` from
`large-backup-restore`. Other class cases may not write JSON; a passing test
result alone must not be presented as a missing artifact.

## Final non-performance gates

Run the unfiltered Release suite separately after explicit high-scale runs.
Do not treat gated early returns as high-scale evidence. Record TRX path and
counts, then run the existing EF design-time, migration `--no-connect`,
forbidden-path diff, and `git diff --check` gates. They remain Sol-owned final
acceptance gates and are intentionally not duplicated here.
