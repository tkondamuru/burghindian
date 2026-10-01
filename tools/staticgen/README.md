# Static listing generator

Pre-renders the event and business listing cards directly into HTML so the
live site serves them with zero runtime `/api/events` or `/api/businesses`
listing fetches.

## How it fits together

1. Listings are written to Azure Table Storage (website submit UI, or Marley
   via the `/api/ingest/*` endpoint).
2. The Static Web Apps CI/CD workflow
   (`.github/workflows/azure-static-web-apps-gentle-desert-0872cca0f.yml`)
   runs this generator on every deploy with `--source api` against the live
   site's anonymous GET endpoints. It rewrites `site/events/index.html` and
   `site/businesses/index.html` with the cards baked in, right before the
   Azure deploy step uploads `site/`.
3. To publish new listings, trigger the workflow manually (**Run workflow**
   button in the GitHub app's Actions tab). The site is live ~1-2 minutes
   later. Normal pushes to `main` also regenerate, so code changes never
   ship stale listings.

`site/` in the repo always holds the dynamic page shells (the source); the
baked HTML is a build artifact produced at deploy time, never committed.

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
