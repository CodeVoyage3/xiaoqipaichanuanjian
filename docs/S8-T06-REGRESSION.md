# S8-T06 final-regression reuse map

This is a run map, not a substitute for fresh S8-T06 evidence.  Every listed
high-scale entry creates its own explicit TEMP/GUID root; do not supply a
database, backup, or Excel path.

## Isolation preflight

```powershell
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj --no-restore --filter "FullyQualifiedName~S8T02IsolationTests"
```

Expected: 9 passed.  This is the default-factory/loader fail-closed gate; it
does not measure high-scale work.

## Fresh high-scale evidence

```powershell
$env:S8_T01_PERF = '1'
$env:S8_T01_COMMIT = (git rev-parse HEAD)
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj --no-restore --filter "FullyQualifiedName~S8T01PerformanceBaselineTests.MeasuresIsolated100kBatch300kInspectionBaseline" --logger "console;verbosity=normal"
Remove-Item Env:S8_T01_PERF,Env:S8_T01_COMMIT
```

The console prints the `S8-T01-baseline.json` TEMP path.  It contains the
three measured samples, median/max, captured SQL plus `EXPLAIN QUERY PLAN`,
counts, logical fingerprints, integrity/FK/migration, and blocker diagnostics.

```powershell
$env:S8_T03_RUN_HIGH_SCALE = '1'
$env:S8_T03_ROWS = '100000'
$env:S8_T03_COMMIT = (git rev-parse HEAD)
$env:S8_T03_EVIDENCE_KIND = 's8-t06-final'
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj --no-restore --filter "FullyQualifiedName~S8T03ImportPerformanceTests.High_scale_real_import_requires_explicit_gate" --logger "console;verbosity=normal"
Remove-Item Env:S8_T03_RUN_HIGH_SCALE,Env:S8_T03_ROWS,Env:S8_T03_COMMIT,Env:S8_T03_EVIDENCE_KIND
```

This runs only the 100,000-row theory case; its TEMP `evidence` JSON records
parse/validate/plan/snapshot/write/post timings, workbook/DB size, business
counts, excluded categories, integrity and FK.  The separate representative
post-orchestration rollback is:

```powershell
$env:S8_T03_RUN_HIGH_SCALE_FAILURE = '1'
$env:S8_T03_COMMIT = (git rev-parse HEAD)
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj --no-restore --filter "FullyQualifiedName~S8T03ImportPerformanceTests.High_scale_resolve_facts_failure_requires_explicit_gate" --logger "console;verbosity=normal"
Remove-Item Env:S8_T03_RUN_HIGH_SCALE_FAILURE,Env:S8_T03_COMMIT
```

Together with `Controlled_real_write_failures_roll_back_every_business_table`
these preserve full-table/BLOB before/after fingerprints, integrity and FK=0.

## Crash smoke and recovery

`80b2c57..f981211` changes no Import, inspection, or inventory transaction
source; it changes only restore/startup code and tests. Thus the existing
S8-T04 representative smoke is sufficient unless Sol's final diff review
finds a later relevant transaction change. Run exactly the six pre/post
points below; each selected theory case loops three times. `DisplayName`
filtering was checked with `--list-tests` against the current assembly, so it
selects one parameterized case rather than the whole theory.

```powershell
$project = 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'
dotnet test $project --no-restore --filter 'DisplayName~inspection&DisplayName~precommit'
dotnet test $project --no-restore --filter 'DisplayName~inspection&DisplayName~postcommit'
dotnet test $project --no-restore --filter 'DisplayName~inventory&DisplayName~precommit'
dotnet test $project --no-restore --filter 'DisplayName~inventory&DisplayName~postcommit'
dotnet test $project --no-restore --filter 'DisplayName~Import_10k_process_kill_commit_boundaries_are_atomic&DisplayName~precommit'
dotnet test $project --no-restore --filter 'DisplayName~Import_10k_process_kill_commit_boundaries_are_atomic&DisplayName~postcommit'
```

The TEMP evidence JSON includes kill PID, pre/reference/post complete
fingerprints, reopen/read-write, integrity, FK and migration. The default
100k-import theory returns early unless `S8_T04_RUN_100K=1`; do not count that
closed gate as evidence. Historical 48/48 remains historical evidence, not
this smoke's result.

```powershell
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj --no-restore --filter "FullyQualifiedName~S8T05CorruptionSafetyTests"
```

This existing class covers bad-backup rejection, corrupt-current protection
block, healthy restore, final-validation rollback, authority reads and the
large 100k/300k seed backup/restore sample.  Each scenario writes a small JSON
under its own TEMP/GUID root; extract `backupMilliseconds`,
`restoreMilliseconds`, `finalValidationMilliseconds`, `databaseBytes`, and
`finalFingerprint` from `large-backup-restore`.

## Final non-performance gates

Run the unfiltered Release suite separately after the explicit high-scale
runs.  Do not treat gated tests which return early as high-scale evidence.
Record the TRX path/counts, then run the repository's existing Release build,
EF design-time `has-pending-model-changes`, migration `--no-connect`, forbidden
path diff, and `git diff --check` gates.  Those commands are intentionally not
duplicated here because they are Sol-owned final acceptance gates.
