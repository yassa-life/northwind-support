# Northwind Support Desk — Candidate Approach

This document is a live implementation report. Update it as work is completed. Sections marked **Not started** must not be described as completed in `README.md` or the final submission.

## Current status

| Work item | Status | Evidence / commit |
| --- | --- | --- |
| DEFECT-117 regression test and fix | Complete | `6efc6ae` |
| FL-1 to FL-6 server-side filtering | Complete | `38ffa16` |
| Task 2 triage, SLA and escalation | Not started | — |
| Task 3 authentication | Not started | — |
| Task 4 API container | Not started | — |
| Final README and verification | Not started | — |

## Task 1.1 — DEFECT-117

### Problem and reproduction

Selecting a non-search filter such as Status updates filter state but sends no ticket-list request. A later Search change sends a request that contains the previously selected filter.

### Root cause

The request effect in `useTicketList.ts` observes Search, Page, and Page Size only. Other filter-state changes therefore do not execute the effect. Search later executes the effect using the latest render and exposes the older filter value in that request.

### Options considered

I considered forcing a reload from each filter control and depending on the entire filter object.
The first option hides the hook defect and duplicates request behaviour across the UI. The second
would also request on every search keystroke. Both were rejected.

### Chosen approach

Search input is copied to a debounced value after 300 ms. The request filter object uses that
debounced value together with the latest dropdown, checkbox, paging, and sorting values. A request
effect observes the resulting object, so non-search changes request on the next event-loop tick
without waiting for the search delay.

### Rejected approaches

Do not use in-memory filtering, forced page reloads, remount keys, or one-off request wiring for individual fields. Record any additional rejected option and why.

### Tests and verification

The test `requests tickets immediately when the status filter changes` changes only Status, flushes
the immediate request tick without advancing the 300 ms search delay, and checks the exact request
values. The frontend suite passes with 3 tests across 2 files; type checking and linting also pass.
Browser Network-tab verification is still required before final submission. Commit: `6efc6ae`.

## Task 1.2 — server-side filtering

### Existing behaviour

`TicketQuery` receives filter values, but `TicketQueries.GetPagedAsync` counts, sorts, and pages without applying them.

### Chosen design

`TicketQueries` applies one composable `ApplyFilters` stage to its `IQueryable` before counting,
sorting, skipping, taking, and projection. Each supplied exact-match filter adds a database `Where`
condition. Trimmed search covers title, reference, and customer name, while blank search is ignored.
Sequential conditions naturally combine using AND. A future SLA filter can be added as another
condition without restructuring the query.

### SQL verification

The SQLite-backed query tests execute the composed EF query against a relational database. The
development SQL Server command log still needs to be inspected manually before the final README can
claim SQL Server SQL verification.

### Tests and verification

Three new query tests cover exact priority, combined customer/category/priority filters, trimmed
customer-name search, and filtered counts. The focused query-test run and the full .NET test command
both exited successfully. Commit: `38ffa16`. Time was not recorded and must not be guessed.

## Task 2 — escalation, triage and SLA

### Business-rule design

**Not started.** Explain how priority, SLA windows, premium adjustment, specialist eligibility, capacity and deterministic assignment are represented in small reusable units.

### SLA-window start decision

**Decision required.** At Risk needs the start of the current SLA window. Explain whether a dedicated timestamp is stored or the start is derived from creation/latest escalation time. Explain why `UpdatedAtUtc` is unsuitable.

### Agent-selection decisions

**Not started.** Record the deterministic tie-break and how open-ticket counts and specialist requirements are evaluated.

### Escalation persistence

**Not started.** Describe the escalation table, Ticket ID index, field sizes, optional fields, and delete behaviour.

### API and frontend

**Not started.** Record endpoints, status codes, UI behaviour, refresh behaviour and error handling.

### Tests and verification

**Not started.** Record all boundary and rule tests, migration verification, commit IDs, and time spent.

## Task 3 — authentication

### Credential model

The password hash is stored directly on `Agent`. Every account in this application is an agent, so
a linked users table would create a one-to-one record and lifecycle without enabling a second user
type or a separate identity boundary. The linked-table alternative was rejected for that unnecessary
complexity. `AddAgentAuthentication` adds the required column, and the development seeder fills new
and pre-existing empty hashes at seed time.

