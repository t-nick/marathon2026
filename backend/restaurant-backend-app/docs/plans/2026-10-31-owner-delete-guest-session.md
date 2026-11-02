# Owner Delete Guest Session Plan

## Requirement

Allow the reservation Owner to delete a guest session from a group reservation:

`DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}`

- Only the reservation Owner may perform the deletion, using the existing
  authenticated owner validation.
- Return `400 Bad Request` when the target session belongs to a different
  group.
- Return `404 Not Found` when the target session does not exist.
- On success, delete the session and return the updated group snapshot in the
  existing `{ "participants": [...], "total": n }` shape. The overall `total`
  must reflect the remaining participants after deletion.

## Assumptions

- Use the existing authenticated owner validation and established response
  DTO/service pattern.
- The route, handler, and service method appear to exist already. Their
  behavior, including authorization, group matching, error mapping, deletion,
  and recalculation of the snapshot, still needs verification; retain compliant
  code rather than duplicating it.
- Map session-not-found and group-mismatch conditions according to the
  requirement: `404` for a missing session and `400` for a session in a
  different group.
- Existing handler conventions determine the response body and representation
  of these status codes.

## Out of scope

- Deployment, AWS, or infrastructure changes.
- Frontend changes or unrelated endpoints.
- Changes beyond those needed to satisfy this endpoint contract.

## Tasks

### Lambda implementation

- Verify the existing `DELETE` route registration, handler, service contract
  and implementation, response DTO, owner authorization, and error mapping
  against the requirement.
- Preserve existing compliant behavior. Make only targeted corrections for
  verified gaps; do not duplicate an implementation that already satisfies
  the contract.
- Ensure successful deletion produces the updated `{participants,total}`
  snapshot with the new overall total.

### Unit tests

- Verify or add focused handler and service tests for owner-only authorization,
  session-not-found (`404`), group mismatch (`400`), and successful deletion.
- Confirm the success response excludes the deleted session and reports the
  updated overall total.

### Code review

- Review the verified implementation and focused tests against the endpoint
  contract and existing backend patterns.
- Address any findings before marking the work complete.

## Acceptance criteria

- [ ] `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` is
  registered and handled.
- [ ] Only the reservation Owner can delete a session.
- [ ] A target session belonging to a different group returns `400 Bad
  Request`; a session that does not exist returns `404 Not Found`.
- [ ] Successful deletion returns the updated group snapshot in the existing
  `{ "participants": [...], "total": n }` shape, with the deleted session
  removed and the overall total recalculated.
- [ ] Focused handler and service tests cover the contract and pass, and code
  review is complete.
- [ ] No deployment, AWS, or infrastructure changes are made.

## Status

**Plan written. Lambda implementation, unit tests, and code review: pending.**
Existing route/handler/service code needs verification before any changes are
made.

## Outcomes

- **Plan written — complete.**
- **Lambda implementation — pending verification and any targeted fixes.**
- **Unit tests — pending verification and any necessary additions.**
- **Code review — pending.**
- No deployment, AWS, or infrastructure changes are in scope.
