# ADR-0012: Serve the booking console from the API

- Status: accepted
- Date: 2026-10-23

## Context

Reviewers need to see concurrency behavior, not just read tests. A separate
single-page application would add a Node toolchain, a second CI pipeline, CORS
and a deployment artifact for what is essentially a diagnostic console.

## Decision

Ship a dependency-free console at `/ui`, served by `UseStaticFiles` from
`wwwroot/ui`, with `/` redirecting to it.

- Plain ES modules and CSS; no build step, no CDN, no external fonts.
- Same-origin requests, so no CORS and no duplicated DTOs.
- Features: seat map by section and row, availability polling, hold/confirm/
  cancel, a two-step cancel confirmation, idempotent replay, a concurrent race
  simulator, a request log and an event creation dialog.
- The admin key is never baked into the page except as a convenience prefill on
  `localhost`; otherwise it is entered and stored in `localStorage`.
- The UI is English-only and uses `en-US` number/currency formatting.

The console also acts as a manual acceptance test: it exercises the same public
API as any external client, including error handling.

## Consequences

- One deployable artifact and one image; the UI is always version-matched to the
  API.
- Static assets are served by Kestrel; a CDN is unnecessary for an internal
  console. A production public UI should move to a CDN or separate app.
- No component framework means more manual DOM code, but zero supply-chain
  surface and instant startup.
- The UI cannot test customer authentication because the API does not have it;
  that is an explicit known limitation.

## Alternatives considered

- Vue/React SPA: better for a large product UI, overkill for a diagnostic console
  and adds tooling and deployment complexity.
- Scalar only: good for API exploration, not for simulating races or visualizing
  seats.
- Server-rendered Razor pages: possible, but couples UI changes to API builds
  without a real benefit here.
