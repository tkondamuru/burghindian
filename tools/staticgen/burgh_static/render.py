"""Render pre-rendered listing pages from normalized datasets.

Takes the dynamic site/events/index.html and site/businesses/index.html page
shells from the repo and bakes the listing cards directly into the DOM:
  events:      event cards are the primary DOM (data-created-at); the date
               filter toggles .hidden on baked cards; empty state pre-rendered.
               No fetch('/api/events'), no embedded dataset JSON.
  businesses:  business cards are the primary DOM (data-business-key); Alpine
               only manages search/filter/modal state over the baked cards.
               No fetch('/api/businesses'), no embedded dataset JSON.
"""
import json
import re
from datetime import datetime
from pathlib import Path

from jinja2 import Environment, FileSystemLoader, select_autoescape

TEMPLATES_DIR = Path(__file__).resolve().parent.parent / "templates"

env = Environment(
    loader=FileSystemLoader(str(TEMPLATES_DIR)),
    autoescape=select_autoescape(["html", "j2"]),
)

FALLBACK_BUSINESS_IMAGE = (
    "https://stburghindian8808.blob.core.windows.net/"
    "business-photos/mintt-indian-cuisine-20260430/photo-0.jpg"
)


# --------------------------------------------------------------------------
# Data preparation (mirrors the client-side normalizers)
# --------------------------------------------------------------------------

def sort_newest_first(items):
    def key(item):
        raw = item.get("createdAtUtc") or ""
        try:
            return datetime.fromisoformat(raw.replace("Z", "+00:00")).timestamp()
        except (ValueError, TypeError):
            return 0.0

    return sorted(items, key=key, reverse=True)


def _clean_legacy_unspecified(value):
    """Old entries stored the literal 'Not specified' for missing date/time.
    Treat it as empty so the card shows the fallback instead."""
    text = (value or "").strip()
    return "" if text.lower() == "not specified" else text


def prepare_event(item):
    title = item.get("title") or "Untitled event"
    full_description = item.get("description") or item.get("summary") or ""
    image_url = (item.get("imageUrl") or "").strip()
    return {
        "title": title,
        "date": _clean_legacy_unspecified(item.get("date")),
        "time": _clean_legacy_unspecified(item.get("time")),
        "location": item.get("location") or "Location not specified",
        "full_description": full_description,
        "image_url": image_url,
        "has_image": bool(image_url),
        "show_read_more": len(full_description.strip()) > 160,
        "created_at": item.get("createdAtUtc") or "",
    }


def _split_tags(value):
    return [p.strip() for p in str(value or "").replace(";", ",").split(",") if p.strip()]


def _split_urls(value):
    return [p.strip() for p in re.split(r"[;,]", str(value or "")) if p.strip()]


def prepare_business(item, index=0):
    image_urls = _split_urls(item.get("imageUrl"))
    name = item.get("name") or "Untitled business"
    category = item.get("category") or ""
    raw_address = item.get("address") or ""
    raw_phone = (item.get("phone") or "").strip()
    tags = _split_tags(item.get("tags"))
    card_copy = item.get("summary") or item.get("description") or ""
    return {
        "key": item.get("rowKey") or f"{name}-{index}",
        "name": name,
        "category": category,
        # Display fallbacks mirror the original card's `|| '... not specified'`.
        "address": raw_address or "Address not specified",
        "phone": raw_phone or "Phone not specified",
        "tags": tags,
        "image_urls": image_urls,
        "primary_image": image_urls[0] if image_urls else FALLBACK_BUSINESS_IMAGE,
        "card_copy": card_copy,
        # Mirrors showReadMore(): (cardCopy || '').length > 180
        "show_read_more": len(card_copy) > 180,
        # Mirrors the original search haystack (raw values, joined with spaces).
        "search_text": " ".join(
            part
            for part in [name, category, raw_address, raw_phone, " ".join(tags), card_copy]
            if part
        ),
    }


def _parse_demo_business(js_obj):
    """Turn the page's hardcoded demo business (a JS object literal) into a
    normalized dict so it can be baked as the empty-state fallback card."""
    fields = {}
    for match in re.finditer(r"(\w+):\s*'((?:[^'\\]|\\.)*)'", js_obj):
        fields[match.group(1)] = match.group(2).replace("\\'", "'").replace("\\\\", "\\")
    return {
        "partitionKey": "demo",
        "rowKey": fields.get("key") or "demo-mintt",
        "createdAtUtc": "",
        "updatedAtUtc": "",
        "name": fields.get("name") or "",
        "address": fields.get("address") or "",
        "phone": fields.get("phone") or "",
        "category": fields.get("category") or "",
        "summary": fields.get("summary") or "",
        "description": "",
        "tags": "",
        "imageUrl": fields.get("imageUrl") or "",
    }


def embedded_json(items):
    """JSON safe to inline inside <script type='application/json'>."""
    return json.dumps(items, ensure_ascii=False).replace("<", "\\u003c")


# --------------------------------------------------------------------------
# Page transforms
# --------------------------------------------------------------------------

def _require_once(text, anchor, page):
    count = text.count(anchor)
    if count != 1:
        raise RuntimeError(f"{page}: anchor found {count}x (expected 1x): {anchor[:70]!r}")


def render_events_page(source_html, events):
    events = sort_newest_first(events)
    cards = "\n".join(
        env.get_template("event_card.html.j2").render(**prepare_event(e)) for e in events
    )

    page = "events/index.html"

    # 1. Bake cards into the grid; the JS below only shows/hides them.
    #    The empty-state panel is pre-rendered too and toggled by the filter.
    grid_anchor = '<div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6" data-event-grid></div>'
    _require_once(source_html, grid_anchor, page)
    empty_state = (
        '<div id="event-empty-state" class="hidden md:col-span-2 xl:col-span-3 '
        'rounded-[1.75rem] border border-outline-variant/15 bg-surface-container-lowest '
        'px-6 py-8 text-center shadow-editorial">\n'
        '    <p class="font-headline text-2xl font-bold text-on-surface mb-2">'
        "No events match this date yet</p>\n"
        '    <p class="text-sm text-on-surface/65">'
        "Try an earlier created date to see more recent submissions.</p>\n"
        "</div>"
    )
    html = source_html.replace(
        grid_anchor,
        '<div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6" data-event-grid>\n'
        + cards
        + "\n"
        + empty_state
        + "\n</div>",
    )

    # 2. Grab the empty-state node next to the other lookups.
    decl_anchor = "            const feedStatusClose = document.querySelector('[data-event-status-close]');"
    _require_once(html, decl_anchor, page)
    html = html.replace(
        decl_anchor,
        decl_anchor + "\n            const emptyState = document.getElementById('event-empty-state');",
    )

    # 3. Drop the now-dead data plumbing (allEvents array, getters, showReadMore).
    for dead in (
        "            let allEvents = [];\n",
        "            const showReadMore = (description) => String(description || '').trim().length > 160;\n",
    ):
        _require_once(html, dead, page)
        html = html.replace(dead, "")
    get_created_at = (
        "            const getEventCreatedAt = (eventItem) => {\n"
        "                const parsed = Date.parse(eventItem?.createdAtUtc || eventItem?.CreatedAtUtc || '');\n"
        "                return Number.isNaN(parsed) ? null : new Date(parsed);\n"
        "            };\n"
    )
    _require_once(html, get_created_at, page)
    html = html.replace(get_created_at, "")
    get_visible = (
        "            const getVisibleEvents = () => {\n"
        "                const createdAfterDate = getCreatedAfterDate();\n"
        "                if (!createdAfterDate) {\n"
        "                    return allEvents;\n"
        "                }\n"
        "\n"
        "                return allEvents.filter((eventItem) => {\n"
        "                    const createdAt = getEventCreatedAt(eventItem);\n"
        "                    return createdAt ? createdAt >= createdAfterDate : false;\n"
        "                });\n"
        "            };\n"
    )
    _require_once(html, get_visible, page)
    html = html.replace(get_visible, "")

    # createEventCard() built cards from JS objects; the cards are baked now.
    card_fn_start = "            const createEventCard = (eventItem) => {"
    card_fn_end_anchor = "            const renderEvents = () => {"
    _require_once(html, card_fn_start, page)
    _require_once(html, card_fn_end_anchor, page)
    html = html[: html.index(card_fn_start)] + html[html.index(card_fn_end_anchor):]

    # 4. Replace renderEvents()/loadServerBusinesses-style re-rendering with a
    #    DOM filter over the pre-rendered cards. No fetch, no re-render.
    block_start = "            const renderEvents = () => {"
    block_end_anchor = "            eventGrid.addEventListener('click', (event) => {"
    _require_once(html, block_start, page)
    _require_once(html, block_end_anchor, page)
    start = html.index(block_start)
    end = html.index(block_end_anchor)
    new_block = (
        "            const applyEventFilter = () => {\n"
        "                const createdAfterDate = getCreatedAfterDate();\n"
        "                let visibleCount = 0;\n"
        "                eventGrid.querySelectorAll('[data-event-record]').forEach((card) => {\n"
        "                    const createdAt = Date.parse(card.dataset.createdAt || '');\n"
        "                    const show = !createdAfterDate\n"
        "                        || (!Number.isNaN(createdAt) && createdAt >= createdAfterDate);\n"
        "                    card.classList.toggle('hidden', !show);\n"
        "                    if (show) {\n"
        "                        visibleCount += 1;\n"
        "                    }\n"
        "                });\n"
        "                if (emptyState) {\n"
        "                    emptyState.classList.toggle('hidden', visibleCount > 0);\n"
        "                }\n"
        "            };\n"
        "\n"
        "            const initStaticEvents = () => {\n"
        "                const cards = eventGrid.querySelectorAll('[data-event-record]');\n"
        "                setFeedStatus(\n"
        "                    `${cards.length} community event${cards.length === 1 ? ' is' : 's are'} now live from recent submissions.`,\n"
        "                    'success'\n"
        "                );\n"
        "                loadingPanel?.classList.add('hidden');\n"
        "                applyEventFilter();\n"
        "            };\n"
        "\n"
    )
    html = html[:start] + new_block + html[end:]

    # 5. Rewire the two remaining call sites.
    listener_old = (
        "            createdAfterInput.addEventListener('input', () => {\n"
        "                renderEvents();\n"
        "            });"
    )
    _require_once(html, listener_old, page)
    html = html.replace(
        listener_old,
        "            createdAfterInput.addEventListener('input', () => {\n"
        "                applyEventFilter();\n"
        "            });",
    )
    call_old = "            loadServerEvents();"
    _require_once(html, call_old, page)
    html = html.replace(call_old, "            initStaticEvents();")

    return html


def _extract_demo_business(source_html):
    """Pull the hardcoded demo business object out of the original page so the
    empty-state fallback survives regeneration."""
    match = re.search(
        r"this\.businesses = \[this\.normalizeBusiness\(\{(.*?)\}\)\];",
        source_html,
        re.DOTALL,
    )
    if not match:
        raise RuntimeError("businesses/index.html: demo business seed not found")
    return match.group(1)


def render_businesses_page(source_html, businesses):
    page = "businesses/index.html"
    demo_obj = _extract_demo_business(source_html)

    # Empty dataset -> bake the page's own demo card as the fallback listing.
    if not businesses:
        businesses = [_parse_demo_business(demo_obj)]
    businesses = sort_newest_first(businesses)
    cards = "\n".join(
        env.get_template("business_card.html.j2").render(**prepare_business(b, i))
        for i, b in enumerate(businesses)
    )
    data = embedded_json(businesses)

    html = source_html

    # 1. Remove the loading panel: listings are in the HTML, nothing to wait for.
    load_start = '        <div x-cloak x-show="isLoading" class="mb-6 rounded-'
    load_end_anchor = '        <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">'
    _require_once(html, load_start, page)
    _require_once(html, load_end_anchor, page)
    html = html[: html.index(load_start)] + html[html.index(load_end_anchor):]

    # 2. Replace the Alpine x-for grid with pre-rendered cards. Filtering and
    #    modals work off data attributes on these cards; the JSON below feeds
    #    the modal lookups only.
    grid_start = (
        '        <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">\n'
        '            <template x-for="business in filteredBusinesses" :key="business.key">'
    )
    grid_end_anchor = (
        "            </template>\n"
        "        </div>\n"
        "\n"
        "        <div\n"
        '            x-cloak\n'
        '            x-show="feedStatus.open && feedStatus.message"'
    )
    _require_once(html, grid_start, page)
    _require_once(html, grid_end_anchor, page)
    start = html.index(grid_start)
    end = html.index(grid_end_anchor)
    empty_state = (
        '            <div id="business-empty-state" class="hidden md:col-span-2 xl:col-span-3 '
        'rounded-[1.75rem] border border-outline-variant/15 bg-surface-container-lowest '
        'px-6 py-8 text-center shadow-editorial">\n'
        '                <p class="font-headline text-2xl font-bold text-on-surface mb-2">'
        "No businesses match your filters</p>\n"
        '                <p class="text-sm text-on-surface/65">'
        "Try a different keyword or clear the category filters.</p>\n"
        "            </div>"
    )
    new_grid = (
        '        <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6" data-business-grid>\n'
        + cards
        + "\n"
        + empty_state
        + "\n"
        + grid_end_anchor[len("            </template>\n"):]
    )
    html = html[:start] + new_grid + html[end + len("            </template>\n"):]

    # 3a. Component data: lookup map instead of a fetched array; drop isLoading.
    _require_once(html, "                businesses: [],", page)
    html = html.replace("                businesses: [],", "                businessByKey: {},")
    _require_once(html, "                isLoading: false,\n", page)
    html = html.replace("                isLoading: false,\n", "")

    # 3b. Replace the filteredBusinesses getter with a DOM filter over the
    #     pre-rendered cards.
    getter_old = (
        "                get filteredBusinesses() {\n"
        "                    const query = this.searchTerm.toLowerCase();\n"
        "                    return this.businesses.filter((business) => {\n"
        "                        const matchesCategory = this.selectedCategories.length === 0 || this.selectedCategories.includes(business.category);\n"
        "                        const matchesSearch = !query || [\n"
        "                            business.name,\n"
        "                            business.category,\n"
        "                            business.address,\n"
        "                            business.phone,\n"
        "                            business.tags.join(' '),\n"
        "                            business.cardCopy\n"
        "                        ].join(' ').toLowerCase().includes(query);\n"
        "                        return matchesCategory && matchesSearch;\n"
        "                    });\n"
        "                },\n"
    )
    _require_once(html, getter_old, page)
    getter_new = (
        "                applyFilters() {\n"
        "                    const query = this.searchTerm.trim().toLowerCase();\n"
        "                    // NOTE: document-scoped lookup, not this.$el: inside @input/@change\n"
        "                    // handlers Alpine binds $el to the event's element, not the x-data root.\n"
        "                    const grid = document.querySelector('[data-business-grid]');\n"
        "                    if (!grid) {\n"
        "                        return;\n"
        "                    }\n"
        "                    let visibleCount = 0;\n"
        "                    grid.querySelectorAll('[data-business-key]').forEach((card) => {\n"
        "                        const matchesCategory = this.selectedCategories.length === 0\n"
        "                            || this.selectedCategories.includes(card.dataset.category || '');\n"
        "                        const matchesSearch = !query\n"
        "                            || (card.dataset.search || '').toLowerCase().includes(query);\n"
        "                        const show = matchesCategory && matchesSearch;\n"
        "                        card.classList.toggle('hidden', !show);\n"
        "                        if (show) {\n"
        "                            visibleCount += 1;\n"
        "                        }\n"
        "                    });\n"
        "                    const emptyState = document.getElementById('business-empty-state');\n"
        "                    if (emptyState) {\n"
        "                        emptyState.classList.toggle('hidden', visibleCount > 0);\n"
        "                    }\n"
        "                },\n"
    )
    html = html.replace(getter_old, getter_new)

    # 3c. Rewrite init(): seed the modal lookup map from embedded JSON, no fetch.
    init_start = "                async init() {"
    init_call = "                    await this.loadServerBusinesses();"
    _require_once(html, init_start, page)
    _require_once(html, init_call, page)
    start = html.index(init_start)
    call_end = html.index(init_call, start) + len(init_call)
    close = html.index("\n                },", call_end)
    new_init = (
        "                async init() {\n"
        "                    this.selectedCategories = this.getInitialCategories();\n"
        "                    try {\n"
        "                        const embeddedData = document.getElementById('pre-rendered-businesses');\n"
        "                        const items = embeddedData ? JSON.parse(embeddedData.textContent || '[]') : [];\n"
        "                        items.sort((left, right) => {\n"
        "                            const leftTime = Date.parse(left?.createdAtUtc || left?.CreatedAtUtc || '');\n"
        "                            const rightTime = Date.parse(right?.createdAtUtc || right?.CreatedAtUtc || '');\n"
        "                            const safeLeft = Number.isNaN(leftTime) ? 0 : leftTime;\n"
        "                            const safeRight = Number.isNaN(rightTime) ? 0 : rightTime;\n"
        "                            return safeRight - safeLeft;\n"
        "                        });\n"
        "                        this.businessByKey = {};\n"
        "                        items.forEach((item, index) => {\n"
        "                            const business = this.normalizeBusiness(item, index);\n"
        "                            this.businessByKey[business.key] = business;\n"
        "                        });\n"
        "                        if (items.length === 0) {\n"
        "                            const demo = this.normalizeBusiness({" + demo_obj + "});\n"
        "                            this.businessByKey[demo.key] = demo;\n"
        "                        } else {\n"
        "                            this.showFeedStatus(\n"
        "                                `${items.length} local business${items.length === 1 ? ' is' : 'es are'} now showing from community submissions.`,\n"
        "                                'success'\n"
        "                            );\n"
        "                        }\n"
        "                    } catch (error) {\n"
        "                        this.showFeedStatus(\n"
        "                            'Business listings are bundled with this page and could not be read.',\n"
        "                            'error'\n"
        "                        );\n"
        "                    }\n"
        "                    this.applyFilters();\n"
        "                },"
    )
    html = html[:start] + new_init + html[close + len("\n                },"):]

    # 3d. Remove the now-unused loadServerBusinesses() (it fetched /api/businesses).
    method_start = "                async loadServerBusinesses() {"
    _require_once(html, method_start, page)
    mstart = html.index(method_start)
    method_end_anchor = "                }\n            };\n        }"
    mend = html.index(method_end_anchor, mstart) + len("                }")
    html = html[:mstart] + html[mend:]

    # 3e. Modals look records up by key instead of receiving the object.
    modal_old = (
        "                openDescriptionModal(business) {\n"
        "                    this.descriptionModal = {\n"
        "                        open: true,\n"
        "                        title: business.name || 'Business Description',\n"
        "                        body: business.cardCopy || 'No additional description is available.'\n"
        "                    };"
    )
    modal_new = (
        "                openDescriptionModal(businessKey) {\n"
        "                    const business = this.businessByKey[businessKey];\n"
        "                    if (!business) {\n"
        "                        return;\n"
        "                    }\n"
        "                    this.descriptionModal = {\n"
        "                        open: true,\n"
        "                        title: business.name || 'Business Description',\n"
        "                        body: business.cardCopy || 'No additional description is available.'\n"
        "                    };"
    )
    _require_once(html, modal_old, page)
    html = html.replace(modal_old, modal_new)

    gallery_old = (
        "                openPhotoGallery(business, index = 0) {\n"
        "                    if (!business.imageUrls.length) {\n"
        "                        return;\n"
        "                    }\n"
    )
    gallery_new = (
        "                openPhotoGallery(businessKey, index = 0) {\n"
        "                    const business = this.businessByKey[businessKey];\n"
        "                    if (!business) {\n"
        "                        return;\n"
        "                    }\n"
        "                    if (!business.imageUrls.length) {\n"
        "                        return;\n"
        "                    }\n"
    )
    _require_once(html, gallery_old, page)
    html = html.replace(gallery_old, gallery_new)

    # 3f. showReadMore is now a build-time conditional in the card template.
    readmore_old = (
        "                showReadMore(business) {\n"
        "                    return (business.cardCopy || '').length > 180;\n"
        "                },\n"
    )
    _require_once(html, readmore_old, page)
    html = html.replace(readmore_old, "")

    # 3g. Category changes re-filter the pre-rendered cards.
    toggle_old = (
        "                toggleCategory(category) {\n"
        "                    if (this.selectedCategories.includes(category)) {\n"
        "                        this.selectedCategories = this.selectedCategories.filter((item) => item !== category);\n"
        "                    } else {\n"
        "                        this.selectedCategories = [...this.selectedCategories, category];\n"
        "                    }\n"
        "                    this.syncCategoryQuery();\n"
        "                },"
    )
    _require_once(html, toggle_old, page)
    html = html.replace(
        toggle_old,
        toggle_old[:-len("                },")]
        + "                    this.syncCategoryQuery();\n"
        + "                    this.applyFilters();\n"
        + "                },",
    )
    clear_old = (
        "                clearCategoryFilter() {\n"
        "                    this.selectedCategories = [];\n"
        "                    this.syncCategoryQuery();\n"
        "                },"
    )
    _require_once(html, clear_old, page)
    html = html.replace(
        clear_old,
        "                clearCategoryFilter() {\n"
        "                    this.selectedCategories = [];\n"
        "                    this.syncCategoryQuery();\n"
        "                    this.applyFilters();\n"
        "                },",
    )

    # 3h. Typing in the search box re-filters the pre-rendered cards.
    search_old = '                            x-model.trim="searchTerm"\n'
    _require_once(html, search_old, page)
    html = html.replace(
        search_old,
        '                            x-model.trim="searchTerm"\n'
        '                            @input="applyFilters()"\n',
    )

    # 4. Embed the dataset before the Alpine component script (modal lookups).
    script_anchor = "    <script>\n        function businessDirectory() {"
    _require_once(html, script_anchor, page)
    html = html.replace(
        script_anchor,
        "    <script type=\"application/json\" id=\"pre-rendered-businesses\">\n"
        + data
        + "\n    </script>\n"
        + script_anchor,
    )

    return html
