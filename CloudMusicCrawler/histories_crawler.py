from playwright.async_api import Playwright
from bs4 import BeautifulSoup
import time

async def run_extract(playwright: Playwright, target):
    chromium = playwright.chromium
    browser = await chromium.launch()
    page = await browser.new_page()
    await page.goto(target)
    total = await extract_total(page)
    weekly_histories = await extract_histories(page)
    await switch_histories(page)
    time.sleep(2.5)
    all_histories = await extract_histories(page)
    await browser.close()
    return {"total": total, "weekly": weekly_histories, "all": all_histories}

async def extract_histories(page):
    contentFrame = page.main_frame.child_frames[0]
    html = await contentFrame.inner_html("#m-record")
    return list(extract(html))

async def extract_total(page):
    contentFrame = page.main_frame.child_frames[0]
    html = await contentFrame.inner_html("h4")
    return html

async def switch_histories(page, showAll=True):
    contentFrame = page.main_frame.child_frames[0]
    eleId = "#songsall" if showAll else "#songsweek"
    await contentFrame.locator(eleId).click()

def extract(html):
    soup = BeautifulSoup(html, "lxml")
    ele_ul = soup.find("ul")
    ele_lis = ele_ul.find_all("li")
    for ele_li in ele_lis:
        ele_span_text = ele_li.find("span", class_="txt")
        ele_span_song = ele_span_text.find("a")
        ele_span_artist = ele_span_song.next_sibling
        ele_span_artist = ele_span_artist.find("a") if ele_span_artist != None else None
        ele_span_percent = ele_li.find("span", class_="bg")
        yield { 
            "title": ele_span_song.get_text(), 
            "artist": "" if ele_span_artist == None else ele_span_artist.get_text(), 
            "url": "https://music.163.com/#" + ele_span_song['href'],
            "percentage": "" if ele_span_percent == None else ele_span_percent.attrs["style"].replace('width:','').replace(';','')
        }
