import asyncio
from playwright.async_api import async_playwright
from collections_crawler import run_extract as run_extract_collections
from histories_crawler import run_extract as run_extract_histories
import os, json

async def main():
    async with async_playwright() as playwright:
        collections = await run_extract_collections(playwright, "https://music.163.com/#/user/home?id=47002864")
        histories = await run_extract_histories(playwright, "https://music.163.com/#/user/songs/rank?id=47002864")
        with open("./output/collections.json", "w", encoding="utf-8") as f:
            json.dump(collections, f, ensure_ascii=False, indent=2)
        with open("./output/histories.json", "w", encoding="utf-8") as f:
            json.dump(histories, f, ensure_ascii=False, indent=2)

os.makedirs("./output", exist_ok=True)
asyncio.run(main())
