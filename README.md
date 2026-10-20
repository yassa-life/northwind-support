# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

## Documentation map

- **[`CODING_STORY.md`](CODING_STORY.md)** — the complete beginning-to-end coding story, written in
  plain language with technical terms introduced in brackets. It explains the code, not how to run it.
- **[`APPROACH.md`](APPROACH.md)** — implementation decisions, rejected alternatives, evidence, and
  honest work status.
- **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)** — the original requirements.

## Implementation status

| Work | Status | Main commits |
| --- | --- | --- |
| DEFECT-117 and server-side filtering | Complete | `650b36d`, `171a598` |
| Automatic triage, SLA, assignment, and escalation | Complete | `276f772`, `88f2e5d` |
| Agent login and protected application | Complete | `26c79c7`, `26b6982` |
| Optional API container | Not implemented | — |
| Coding explanation and implementation report | Complete | `CODING_STORY.md`, `APPROACH.md` |

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

## Candidate implementation notes

### DEFECT-117

The ticket-list request effect originally observed only search and paging values. Changes to
status, priority, category, customer, assigned agent, unassigned-only, or sorting updated React
state but did not trigger another API request. A later search change exposed the stale behaviour
because it caused the request to run with all the latest filter values.

The fix separates the debounced search value from the request filters. Search waits until typing
settles, while dropdown, checkbox, sorting, and paging changes request tickets immediately. A
regression test changes only the status filter and verifies that the request is sent immediately
with `status=InProgress` and `page=1`.

### Server-side ticket filtering

Ticket filters are applied to the EF Core `IQueryable` before `CountAsync`, sorting, `Skip`, and
`Take`. Exact-match filters, unassigned-only, and trimmed search conditions compose using AND, so
the returned items, `totalCount`, and pager describe the same filtered set. Search covers ticket
title, reference, and customer name, while blank search text is ignored.

SQLite query tests cover exact priority, combined filters, customer-name search, and filtered
counts without requiring the development SQL Server.

### Ticket triage, SLA and escalation

Creating a ticket now performs automatic triage. Requested priority defaults to Medium, while flagged
categories force Critical. SLA windows are configurable (Low 72 h, Medium 24 h, High 8 h, Critical 4 h),
Premium customers receive half the window with a one-hour minimum, and the response explains every decision.

Assignment considers only active agents below their open-ticket capacity. Specialist categories require the
matching specialization; the lowest open-ticket count wins, with agent ID as the deterministic tie-break.
If nobody is eligible, the ticket remains unassigned and the explanation says so.

SLA state is derived as `NotApplicable`, `WithinSla`, `AtRisk`, `Breached`, or `Met`. A dedicated SLA start timestamp and
window length are stored because ordinary edits change `UpdatedAtUtc` and must not restart the clock. At Risk
begins when exactly 25% of the current window remains. The SLA Status list filter is applied in SQL before
counting and paging.

Agents can escalate non-terminal, non-Critical tickets with a 5–500 character reason. Escalation raises
priority by one level, starts a new SLA window from the escalation time, re-evaluates assignment, and appends
an immutable audit record containing before/after priority, assignee and due date plus the authenticated actor.
The detail UI refreshes the ticket and history after success and shows validation or business-rule errors.

### Login and authentication

Agents authenticate with their existing email and a one-way ASP.NET Identity password hash stored
on the `Agent` row. A separate users table was rejected because this application has only one user
type and the additional one-to-one lifecycle would add complexity without separating any real
responsibility yet.

`POST /api/auth/login` returns a signed JWT containing the agent ID, name, and email. Tokens expire
after 60 minutes. Unknown email, incorrect password, and inactive-agent attempts all return the same
generic `401` response. `GET /api/auth/me` returns the signed-in agent, and a fallback authorization
policy protects every other controller action. The login endpoint and development Swagger UI remain
public. JWT settings come from configuration and can be overridden with the `Jwt__...` environment
variables shown in `.env.example`; production must provide a long random signing key.

The web app stores the session in `localStorage`, attaches the bearer token through the shared API
client, protects application routes, returns users to their originally requested page, and clears
the session after logout or any `401`. `localStorage` keeps the exercise simple across page reloads,
but JavaScript can read it, so an XSS vulnerability could steal the token. A production system would
normally consider a secure, HttpOnly cookie alongside CSRF protection and a fuller session design.

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

### Development login

Use any active seeded agent. For example:

```text
Email: alex.turner@northwind-support.example
Password: LocalAgent123!
```

The password is an obvious local fixture. It is hashed during development seeding and must not be
used outside local development.

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

The complete verified result is 55 unit tests, 4 architecture tests and 12 frontend tests. ESLint and the
production TypeScript/Vite build also pass. Automated tests do not need a database; the Task 2 migration and
live create/filter/escalate/history workflow were additionally verified against local SQL Server.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `POST /api/auth/login` | Public login; returns a JWT, expiry, and agent details |
| `GET /api/auth/me` | Current authenticated agent |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `slaStatus`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Filters are applied before counting and paging. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create and automatically triage a ticket; returns decision explanations |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `POST /api/tickets/{id}/escalate` | Raise priority, restart SLA, re-evaluate assignment and append history |
| `GET /api/tickets/{id}/escalations` | Newest-first immutable escalation history |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.
Except for login and development Swagger, API access requires `Authorization: Bearer <token>`.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Known limitations

- There is no registration, password reset, email verification, OAuth/SSO, refresh token, MFA,
  account lockout, rate limiting, or roles matrix; these are deliberately outside the assignment.
- The browser token storage is intentionally simple and has the XSS trade-off described above.
- HTTPS termination, secret rotation, and production identity administration are deployment concerns
  and are not implemented in this local exercise.
- SLA clocks use wall-clock hours rather than business calendars; notifications and automatic background
  escalation are outside Task 2.

## AI usage

Codex was used to inspect the starter code, trace DEFECT-117, and help implement the filtering,
authentication, triage, SLA, escalation, migrations, tests, and documentation. Suggestions were
reviewed against the requirements and checked with focused tests, full suites, lint, type checking,
builds, migrations, and a live SQL Server workflow. One request-effect approach was changed after a
React lint rule exposed a synchronous loading-state update. Two Entity Framework query shapes were
also corrected when live SQL Server verification exposed translation problems. `CODING_STORY.md`
explains the accepted design in learning-friendly language.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |
