# Static Website Submissions With Managed Functions and Table Storage

## Overview

Our website has two public submission types:

- `Event`
- `Business`

Both can have multiple tags.

Those tags come from fixed category lists, not free-form user-created categories.

This is important because:

- the website filter chips depend on a stable tag vocabulary
- users can assign more than one tag to a single post
- tags are a better fit for UI filtering than a single category field

Current fixed tag lists:

### Event tags

```text
Community
Culture
Family
Food
Temple
Professional
Kids
Other
```

### Business tags

```text
Restaurant
Grocery
Temple
Service
Shopping
Education
Health
Other
```

Because a post can have multiple tags, tags should not be used as the Azure Table Storage `PartitionKey`.

Instead, we will use:

- month bucket partitioning for the main content tables
- a separate lookup table for `EditCode`

For user identity, we only need one auth-derived field:

- `SubmitterEmail`

Because we are using Gmail-based authentication, we do not need separate identity fields such as username, display name, or user-entered owner contact for ownership validation.

## Hosting Model

We are using:

- static HTML pages under `site/`
- Azure Static Web Apps managed Functions under `api/`
- Azure Table Storage for persistence

We are not using the separate `.NET` Telegram/Gemini Functions app for the public website submission flow.

Repository structure:

```text
/
  site/
  api/
  telegram-ai-functions/
  docs/
```

## High-Level Flow

1. User opens `site/submit/index.html`.
2. User signs in through Static Web Apps authentication.
3. User fills either Event or Business form.
4. User selects one or more fixed tags.
5. Static page sends JSON to a managed Function endpoint under `/api`.
6. Managed Function validates:
   - authenticated email
   - required fields
   - tag values are from the allowed list
7. Managed Function generates:
   - `EditCode`
   - month bucket partition key like `2026-04`
   - content row key
8. Managed Function saves:
   - main row in `Events` or `Businesses`
   - lookup row in `EditCodeLookup`
9. Managed Function returns the `EditCode`
10. Static page shows the `EditCode` to the user

## Why Month Buckets

We are using a month bucket such as:

```text
2026-04
```

for the main table `PartitionKey`.

This gives us:

- easy cleanup of old posts
- predictable partition naming
- easier archiving and admin browsing
- no dependence on tags for storage layout

Recommended rule:

```text
PartitionKey = yyyy-MM based on CreatedAtUtc
```

Examples:

```text
2026-04
2026-05
2026-06
```

## Why We Need a Lookup Table

Users will retrieve or update their post using `EditCode`.

If `EditCode` is stored only as a normal property in `Events` or `Businesses`, Azure Table Storage lookups become awkward and inefficient because:

- `PartitionKey + RowKey` is the fast path
- arbitrary property lookups are not a strong primary access pattern
- users do not know the month bucket when they enter an `EditCode`

So we will use a dedicated lookup table.

This is the recommended design.

## Main Tables

We will have two main content tables:

- `Events`
- `Businesses`

### Events table

Table name:

```text
Events
```

Columns:

| Column | Type | Notes |
|---|---|---|
| `PartitionKey` | String | Month bucket, example `2026-04` |
| `RowKey` | String | Unique event row id |
| `Title` | String | Required |
| `Date` | String | Required |
| `Time` | String | Optional |
| `Location` | String | Required |
| `Summary` | String | Required |
| `Description` | String | Required |
| `Tags` | String | Comma-separated fixed tags |
| `ImageUrl` | String | Optional image link |
| `EditCode` | String | Server-generated |
| `SubmitterEmail` | String | Derived from authenticated Gmail user |
| `IsApproved` | Boolean | Default `false` for website submissions |
| `CreatedAtUtc` | DateTimeOffset | Server-generated |
| `UpdatedAtUtc` | DateTimeOffset | Server-generated |
| `Source` | String | Example: `website-form` |

### Businesses table

Table name:

```text
Businesses
```

Columns:

| Column | Type | Notes |
|---|---|---|
| `PartitionKey` | String | Month bucket, example `2026-04` |
| `RowKey` | String | Unique business row id |
| `Name` | String | Required |
| `Address` | String | Required |
| `Phone` | String | Optional |
| `Summary` | String | Required |
| `Description` | String | Required |
| `Tags` | String | Comma-separated fixed tags |
| `ImageUrl` | String | Optional image link |
| `EditCode` | String | Server-generated |
| `SubmitterEmail` | String | Derived from authenticated Gmail user |
| `IsApproved` | Boolean | Default `false` for website submissions |
| `CreatedAtUtc` | DateTimeOffset | Server-generated |
| `UpdatedAtUtc` | DateTimeOffset | Server-generated |
| `Source` | String | Example: `website-form` |

## Lookup Table

Table name:

```text
EditCodeLookup
```

Purpose:

- resolve an `EditCode` quickly
- find the real table row without scanning month partitions
- support update and retrieval workflows

Recommended keys:

```text
PartitionKey = edit
RowKey       = EditCode
```

Example:

```text
PartitionKey = edit
RowKey       = A7K2P
```

Columns:

