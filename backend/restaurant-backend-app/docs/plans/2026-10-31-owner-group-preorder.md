# Owner Group Preorder Implementation Plan

## Requirement

Implement the owner group-preorder flow in the restaurant backend:

1. `GET /reservations/{reservationId}/guest-session-id` returns
   `{ "guestSessionId": "..." }` for the authenticated owner session associated
   with that reservation.
2. `POST /group-reservations/{groupReservationId}/submit` accepts a
   `reservationId` and is callable only by the reservation Owner. It loads only
   submitted guest sessions, aggregates their dishes by `dishId`, adds the
   consolidated dishes to the existing order, sets the Order status to
   `SubmittedByCustomer` and the GroupReservation status to `Submitted`, and
   responds with an array of aggregated `{ "dishId": "...", "quantity": n }`
   entries.
3. Return `404 Not Found` when the reservation or group reservation is
   missing. Return `400 Bad Request` when there are no submitted sessions,
   submitted sessions have no dishes, or the order is not found.

## Assumptions

- The authenticated caller identity and owner authorization must follow the
  backend's established Cognito claim and reservation-owner validation
  patterns. The Lambda implementer will verify the current patterns and record
  the exact claim, authorization behavior, and corresponding `401`/`403`
  conventions before implementing.
- Existing handler error-response conventions determine the response body and
  how the specified `400` and `404` cases are represented. The Lambda
  implementer will verify and preserve those conventions.
- The submit request body contains `reservationId`; validation and malformed
  body handling should follow existing API-handler conventions.
- The response for submit is the aggregated dish-id/quantity array; its
  enclosing response DTO and serialization should follow existing backend API
  conventions.
- The submit flow updates the existing order and group reservation rather than
  creating a replacement order. Existing persistence and status-transition
  patterns govern how these updates are performed.

## Out of scope

- Deployment, AWS, or infrastructure changes.
- Any changes to the tagged Terraform variables file; it is outside scope.
- Changes to unrelated endpoints, frontend behavior, or order workflows.
- Making implementation, test, API-documentation, or infrastructure changes as
  part of authoring this plan.

## Tasks

### Lambda implementation — Owner Lambda Implementer

- Verify owner authentication/authorization and error conventions, and refine
  the assumptions above with the verified patterns.
- Implement route registration, handlers, service/repository operations, and
  response models as needed for both endpoints.
- Ensure the GET resolves the owner guest-session ID for the requested
  reservation and the POST only loads submitted sessions and aggregates
  quantities by dish ID before updating the existing order and group
  reservation.
- Expected production surfaces in `backend/restaurant-backend-app`:
  `dnapp/lambdas/api-handler/Routing/RouteRegistry.cs`,
  relevant reservation and guest-session handlers/services/repositories/models,
  and any directly required order or group-reservation persistence surfaces.
- Update `docs/ApiRoutes.md` to document the implemented request, response,
  authorization, and error behavior.
- Do not modify deployment or AWS infrastructure, including the tagged
  Terraform variables file.

### Unit tests — Backend Unit Test Writer

- Add or update focused handler and service tests for owner authorization,
  owner-session lookup, submitted-session filtering, dish aggregation,
  existing-order updates, and status transitions.
- Cover the specified `400` and `404` cases and the success response contract.
- Expected test surfaces in `backend/restaurant-backend-app/Tests`, including
  the relevant handler and service test files.

### Code review — Backend Code Reviewer

- Review the Lambda implementation, tests, and directly related API route
  documentation against this requirement and the verified backend conventions.
- Report findings; the implementation stage addresses any findings before
  completion.

## Acceptance criteria

- [ ] `GET /reservations/{reservationId}/guest-session-id` returns the owner
  session ID in the required JSON shape for an authenticated reservation Owner.
- [ ] `POST /group-reservations/{groupReservationId}/submit` is restricted to
  the Owner, accepts `reservationId`, and loads only submitted guest sessions.
- [ ] Submitted-session dishes are consolidated by `dishId`, added to the
  existing order, and returned as aggregated dish-id/quantity entries.
- [ ] The order is set to `SubmittedByCustomer` and the group reservation to
  `Submitted`.
- [ ] Missing reservation or group reservation returns `404`; no submitted
  sessions, submitted sessions with no dishes, or missing order returns `400`.
- [ ] Focused unit tests cover the behavior and pass; API route documentation
  reflects the implemented contract.
- [ ] No deployment, AWS, or infrastructure changes are made; the tagged
  Terraform variables file remains untouched.

## Status

**Implementation, test repair, and final review complete; final verdict:
Request changes.** One fix cycle was used.

## Outcomes

- **Plan written — complete.**
- **Lambda implementation — complete.** The Lambda Implementer updated
  `RouteRegistry`, the guest-session and group-reservation services and
  interfaces, added two handlers and three request/response models, and updated
  `docs/ApiRoutes.md`. The requested behavior was implemented.
- **Stage 2 — complete: unit tests.** Added 26 test cases in
  `GuestSessionServiceTests`, `GroupReservationServiceTests`,
  `GetOwnerGuestSessionIdHandlerTests`, and
  `SubmitGroupReservationHandlerTests`.
- **Scenario coverage.** Tests cover owner guest-session lookup, missing
  reservation/group/session cases, and non-owner denial; submission's
  submitted-session filtering, dish aggregation, existing-order and status
  updates, and missing-resource, empty-session/dish, missing-order, and
  non-owner failures. Handler tests verify successful response contracts,
  authentication and authorization responses, not-found responses, and
  submission validation errors.
