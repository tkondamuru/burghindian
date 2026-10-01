# Static listing generator

Pre-renders the event and business listing cards directly into HTML so the
live site serves them with zero runtime `/api/events` or `/api/businesses`
listing fetches.

## How it fits together

1. Listings are written to Azure Table Storage (website submit UI, or Marley
   via the `/api/ingest/*` endpoint).
2. The **Sync listings to static site** GitHub Actions workflow
   (`.github/workflows/sync-listings.yml`, manual trigger) runs this generator
   with `--source api` against the live site's anonymous GET endpoints.
3. It rewrites `site/events/index.html` and `site/businesses/index.html` with
   the cards baked in, commits, and pushes only when something changed.
4. The push triggers the existing Static Web Apps CI/CD workflow, which
   deploys the site (~1-2 minutes).

## Run locally

```bash
pip install -r requirements.txt

# From the live API (what CI does):
python build.py --site-src ../../site --out /tmp/sitegen --source api --force

# From Table Storage (needs a connection string):
python build.py --site-src ../../site --out /tmp/sitegen \
  --source tablestorage --conn-str "<connection string>" --force

# From JSON files (testing):
python build.py --site-src ../../site --out /tmp/sitegen \
  --source json --data-dir ./sample_data --force
```

Without `--force`, pages whose dataset hash matches `.build-manifest.json`
are skipped (incremental rebuilds).
