# Managed API `host.json` Reference

This document explains the `host.json` file used by the managed Azure Functions API in this repo:

- API project: [api](C:\Development\labs\burghindian\api)
- Config file: [api/host.json](C:\Development\labs\burghindian\api\host.json)

The goal of this configuration is to keep logging useful for debugging while avoiding excessive telemetry volume and noise in Application Insights.

## Current file

```json
{
  "version": "2.0",
  "extensionBundle": {
    "id": "Microsoft.Azure.Functions.ExtensionBundle",
    "version": "[4.*, 5.0.0)"
  },
  "logging": {
    "logLevel": {
      "default": "Warning",
      "Host.Results": "Information",
      "Host.Aggregator": "Warning",
      "Function": "Warning",
      "Microsoft": "Warning",
      "Worker": "Warning"
    },
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request;Exception",
        "maxTelemetryItemsPerSecond": 5
      },
      "enableLiveMetricsFilters": true
    }
  }
}
```

## What `host.json` is

`host.json` is the top-level runtime configuration file for Azure Functions.

It controls things like:

- runtime behavior
- extension behavior
- logging
- telemetry volume
- monitoring defaults

For this repo, the most important part is the `logging` section because the API is deployed as managed Functions inside Azure Static Web Apps and we rely on Application Insights for diagnostics.

## `version`

```json
"version": "2.0"
```

This is the Azure Functions host schema version.

Why it matters:

- tells the Functions runtime which config format to expect
- `2.0` is the normal setting for modern Azure Functions v3/v4 style apps

You usually do not need to change this.

## `extensionBundle`

```json
"extensionBundle": {
  "id": "Microsoft.Azure.Functions.ExtensionBundle",
  "version": "[4.*, 5.0.0)"
}
```

This tells Azure Functions which extension bundle range to use.

Why it matters:

- bundles provide common runtime extensions without manually managing each one
- helps the host resolve bindings and extension behavior consistently

### `id`

```json
"id": "Microsoft.Azure.Functions.ExtensionBundle"
```

This is the standard Azure-maintained extension bundle.

### `version`

```json
"version": "[4.*, 5.0.0)"
```

This means:

- allow bundle versions in the `4.x` range
- do not cross into `5.0.0`

Why this is useful:

- keeps updates within a compatible major version
- avoids unexpected breaking changes from a future major bundle

## `logging`

```json
"logging": { ... }
```

This section controls:

- how much runtime/app logging is emitted
- which categories are noisy or quiet
- how Application Insights sampling behaves

This is the most important part of the file for day-to-day debugging and cost control.

## `logging.logLevel`

```json
"logLevel": {
  "default": "Warning",
  "Host.Results": "Information",
  "Host.Aggregator": "Warning",
  "Function": "Warning",
  "Microsoft": "Warning",
  "Worker": "Warning"
}
```

This section controls the minimum log level emitted for each category.

General rule:

- `Information` = chatty
- `Warning` = useful caution-level events
- `Error` = failures

### `default`

```json
"default": "Warning"
```

This is the fallback for categories not explicitly listed.

Why we chose it:

- reduces noisy informational traces
- keeps warnings and errors visible
- helps control Application Insights volume

If this were `Information`, traces would grow much faster.

### `Host.Results`

```json
"Host.Results": "Information"
```

This controls logging for function invocation results.

Why we kept it at `Information`:

- request outcomes are useful during debugging
- helps confirm whether an HTTP-triggered function ran and how it completed
- useful when correlating failures with request telemetry

This is one of the more valuable categories to keep a little more visible.

### `Host.Aggregator`

```json
"Host.Aggregator": "Warning"
```

This controls host aggregation logs.

Why we lowered it to `Warning`:

- aggregation logs are often not useful for app debugging
- they can add noise without helping us diagnose submit/save issues

### `Function`

```json
"Function": "Warning"
```

This is the category for function-level logging.

Why we chose `Warning`:

- keeps function warnings and errors
- avoids flooding traces with lower-level noise

Our own `_logger.LogError(...)` and `_logger.LogWarning(...)` statements still show up because they are above this threshold.

### `Microsoft`

```json
"Microsoft": "Warning"
```

This controls logs from Microsoft framework libraries.

Why we lowered it:

- framework logs can be very noisy
- many are not actionable for application debugging
- it helps suppress repetitive platform chatter

### `Worker`

```json
"Worker": "Warning"
```

This controls Azure Functions worker-level logs.

Why we lowered it:

- worker/runtime internals can produce a lot of traces
- most are not directly useful when debugging our event/business submission flow

This helps reduce flood from platform-generated messages.

## `logging.applicationInsights`

```json
"applicationInsights": {
  "samplingSettings": {
    "isEnabled": true,
    "excludedTypes": "Request;Exception",
    "maxTelemetryItemsPerSecond": 5
  },
  "enableLiveMetricsFilters": true
}
```

This section controls how telemetry is sent to Application Insights.

It is mainly about:

- cost control
- reducing noisy traces
- preserving the most valuable telemetry

## `samplingSettings`

### `isEnabled`

```json
"isEnabled": true
```

This turns on sampling.

Why it matters:

- sampling reduces telemetry volume before it is fully ingested
- helps prevent trace data from growing too quickly
- lowers cost risk when traffic or logging increases

Without sampling, very chatty logs can generate much more ingestion.