### Password and token security

ASP.NET Identity `PasswordHasher<Agent>` supplies the salted, one-way PBKDF2 hash and verification;
plain passwords are never stored or logged. Unknown email, incorrect password, and inactive agent
attempts use the same `401` message so the endpoint does not reveal which accounts exist.

JWT configuration is bound from the `Jwt` configuration section, with production overrides expected
through `Jwt__...` environment variables. The signing key must be at least 32 characters and is not
hard-coded as a real secret. Tokens contain agent ID, full name, email, and a unique token ID, and
expire after 60 minutes. A fallback policy protects existing controllers centrally while `[AllowAnonymous]`
keeps login public; Swagger middleware remains public in Development.

### Frontend session

The React app stores the token, expiry, and display data in `localStorage`, chosen for a small exercise
because it survives refreshes and keeps the API client simple. The accepted trade-off is that injected
JavaScript could read it if an XSS defect existed; a production design should consider a secure,
HttpOnly cookie and CSRF controls.

An authentication provider owns the session, protected routes send unauthenticated visitors to
`/login`, and the requested URL is carried in router state and restored after successful login. The
shared API client attaches the bearer token to every request. Logout removes the session, and any
API `401` removes it globally so the route guard returns the user to login.

### Tests and verification

Five API behaviour tests cover generic wrong-password/unknown-email responses, inactive agents,
missing-token rejection, successful login, token acceptance, and the `/me` identity. Frontend tests
cover bearer-token attachment and clearing the session on `401`. The full backend suite, frontend
suite, lint, type-check, and production web build passed. The generated migration was also applied
successfully to the local SQL Server database. A live request then signed in as the seeded Alex Turner
account, returned a token, and used that token successfully with `/api/auth/me`. Backend commit:
`9a97aa1`. Frontend commit: `6d60df8`.

## Task 4 — API container

### Image design

**Not started.** Describe build stages, cached restore, runtime image, non-root user, runtime configuration and port.

### Migration strategy

**Not started.** Explain why local Compose may migrate on startup and why production should use a separate controlled migration step.

### Verification

**Not started.** Record clean-volume startup, health, seeded data, cached layer, runtime image size, non-root output and web-proxy result.

## AI usage

Update this section honestly before submission.

### Tools and purposes

- Codex was used to inspect the starter repository, compare it with the assignment, identify the initial DEFECT-117 root cause, and create a step-by-step implementation plan.
- Codex was also used to implement Task 3 authentication, generate its EF migration, add behavioural
  tests, diagnose test-host configuration, and update the reviewer documentation. Every generated
  change was checked with repository builds, tests, lint, and type-checking.

### Verification workflow

For every AI-assisted change, record how it was understood, reviewed, tested and manually verified before acceptance.

### Accepted suggestion

The split between debounced Search and immediate non-search request filters was accepted because it
fixes the effect dependency defect while preserving the requirement not to request per keystroke.

### Changed or rejected suggestion

The first request effect called a helper that synchronously set loading state. The repository's
React lint rule rejected that pattern, so it was changed to schedule the request on the next
event-loop tick and the regression test was updated to distinguish that immediate tick from the
300 ms search delay.

### AI-assisted bug discovery

The initial audit identified that the ticket-list effect omitted non-search filter dependencies. Update this section with the eventual regression test and fix result.

## Known limitations

- No registration, password reset, email verification, OAuth/SSO, refresh tokens, MFA, lockout,
  rate limiting, or roles matrix; these are deliberately outside Task 3.
- `localStorage` session persistence has an XSS token-theft trade-off.
- Production HTTPS termination, secret rotation, and identity administration are not implemented.

## Time log

| Work item | Started | Finished | Time spent | Notes |
| --- | --- | --- | ---: | --- |
| DEFECT-117 | — | — | Not recorded | Complete; manual Network check remains |
| Server filtering | — | — | Not recorded | Complete; manual SQL Server log check remains |
| Task 2 | — | — | — | Not started |
| Task 3 | 2026-10-19 | 2026-10-19 | ~1 h | Implemented; automated verification complete |
| Task 4 | — | — | — | Not started |
| Documentation and verification | — | — | — | Not started |
