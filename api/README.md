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
