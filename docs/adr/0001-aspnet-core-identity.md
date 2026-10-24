# ADR 0001: Use ASP.NET Core Identity for local accounts

- Status: Accepted
- Date: 2026-10-24

## Context

P0-04 requires a later tenant to be chosen from an authenticated membership. A client-supplied tenant id, slug, or header is not sufficient. The architecture notes written before this decision postponed ASP.NET Core Identity, while the security notes required an authenticated user. Those statements conflicted.

A custom password table would also require this project to design storage, hashing, and verification itself.

## Decision

Use ASP.NET Core Identity for local account management and password hashing.

This decision, by itself, did not add an Identity package or migration.

## Rationale

Identity supplies the local user store and the password hasher. The application does not store a reversible password and does not invent a hashing scheme. Account rules stay in the existing project boundaries: `PhotoPlatform.Domain` does not reference ASP.NET Core, the use case and response contract belong in `PhotoPlatform.Application`, and the Identity store belongs in `PhotoPlatform.Infrastructure`.

## Consequences

- A later, approved implementation may add the Identity packages required for local accounts and a migration for those tables.
- The password hasher is the one provided by Identity.
- Sign-in failure must not reveal whether the account exists.
- Password material, hashes, and tokens stay out of logs and client responses.
- Tenant selection still comes from membership after authentication. Identity alone does not make other tenant-owned data operational.

P0-05 later implemented this decision for studio membership. The paragraphs above record the decision as it was accepted.
