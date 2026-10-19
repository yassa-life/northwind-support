# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

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

Both pass on a clean checkout and neither needs a database.

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
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Filters are applied before counting and paging. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
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

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |
