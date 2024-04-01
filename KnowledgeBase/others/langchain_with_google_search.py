from langchain.retrievers.web_research import WebResearchRetriever
from langchain_community.utilities import GoogleSearchAPIWrapper
from langchain_community.vectorstores import Chroma
from langchain_openai import ChatOpenAI
from langchain_community.embeddings import HuggingFaceEmbeddings
from langchain.chains import RetrievalQAWithSourcesChain
from rich.console import Console
from rich.prompt import Prompt
import os, logging

os.environ["HF_ENDPOINT"] = ""
os.environ["OPENAI_API_KEY"] = ""
# https://programmablesearchengine.google.com/
os.environ["GOOGLE_CSE_ID"] = ""
# https://console.cloud.google.com/apis/api/customsearch.googleapis.com/credentials
os.environ["GOOGLE_API_KEY"] = ""

vectorstore = Chroma(
    embedding_function=HuggingFaceEmbeddings(model_name="GanymedeNil/text2vec-large-chinese"), 
    persist_directory="./output/chroma/"
)

llm = ChatOpenAI(
    base_url="https://openai-proxy.yuanpei.me/v1/",
    api_key=os.environ["OPENAI_API_KEY"],
    model_name="gpt-3.5-turbo", 
    temperature=0.75,
)

# python -m pip install google-api-python-client
search = GoogleSearchAPIWrapper()
result = search.run('blog.yuanpei.me')

web_research_retriever = WebResearchRetriever.from_llm(
    llm=llm, 
    search=search,
    vectorstore=vectorstore, 
)

def get_qa_chain_with_google_search():
    return RetrievalQAWithSourcesChain.from_chain_type(
        llm=llm, 
        retriever=web_research_retriever,
    )

if __name__ == "__main__":
   console = Console()
   logging.basicConfig()
   logging.getLogger(__name__).setLevel(logging.INFO)
   chain = get_qa_chain_with_google_search()
   while (True):
        question = Prompt.ask("[blue]Question[blue]", default="西安的天气怎么样?")
        result = chain.invoke(question)
        console.print("[green]Answer: [/green]" + result['answer'] + "\r\n")