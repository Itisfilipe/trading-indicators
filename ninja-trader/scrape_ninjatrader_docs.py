#!/usr/bin/env python3
"""
NinjaTrader developer documentation scraper.

Mirrors https://developer.ninjatrader.com/docs/ as markdown.

The site is a server-rendered Next.js app that ships the authored Markdoc source
of every page inside its RSC payload. Extracting that source gives us the real
document instead of a lossy HTML-to-markdown rendering of the page chrome, so a
plain HTTP fetch is enough and no browser is needed.

Change detection is content based: each page is keyed by the SHA-256 of its
extracted body. The sitemap's <lastmod> cannot be used for this because the site
stamps every URL with the same site-build time, so it changes for all 1200+ pages
whenever any one of them is rebuilt.
"""

import argparse
import asyncio
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional
from urllib.parse import urlparse

import aiohttp
from bs4 import BeautifulSoup

# Bump when the extraction logic changes in a way that alters stored markdown.
# Pages whose recorded version differs are re-extracted even if their hash matches.
EXTRACTOR_VERSION = 2

SITEMAP_NAMESPACE = {"ns": "http://www.sitemaps.org/schemas/sitemap/0.9"}

# /docs/api and /docs/api/websocket are 308 redirects to Tradovate's client-rendered
# API docs. They render nothing without JavaScript and belong to a different product,
# so we record them as links rather than saving two "enable JavaScript" stubs.
OFFSITE_NAMESPACES = {"api"}

# Visible text of pages the site has published but not yet written.
PLACEHOLDER_MARKER = "content TBD"

USER_AGENT = "Mozilla/5.0 (compatible; ninjatrader-docs-mirror/2.0)"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


class ExtractionError(Exception):
    """Raised when a page's Markdoc source cannot be recovered."""


def decode_rsc_stream(page_html: str) -> str:
    """Concatenate the RSC payload chunks the page pushes into self.__next_f."""
    soup = BeautifulSoup(page_html, "html.parser")
    chunks = []
    for script in soup.find_all("script"):
        script_text = script.string or script.get_text()
        match = re.fullmatch(r"self\.__next_f\.push\((.*)\)", script_text or "", re.S)
        if not match:
            continue
        try:
            payload = json.loads(match.group(1))
        except json.JSONDecodeError:
            continue
        if len(payload) > 1 and isinstance(payload[1], str):
            chunks.append(payload[1])
    return "".join(chunks)


def parse_rsc_records(stream: str) -> Dict[str, str]:
    """
    Map RSC text-record ids to their contents.

    Records look like `1a:T5f2,<payload>` where the hex length is a count of UTF-8
    bytes, not characters -- slicing the string directly would truncate any record
    containing non-ASCII text.
    """
    records = {}
    for match in re.finditer(r"(?:^|\n)([0-9a-f]+):T([0-9a-f]+),", stream):
        size = int(match.group(2), 16)
        payload = stream[match.end():].encode("utf-8")[:size]
        try:
            decoded = payload.decode("utf-8")
        except UnicodeDecodeError:
            continue
        if len(decoded.encode("utf-8")) == size:
            records[match.group(1)] = decoded
    return records


def extract_markdoc_source(page_html: str) -> Optional[str]:
    """
    Return the authored Markdoc body of a documentation page.

    The body reaches the client as a "source" prop that either holds the markdown
    inline or points at a separate RSC record via a `$<id>` reference. A few
    section landing pages carry their markdown as the page's only text record
    without ever naming it in a "source" prop.

    Returns None for pages that ship no document body at all -- the site has
    published placeholders whose visible content is the literal text "content TBD".
    Raises ExtractionError when the body is ambiguous, which is deliberately noisy:
    silently guessing at the largest record would let a nav blob masquerade as
    documentation.
    """
    stream = decode_rsc_stream(page_html)
    if not stream:
        raise ExtractionError("no RSC payload found")

    records = parse_rsc_records(stream)
    source_props = re.finditer(r'"source":("(?:\\.|[^"\\])*")', stream, flags=re.S)

    # Later props win: the innermost page component is emitted after its layout.
    for prop in reversed(list(source_props)):
        try:
            source = json.loads(prop.group(1))
        except json.JSONDecodeError:
            continue
        if not source.startswith("$"):
            return source
        if source[1:] in records:
            return records[source[1:]]
        # The page named a body we cannot resolve. Anything we returned here would
        # be a guess, and a truncated payload must not read as an empty page.
        raise ExtractionError(f"source prop references missing record {source!r}")

    if len(records) == 1:
        return next(iter(records.values()))
    if records:
        raise ExtractionError(f"no source prop and {len(records)} candidate records")
    return None


