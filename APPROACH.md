# Northwind Support Desk — Candidate Approach

This document is the implementation report. `CODING_STORY.md` is its companion learning guide and
explains the completed coding work from beginning to end in simpler language.

## Current status

| Work item | Status | Evidence / commit |
| --- | --- | --- |
| DEFECT-117 regression test and fix | Complete | `650b36d` |
| FL-1 to FL-6 server-side filtering | Complete | `171a598` |
| Task 2 triage, SLA and escalation | Complete | `276f772`, `88f2e5d` |
| Task 3 authentication | Complete | `26c79c7`, `26b6982` |
| Task 4 API container | Not started | — |
| Final documentation and recorded verification | Complete | `README.md`, `CODING_STORY.md` |

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
The status-only request behavior is covered by the regression test and was included in the passing
frontend verification. Commit: `650b36d`.

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

The SQLite-backed query tests execute the composed EF query against a relational database. Later live
Task 2 verification exercised the filtered query against SQL Server with EF Core database-command
logging enabled.

### Tests and verification

Three new query tests cover exact priority, combined customer/category/priority filters, trimmed
customer-name search, and filtered counts. The focused query-test run and the full .NET test command
both exited successfully. Commit: `171a598`. Time was not recorded and must not be guessed.

## Task 2 — escalation, triage and SLA

### Business-rule design

The rules are split into three small policies so creation and escalation use the same logic. `PriorityPolicy`
uses the requested priority (Medium when omitted) unless the category forces Critical. `SlaPolicy` reads
configurable Low/Medium/High/Critical windows of 72/24/8/4 hours and halves the window for Premium
customers, with a one-hour minimum. `AssignmentPolicy` asks the repository only for active, eligible
agents below capacity and chooses the candidate with the fewest open tickets.

Each decision returns a short explanation. The create response includes the priority, SLA and assignment
explanations so the UI can tell the agent why the automatic triage result was chosen.

### SLA-window start decision

A dedicated `SlaStartedAtUtc` and the selected `SlaWindowMinutes` are stored on each ticket. Creation starts
the first window and escalation starts a new one. This makes the 25% At Risk boundary exact and preserves
the effective configured window even if configuration changes later. `UpdatedAtUtc` was rejected because
ordinary changes such as assignment or status would incorrectly restart the SLA clock.

### Agent-selection decisions

The SQL query filters to active agents, requires a matching specialization when the category needs one,
counts only non-Resolved/non-Closed tickets, and excludes agents whose open count has reached their maximum.
It orders by open count and then agent ID, giving a stable deterministic tie-break. On escalation the same
policy re-evaluates ownership; when no eligible agent exists the ticket remains unassigned and the response
explains why.

### Escalation persistence

`TicketEscalations` stores the ticket, before/after priority, optional before/after agent IDs, optional
before/after due dates, reason (maximum 500 characters), actor (maximum 100), and UTC timestamp. A composite
index on `(TicketId, EscalatedAtUtc)` supports the history view. Ticket and agent foreign keys use Restrict
so audit history cannot disappear through a cascade delete. Escalations are append-only through the exposed
application API.

### API and frontend

`POST /api/tickets/{id}/escalate` validates a 5–500 character reason, takes the actor from the JWT, raises
priority one level, restarts the SLA from the current time, re-runs assignment and returns the ticket plus
the new audit entry. Critical, Resolved and Closed tickets return `409`; invalid requests return `400` and
missing tickets return `404`. `GET /api/tickets/{id}/escalations` returns newest-first history.

The list has an SLA Status filter that remains SQL-side. The detail screen shows a reason field, disables the
action while saving, displays API errors, refreshes the ticket after success and reloads immutable history.
The create screen displays the three triage explanations returned by the API.

### Tests and verification

