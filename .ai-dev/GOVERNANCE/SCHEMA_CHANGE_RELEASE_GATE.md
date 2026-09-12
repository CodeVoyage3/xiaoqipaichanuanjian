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
| 3 | online automatic upgrade | BLOCKED (not rerun in this release card) |
| 4 | Updater same-schema | PASS |
| 5 | Updater cross-schema | PASS |
| 6 | Installer fresh install | PASS |
| 7 | Installer repair | BLOCKED (not rerun in this release card) |
| 8 | Installer previous-version overwrite | PASS (external-16) |
| 9 | database-protection snapshot | PASS |
| 10 | failure rollback | NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED |
| 11 | manual backup/restore | BLOCKED/MIGRATION_REQUIRED |
| 12 | automatic backup | BLOCKED/MIGRATION_REQUIRED |
| 13 | pre-import snapshot | PASS |
| 14 | pending recovery/hard-kill | PASS (existing precise gate; no rerun) |
| 15 | manifest source/target permission | PASS |
| 16 | release assets/signature/archive | PENDING clean-commit refreeze |
| 17 | normal candidate ACK | PASS (external-16) |
| 18 | rollback old-version ACK | NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED |

Only the explicitly named rollback-risk dispositions are accepted without a PASS. All `BLOCKED` rows remain publication blockers unless a separate authorized release decision changes them; they are not silently treated as N/A.

## Permanent baseline

`v1.0.9/migration9 -> v1.1.0/migration10` is the baseline for online, Setup, rollback, same-schema and CurrentSchemaIdentity tests. The first discovery that ordinary Setup blocked migration9 is `RELEASE_GOVERNANCE_ESCAPE`; this document and its permanent regression close that escape, not a one-off acceptance note.

## SCHEMA_COMPATIBILITY_MATRIX

The release card must contain: source app version, source migration sequence, target app version, target migration sequence, online allowed, Setup allowed, snapshot format, migration execution, rollback result, normal ACK result, old-version ACK result. `UNKNOWN` in any field is `PUBLISH_NOT_AUTHORIZED`. For v1.1.0: `1.0.9/m9 -> 1.1.0/m10`, online=required, Setup=PASS (external-16), snapshot=existing SchemaUpgradeSnapshot, execution=existing Updater, rollback=`NOT_FULLY_VERIFIED/PRODUCT_RISK_ACCEPTED`, normal ACK=PASS, old ACK=not claimed; any cross-schema backup restore not explicitly designed is `BLOCKED/MIGRATION_REQUIRED`.