def split_leading_heading(body: str) -> tuple[Optional[str], str]:
    """
    Peel off a body's own opening h1 so it can serve as the page title.

    Most pages start at h2 and take their title from the rendered h1, but a few
    landing pages author the h1 inline; without this they would get two headings.
    """
    stripped = body.lstrip("\n")
    match = re.match(r"#\s+(.+?)\s*\n", stripped)
    if not match:
        return None, body
    return match.group(1), stripped[match.end():]


def extract_title(page_html: str, fallback: str) -> str:
    """The <title> tag is site-wide boilerplate, so the h1 is the only real title."""
    soup = BeautifulSoup(page_html, "html.parser")
    heading = soup.find("h1")
    if heading:
        title = heading.get_text(strip=True)
        if title:
            return title
    return fallback


def namespace_of(url: str) -> str:
    """The product section a docs URL belongs to: desktop, web, ecosystem, ..."""
    path = urlparse(url).path.strip("/")
    parts = path.split("/")
    return parts[1] if len(parts) > 1 else ""


class NinjaTraderDocsScraper:
    def __init__(self, base_url="https://developer.ninjatrader.com", output_dir="docs",
                 max_concurrent=8):
        self.base_url = base_url.rstrip("/")
        self.output_dir = Path(output_dir)
        self.state_file = self.output_dir / "scraper_state.json"
        self.max_concurrent = max_concurrent
        self.semaphore = asyncio.Semaphore(max_concurrent)
        self.state = self.load_state()
        self.sitemap_errors: List[str] = []
        self.results = {"new": [], "changed": [], "unchanged": [], "offsite": [],
                        "placeholder": [], "pruned": [], "failed": []}

    def load_state(self) -> Dict:
        if self.state_file.exists():
            with open(self.state_file) as f:
                return json.load(f)
        return {"pages": {}, "runs": []}

    def save_state(self):
        self.output_dir.mkdir(parents=True, exist_ok=True)
        with open(self.state_file, "w") as f:
            json.dump(self.state, f, indent=2, sort_keys=True)

    def discard_local_file(self, url: str) -> None:
        """Drop a page's markdown once it stops being real documentation."""
        filepath = self.url_to_filepath(url)
        if filepath.exists():
            filepath.unlink()
            self.results["pruned"].append(str(filepath.relative_to(self.output_dir)))

    def url_to_filepath(self, url: str) -> Path:
        """
        Map a docs URL to a namespaced local path.

        The namespace must be preserved: desktop and web genuinely share the slugs
        index, indicator, plots and timeseries, and a flat layout silently
        overwrites one product's page with the other's.
        """
        path = urlparse(url).path.strip("/")
        relative = path[len("docs/"):] if path.startswith("docs/") else path
        relative = re.sub(r'[<>:"|?*]', "_", relative)
        if not relative:
            relative = "index"
        return self.output_dir / f"{relative}.md"

    async def fetch_sitemap_urls(self, session: aiohttp.ClientSession, sitemap_url: str) -> List[str]:
        """Collect every documentation URL, following sitemap indexes one level down."""
        try:
            async with session.get(sitemap_url, timeout=aiohttp.ClientTimeout(total=30)) as response:
                response.raise_for_status()
                root = ET.fromstring(await response.text())
        except Exception as error:
            # Recorded rather than swallowed: a half-read sitemap would look like
            # a successful run against a shrunken set of URLs.
            self.sitemap_errors.append(f"{sitemap_url}: {error}")
            print(f"  ! sitemap {sitemap_url}: {error}")
            return []

        nested = root.findall("ns:sitemap", SITEMAP_NAMESPACE)
        if nested:
            urls = []
            for entry in nested:
                loc = entry.find("ns:loc", SITEMAP_NAMESPACE)
                if loc is not None and loc.text:
                    urls.extend(await self.fetch_sitemap_urls(session, loc.text))
            return urls

        urls = []
        for entry in root.findall("ns:url", SITEMAP_NAMESPACE):
            loc = entry.find("ns:loc", SITEMAP_NAMESPACE)
            if loc is None or not loc.text:
                continue
            path = urlparse(loc.text).path.strip("/")
            if path == "docs" or path.startswith("docs/"):
                urls.append(loc.text)
        return urls

    async def discover_urls(self, session: aiohttp.ClientSession) -> List[str]:
        urls = await self.fetch_sitemap_urls(session, f"{self.base_url}/sitemap.xml")
        unique = sorted(set(urls))
        counts: Dict[str, int] = {}
        for url in unique:
            counts[namespace_of(url)] = counts.get(namespace_of(url), 0) + 1
        print(f"Sitemap: {len(unique)} documentation URLs")
        for name, count in sorted(counts.items()):
            skipped = " (offsite, skipped)" if name in OFFSITE_NAMESPACES else ""
            print(f"  {name or '<root>'}: {count}{skipped}")
        return unique

    async def scrape_page(self, session: aiohttp.ClientSession, url: str, force: bool) -> None:
        namespace = namespace_of(url)
        if namespace in OFFSITE_NAMESPACES:
            self.discard_local_file(url)
            self.state["pages"][url] = {
                "status": "offsite_redirect",
                "namespace": namespace,
                "checked_at": utc_now(),
            }
            self.results["offsite"].append(url)
            return

        async with self.semaphore:
            try:
                async with session.get(url, timeout=aiohttp.ClientTimeout(total=45)) as response:
                    response.raise_for_status()
                    final_url = str(response.url)
                    page_html = await response.text()
                    http_status = response.status
            except Exception as error:
                self.results["failed"].append({"url": url, "error": str(error)})
                return

        if urlparse(final_url).netloc != urlparse(self.base_url).netloc:
            self.discard_local_file(url)
            self.state["pages"][url] = {
                "status": "offsite_redirect",
                "final_url": final_url,
                "namespace": namespace,
                "checked_at": utc_now(),
            }
            self.results["offsite"].append(url)
            return

        try:
            body = extract_markdoc_source(page_html)
        except ExtractionError as error:
            self.results["failed"].append({"url": url, "error": f"extraction: {error}"})
            return

        if body is None:
            # Only trust an empty page when it says so. Otherwise an extraction
            # regression would quietly reclassify real docs as placeholders.
            if PLACEHOLDER_MARKER not in page_html:
                self.results["failed"].append(
                    {"url": url, "error": "no document body and no placeholder marker"})
                return
            self.discard_local_file(url)
            self.state["pages"][url] = {
                "status": "placeholder",
                "namespace": namespace,
                "final_url": final_url,
                "checked_at": utc_now(),
            }
            self.results["placeholder"].append(url)
            return

        slug = urlparse(url).path.rstrip("/").split("/")[-1] or "index"
        # Hash the body as served, before any heading is peeled off, so that an
        # edit to that heading still registers as a change.
        body_hash = hashlib.sha256(body.encode("utf-8")).hexdigest()
        authored_title, body = split_leading_heading(body)
        title = authored_title or extract_title(page_html, fallback=slug)

        previous = self.state["pages"].get(url, {})
        unchanged = (
            previous.get("body_sha256") == body_hash
            and previous.get("title") == title
            and previous.get("extractor_version") == EXTRACTOR_VERSION
            and self.url_to_filepath(url).exists()
        )
        if unchanged and not force:
            self.results["unchanged"].append(url)
            return

        filepath = self.url_to_filepath(url)
        filepath.parent.mkdir(parents=True, exist_ok=True)
        frontmatter = "\n".join([
            "---",
            f"title: {title}",
            f"url: {url}",
            f"namespace: {namespace}",
            f"scraped_at: {utc_now()}",
            f"source_sha256: {body_hash}",
            "---",
            "",
            f"# {title}",
            "",
            "",
        ])
        filepath.write_text(frontmatter + body.strip() + "\n", encoding="utf-8")

        self.state["pages"][url] = {
            "status": "ok",
            "http_status": http_status,
            "final_url": final_url,
            "namespace": namespace,
            "title": title,
            "filepath": str(filepath.relative_to(self.output_dir)),
            "body_sha256": body_hash,
            "extractor_version": EXTRACTOR_VERSION,
            "fetched_at": utc_now(),
        }
        bucket = "changed" if previous.get("body_sha256") else "new"
        self.results[bucket].append(url)

    async def run(self, force: bool = False, limit: Optional[int] = None) -> int:
        headers = {"User-Agent": USER_AGENT}
        async with aiohttp.ClientSession(headers=headers) as session:
            urls = await self.discover_urls(session)
            if self.sitemap_errors:
                print("\nSitemap incomplete -- aborting without touching local files:")
                for error in self.sitemap_errors:
                    print(f"  ! {error}")
                return 1
            if not urls:
                print("No URLs discovered -- aborting without touching local files.")
                return 1
            if limit:
                urls = urls[:limit]
                print(f"\nLimited to first {limit} URLs")

            known = set(self.state["pages"])
            removed = sorted(known - set(urls))

            print(f"\nFetching {len(urls)} pages ({self.max_concurrent} at a time)...")
            await asyncio.gather(*(self.scrape_page(session, url, force) for url in urls))

        # Only drop pages the site really retired: a run with failures, or one
        # narrowed by --limit, has not proved anything about the missing URLs.
        if removed and not limit and not self.results["failed"]:
            for url in removed:
                self.discard_local_file(url)
                self.state["pages"].pop(url, None)

        self.state["runs"].append({
            "finished_at": utc_now(),
            "extractor_version": EXTRACTOR_VERSION,
            "counts": {key: len(value) for key, value in self.results.items()},
        })
        self.save_state()

        print("\n" + "=" * 52)
        print(f"  new:         {len(self.results['new'])}")
        print(f"  changed:     {len(self.results['changed'])}")
        print(f"  unchanged:   {len(self.results['unchanged'])}")
        print(f"  offsite:     {len(self.results['offsite'])}")
        print(f"  placeholder: {len(self.results['placeholder'])}")
        print(f"  pruned:      {len(self.results['pruned'])}")
        print(f"  failed:      {len(self.results['failed'])}")

        for url in self.results["changed"][:40]:
            print(f"  ~ {url}")
        for item in self.results["failed"][:20]:
            print(f"  ! {item['url']}: {item['error']}")
        for path in self.results["pruned"][:20]:
            print(f"  - removed {path}")
        if removed and (limit or self.results["failed"]):
            print(f"\n{len(removed)} URL(s) absent from this run; local files kept "
                  f"because the run was incomplete:")
            for url in removed[:20]:
                print(f"  ? {url}")

        return 1 if self.results["failed"] else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true",
                        help="rewrite every page even when its content hash is unchanged")
    parser.add_argument("--limit", type=int, help="only process the first N URLs (smoke test)")
    parser.add_argument("--output-dir", default=str(Path(__file__).parent / "docs"))
    parser.add_argument("--max-concurrent", type=int, default=8)
    args = parser.parse_args()

    scraper = NinjaTraderDocsScraper(
        output_dir=args.output_dir,
        max_concurrent=args.max_concurrent,
    )
    return asyncio.run(scraper.run(force=args.force, limit=args.limit))


if __name__ == "__main__":
    sys.exit(main())
