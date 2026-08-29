# Web Interface Feature 2 Slice 6 — Sol xhigh ratification

Status: **ratified**  
Reviewed boundary: [Slice 6 implementation](WEB-CONTROL-CENTER-SLICE-6-IMPLEMENTATION.md)  
Original evidence: [Slice 6 receipt](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md)  
Reviewed: **2026-08-24**

## Review decision

The durable schema, closed seven-key allowlist, normalization, restart-only application, operation
linkage, startup ordering, authorization, identity derivation, body bounds, history, and recovery
contracts match the accepted Slice 6 boundary. The implementation contains no live refresh or
restart control and exposes no arbitrary configuration or secret.

One concurrency gap was found during retrospective review. Two simultaneous first writes for the
same key could both observe revision zero before SQLite selected a winner. Durable constraints
prevented duplicate history, but the losing HTTP request could receive a provider-specific database
error rather than the promised `SETTING_REVISION_STALE` conflict.

The remediation serializes these rare setting mutations within the single supported host process.
The loser now re-reads the committed head, follows the ordinary optimistic-revision path, writes no
revision or operation, and receives the stable conflict. Database constraints and the EF concurrency
token remain the final integrity boundary.

## Ratification evidence

- Focused host-settings/provider/web run: **66 passed**, 0 failed, including the simultaneous
  first-write conflict test.
- Solution build: **passed**, 0 warnings and 0 errors.
- `git diff --check` on the reviewed/remediated boundary: **passed**.

Slice 6 is ratified as accepted. Slice 8 retains its own independent Sol gate for the Codex
app-server protocol, child-process, streaming, cancellation, and read-only sandbox boundary.