Backend tests cover all eight Standard/Premium SLA combinations, the one-hour floor, forced Critical priority,
specialist/capacity selection, deterministic selection, no-candidate handling, escalation before/after values,
Critical rejection, terminal-state rejection, and SLA boundaries (including exactly 25%). Frontend tests cover
all five SLA labels, short-reason validation and `409` error display.

Final verification passed: 55 unit tests, 4 architecture tests, 12 frontend tests, ESLint, TypeScript/production
build, migration application, and a live SQL Server workflow. The live check created `TCK-0041`, confirmed
automatic Premium/specialist triage, escalated Low → Medium → High, returned two ordered history entries with
the authenticated actor, and successfully executed the At Risk filter in SQL. Backend commit: `276f772`.
Frontend commit: `88f2e5d`. Approximate active Task 2 time: 2.5 hours.

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
`26c79c7`. Frontend commit: `26b6982`.

## Task 4 — API container

### Image design

**Not started.** Describe build stages, cached restore, runtime image, non-root user, runtime configuration and port.

### Migration strategy

**Not started.** Explain why local Compose may migrate on startup and why production should use a separate controlled migration step.

### Verification

**Not started.** Record clean-volume startup, health, seeded data, cached layer, runtime image size, non-root output and web-proxy result.

## AI usage

### Tools and purposes

- Codex was used to inspect the starter repository, compare it with the assignment, identify the initial DEFECT-117 root cause, and create a step-by-step implementation plan.
- Codex was also used to implement Task 3 authentication, generate its EF migration, add behavioural
  tests, diagnose test-host configuration, and update the reviewer documentation. Every generated
  change was checked with repository builds, tests, lint, and type-checking.
- Codex was used for Task 2 to implement the triage policies, escalation audit flow, SLA UI, tests and
  migration, and to diagnose two EF Core query-translation issues found during live SQL Server verification.

### Verification workflow

AI-assisted changes were compared with the assignment, reviewed in the affected code path, and then
checked with focused regression tests and the complete automated verification. Database-affecting
work was also applied to local SQL Server and exercised through a live authenticated workflow. The
final recorded result was 55 unit tests, 4 architecture tests, 12 frontend tests, lint, type checking,
and the production build passing.

### Accepted suggestion

The split between debounced Search and immediate non-search request filters was accepted because it
fixes the effect dependency defect while preserving the requirement not to request per keystroke.

### Changed or rejected suggestion

The first request effect called a helper that synchronously set loading state. The repository's
React lint rule rejected that pattern, so it was changed to schedule the request on the next
event-loop tick and the regression test was updated to distinguish that immediate tick from the
300 ms search delay.

### AI-assisted bug discovery

The initial audit identified that the ticket-list effect omitted non-search filter dependencies. A
status-only regression test reproduced the failure, then passed after debounced Search was separated
from immediate request filters. Live Task 2 verification later exposed two EF Core query shapes that
SQL Server could not translate; both were rewritten as SQL-translatable expressions before acceptance.

## Known limitations

- No registration, password reset, email verification, OAuth/SSO, refresh tokens, MFA, lockout,
  rate limiting, or roles matrix; these are deliberately outside Task 3.
- `localStorage` session persistence has an XSS token-theft trade-off.
- Production HTTPS termination, secret rotation, and identity administration are not implemented.

## Time log

| Work item | Started | Finished | Time spent | Notes |
| --- | --- | --- | ---: | --- |
| DEFECT-117 | 2026-10-19 | 2026-10-19 | Not separately recorded | Complete; regression verification passed |
| Server filtering | 2026-10-19 | 2026-10-19 | Not separately recorded | Complete; relational tests and later live SQL verification passed |
| Task 2 | 2026-10-19 | 2026-10-20 | ~2.5 h | Complete; automated and live SQL Server verification passed |
| Task 3 | 2026-10-19 | 2026-10-19 | ~1 h | Implemented; automated verification complete |
| Task 4 | — | — | — | Not started |
| Documentation and verification | 2026-10-19 | 2026-10-20 | Not separately recorded | Complete; coding story added after implementation |
