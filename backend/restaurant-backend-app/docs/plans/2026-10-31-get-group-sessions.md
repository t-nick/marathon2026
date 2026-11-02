# GET Group Sessions Implementation Plan

## Status

**Plan written. Lambda implementation, unit tests, and code review: complete. Fix cycle: complete (not needed).**

The existing handler, service method, route registration, response DTOs, and related tests were verified against the acceptance criteria below. The implementation satisfies the contract, so no production changes were necessary.

## Objective

Provide the reservation Owner with a view of all joined group-reservation participants, their ordered dishes and quantities, each participant's total, and the overall total through:

`GET /guest/groups/{groupReservationId}/sessions`

## Observable behavior

- Read the authenticated user's Cognito `sub` claim and use it to validate that the caller owns the group reservation. Do not return participant details to non-owners.
- On success, return `200 OK` with the existing response DTO shape:

  ```json
  {
    "participants": [
      {
        "id": "session-id",
        "type": "Owner | Guest",
        "name": "participant-name",
        "items": [
          {
            "dish": {
              "id": "dish-id",
              "name": "dish-name",
              "price": 12.34
            },
            "quantity": 2
          }
        ],
        "total": 24.68,
        "isSubmitted": false
      }
    ],
    "total": 24.68
  }
  ```

- Include all joined sessions, including the owner's session when one exists. Identify that participant as `Owner`; identify other participants as `Guest`. Use the owner's Cognito `given_name` for the owner participant's name.
- Include each resolvable saved dish with its price and quantity. Calculate each participant's `total` as the sum of dish price multiplied by quantity, and the response `total` as the sum of participant totals.
- Preserve the existing DTO behavior for no joined sessions (empty `participants`, zero `total`) and for saved dishes that no longer resolve (omit the missing dish from the items and totals).
- Return `401 Unauthorized` when the `sub` claim is missing. Return `403 Forbidden` when the caller is not the reservation owner. Return `404 Not Found` when the group reservation or its parent reservation does not exist.

## Existing patterns and implementation surfaces

Preserve the established authenticated Cognito `sub` flow, reservation-owner validation, existing handler/service/routes, existing response DTOs, and owner-session inclusion behavior.

- `dnapp/lambdas/api-handler/Routing/RouteRegistry.cs` — existing GET route registration.
- `dnapp/lambdas/api-handler/Handlers/GuestSessions/GetGroupSessionsHandler.cs` — path parameter and `sub` handling, service invocation, and 401/403/404 mapping.
- `dnapp/lambdas/api-handler/Services/GuestSessions/GuestSessionService.cs` — owner validation, session/dish lookup, participant mapping, and total calculations.
- `dnapp/lambdas/api-handler/Services/GuestSessions/IGuestSessionService.cs` — service contract.
- `dnapp/lambdas/api-handler/Models/GuestSessions/GetAllResponse.cs` — existing response DTO definitions.
- `Tests/Handlers/GuestSessions/GetGroupSessionsHandlerTests.cs` — handler contract and HTTP status coverage.
- `Tests/Services/GuestSessionServiceTests.cs` — owner authorization, participant/dish mapping, totals, missing-resource, and empty-session behavior.
- `docs/ApiRoutes.md` — existing endpoint response and authorization documentation; keep it consistent if implementation verification finds any required contract correction.

## Implementation and verification steps

1. Verify route registration, handler claim extraction, owner validation, response DTOs, owner-session inclusion, dish quantities, participant totals, overall total, and error mapping against the observable behavior above.
2. Keep the existing implementation and behavior if it meets the criteria. Make only targeted corrections where verification identifies a gap; do not replace established patterns or DTOs.
3. Run focused handler and service tests. Add or adjust focused tests only for any uncovered behavior or regression found during verification.
4. Confirm route/API documentation matches the verified observable response and expected status codes.

## Acceptance criteria

- [x] The route resolves to the existing GetGroupSessions handler.
- [x] An authenticated reservation Owner receives all joined sessions, including the owner's session if present, with participant identity/type, dish details, quantities, submission state, participant totals, and the overall total in the existing DTO shape.
- [x] Participant and overall totals are consistent with dish prices multiplied by quantities; an empty session set returns an empty participant list and zero total.
- [x] A missing Cognito `sub` returns `401`; a non-owner receives `403`; missing group or parent reservation receives `404`.
- [x] Focused handler/service tests pass, and API route documentation agrees with the verified contract.

## Completion

### Outcomes

- **Plan written — complete.**
- **Lambda implementation — complete.** Implementation review found the existing route, handler, service, and DTO satisfy the requested behavior; no production code was changed.
- **Unit tests — complete.** No test changes were needed.
- **Code review — complete.** Backend Code Reviewer verdict: approve; no findings.
- **Fix cycle — complete (not needed).** No fixes were required.
- `docs/ApiRoutes.md` was updated to match the verified contract.
- `dotnet build restaurant-backend-app.sln` succeeded with 0 errors and 75 warnings.
- Full `dotnet test Tests/APIHandler.Tests.csproj` by the implementation stage succeeded with 154 passed, 0 failed.
- Targeted `dotnet test backend/restaurant-backend-app/Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~GuestSessionServiceTests|FullyQualifiedName~GetGroupSessionsHandlerTests"` succeeded with 49 passed, 0 failed, 0 skipped.
- No deployment or AWS changes were made.
