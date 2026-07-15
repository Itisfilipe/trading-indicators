#!/usr/bin/env python3
"""
NinjaTrader Documentation Scraper with Playwright
Downloads all documentation pages using Playwright for JavaScript rendering
Tracks progress and supports incremental updates via sitemap
"""

import asyncio
import json
import os
import re
from pathlib import Path
from urllib.parse import urljoin, urlparse, unquote
from playwright.async_api import async_playwright
from bs4 import BeautifulSoup
import html2text
from datetime import datetime
from typing import Dict, List, Optional, Tuple
import xml.etree.ElementTree as ET
from dateutil import parser as date_parser
import aiohttp
import time

class NinjaTraderPlaywrightScraper:
    def __init__(self, base_url="https://developer.ninjatrader.com", output_dir="docs", max_concurrent=5):
        self.base_url = base_url
        self.docs_url = f"{base_url}/docs/desktop/"
        self.output_dir = Path(output_dir)
        self.max_concurrent = max_concurrent  # Lower concurrency for browser instances
        self.progress_file = self.output_dir / "scraper_progress.json"
        self.sitemap_file = self.output_dir / "sitemap.json"

        # HTML to Markdown converter
        self.h2t = html2text.HTML2Text()
        self.h2t.ignore_links = False
        self.h2t.ignore_images = False
        self.h2t.body_width = 0  # Don't wrap lines
        self.h2t.ignore_emphasis = False
        self.h2t.single_line_break = True

        # Progress tracking
        self.progress = self.load_progress()
        self.semaphore = asyncio.Semaphore(max_concurrent)
        self.browser = None
        self.playwright = None

    def load_progress(self) -> Dict:
        """Load progress from JSON file if it exists"""
        if self.progress_file.exists():
            with open(self.progress_file, 'r') as f:
                return json.load(f)
        return {
            "completed": {},  # URL: metadata pairs
            "failed": [],
            "in_progress": [],
            "url_metadata": {},  # Store lastmod dates and other metadata
            "stats": {
                "total": 0,
                "downloaded": 0,
                "failed": 0,
                "updated": 0,
                "skipped": 0,
                "start_time": datetime.now().isoformat(),
                "last_update": datetime.now().isoformat()
            }
        }

    def save_progress(self):
        """Save current progress to JSON file"""
        self.progress["stats"]["last_update"] = datetime.now().isoformat()
        self.progress["stats"]["downloaded"] = len(self.progress["completed"])
        self.progress["stats"]["failed"] = len(self.progress["failed"])

        self.output_dir.mkdir(exist_ok=True)
        with open(self.progress_file, 'w') as f:
            json.dump(self.progress, f, indent=2)

    def url_to_filepath(self, url: str) -> Path:
        """Convert URL to local file path"""
        parsed = urlparse(url)
        path = parsed.path

        # Remove /docs/desktop/ prefix
        if path.startswith('/docs/desktop/'):
            path = path[14:]
        elif path.startswith('/docs/'):
            path = path[6:]

        # Clean up the path
        path = unquote(path)
        path = re.sub(r'[<>:"|?*]', '_', path)  # Remove invalid characters

        # Handle index pages
        if not path or path.endswith('/'):
            path = path + 'index'

        # Add .md extension
        if not path.endswith('.md'):
            path = path + '.md'

        return self.output_dir / path

    async def fetch_sitemap_urls(self, session: aiohttp.ClientSession, sitemap_url: str) -> List[Tuple[str, Optional[str]]]:
        """Fetch and parse a sitemap XML file, returning URLs with their lastmod dates"""
        urls_with_dates = []
        try:
            async with session.get(sitemap_url, timeout=30) as response:
                if response.status == 200:
                    xml_content = await response.text()
                    root = ET.fromstring(xml_content)

                    # Handle both sitemap index and regular sitemap
                    namespace = {'ns': 'http://www.sitemaps.org/schemas/sitemap/0.9'}

                    # Check if it's a sitemap index
                    sitemaps = root.findall('ns:sitemap', namespace)
                    if sitemaps:
                        # It's a sitemap index, recursively fetch referenced sitemaps
                        print(f"Found sitemap index with {len(sitemaps)} sitemaps")
                        for sitemap in sitemaps:
                            loc = sitemap.find('ns:loc', namespace)
                            if loc is not None and loc.text:
                                sub_urls = await self.fetch_sitemap_urls(session, loc.text)
                                urls_with_dates.extend(sub_urls)
                    else:
                        # Regular sitemap with URLs
                        urls = root.findall('ns:url', namespace)
                        for url_elem in urls:
                            loc = url_elem.find('ns:loc', namespace)
                            lastmod = url_elem.find('ns:lastmod', namespace)

                            if loc is not None and loc.text:
                                # Only include documentation URLs
                                if '/docs/' in loc.text:
                                    lastmod_date = lastmod.text if lastmod is not None else None
                                    urls_with_dates.append((loc.text, lastmod_date))

                        print(f"Extracted {len(urls_with_dates)} URLs from {sitemap_url}")

        except Exception as e:
            print(f"Error fetching sitemap {sitemap_url}: {e}")

        return urls_with_dates

    async def extract_all_urls_from_sitemap(self) -> Dict[str, Dict]:
        """Extract all documentation URLs from the sitemap with their metadata"""
        print("Extracting URLs from sitemap...")
        url_metadata = {}

        async with aiohttp.ClientSession() as session:
            # Start with the main sitemap
            urls_with_dates = await self.fetch_sitemap_urls(session, f"{self.base_url}/sitemap.xml")

            # Process the URLs and their metadata
            for url, lastmod in urls_with_dates:
                # Only include desktop docs
                if '/docs/desktop/' in url:
                    url_metadata[url] = {
                        'lastmod': lastmod,
                        'last_downloaded': None,
                        'needs_update': True
                    }

        print(f"Found {len(url_metadata)} documentation pages in sitemap")
        return url_metadata

    async def download_page_with_browser(self, page, url: str, force_update: bool = False) -> bool:
        """Download and convert a single page to markdown using Playwright"""
        async with self.semaphore:
            # Check if we need to download this page
            if url in self.progress["completed"] and not force_update:
                # Check if the page has been updated since last download
                if url in self.progress["url_metadata"]:
                    metadata = self.progress["url_metadata"][url]
                    if not metadata.get('needs_update', True):
                        self.progress["stats"]["skipped"] = self.progress["stats"].get("skipped", 0) + 1
                        print(f"⊙ Skipped (up-to-date): {url}")
                        return True

            # Mark as in progress
            if url not in self.progress["in_progress"]:
                self.progress["in_progress"].append(url)
                self.save_progress()

            try:
                # Navigate to the page
                print(f"Loading: {url}")
                await page.goto(url, wait_until='networkidle', timeout=60000)

                # Wait for content to load (adjust selector as needed)
                # Try multiple possible content selectors
                content_loaded = False
                for selector in ['main', 'article', '.content', '.doc-content', '[class*="content"]']:
                    try:
                        await page.wait_for_selector(selector, timeout=5000)
                        content_loaded = True
                        break
                    except:
                        continue

                # Additional wait for dynamic content
                await page.wait_for_timeout(2000)

                # Get the rendered HTML
                html_content = await page.content()
                soup = BeautifulSoup(html_content, 'html.parser')

                # Extract main content
                content = None
                for selector in ['main', 'article', '.content', '#content', '.documentation', '.doc-content', '[class*="content"]']:
                    content = soup.select_one(selector)
                    if content:
                        break

                if not content:
                    # Fallback to body
                    content = soup.body if soup.body else soup

                # Remove navigation, headers, footers, and other non-content elements
                for elem in content.select('nav, header, footer, .navigation, .sidebar, .menu, script, style, [class*="nav"], [class*="header"], [class*="footer"]'):
                    elem.decompose()

                # Extract title
                title = "Untitled"
                # Try multiple ways to get the title
                h1 = soup.find('h1')
                if h1:
                    title = h1.get_text(strip=True)
                elif soup.title:
                    title = soup.title.string
                    # Remove common suffixes
                    title = re.sub(r'\s*[-|].*$', '', title)

                # Convert to markdown
                markdown_content = self.h2t.handle(str(content))

                # Clean up excessive blank lines
                markdown_content = re.sub(r'\n\n\n+', '\n\n', markdown_content)

                # Add metadata header with lastmod info
                lastmod = self.progress["url_metadata"].get(url, {}).get('lastmod', 'Unknown')
                metadata = f"""---
title: {title}
url: {url}
scraped_at: {datetime.now().isoformat()}
lastmod: {lastmod}
---

# {title}

"""
                markdown_content = metadata + markdown_content

                # Save to file
                filepath = self.url_to_filepath(url)
                filepath.parent.mkdir(parents=True, exist_ok=True)

                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(markdown_content)

                # Update progress with metadata
                self.progress["completed"][url] = {
                    "downloaded_at": datetime.now().isoformat(),
                    "filepath": str(filepath.relative_to(self.output_dir)),
                    "lastmod": lastmod
                }

                # Update URL metadata
                if url in self.progress["url_metadata"]:
                    self.progress["url_metadata"][url]["last_downloaded"] = datetime.now().isoformat()
                    self.progress["url_metadata"][url]["needs_update"] = False

                if url in self.progress["in_progress"]:
                    self.progress["in_progress"].remove(url)

                action = "Updated" if url in self.progress["completed"] else "Downloaded"
                print(f"✓ {action}: {filepath.relative_to(self.output_dir)}")
                return True

            except Exception as e:
                print(f"✗ Failed to download {url}: {e}")
                self.progress["failed"].append({"url": url, "error": str(e)})
                if url in self.progress["in_progress"]:
                    self.progress["in_progress"].remove(url)
                return False
            finally:
                self.save_progress()

    async def download_all_pages(self, urls: List[str]):
        """Download all pages using Playwright browser instances"""
        print(f"\nDownloading {len(urls)} pages with {self.max_concurrent} concurrent browser tabs...")

        # Initialize Playwright and browser
        self.playwright = await async_playwright().start()
        self.browser = await self.playwright.chromium.launch(
            headless=True,
            args=['--disable-blink-features=AutomationControlled']
        )

        # Create browser contexts for concurrent downloads
        contexts = []
        pages = []
        for i in range(min(self.max_concurrent, len(urls))):
            context = await self.browser.new_context(
                viewport={'width': 1920, 'height': 1080},
                user_agent='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
            )
            page = await context.new_page()
            contexts.append(context)
            pages.append(page)

        start_time = time.time()

        # Process URLs in batches
        results = []
        for i in range(0, len(urls), self.max_concurrent):
            batch = urls[i:i+self.max_concurrent]
            batch_tasks = []

            for j, url in enumerate(batch):
                if j < len(pages):
                    batch_tasks.append(self.download_page_with_browser(pages[j], url))

            batch_results = await asyncio.gather(*batch_tasks)
            results.extend(batch_results)

            # Small delay between batches to avoid overwhelming the server
            if i + self.max_concurrent < len(urls):
                await asyncio.sleep(1)

        # Clean up
        for context in contexts:
            await context.close()
        await self.browser.close()
        await self.playwright.stop()

        elapsed = time.time() - start_time
        successful = sum(1 for r in results if r)

        print(f"\n{'='*50}")
        print(f"Download completed in {elapsed:.1f} seconds")
        print(f"Successfully downloaded: {successful}/{len(urls)}")
        print(f"Failed: {len(urls) - successful}")
        print(f"Average speed: {len(urls)/elapsed:.1f} pages/second")

    def create_enhanced_sitemap(self):
        """Create an enhanced sitemap JSON file with metadata"""
        sitemap = {
            "total_pages": len(self.progress["url_metadata"]),
            "base_url": self.base_url,
            "scraped_at": datetime.now().isoformat(),
            "last_check": datetime.now().isoformat(),
            "stats": {
                "downloaded": len(self.progress["completed"]),
                "failed": len(self.progress["failed"]),
                "up_to_date": self.progress["stats"].get("skipped", 0)
            },
            "pages": []
        }

        for url in sorted(self.progress["url_metadata"].keys()):
            filepath = self.url_to_filepath(url)
            relative_path = filepath.relative_to(self.output_dir)

            page_info = {
                "url": url,
                "local_path": str(relative_path),
                "downloaded": url in self.progress["completed"],
                "lastmod": self.progress["url_metadata"][url].get("lastmod"),
                "last_downloaded": self.progress["url_metadata"][url].get("last_downloaded"),
                "needs_update": self.progress["url_metadata"][url].get("needs_update", False)
            }

            if url in self.progress["completed"]:
                page_info["downloaded_at"] = self.progress["completed"][url].get("downloaded_at")

            sitemap["pages"].append(page_info)

        with open(self.sitemap_file, 'w') as f:
            json.dump(sitemap, f, indent=2)

        print(f"Enhanced sitemap saved to {self.sitemap_file}")

    async def run(self, force_update: bool = False, test_mode: bool = False):
        """Main scraper execution"""
        print("NinjaTrader Documentation Scraper with Playwright")
        print("="*50)

        # Extract all URLs from sitemap
        url_metadata = await self.extract_all_urls_from_sitemap()

        # Merge with existing metadata if we have it
        if self.progress["url_metadata"]:
            print("Merging with existing metadata...")
            for url, metadata in url_metadata.items():
                if url in self.progress["url_metadata"]:
                    # Check if the page has been updated
                    old_lastmod = self.progress["url_metadata"][url].get('lastmod')
                    new_lastmod = metadata.get('lastmod')

                    if old_lastmod and new_lastmod and old_lastmod != new_lastmod:
                        print(f"Page updated: {url}")
                        self.progress["url_metadata"][url]['needs_update'] = True
                        self.progress["url_metadata"][url]['lastmod'] = new_lastmod
                else:
                    # New URL
                    self.progress["url_metadata"][url] = metadata
        else:
            self.progress["url_metadata"] = url_metadata

        self.progress["stats"]["total"] = len(self.progress["url_metadata"])
        self.save_progress()

        # Determine which URLs need downloading
        urls_to_download = []
        for url, metadata in self.progress["url_metadata"].items():
            if force_update or metadata.get('needs_update', True) or url not in self.progress["completed"]:
                urls_to_download.append(url)

        # In test mode, only download a few pages
        if test_mode:
            urls_to_download = urls_to_download[:5]
            print(f"\nTEST MODE: Only downloading first 5 pages")

        print(f"Total pages in sitemap: {len(self.progress['url_metadata'])}")
        print(f"Already downloaded: {len(self.progress['completed'])}")
        print(f"Need to download/update: {len(urls_to_download)}")

        if urls_to_download:
            # Download all pages
            await self.download_all_pages(urls_to_download)

        # Create enhanced sitemap
        self.create_enhanced_sitemap()

        # Final statistics
        print("\n" + "="*50)
        print("Final Statistics:")
        print(f"Total pages: {len(self.progress['url_metadata'])}")
        print(f"Downloaded: {len(self.progress['completed'])}")
        print(f"Skipped (up-to-date): {self.progress['stats'].get('skipped', 0)}")
        print(f"Failed: {len(self.progress['failed'])}")

        if self.progress["failed"]:
            print("\nFailed URLs:")
            for item in self.progress["failed"][:10]:  # Show first 10 failures
                if isinstance(item, dict):
                    print(f"  - {item['url']}: {item['error']}")
                else:
                    print(f"  - {item}")
            if len(self.progress["failed"]) > 10:
                print(f"  ... and {len(self.progress['failed']) - 10} more")

async def main():
    # Configuration
    scraper = NinjaTraderPlaywrightScraper(
        base_url="https://developer.ninjatrader.com",
        output_dir="docs",
        max_concurrent=3  # Lower concurrency for browser instances
    )

    # Run full scrape - downloads all pages
    await scraper.run(force_update=False, test_mode=False)

if __name__ == "__main__":
    asyncio.run(main())