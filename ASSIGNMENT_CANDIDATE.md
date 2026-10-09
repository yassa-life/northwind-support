# Northwind Support Desk

Candidate assignment | .NET 10 Web API, React 19, SQL Server

| | |
| --- | --- |
| **Time budget** | 2 days. |
| **Priority order** | Task 1, then Task 2, then Task 3, then Task 4. Honestly marking the last task "not finished" beats four rushed ones. |
| **AI tools** | Allowed without penalty. The candidate must understand and be able to defend every line submitted. |
| **Setup** | Fork the repository to the candidate's own GitHub account and work there. See `README.md` for how to run the project and how the code is laid out. |
| **Submission** | Submit the link to the fork (see "README and submission"). |

The candidate joins a team that already has a working Support Ticket Management System. The four tasks below are completed in order. Where the spec is ambiguous, make a decision, record the decision in the README, and move on.

---

## Overview

| # | Task | Outcome | Effort |
| --- | --- | --- | --- |
| 1 | Fix a defect, add server-side filtering | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | Ticket Escalation & Assignment | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | Login and authentication | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | Dockerise the API | `docker compose up` yields a migrated, seeded API with no baked-in secret | ~1.5 h |

### Seed data

| Agent | Active | Open / limit | Specializations |
| --- | --- | --- | --- |
| Alex Turner | Yes | 5 / 10 | General, Technical |
| Ben Osei | Yes | 3 / 8 | Technical, Outage |
| Priya Nair | Yes | 4 / 8 | Outage, Security |
| Sara Lindqvist | Yes | 2 / 6 | General, Billing |
| Marcus Doyle | Yes | 6 / 6 (full) | Billing, Security |
| Yuki Tanaka | No | 0 / 8 | Technical |

Premium customers: Contoso, Adventure Works, Wide World Importers. Standard: Fabrikam, Northwind Traders, Tailspin Toys. Categories: General, Billing, Technical, Outage, Security.

---

## Task 1: Defect fix and server-side filtering

*Commit the defect fix and the filtering separately from each other and from the feature work.*

### 1.1 DEFECT-117: filters sometimes do not take effect

**Report:** On the ticket list, changing the Status dropdown to *In progress* sends no request to `GET /api/tickets`. Typing in the search box then sends a request that includes the earlier status. Clearing the search makes it stale again.

- Reproduce it, find the root cause, and fix the cause. Hiding the effect (in-memory filtering, forced reload, remount key) counts as not fixed.
- Add a regression test that fails on the original code and passes on the fix, asserting the request that is sent.
- State the root cause in one or two sentences in the commit message and in the README.
- Acceptance: any single filter change triggers an immediate request; no request per keystroke in search; the Task 2 SLA filter behaves correctly without special wiring.

### 1.2 Server-side filtering

The API binds filter values into `TicketQuery` but ignores them. Implement them in the backend.

