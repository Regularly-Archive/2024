from playwright.async_api import Playwright
from bs4 import BeautifulSoup

async def run_extract(playwright: Playwright, target):
    chromium = playwright.chromium
    browser = await chromium.launch()
    page = await browser.new_page()
    await page.goto(target)
    weekly_histories = await extract_histories(page)
    #await switch_histories(page)
    all_histories = await extract_histories(page)
    await browser.close()
    return {"weekly": weekly_histories, "all": all_histories}

async def extract_histories(page):
    contentFrame = page.main_frame.child_frames[0]
    html = await contentFrame.inner_html("#m-record")
    return list(extract(html))

async def switch_histories(page, showAll=True):
    contentFrame = page.main_frame.child_frames[0]
    eleId = "songsall" if showAll else "songsweek"
    await contentFrame.page.get_by_test_id(eleId).click()

def extract(html):
    soup = BeautifulSoup(html)
    ele_ul = soup.find("ul")
    ele_lis = ele_ul.find_all("li")
    for ele_li in ele_lis:
        ele_span_text = ele_li.find("span", class_="txt")
        ele_span_song = ele_span_text.find("a")
        ele_span_artist = ele_span_song.next_sibling
        ele_span_artist = ele_span_artist.find("a")
        yield { "title": ele_span_song.get_text(), "artist": ele_span_artist.get_text(), "url": "https://music.163.com/#" + ele_span_song['href']}