### `excludedTypes`

```json
"excludedTypes": "Request;Exception"
```

This means requests and exceptions are **not sampled out**.

Why we excluded them:

- request records are important for understanding traffic and endpoint outcomes
- exceptions are the most important telemetry during debugging
- losing exception records would make diagnosis harder

So this setup says:

- reduce lower-value telemetry like repetitive traces
- keep the most critical telemetry complete

### `maxTelemetryItemsPerSecond`

```json
"maxTelemetryItemsPerSecond": 5
```

This limits how many telemetry items are sent per second when sampling is active.

Why this helps:

- avoids sudden telemetry spikes
- puts a soft control on noisy traces
- helps keep costs and noise manageable

This is a conservative setting appropriate for a small-to-medium community site.

If traffic grows a lot, we may revisit it.

## `enableLiveMetricsFilters`

```json
"enableLiveMetricsFilters": true
```

This applies filters to Live Metrics.

Why it helps:

- reduces live-stream noise
- keeps the real-time view more focused
- makes it easier to spot useful issues instead of being buried in runtime chatter

## Why this config was chosen

The current settings reflect a specific goal:

- keep warnings and errors from our app visible
- keep request and exception telemetry complete
- reduce framework and platform noise
- avoid flooding Application Insights with low-value traces

This is especially important in Azure Static Web Apps managed Functions because:

- we do not get the classic standalone Azure Functions log-stream experience
- Application Insights becomes the primary debugging tool
- too much trace volume makes debugging harder and can increase ingestion costs

## What still shows up with this config

You should still see:

- `_logger.LogWarning(...)`
- `_logger.LogError(...)`
- exceptions
- request records
- failed or suspicious function activity

You should see less of:

- generic framework chatter
- worker noise
- routine informational traces

## Useful queries with this setup

### App-level error traces

```kusto
traces
| where message contains "Events endpoint failed"
   or message contains "Businesses endpoint failed"
   or message contains "Event update failed"
   or message contains "Business update failed"
   or message contains "Post lookup failed"
| order by timestamp desc
```

What this query does:

- searches the `traces` table
- looks only for our own app-level error messages
- ignores most platform/framework chatter
- gives a fast list of recent API failures from our custom `_logger.LogError(...)` messages

Why this is useful:

- it is the quickest way to find whether the problem came from:
  - event create
  - business create
  - event update
  - business update
  - edit-code lookup
- it works especially well after we changed logging to structured warning/error-focused output

Typical usage:

1. trigger the failing action in the browser
2. open Application Insights `Logs`
3. run this query
4. inspect the newest trace rows

This is often the best first query when a submit or update request fails and the UI only says:

`Unexpected server error.`

### Exceptions for a known request

```kusto
exceptions
| where operation_Id == "b2ae8b1d7b09d6a01d02ea6182229de5"
| order by timestamp asc
```

What this query does:

- searches the `exceptions` table
- filters to a single request correlation id using `operation_Id`
- shows exception records in time order for that one request

Why this is useful:

- `operation_Id` ties together telemetry created during the same request flow
- it helps isolate one failure from all the other traffic and traces in the system
- it is often the cleanest way to get the exact exception stack/details for a specific failing API call

Typical usage:

1. find the failing request in the `requests` table
2. copy its `operation_Id`
3. run the `exceptions` query with that id
4. inspect the exact exception details for that request only

This is especially useful when:

- many traces are coming in at once
- the same endpoint is being hit repeatedly
- you want the precise exception chain for one broken save/update action

### Good debugging sequence

For most API problems, use the queries in this order:

1. Run the app-level trace query to confirm which endpoint failed.
2. Find the request row and copy the `operation_Id`.
3. Run the exception query for that `operation_Id`.
4. If needed, run a matching `traces` query filtered by the same `operation_Id`.

Example request correlation query:

```kusto
requests
| where url contains "/api/events" or url contains "/api/businesses"
| order by timestamp desc
| project timestamp, name, url, resultCode, success, operation_Id
```

Example trace correlation query:

```kusto
traces
| where operation_Id == "b2ae8b1d7b09d6a01d02ea6182229de5"
| order by timestamp asc
```

### General error/warning traces without framework noise

```kusto
traces
| where severityLevel >= 2
| where message !contains "azure.functions.webjobs.storage"
| order by timestamp desc
```

## When to change this file

You may want to revisit `host.json` when:

- debugging a very specific problem and you temporarily need more detail
- traffic grows and telemetry costs increase
- platform traces become too noisy again
- you add background triggers or more complex runtime behavior

## Safe adjustment patterns

### If you need more detail temporarily

Change:

```json
"Function": "Information"
```

Use this only during focused debugging, then revert it later to avoid trace growth.

### If traces are still too noisy

Keep:

```json
"default": "Warning"
```

and avoid adding more `LogInformation(...)` in code unless really needed.

### If you want more app-specific visibility

Prefer:

- structured `LogWarning`
- structured `LogError`

instead of globally raising all log levels to `Information`.

That gives better signal with less cost.


## Summary

The current `host.json` is a balanced logging configuration for this project:

- enough visibility for errors and failures
- reduced noise from runtime/framework traces
- request and exception telemetry preserved
- lower risk of excessive telemetry ingestion

It is a good baseline for a production-style static site with managed Azure Functions and Application Insights.
