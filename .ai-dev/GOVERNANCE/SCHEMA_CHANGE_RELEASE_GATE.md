# SCHEMA_CHANGE_RELEASE_GATE

Any EF migration change blocks publication until every item is `PASS`, `NOT_APPLICABLE`, or `BLOCKED`; any unknown value means `PUBLISH_NOT_AUTHORIZED`.

## Required matrix (18)

Every migration release records `PASS`, `NOT_APPLICABLE`, or `BLOCKED` for: (1) CurrentSchemaIdentity vs EF; (2) App normal startup; (3) online automatic upgrade; (4) Updater same-schema; (5) Updater cross-schema; (6) Installer fresh install; (7) Installer repair; (8) Installer previous-version overwrite; (9) database-protection snapshot; (10) failure rollback; (11) manual backup/restore; (12) automatic backup; (13) pre-import snapshot; (14) pending recovery/hard-kill; (15) manifest source/target permission; (16) release assets/signature/archive; (17) normal candidate ACK; (18) rollback old-version ACK. No omitted path is implicitly N/A.

## Required release evidence

Online and Setup are separately exercised against the previous released installed tree and real business fixture (products, batches, open tasks, history, settings, backup metadata). Record pre/post business fingerprints, integrity and FK checks. Exercise a stable failure rollback and one pending/hard-kill recovery. The backup matrix records every supported source schema against every restore target; an untested cell is `BLOCKED`.

## v1.1.0 governed disposition

The authoritative success receipt is `v1.0.9/m9 -> v1.1.0/m10` through ordinary Setup: external-16 JSON, Setup exit `0`, journal `Completed/CandidateCommitted`, integrity/FK healthy, and unchanged business fingerprint. `FAULT_INJECTION_ROLLBACK=NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED`: do not rerun that scenario, add a downgrade protocol, or weaken a gate. `FULL=NOT_RUN/NO_FULL`.

## v1.1.0 18-path disposition

| # | Path | Disposition |
|---:|---|---|
| 1 | CurrentSchemaIdentity vs EF | PASS |
| 2 | App normal startup | PASS |
| 3 | online automatic upgrade | BLOCKED: existing updater tests prove package/preparation behavior, but no current `v1.0.9/m9 -> m10` online end-to-end receipt was run; external-16 is Setup-only. |
| 4 | Updater same-schema | PASS |
| 5 | Updater cross-schema | PASS |
| 6 | Installer fresh install | PASS |
| 7 | Installer repair | BLOCKED: current `InstallerPreflightTests` proves m10 ALLOW (21-test backup/preflight precision run), but no isolated TestMode Setup repair receipt exists. |
| 8 | Installer previous-version overwrite | PASS (external-16) |
| 9 | database-protection snapshot | PASS |
| 10 | failure rollback | NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED |
| 11 | manual backup/restore | PASS for backup create and m10-to-m10 restore (`S7T01LocalDatabaseBackupTests` + `S7T02DatabaseRestoreTests`, 21/21 precision run); m9-to-m10 restore is `PASS(expected BLOCK)/MIGRATION_REQUIRED`, not an allowed compatibility path. |
| 12 | automatic backup | BLOCKED: metadata and external-16 business fixtures include auto-backup records, but no direct current automatic-backup creation/artifact test was found. Cross-schema restore remains `PASS(expected BLOCK)/MIGRATION_REQUIRED`. |
| 13 | pre-import snapshot | PASS |
| 14 | pending recovery/hard-kill | PASS (existing precise gate; no rerun) |
| 15 | manifest source/target permission | PASS |
| 16 | release assets/signature/archive | PASS (clean product source `18230c3`; production RSA-PSS and `RevalidateForInstall`) |
| 17 | normal candidate ACK | PASS (external-16) |
| 18 | rollback old-version ACK | NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED |

`UNKNOWN` and unresolved `BLOCKED` rows prohibit publication. A deliberately designed compatibility refusal, recorded as `PASS(expected BLOCK)/MIGRATION_REQUIRED`, is a safe matrix outcome rather than an unknown release gap. Only the explicitly named rollback-risk dispositions are accepted without a PASS.

## Permanent baseline

`v1.0.9/migration9 -> v1.1.0/migration10` is the baseline for online, Setup, rollback, same-schema and CurrentSchemaIdentity tests. The first discovery that ordinary Setup blocked migration9 is `RELEASE_GOVERNANCE_ESCAPE`; this document and its permanent regression close that escape, not a one-off acceptance note.

## SCHEMA_COMPATIBILITY_MATRIX

The release card must contain: source app version, source migration sequence, target app version, target migration sequence, online allowed, Setup allowed, snapshot format, migration execution, rollback result, normal ACK result, old-version ACK result. `UNKNOWN` in any field is `PUBLISH_NOT_AUTHORIZED`. For v1.1.0: `1.0.9/m9 -> 1.1.0/m10`, online=required, Setup=PASS (external-16), snapshot=existing SchemaUpgradeSnapshot, execution=existing Updater, rollback=`NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED`, normal ACK=PASS, old ACK=not claimed; any cross-schema backup restore not explicitly designed is `BLOCKED/MIGRATION_REQUIRED`.