| ID | Requirement |
| --- | --- |
| FL-1 | `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId` are exact matches. |
| FL-2 | `unassignedOnly=true` returns only tickets with no assigned agent. |
| FL-3 | `search` matches ticket title, reference (TCK-0042) or customer name. Trimmed; blank is ignored. |
| FL-4 | All supplied filters combine with AND. |
| FL-5 | The database filters, before counting and paging. `totalCount`, `totalPages` and the pager are correct for the filtered set. No filtering in C#. |
| FL-6 | Adding a further filter (Task 2's `slaStatus`) must mean adding one filter, not restructuring the query. |

Tests must cover at least: exact-match priority, a combination of filters, and a search on customer name.

---

## Task 2: Ticket Escalation & Assignment

Team leads currently triage every ticket by hand. The system should decide priority, due date and owner automatically, and let a ticket be escalated with a record of who and why. The rules are simple on purpose; the challenge is **where they are placed**.

### 2.1 Business rules

| Rule | Specification |
| --- | --- |
| BR-1 Priority | Use the requested priority; default *Medium*. A category flagged `ForcesCriticalPriority` (Security, Outage) is always *Critical*. |
| BR-2 SLA window | Due = creation time + base window. Critical 4 h, High 8 h, Medium 24 h, Low 72 h. |
| BR-3 Premium | Premium customers get half the window, never shorter than 1 hour. |
| BR-4 Specialists | A category flagged `RequiresSpecialist` (Billing, Outage, Security) may only go to an agent with that specialization. |
| BR-5 Assignment | Among eligible agents (active, specialised where BR-4 applies, strictly below `MaxOpenTickets`), choose the fewest open tickets (open = not Resolved/Closed). Deterministic tie-break. If nobody is eligible, create the ticket unassigned with a reason; this is a normal outcome, not an error. |
| BR-6 Configurable | Base windows, premium multiplier, 1 h floor and at-risk threshold are changeable without touching code. Use the existing `Sla` section in `appsettings.Development.json` and the category flags, or justify an alternative. No hard-coded category names. |
| BR-7 Escalation | Raises priority one level. Recomputes due date from the moment of escalation (BR-2, BR-3). Re-evaluates owner: keep the current agent if still eligible, else reassign per BR-5. Reason of 5-500 characters required. Writes an immutable history row. Critical tickets cannot be escalated; Resolved/Closed tickets cannot be escalated. |
| BR-8 SLA status | Derived, never stored. No due date: *NotApplicable*. Resolved on or before due: *Met*. Resolved late, or unresolved and past due: *Breached*. Unresolved with 25% or less of the window remaining: *AtRisk*. Otherwise *WithinSla*. "Now" must be injectable. |

### 2.2 Backend

- **Triage on create.** Extend `POST /api/tickets` to apply BR-1 to BR-6 and return the decision: priority, assigned agent (or null), due date, SLA status, and a `triage` object with a human-readable reason for priority, SLA window and assignment. No eligible agent still returns **201**, unassigned, with the reason.
- **Escalate.** `POST /api/tickets/{id}/escalate` with a reason and an `escalatedBy` value (until Task 3 replaces it). Returns **200** with the updated ticket and recorded escalation; **400** for invalid reason or missing actor; **404** for unknown ticket; **409** for Critical or Resolved/Closed, with a message saying which.
- **History.** `GET /api/tickets/{id}/escalations`, newest first, with before/after priority, agent and due date, plus reason, who and when.
- **Persistence.** New `TicketEscalations` table via a committed EF Core migration: Id, TicketId (FK, indexed), From/To Priority, From/To AgentId, From/To DueAtUtc, Reason (500), EscalatedBy (100), EscalatedAtUtc. Configure keys, lengths, optionality and delete behaviour; document any index added and why.
- **SLA filter.** `GET /api/tickets?slaStatus=...`. Filtering, sorting, paging and the count all happen in SQL.

### 2.3 Frontend (`apps/web/src/features/tickets`)

- One reusable SLA indicator (Within SLA, At risk, Breached, Met, No SLA) used by both list and detail page; state not conveyed by colour alone.
- SLA status filter in the filter bar, wired to the new parameter.
- Escalation panel on the detail page: reason field, client validation matching the server (invalid input never reaches the API), pending state, server errors shown including 409, ticket and history refreshed without a full reload.
- Escalation history: priority, agent and due-date changes, reason, who, when.
- Triage result after create: applied priority, due date, assigned agent, or the "unassigned because..." case.
- Escalate control disabled or hidden when the ticket cannot be escalated, while still handling a server rejection.
- Everything touched is fully typed. No `any`, no `@ts-ignore`.

### 2.4 Tests

- **Backend** (xUnit + Moq; reuse `FixedClock` and `TicketBuilder`): SLA windows per priority and tier incl. the floor; forced-Critical categories; agent selection (fewest open, specialists, limits, inactive, deterministic ties, nobody eligible); escalation (one-step increase, due date from now, assignee kept or changed, correct history row, rejections); SLA status at exact boundaries.
- **Frontend** (Vitest + React Testing Library): short reason blocks submission and does not call the API; a 409 is shown and the ticket is unchanged; the SLA indicator renders every state distinguishably.

### 2.5 Optional stretch: SLA summary report

Only if Tasks 1-3 are finished and tested. `GET /api/reports/sla-summary?from=&to=&groupBy=agent|priority` returning, per group: total, breached, at risk, within, met, breach rate and average resolution hours. Must be a single database-side aggregation.

---

## Task 3: Login and authentication

The app is open to anyone who can reach the URL. The existing agents should sign in; everything else is closed.

### 3.1 Backend

| ID | Requirement |
| --- | --- |
| AU-1 | Agents sign in with email and password. Add credentials (a column on Agent or a linked Users table; justify the choice) via a committed migration. |
| AU-2 | Passwords hashed and salted using a vetted mechanism (Identity `PasswordHasher`, BCrypt or PBKDF2). Never plain text, reversible encryption, or a home-made hash. |
| AU-3 | Seed development credentials, hashed at seed time. Document the dev password; it must be obviously a local fixture. |
| AU-4 | `POST /api/auth/login` returns a JWT, its expiry and enough user data to greet them. Wrong email and wrong password give the identical 401 and message. Inactive agents cannot log in. |
| AU-5 | `GET /api/auth/me` returns the current user or 401. |
| AU-6 | Every existing endpoint requires a valid token, enforced centrally (global or fallback policy). Login and Swagger stay open. |
| AU-7 | Signing key comes from configuration or an environment variable, listed in `.env.example`. Not a literal in code; no real secret committed. State the token lifetime chosen. |
| AU-8 | `escalatedBy` comes from the token, not the request body. |

### 3.2 Frontend

| ID | Requirement |
| --- | --- |
| AU-9 | `/login` page: labelled, accessible fields, client validation, pending state, clear error on rejected credentials. |
| AU-10 | Token stored and attached to every call by extending `apps/web/src/api/client.ts`. README states where it is kept and the trade-off. |
| AU-11 | Protected routes redirect unauthenticated visitors to login, then return them to the page they wanted (or the ticket list). |
| AU-12 | Signed-in agent's name visible, with a logout that clears the session. |
| AU-13 | Any 401 (e.g. expired token) clears the session and redirects to login. |

### 3.3 Tests

At least three behavioural tests, for example: wrong password gives 401 and no token; a protected endpoint is 401 without a token and 200 with one; password verification passes and fails correctly; the frontend redirects unauthenticated users or attaches the token.

**Not wanted:** registration, password reset, email verification, OAuth/SSO, refresh tokens, MFA, lockout or rate limiting, a roles matrix, HTTPS certificate work. Mention any that matter under Known limitations.

---

## Task 4: Dockerise the API

One command should bring up a working backend identically on every machine.

| ID | Requirement |
| --- | --- |
| DK-1 | Multi-stage `apps/api/Dockerfile`, built from the repo root: .NET 10 SDK to build and publish, ASP.NET runtime image to run. Restore (project files, `Directory.Build.props`, `Directory.Packages.props`, `global.json`) happens before the rest of the source is copied, so restore is cached. |
| DK-2 | `.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, `.git/`, `.env` and the web app. |
| DK-3 | Runs as a non-root user on HTTP port 8080. No certificates in the image. |
| DK-4 | No configuration or secret baked in. Connection string, JWT key and environment name come from run-time environment variables; new ones go in `.env.example`. |
| DK-5 | `api` service in `docker-compose.yml`, built from the Dockerfile, published on `localhost:5080`, reaching SQL Server by service name, started only when SQL Server is healthy. |
| DK-6 | On an empty volume the API applies migrations and seeds demo data. README explains how migrations would run in a real deployment instead, and why. |
| DK-7 | `GET /health` reports database reachability and is open without a token, configured centrally. |
| DK-8 | `VITE_API_PROXY_TARGET=http://localhost:5080 npm run web` works against the container; README says so. |

### Verification (describe in the README how each was checked)

- `docker compose down -v && docker compose up --build` ends with a healthy `/health` and seeded data at `/api/tickets`.
- Changing one `.cs` file and rebuilding reuses the cached restore layer.
- Runtime image size versus the SDK image (report the figure).
- `docker compose exec api whoami` does not print root.

**Not wanted:** Kubernetes/Helm, registry pushes, CI/CD, HTTPS in the container, dockerising the React app, Docker secrets or a vault, multi-architecture builds.

---

## Assessment

| Area | Weight | What is assessed |
| --- | ---: | --- |
| .NET / backend | 14% | Correct status codes, validation on every request, async throughout, shared error handling |
| OOP & SOLID | 13% | Rules in small testable units shared by create and escalate; interfaces only at real seams |
| React / TypeScript | 13% | One SLA indicator, custom data hook, loading/error/empty states, full typing, accessible inputs |
| Security & authentication | 10% | Hashed passwords, generic failures, key in config, central authorization, actor from token, no baked secrets, non-root container |
| Debugging | 10% | Defect fixed at the cause, regression test, explained in the candidate's own words |
| Testing & code quality | 10% | Boundary cases, fake clock, readable names, no duplicated rules or magic numbers |
| SQL / database | 9% | Proper migration, AsNoTracking reads, narrow projections, no N+1, filters in SQL before paging, deliberate index |
| Design patterns | 8% | Two or three that genuinely fit, each justified in the README |
| Git & documentation | 5% | Small focused commits, defect and filtering committed separately, readable history in the fork |
| Requirements & delivery | 4% | Edge cases handled, assumptions written down |
| Effective AI use | 4% | Honest account of what was used, rejected and verified |

A follow-up conversation will pick a file to walk through, and cover questions such as scaling to 10 million tickets, the weakest part of the authentication, and what would change in the image before production.

---

## README and submission

The README.md (one to three pages) is updated with:

- What was implemented and the rules as understood
- Design decisions: what was chosen, rejected and why (the most carefully read section)
- The defect: root cause, fix, how to verify
- Filtering: where applied, how the SQL was checked, how the next filter is added
- Authentication: credential storage, hashing, token issue and storage, lifetime, production changes
- Development login credentials
- Docker: build and run, environment variables, image size, production migration strategy
- Database: what was added and which index and why
- Testing: how to run, what was tested and what deliberately was not
- AI usage: tools and purpose; a workflow that worked; something accepted as-is; something changed or rejected and why; any bug AI introduced or helped find; how generated code was verified
- Known limitations (this never counts against the candidate)

### Submit

- **Fork** the project repository to the candidate's own GitHub account and do all work in the fork. Do not push to the original repository.
- Commit as work progresses, keeping the real history. A single squashed "initial commit" is not acceptable.
- Make the fork **private** if preferred, and add the reviewers as collaborators; otherwise leave it public.
- Submit the **link to the fork**, the development login credentials, and a rough note of time spent and on what.
- The README must let a reviewer run the submission without asking the candidate anything.

### Out of scope

Registration or password reset, roles and permissions, OAuth/SSO, refresh tokens, MFA, notifications, background jobs, real-time updates, microservices, deployment beyond the Task 4 image, CI, caching, event sourcing, multi-tenancy, internationalisation, visual redesign. Write a sentence under Known limitations instead of building any of these.
