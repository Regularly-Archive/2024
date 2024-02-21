from playwright.async_api import Playwright
from bs4 import BeautifulSoup

async def run_extract(playwright: Playwright, target):
    chromium = playwright.chromium
    browser = await chromium.launch()
    page = await browser.new_page()
    await page.goto(target)
    collections = await extract_collections(page)
    await browser.close()
    return collections

async def extract_collections(page):
    contentFrame = page.main_frame.child_frames[0]
    cBoxHtml = await contentFrame.inner_html("#cBox")
    created_collections = extract_created_collections(cBoxHtml)
    sBoxHtml = await contentFrame.inner_html("#sBox")
    saved_collections = extract_saved_collections(sBoxHtml)
    return { "created_collections": list(created_collections), "saved_collections": list(saved_collections)}

def extract_created_collections(html):
    soup = BeautifulSoup(html, "lxml")
    ele_lis = soup.find_all("li")
    for ele_li in ele_lis:
        ele_li_img = ele_li.find("img")
        ele_li_a = ele_li.find("a")
        yield { "title": ele_li_a['title'], "cover": ele_li_img['src'], "url": "https://music.163.com/#" + ele_li_a['href']}

def extract_saved_collections(html):
    soup = BeautifulSoup(html, "lxml")
    ele_lis = soup.find_all("li")
    for ele_li in ele_lis:
        ele_li_img = ele_li.find("img")
        ele_li_a = ele_li.find("a")
        yield { "title": ele_li_a['title'], "cover": ele_li_img['src'], "url": "https://music.163.com/#" + ele_li_a['href']}