| Column | Type | Notes |
|---|---|---|
| `PartitionKey` | String | Always `edit` |
| `RowKey` | String | The `EditCode` |
| `EntityType` | String | `Event` or `Business` |
| `TargetTable` | String | `Events` or `Businesses` |
| `TargetPartitionKey` | String | Month bucket of the main row |
| `TargetRowKey` | String | Main row key |
| `SubmitterEmail` | String | Authenticated Gmail email |
| `IsApproved` | Boolean | Optional mirror of content row |
| `CreatedAtUtc` | DateTimeOffset | Server-generated |

This lets the website resolve:

```text
EditCode -> table name + partition key + row key
```

very quickly.

## Key Design

### Main content row keys

Recommended:

```text
PartitionKey = yyyy-MM
RowKey       = yyyyMMddHHmmssfff-<short random suffix>
```

Example:

```text
PartitionKey = 2026-04
RowKey       = 20260424163512018-X82M1
```

This gives:

- chronological sorting
- uniqueness
- easy monthly deletion

### Lookup row keys

Recommended:

```text
PartitionKey = edit
RowKey       = EditCode
```

## Managed Function Endpoints

These endpoints should live under the Static Web Apps managed API in `api/`.

### Public submit endpoints

```text
POST /api/events
POST /api/businesses
```

Purpose:

- create new website submissions
- validate login and payload
- create main row + lookup row
- return `EditCode`

### Public retrieve-by-edit-code endpoint

```text
POST /api/posts/lookup
```

Request body:

```json
{
  "editCode": "A7K2P"
}
```

Purpose:

- resolve the `EditCode`
- find the real post
- optionally check authenticated email
- return the editable record metadata

### Public update endpoints

```text
PUT /api/events/{partitionKey}/{rowKey}
PUT /api/businesses/{partitionKey}/{rowKey}
```

Purpose:

- update an existing row
- require `EditCode`
- require authenticated email
- verify against `EditCodeLookup`

### Public read endpoints

```text
GET /api/events
GET /api/businesses
```

Purpose:

- return approved posts only
- optionally support filtering by tag

## Endpoint Behavior

### `POST /api/events`

Input fields:

| Field | Required |
|---|---|
| `Title` | Yes |
| `Date` | Yes |
| `Time` | No |
| `Location` | Yes |
| `Summary` | Yes |
| `Description` | Yes |
| `Tags` | Yes |
| `ImageUrl` | No |

Server actions:

1. validate authenticated email
2. validate required fields
3. validate all `Tags` belong to allowed event tags
4. generate `EditCode`
5. compute month bucket
6. create row in `Events`
7. create row in `EditCodeLookup`
8. return `EditCode`

### `POST /api/businesses`

Input fields:

| Field | Required |
|---|---|
| `Name` | Yes |
| `Address` | Yes |
| `Phone` | No |
| `Summary` | Yes |
| `Description` | Yes |
| `Tags` | Yes |
| `ImageUrl` | No |

Server actions:

1. validate authenticated email
2. validate required fields
3. validate all `Tags` belong to allowed business tags
4. generate `EditCode`
5. compute month bucket
6. create row in `Businesses`
7. create row in `EditCodeLookup`
8. return `EditCode`

### `POST /api/posts/lookup`

Request:

```json
{
  "editCode": "A7K2P"
}
```

Server actions:

1. validate `EditCode`
2. fetch row from `EditCodeLookup` using:
   - `PartitionKey = edit`
   - `RowKey = editCode`
3. optionally validate signed-in email matches `SubmitterEmail`
4. fetch actual content row from `Events` or `Businesses`
5. return content row for edit UI

### `PUT /api/events/{partitionKey}/{rowKey}`

Request body should include:

```json
{
  "editCode": "A7K2P",
  "title": "Updated title",
  "date": "Updated date",
  "tags": ["Food", "Family"]
}
```

Server actions:

1. validate user is authenticated
2. look up `EditCodeLookup`
3. confirm:
   - entity type is `Event`
   - target partition and row match the route
   - email matches
4. validate updated fields
5. validate tags from allowed list
6. update row in `Events`
7. update `UpdatedAtUtc`

### `PUT /api/businesses/{partitionKey}/{rowKey}`

Same pattern as Event updates, but against `Businesses`.

## Tag Storage Format

Recommended storage format in the table:

```text
Food,Family,Culture
```

or

```text
Restaurant,Shopping
```

Recommended server behavior:

- trim whitespace
- remove duplicates
- preserve only allowed values
- store in a canonical order if possible

Example canonical output:

```text
Culture,Family,Food
```

This keeps comparisons predictable.

## Filtering on the Website

The website filter chips should match the fixed tag lists exactly.

Filtering logic should work from the stored tags, not from partition key.

That means:

- `PartitionKey` is only for storage organization by month
- `Tags` drive UI filtering

Example:

- post stored in `Events` with:
  - `PartitionKey = 2026-04`
  - `Tags = Culture,Family`
- front-end filter for `Culture` should show it
- front-end filter for `Family` should also show it

## Authentication

The current submit page uses a client-side Gmail mock only for UI behavior.

