"""Pre-rendered static site builder for Burgh Indian.

Copies site/ to the output dir, then rewrites the listing pages with baked-in
content. Incremental: pages whose dataset hash matches the manifest are skipped.
"""
import argparse
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from burgh_static import manifest, render, sources


GENERATED_PAGES = ("events/index.html", "businesses/index.html")


def build(args):
    site_src = Path(args.site_src)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    if args.source == "tablestorage":
        if not args.conn_str:
            raise SystemExit("--conn-str is required with --source tablestorage")
        source = sources.TableStorageSource(args.conn_str)
    elif args.source == "api":
        source = sources.ApiSource(args.api_base)
    else:
        source = sources.JsonFileSource(args.data_dir)

    events = source.get_events()
    businesses = source.get_businesses()

    manifest_path = Path(args.manifest) if args.manifest else out / ".build-manifest.json"
    prior = manifest.load(manifest_path)
    now = datetime.now(timezone.utc).isoformat(timespec="seconds")

    jobs = [
        ("events", events, "events/index.html", render.render_events_page),
        ("businesses", businesses, "businesses/index.html", render.render_businesses_page),
    ]

    # Decide skips BEFORE copying: a skipped page keeps its previously
    # generated output and must never be overwritten by the dynamic source.
    plan = []
    for key, items, rel_path, renderer in jobs:
        digest = manifest.dataset_hash(items)
        unchanged = not args.force and prior.get(key, {}).get("hash") == digest
        if unchanged and not (out / rel_path).exists():
            unchanged = False  # output missing: render anyway
        plan.append((key, items, rel_path, renderer, digest, unchanged))

    # Copy the whole site except the generated listing pages, which are
    # written by the renderers below (or preserved when skipped).
    for item in site_src.iterdir():
        dest = out / item.name
        if item.is_dir():
            ignore = (
                shutil.ignore_patterns("index.html")
                if item.name in ("events", "businesses")
                else None
            )
            shutil.copytree(item, dest, dirs_exist_ok=True, ignore=ignore)
        else:
            shutil.copy2(item, dest)

    results = []
    for key, items, rel_path, renderer, digest, unchanged in plan:
        if unchanged:
            results.append((key, "skipped (unchanged)", len(items)))
            continue
        template_html = (site_src / rel_path).read_text(encoding="utf-8")
        rendered = renderer(template_html, items)
        (out / rel_path).write_text(rendered, encoding="utf-8")
        prior[key] = {"hash": digest, "count": len(items), "built_at": now}
        results.append((key, "rendered", len(items)))

    manifest.save(manifest_path, prior)

    for key, status, count in results:
        print(f"{key:10s} {status:20s} ({count} items)")
    print(f"output: {out}")


def main():
    parser = argparse.ArgumentParser(description="Pre-render Burgh Indian listing pages")
    parser.add_argument("--site-src", required=True, help="path to the repo's site/ dir")
    parser.add_argument("--out", required=True, help="output dir (deploy-ready site/)")
    parser.add_argument("--source", choices=["json", "tablestorage", "api"], default="json",
                        help="json: local files (testing); tablestorage: live tables via connection string; api: live data via the site's public GET endpoints (no secret needed)")
    parser.add_argument("--data-dir", default=str(Path(__file__).resolve().parent / "sample_data"))
    parser.add_argument("--conn-str", default=None, help="Azure Table Storage connection string")
    parser.add_argument("--api-base", default="https://gentle-desert-0872cca0f.7.azurestaticapps.net",
                        help="base URL for --source api (the deployed site)")
    parser.add_argument("--manifest", default=None, help="manifest path (default: <out>/.build-manifest.json)")
    parser.add_argument("--force", action="store_true", help="rebuild even when unchanged")
    build(parser.parse_args())


if __name__ == "__main__":
    main()
