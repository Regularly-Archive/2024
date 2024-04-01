from langchain.retrievers.web_research import WebResearchRetriever
from langchain_community.utilities import GoogleSearchAPIWrapper
from langchain_community.vectorstores import Chroma
from langchain_openai import ChatOpenAI
from langchain_community.embeddings import HuggingFaceEmbeddings
from langchain.chains import RetrievalQAWithSourcesChain
from langchain.docstore.document import Document
from langchain.indexes import VectorstoreIndexCreator
from langchain_community.utilities import ApifyWrapper
from rich.console import Console
from rich.prompt import Prompt
import os, logging

os.environ["OPENAI_BASE_URL"] = ""
os.environ["OPENAI_API_KEY"] = ""
os.environ["APIFY_API_TOKEN"] = ""

# python -m pip install apify-client
apify = ApifyWrapper()
loader = apify.call_actor(
    actor_id="apify/website-content-crawler",
    run_input={
        "startUrls": [{"url": "https://blog.yuanpei.me"}]
    },
    dataset_mapping_function=lambda item: Document(
        page_content=item["text"] or "", metadata={"source": item["url"]}
    ),
)

# Create a vector store based on the crawled data
index = VectorstoreIndexCreator().from_loaders([loader])

if __name__ == "__main__":
   console = Console()
   logging.basicConfig()
   logging.getLogger(__name__).setLevel(logging.INFO)
   while (True):
        question = Prompt.ask("[blue]Question[blue]", default="西安的天气怎么样?")
        result = index.query(question)
        console.print("[green]Answer: [/green]" + result + "\r\n")