- **Focused unit tests — passed.**
  `dotnet test Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~GuestSessionServiceTests|FullyQualifiedName~GroupReservationServiceTests|FullyQualifiedName~GetOwnerGuestSessionIdHandlerTests|FullyQualifiedName~SubmitGroupReservationHandlerTests"`
  => 85 passed, 0 failed.
- **Full unit tests — passed.** `dotnet test Tests/APIHandler.Tests.csproj`
  => 180 passed, 0 failed.
- **Stage 1 build — passed.** `dotnet build restaurant-backend-app.sln`
  completed with 0 errors and 81 warnings.
- **Code review — complete; verdict: Request changes.**
- **Review finding (Critical):** `GroupReservationService.cs:191–213`
  submission is non-idempotent and can partially save order before group
  status, duplicating quantities on retry; recommended atomic/conditional
  DynamoDB transaction and repeat/partial failure tests.
- **Review finding (Warning):** `SubmitGroupReservationHandlerTests.cs` lacks
  tests for missing path parameter, missing reservationId, malformed JSON /
  invalid request, and should verify service not called.
- Route/auth/required errors otherwise appeared consistent and no other
  findings.
- **Fix cycle — Lambda fix complete.** The Lambda Implementer changed the
  Order and GroupReservation entities, order and guest-session repositories
  and interfaces, `GuestSessionService`, and `GroupReservationService` to make
  submission transactional and idempotent. Submission now uses DynamoDB
  `TransactWriteItems`, conditional group state, order version checks, and
  guest-submit revision tracking.
- **Config/infra during the Lambda fix cycle — reported unchanged by the fix
  agent.** The final review separately identified a Syndicate configuration
  diff; its provenance and authorization have not been established (see final
  review findings below).
- **Build after Lambda fix — reported passed by the fix agent;** no exact
  warning count was provided. The later focused and full test runs passed.
- **Test repair — complete.** Tests were changed only under `Tests`. Handler
  invalid-request tests cover a missing path parameter, missing or empty
  `reservationId`, and malformed JSON; each verifies that the service is not
  called. Service tests verify transactional submission and cover retry /
  idempotency and transaction-cancellation scenarios.
- **Focused unit tests — passed.**
  `dotnet test Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~GroupReservationServiceTests|FullyQualifiedName~SubmitGroupReservationHandlerTests"`
  => 42 passed, 0 failed, 0 skipped.
- **Full unit tests — passed.** `dotnet test Tests/APIHandler.Tests.csproj`
  => 192 passed, 0 failed, 0 skipped.
- **Test coverage gap:** Repository-level exact DynamoDB transaction request
  contents and condition expressions are not asserted; tests use the mocked
  repository seam.
- **Operational prerequisite:** The Lambda IAM role must allow
  `dynamodb:TransactWriteItems` on the Orders, GroupReservations, and
  GuestSessions tables. The local role declaration was checked and does not
  explicitly show this permission.
- **Limitation:** Submitted sessions are read through an eventually
  consistent GSI. Revision tracking protects racing guest submits, but a very
  recent earlier submission may not yet be visible. A stronger read path or
  maintained aggregate would remove this limitation.
- **Lambda fix — done.**
- **Test repair — done.**
- No production changes were made during the test stage.
- No deployment was performed. The final review flagged a Syndicate
  configuration diff whose provenance and authorization are unresolved; see
  the final review findings.

### Final code review rerun

- **Code review rerun — complete.**
- **Final verdict — Request changes.**
- **Fix cycle — complete; one cycle used.**
- **Historical finding (Warning):** The submit handler lacked coverage for
  invalid requests. **Fixed** in the one fix cycle: tests cover missing path
  parameter, missing or empty `reservationId`, and malformed JSON, and verify
  the service is not called.
- **Historical finding (Critical):** Submission could be non-idempotent and
  partially persist the order before the group status. **Improved** in the
  fix cycle with a DynamoDB `TransactWriteItems` transaction, conditional
  writes/state checks, order version checks, and guest-submit revision
  tracking. This improvement does not resolve the final snapshot-consistency
  finding below.
- **Unresolved (Critical):** Submitted guest sessions are read through an
  eventually consistent GSI. A recently committed guest submission can be
  omitted even when the revision condition is used. A complete snapshot from
  a strongly consistent source is needed.
- **Unresolved (Warning):** A closed group can still transition to `Submitted`;
  the transaction checks that status changed, but does not require the prior
  status to be `Open`. Reject submission for `Closed` groups.
- **Unresolved (Warning, deployment prerequisite):** The Lambda requires
  `dynamodb:TransactWriteItems` on the Orders, GroupReservations, and
  GuestSessions tables. This permission is not shown in the inspected role
  declaration. No infrastructure edit was made.
- **Unresolved (Warning):** Repository-level tests do not assert the exact
  DynamoDB transaction request, conditions, or serialized attributes; current
  tests use the mocked repository seam.
- **Unresolved (Warning):** The reviewer reports that the Syndicate
  configuration diff contains a literal account ID and account-specific
  buckets. The diff's provenance and authorization have not been established;
  do not change it without confirming it is task-owned. No infrastructure
  edit was made as part of this plan.
- **Latest validation:** Full unit tests passed (192); focused tests passed
  (42). The Stage 1 build passed with 0 errors and 81 warnings. After the fix,
  the agent reported a successful build without an exact warning count, and
  the later test runs passed.
- **Deployment — not performed.**
