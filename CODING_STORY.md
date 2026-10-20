# The Coding Story of Northwind Support Desk

This is the beginning-to-end story of what changed in the code. It deliberately leaves out
installation, commands, ports, and startup instructions. Those operational details belong in
`README.md`. Here, the goal is to understand and defend the coding decisions.

Whenever a technical word first matters, its usual software name appears in brackets.

## 1. The world we started with

Northwind Support Desk already had two connected programs: a React website [frontend] and a .NET
service [backend/API] that talks to SQL Server [database]. The backend was split into four areas
[layers]:

| Story role | Technical name | Responsibility |
| --- | --- | --- |
| The rule book | Domain layer | The Ticket and rules that must always be true |
| The coordinator | Application layer | Steps for a use case, such as raising a ticket |
| The store room | Infrastructure layer | Reading and writing database data |
| The front counter | Presentation layer | Receiving HTTP requests and returning responses |

This separation matters [separation of concerns]. A controller should not decide who receives a
ticket, and a database query should not decide whether a Critical ticket can be escalated.

The starting app could create, view, assign, and update tickets. The work added three main chapters:
repair filtering, automate triage and escalation, and protect the app with login. The optional API
container was not implemented.

## 2. Chapter one: the filter that looked awake but was asleep

Choosing a status changed the value on screen, but the website did not ask the server for new
tickets. Typing in Search later did send a request, carrying the older status too.

The cause lived in
[`useTicketList.ts`](apps/web/src/features/tickets/hooks/useTicketList.ts). React was told to watch
only Search and page values [effect dependencies]. Status, priority, category, customer, agent,
unassigned-only, and sorting could change without waking the loading effect [useEffect].

The fix separated two kinds of change:

1. Search waits 300 milliseconds after typing stops [debounce], avoiding a request after every key.
2. Every select box, checkbox, sort, and page change immediately updates `requestFilters`. When that
   object changes, the ticket request runs on the next event-loop turn.

`updateFilters` also returns to page 1 after a normal filter change. Otherwise, someone on page 4
could choose a narrow filter and see nothing even though matching tickets exist on page 1.

A regression test [a test that prevents an old bug from returning] changes only Status and proves
that an immediate request contains `InProgress` and page 1. The shared hook was fixed once instead of
adding separate request code to every filter control.

Commit: `650b36d` — `fix(web): request tickets when list filters change`.

## 3. Chapter two: make the database do the searching

The website could now send every filter, but the server still counted and paged all tickets. The
request values existed without being applied to the database question.

[`TicketQueries.cs`](apps/api/src/SupportDesk.Infrastructure/Queries/TicketQueries.cs) now builds one
unfinished database question [IQueryable], then adds conditions [Where clauses] for each supplied
filter: status, priority, category, customer, agent, unassigned-only, trimmed Search, and later SLA
state. Search covers ticket reference, title, and customer name.

Only after filtering does the code count, sort, skip earlier pages, take the requested page, and
shape rows into response objects [projection/DTO]. Entity Framework [ORM] translates the unfinished
question into SQL, so SQL Server performs the work.

The order is important:

`join labels → filter → count → sort → skip/take → shape the response`

Paging before filtering would produce false pages and totals. Converting rows into a C# list too
early [materializing] would waste memory and move database work into the API. A stable second sort
by ticket ID keeps pages predictable when tickets share the first sort value [deterministic order].

SQLite-backed relational tests prove exact filtering, combined filters, customer-name search, and
filtered counts.

Commit: `171a598` — `feat(api): apply ticket filters before count and paging`.

## 4. Chapter three: give the app a guarded front door

The original app trusted anyone who could reach it. Authentication added an identity check
[authentication] before protected work.

### 4.1 Keeping credentials safely

Every user in this exercise is already an `Agent`, so the password hash was added to that row. A
second one-to-one users table was rejected because it added another record and lifecycle without
representing a different kind of user.

Plain passwords are never stored. ASP.NET Identity's `PasswordHasher<Agent>` creates a salted,
one-way value [PBKDF2 password hash], and development hashes are made during data seeding. Unknown
email, wrong password, and inactive agent all receive the same message, preventing the API from
revealing which accounts exist [account-enumeration protection].

The database change is recorded in
[`AddAgentAuthentication`](apps/api/src/SupportDesk.Infrastructure/Data/Migrations/20261019150252_AddAgentAuthentication.cs)
[EF Core migration]. A migration is a versioned description of a database-shape change.

### 4.2 Issuing and checking the pass

