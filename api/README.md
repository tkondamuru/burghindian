# Static Web Apps Managed API

This folder contains the public website API for Azure Static Web Apps, implemented in C# Azure Functions.

Implemented endpoints:

- `GET /api/auth/session`
- `GET /api/events`
- `POST /api/events`
- `PUT /api/events/{partitionKey}/{rowKey}`
- `GET /api/businesses`
- `POST /api/businesses`
- `PUT /api/businesses/{partitionKey}/{rowKey}`
- `POST /api/posts/lookup`

Keep this API separate from `telegram-ai-functions/`, which contains the Telegram bot, Gemini AI extraction, and admin automation Functions project.

## Agent ingest endpoints (`IngestFunctions.cs`)

Trusted-automation write access to Table Storage without the Gmail-auth web
flow. Used by Marley to create/update/delete listings from chat.

- `POST /api/ingest/events` — create event (same required fields and
  validation as `POST /api/events`; returns `{ success, editCode, id }`)
- `PUT /api/ingest/events/{partitionKey}/{rowKey}` — partial update; only
  fields present in the JSON body are validated and overwritten
- `DELETE /api/ingest/events/{partitionKey}/{rowKey}` — delete event and its
  edit-code lookup row; body must be `{ "confirm": true }`
- `POST /api/ingest/businesses` — create business (category validated against
  the catalog)
- `PUT /api/ingest/businesses/{partitionKey}/{rowKey}` — partial update
- `DELETE /api/ingest/businesses/{partitionKey}/{rowKey}` — delete business;
  body must be `{ "confirm": true }`

Auth: every request must carry the shared secret in the `X-Ingest-Key`
header. The expected value comes from the `INGEST_API_KEY` app setting. The
endpoint fails closed — when the setting is missing or empty, all requests get
500; a wrong key gets 401. Comparison is constant-time.

Setup:

1. Generate a long random secret and add it as the `INGEST_API_KEY`
   application setting on the Static Web App (Azure portal → the SWA →
   Configuration → Application settings).
2. Store the same secret in Marley's Secure Vault as a custom header
   (`X-Ingest-Key`) so chat-driven ingest calls are authenticated.

New rows get `Source`/`SubmitterEmail` = `agent-ingest`, `IsApproved = true`,
and a generated edit code, so the on-screen web edit flow keeps working for
agent-created posts.
