# S13-T01 v1.0.6 corrected final candidate

Date: 2026-09-08 Asia/Shanghai
Status: `CORRECTED_CANDIDATE_STATICALLY_ACCEPTED / OFFICIAL_DUAL_SOURCE_TRANSACTION_BLOCKED / NOT_ACCEPTED`

- Final product source: `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355`.
- Frozen root: `D:\\S13-TestAssets\\v1.0.6-corrected-final\\a8983ed6`.
- ZIP: 109429785 bytes / `63D0BC0A1AEE22965393E21333C9FFA10BBAE53AFAC0C1C2591899EA60FAD100`.
- Setup: 75338357 bytes / `65E2689B57BB7918B589539E3E677B54A6E822C3B0EA384769A820FFA50FFA73`.
- Manifest: 869 bytes / `6D6508FA2B25B9585DCF3741DCE57018D10B825DFAB2690A639689C5C64BF923`.
- Signature: 384 bytes / `6555846A4598B1660870ED74F06D5D8F3518A8BF7204EE20373F38CB781709E4`.
- Release evidence: 72032 bytes / `DA1AD0373571D8321A3D4376E22B9C8464459685465A3E5BA95018A9569152F3`.
- Contract: target 1.0.6; source versions 1.0.4..1.0.5; source migration min=max=`20260901155124_AddPolicyAndBaselineFoundation`; target migration count 9.
- Static gates: production RSA-PSS/SHA256 PASS; manifest/package identity PASS; ZIP 618 entries; tree `B773473E8D2A000393E4A57102B8EE1579DD95FE7447C00169ED46A1086575BF`; tree diff 0; unsafe/duplicate/prohibited entries 0.
- Versions: App/Updater 1.0.6.0; Updater PE subsystem 2; Setup 1.0.6.
- Exact-source Release build 0 warnings/0 errors, EF NO_MODEL_DRIFT and migrationCount 9 remain valid; this publish/Setup build succeeded. No 43/43, 8/8 or Full rerun.
- No production code, Schema, migration, formal database/install tree or public v1.0.4/v1.0.5 Release was modified. No push/tag/Release.

The prior candidate remains permanently `REJECTED / SUPERSEDED_BY_CORRECTED_MIGRATION_CONTRACT`. Equal ZIP/Setup bytes are natural because the product source is unchanged; manifest/signature have a new corrected candidate identity.

Official clients only consume GitHub public Releases, while the unpublished v1.0.6 candidate has no production transport entry. The official Updater also requires the current user's fixed HKCU install identity and formal `%LOCALAPPDATA%` data root; TEMP/GUID is test mapping only and cannot prove the official path. Therefore v1.0.4->v1.0.6 and v1.0.5->v1.0.6 were NOT_RUN. Continuing requires separate approval for Sandbox/VM plus HTTPS proxy/CA transport isolation, or post-release verification. The normal-launch bug is not yet proven fixed by the official v1.0.5 source path.