Production submission and edit flows should use Azure Static Web Apps authentication.

Frontend:

```js
window.location.href = '/.auth/login/google';
```

Current user:

```js
const response = await fetch('/.auth/me');
```

Managed Functions must derive the user identity from the authenticated request context, not from a browser-submitted email field.

For this website flow, email is the only identity field we need to persist for ownership:

- store `SubmitterEmail`
- validate `SubmitterEmail` during lookup and update
- do not require a separate username or owner id from the user

## Suggested Route Protection

Use `site/staticwebapp.config.json` to protect submit and update routes.

Recommended:

- `POST /api/events` -> authenticated
- `POST /api/businesses` -> authenticated
- `POST /api/posts/lookup` -> authenticated
- `PUT /api/events/*` -> authenticated
- `PUT /api/businesses/*` -> authenticated
- `GET /api/events` -> anonymous
- `GET /api/businesses` -> anonymous

## Suggested API Folder Structure

```text
api/
  package.json
  host.json
  shared/
    auth.js
    tables.js
    tags.js
    editcode.js
  events/
    function.json
    index.js
  businesses/
    function.json
    index.js
  posts-lookup/
    function.json
    index.js
```

## Recommended Shared Helpers

### `shared/tags.js`

Responsibilities:

- define allowed event tags
- define allowed business tags
- normalize comma-separated tags
- reject invalid tags

### `shared/editcode.js`

Responsibilities:

- generate unique `EditCode`
- ensure no collision in `EditCodeLookup` if needed

### `shared/tables.js`

Responsibilities:

- return `TableClient` for:
  - `Events`
  - `Businesses`
  - `EditCodeLookup`

### `shared/auth.js`

Responsibilities:

- decode Static Web Apps identity
- return verified email

## Validation Rules

### Event validation

- `Title` required
- `Date` required
- `Location` required
- `Summary` required
- `Description` required
- at least one tag required
- every tag must be from the event fixed list
- `ImageUrl` optional but must be a valid URL if present

### Business validation

- `Name` required
- `Address` required
- `Summary` required
- `Description` required
- at least one tag required
- every tag must be from the business fixed list
- `Phone` optional
- `ImageUrl` optional but must be a valid URL if present

## Approval Model

Recommended for website submissions:

```text
IsApproved = false
```

This keeps public site content moderated.

Public read endpoints should return only:

```text
IsApproved eq true
```

The `EditCodeLookup` table can still keep references to pending rows.

## Example Main Table Rows

### Event row

| Field | Value |
|---|---|
| `PartitionKey` | `2026-04` |
| `RowKey` | `20260424163512018-X82M1` |
| `Title` | `Biryani Mela` |
| `Date` | `Every weekend` |
| `Tags` | `Food,Family` |
| `EditCode` | `A7K2P` |
| `SubmitterEmail` | `user@gmail.com` |
| `IsApproved` | `false` |

### Business row

| Field | Value |
|---|---|
| `PartitionKey` | `2026-04` |
| `RowKey` | `20260424170244111-Q19LM` |
| `Name` | `Mintt @ Banksville` |
| `Tags` | `Restaurant,Shopping` |
| `EditCode` | `C6B38` |
| `SubmitterEmail` | `user@gmail.com` |
| `IsApproved` | `false` |

### Lookup row

| Field | Value |
|---|---|
| `PartitionKey` | `edit` |
| `RowKey` | `A7K2P` |
| `EntityType` | `Event` |
| `TargetTable` | `Events` |
| `TargetPartitionKey` | `2026-04` |
| `TargetRowKey` | `20260424163512018-X82M1` |
| `SubmitterEmail` | `user@gmail.com` |

## Retrieval and Update Strategy

This is the intended flow:

### Retrieve post for editing

1. user signs in
2. user enters `EditCode`
3. frontend calls `POST /api/posts/lookup`
4. backend reads `EditCodeLookup`
5. backend fetches actual row from `Events` or `Businesses`
6. backend returns row data

### Save updates

1. frontend sends edited row back to `PUT` endpoint
2. backend verifies `EditCode`
3. backend verifies authenticated email
4. backend updates main row
5. lookup row remains unchanged unless metadata needs sync

## Security Rules

- never trust email submitted from browser JSON
- derive authenticated email server-side
- generate `EditCode` server-side only
- validate tags against allowed fixed lists
- require at least one tag
- keep storage credentials only in Azure app settings
- keep `EditCodeLookup` table private to Functions
- keep public list endpoints anonymous
- require authentication for submit, lookup, and update endpoints

## Final Recommendation

Use this model:

- `Events` and `Businesses` as main content tables
- `PartitionKey = yyyy-MM`
- `RowKey = timestamp + random suffix`
- `Tags` as fixed multi-value filter categories
- `EditCodeLookup` table for retrieval by code
- Azure Static Web Apps managed Functions under `api/`
- Static Web Apps auth for user identity

This matches our website needs well:

- easy filtering by fixed tags
- easy cleanup by month bucket
- reliable post retrieval by `EditCode`
- clean separation between public site APIs and Telegram/admin automation