[`AuthController.cs`](apps/api/src/SupportDesk.Presentation/Controllers/AuthController.cs) verifies
the password and asks
[`JwtTokenService.cs`](apps/api/src/SupportDesk.Presentation/Auth/JwtTokenService.cs) for a signed pass
[JSON Web Token/JWT]. It contains agent ID, name, email, a unique token ID, and a 60-minute expiry.

Signing settings come from configuration, not a real secret in source code. A central fallback rule
[authorization policy] protects all controllers unless an action is deliberately public. This is
safer than remembering to protect every new endpoint.

### 4.3 Remembering the signed-in agent in React

The browser stores the token, expiry, and display data in `localStorage` [browser storage].
[`AuthProvider.tsx`](apps/web/src/features/auth/AuthProvider.tsx) owns the session for the React tree
[context provider]. [`RequireAuth.tsx`](apps/web/src/features/auth/RequireAuth.tsx) guards private
pages [route guard], remembers the requested address, and returns there after login.

The shared [`client.ts`](apps/web/src/api/client.ts) attaches the token to every call using the
`Authorization: Bearer ...` header. Any `401 Unauthorized` clears the session and tells the app that
login is required. Logout uses the same clearing path.

`localStorage` is simple, but malicious injected JavaScript [XSS] could read it. A production design
should consider a secure HttpOnly cookie with CSRF protection.

Backend commit: `26c79c7`. Frontend commit: `26b6982`.

## 5. Chapter four: teach the system to triage a ticket

Manual triage was replaced by three small decision makers [policies] in
[`TriagePolicies.cs`](apps/api/src/SupportDesk.Application/Features/Tickets/Triage/TriagePolicies.cs).

### 5.1 Priority policy

Requested priority is accepted when present. Missing priority becomes Medium. A category flag can
force Critical. The rule checks `ForcesCriticalPriority`, not category names, so renaming “Security”
does not break behaviour [data-driven rule].

### 5.2 SLA policy

An SLA [service-level agreement] is the promised response window:

| Priority | Standard | Premium |
| --- | ---: | ---: |
| Low | 72 hours | 36 hours |
| Medium | 24 hours | 12 hours |
| High | 8 hours | 4 hours |
| Critical | 4 hours | 2 hours |

Premium uses half the normal window, never below one hour. The values, multiplier, minimum, and
at-risk percentage come from configuration. The policy returns both the chosen window and a readable
reason [decision explanation].

### 5.3 Assignment policy

[`AgentRepository.cs`](apps/api/src/SupportDesk.Infrastructure/Repositories/AgentRepository.cs)
asks for active agents. A specialist category keeps only matching specialists. The query counts open
tickets, removes full agents, orders by smallest load, and uses agent ID as a predictable tie-break.

No eligible agent is a valid outcome. The ticket is saved unassigned with an explanation instead of
rejecting the customer's request.

### 5.4 The coordinator joins the decisions

[`RaiseTicketCommandHandler.cs`](apps/api/src/SupportDesk.Application/Features/Tickets/Commands/RaiseTicket/RaiseTicketCommandHandler.cs)
loads the customer and category, asks the three policies, creates the Ticket, applies the due date and
owner, saves once [unit of work], then returns the full result and all three reasons.

The Ticket owns changes that must remain valid [aggregate/domain model]. Its private setters force
callers through meaningful methods such as `ApplyTriage`, `ChangeStatus`, and `Escalate`.

## 6. Chapter five: tell whether the promise is safe, risky, or broken

SLA status is calculated, not stored [derived state]. A stored status would become wrong as time
passes even when nobody edits the row.

[`SlaEvaluator.cs`](apps/api/src/SupportDesk.Domain/Aggregates/Tickets/SlaEvaluator.cs) says:

- no due date → `NotApplicable`;
- resolved on or before due → `Met`;
- resolved late, or still open after due → `Breached`;
- still open with 25% or less remaining → `AtRisk`;
- otherwise → `WithinSla`.

The Ticket stores `SlaStartedAtUtc` and `SlaWindowMinutes`, not the status. `UpdatedAtUtc` was rejected
because an ordinary edit would accidentally restart the SLA clock. Current time is injectable
[dependency injection], so tests can stand at exact boundary moments.

The list's SLA filter expresses the same conditions in database-friendly form so SQL Server filters
before count and paging. The normal C# evaluator is not called inside database `Where`, because it
may not translate to SQL [query translation].

## 7. Chapter six: escalation with a permanent memory

[`EscalateTicketCommandHandler.cs`](apps/api/src/SupportDesk.Application/Features/Tickets/Commands/EscalateTicket/EscalateTicketCommandHandler.cs)
coordinates the whole operation:

