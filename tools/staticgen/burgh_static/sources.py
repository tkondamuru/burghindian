"""Data sources: Azure Table Storage (live) and local JSON files (testing)."""
import json
from pathlib import Path


def _first(entity, *keys):
    for key in keys:
        value = entity.get(key)
        if value not in (None, ""):
            return value
    return ""


def normalize_event(entity):
    return {
        "partitionKey": _first(entity, "PartitionKey", "partitionKey"),
        "rowKey": _first(entity, "RowKey", "rowKey"),
        "createdAtUtc": _first(entity, "CreatedAtUtc", "createdAtUtc"),
        "updatedAtUtc": _first(entity, "UpdatedAtUtc", "updatedAtUtc"),
        "title": _first(entity, "Title", "title"),
        "date": _first(entity, "Date", "date"),
        "time": _first(entity, "Time", "time"),
        "location": _first(entity, "Location", "location"),
        "summary": _first(entity, "Summary", "summary"),
        "description": _first(entity, "Description", "description"),
        "tags": _first(entity, "Tags", "tags"),
        "imageUrl": _first(entity, "ImageUrl", "imageUrl"),
    }


def normalize_business(entity):
    return {
        "partitionKey": _first(entity, "PartitionKey", "partitionKey"),
        "rowKey": _first(entity, "RowKey", "rowKey"),
        "createdAtUtc": _first(entity, "CreatedAtUtc", "createdAtUtc"),
        "updatedAtUtc": _first(entity, "UpdatedAtUtc", "updatedAtUtc"),
        "name": _first(entity, "Name", "name"),
        "address": _first(entity, "Address", "address"),
        "phone": _first(entity, "Phone", "phone"),
        "category": _first(entity, "Category", "category"),
        "summary": _first(entity, "Summary", "summary"),
        "description": _first(entity, "Description", "description"),
        "tags": _first(entity, "Tags", "tags"),
        "imageUrl": _first(entity, "ImageUrl", "imageUrl"),
    }


class JsonFileSource:
    """Read listings from local JSON files (same shape as the API responses)."""

    def __init__(self, data_dir):
        self.data_dir = Path(data_dir)

    def get_events(self):
        return [normalize_event(e) for e in self._load("events.json")]

    def get_businesses(self):
        return [normalize_business(b) for b in self._load("businesses.json")]

    def _load(self, filename):
        path = self.data_dir / filename
        if not path.exists():
            return []
        return json.loads(path.read_text(encoding="utf-8"))


class TableStorageSource:
    """Read listings live from Azure Table Storage (tables: Events, Businesses)."""

    def __init__(self, conn_str):
        from azure.data.tables import TableServiceClient

        self.service = TableServiceClient.from_connection_string(conn_str)

    def get_events(self):
        return [normalize_event(dict(e)) for e in self._table("Events").list_entities()]

    def get_businesses(self):
        return [normalize_business(dict(e)) for e in self._table("Businesses").list_entities()]

    def _table(self, name):
        return self.service.get_table_client(name)


class ApiSource:
    """Read listings through the site's own public GET endpoints.

    No secret needed: /api/events and /api/businesses are anonymous reads.
    Used for live-data builds when Table Storage credentials are unavailable.
    """

    def __init__(self, base_url):
        import urllib.request

        self.base_url = base_url.rstrip("/")
        self._urlopen = urllib.request.urlopen

    def get_events(self):
        return [normalize_event(e) for e in self._get("/api/events")]

    def get_businesses(self):
        return [normalize_business(b) for b in self._get("/api/businesses")]

    def _get(self, path):
        import json as _json
        import urllib.request

        req = urllib.request.Request(
            self.base_url + path, headers={"Accept": "application/json"}
        )
        with self._urlopen(req, timeout=30) as resp:
            items = _json.loads(resp.read().decode("utf-8"))
        return items if isinstance(items, list) else []
