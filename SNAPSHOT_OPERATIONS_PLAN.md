# Snapshot operations roadmap

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **SP1 accepted — immutable package capture/reference is available; SP2–SP4 remain planned.**
Last updated: 2026-08-21

## Purpose and ownership

Snapshot operations own durable, immutable capture packages: their identity, storage availability,
payload integrity, provenance, retention state, and controlled open/verify behavior. They do not
own campaign/session scope, any domain's state semantics, restore, fork, player authority, or a
backup policy. A consumer such as Session S4 supplies a confirmed scope contract and obtains a
typed immutable reference; it never receives a raw file path, SQL handle, caller-created bytes, or
permission to interpret another domain's payload.

This roadmap is the missing storage/provenance owner required by [Session S4](session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md). It deliberately separates four facts:

1. A scope owner says what boundary and domains its checkpoint represents.
2. A capture producer creates only the typed content its own scope contract permits.
3. Snapshot operations stores and verifies an immutable package.
4. A future restore owner interprets only the package parts it is explicitly authorized to restore.

An event, audit row, recap, live database state, database-file copy, or chat transcript is not a
snapshot package.

## Roadmap

| Feature | Capability | Direct prerequisites | Stop gate |
| --- | --- | --- | --- |
| SP0 | Ratify generic snapshot package/storage boundary | S4/C11 consumer needs; storage/retention/security decision | No component, table, storage backend, public operation, or bytes until the decision record is accepted. |
| [SP1](snapshot/feature-01/SNAPSHOT-FEATURE-01-IMPLEMENTATION-PLAN.md) | Immutable capture package and bounded reference | Accepted SP0; one typed capture producer; same-root transaction strategy | Stores/verifies one opaque package and returns a reference; does not restore or expose bytes. |
| SP2 | Bounded package inspection/availability | Accepted SP1; caller/audience policy | Reads metadata and integrity/availability only; no generic listing/search or payload read. |
| SP3 | Controlled package open for an approved restore/fork owner | Accepted SP1–SP2; named consumer contract | Supplies verified opaque content only to an in-process approved owner; no MCP/browser byte endpoint. |
| SP4 | Retention/expiry/retirement | Accepted SP1; data-lifecycle policy | Changes availability without deleting or mutating referenced game state. |

Restore remains outside this roadmap. It needs an explicit scope owner, all domain classifications,
authorization, conflict policy, and a single atomic restore root; Snapshot operations only verifies
the package it was asked to preserve.

## Cross-cutting invariants

- A snapshot id is permanent and names exactly one immutable captured package.
- The package has one scope-contract id/version, one capture producer/version, a canonical content
  digest, byte count, storage availability state, and server-generated capture evidence.
- Capture is not caller-supplied content. A caller may request a named scope owner to capture, but
  only its typed producer can construct the package.
- Public reads never reveal byte payloads, storage locators, credentials, internal backend names,
  raw domain state, or another scope's metadata.
- A capture/reference, structural evidence, and success audit either share a confirmed transaction
  or the operation fails before claiming a durable checkpoint. External storage without an atomic
  commit strategy is unavailable for SP1.
- Digest mismatch, unavailable/retired content, missing producer, scope-version mismatch,
  cancellation, timeout, or replay is a safe failure. No current state, event history, or chat is
  substituted.

## Dependency flow

~~~text
storage/retention/availability decision
└─ SP0 snapshot package contract
   └─ SP1 immutable opaque capture/reference
      ├─ Session S4 evidence-only checkpoint link/readback
      ├─ Campaign C11 no-write fork classification
      └─ SP2/SP3 bounded inspection or approved consumer open
         └─ future restore owner after full domain/authorization proof
~~~

## Change control

Amend this roadmap before choosing a cloud/vendor backend, filesystem path, encryption/key model,
cross-database replication, direct payload download, automatic capture, mutable/correctable
packages, retention deletion, public/player inspection, or restore/fork semantics. Those are
storage/security/data-lifecycle, Website/API/identity, or scope-owner decisions—not defaults of a
generic snapshot record.