1. Load ticket, customer, and category.
2. Ask the Ticket for exactly the next priority.
3. Reject Critical, Resolved, or Closed tickets [business invariant].
4. Start a fresh SLA window at the escalation time.
5. Keep the agent if still eligible; otherwise choose again.
6. Create an unchangeable before-and-after record [immutable audit record].
7. Save the ticket and history together [transaction/unit of work].

The reason must contain 5–500 characters. The actor comes from the signed token's claims, not from
untrusted request text.

[`TicketEscalation.cs`](apps/api/src/SupportDesk.Domain/Aggregates/Tickets/TicketEscalation.cs) records
old and new priority, owner, due date, reason, actor, and time. Restrictive foreign keys prevent a
related deletion from silently erasing history. A combined ticket/time index [composite index] helps
the newest-first history query.

The matching migration is
[`AddTicketEscalationAndSlaWindow`](apps/api/src/SupportDesk.Infrastructure/Data/Migrations/20261019153333_AddTicketEscalationAndSlaWindow.cs).

## 8. Chapter seven: show the intelligence to the user

The React side was extended without creating a second copy of server rules:

- `SlaCell` uses words and colour [accessibility].
- `TicketFilters` adds SLA to the general filter flow fixed earlier.
- `CreateTicketPage` shows the returned priority, SLA, and assignment reasons.
- `EscalationPanel` validates the reason, disables itself while saving, and shows server errors.
- `EscalationHistory` shows every change, who made it, why, and when.
- `TicketDetailPage` refreshes ticket and history without reloading the browser page.

Browser validation improves the experience, but the server validates again because a client is not
a final guard [defence in depth]. Hiding an invalid action also does not replace backend rules; the
page may hold stale information.

Backend commit: `276f772`. Frontend commit: `88f2e5d`.

## 9. Chapter eight: prove behaviour instead of trusting it

| Test kind | What it protects |
| --- | --- |
| Domain tests [unit tests] | Ticket lifecycle and exact SLA boundaries |
| Policy tests [unit tests] | Priority, Premium windows, floor, and assignment |
| Handler tests [use-case tests] | Creation, escalation, and failure paths |
| Query tests [relational tests] | Filters, counts, paging, and agent selection |
| Authentication tests [API behaviour tests] | Login failures and protected requests |
| React tests [component/hook tests] | Filters, SLA labels, validation, errors, and tokens |
| Architecture tests | The four layers keep allowed dependencies |

The recorded final result was 55 unit tests, 4 architecture tests, and 12 frontend tests, plus lint,
type checking, a production build, database migration, and a live SQL Server
create/filter/escalate/history flow. That is a historical project record; this documentation-only
edit did not rerun the application.

## 10. The real commit journey

| Order | Commit | What changed |
| ---: | --- | --- |
| 1 | `650b36d` | Repaired immediate frontend filtering |
| 2 | `171a598` | Applied server filters before count and paging |
| 3 | `26c79c7` | Added passwords, login, JWTs, and API protection |
| 4 | `26b6982` | Added browser session, login page, and protected routes |
| 5 | `276f772` | Added triage, SLA, escalation, persistence, and backend tests |
| 6 | `88f2e5d` | Added SLA and escalation experiences to the website |
| 7 | `6a18730` | Recorded Task 2 decisions and verification |

Task 3's commits were recorded before Task 2's final feature commits. This table preserves the real
history rather than inventing a cleaner story.

## 11. Patterns worth remembering

1. Put rules beside the thing they protect [domain model]. A Ticket refuses an impossible escalation
   even if a screen makes a mistake.
2. Let small decision makers answer one question [policy pattern]. Priority, SLA, and assignment can
   be tested and changed independently.
3. Depend on promises, not storage details [repository and dependency injection]. Handlers ask
   interfaces for data and time; tests replace them with controlled versions [test doubles].
4. Build one database question and add conditions [composable query]. Correct totals and later
   filters then stay simple.

## 12. What was deliberately left outside

The code does not add registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
notifications, background escalation, business-calendar SLA hours, or production secret management.
The optional API container is also not implemented.

## 13. One ticket's whole journey

`agent signs in → browser receives a signed token → agent creates a ticket → API validates it →
policies choose priority, deadline, and owner → Ticket accepts triage → database saves it → API
returns facts and reasons → React shows them → time changes the derived SLA state → agent escalates
with a reason → Ticket enforces one-level escalation → SLA and owner are reconsidered → an audit row
preserves before and after → the refreshed page shows the new ticket and its history`

If you can explain that journey and why each decision belongs where it does, you understand the
important coding work in this repository.
