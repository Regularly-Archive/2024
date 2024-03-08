from langchain.text_splitter import CharacterTextSplitter, RecursiveCharacterTextSplitter
from langchain_community.document_loaders import DirectoryLoader, TextLoader, PyPDFLoader, WebBaseLoader
from langchain_community.vectorstores.faiss import FAISS
# pip install pgvector
from langchain.vectorstores.pgvector import PGVector
from langchain.vectorstores.pgembedding import PGEmbedding
from langchain_community.embeddings import HuggingFaceEmbeddings
import os, pickle

os.environ["HF_ENDPOINT"] = "https://hf-mirror.com/"

text_splitter = CharacterTextSplitter(separator = "\n\n", chunk_size = 600, chunk_overlap = 100, length_function = len)
text_splitter = RecursiveCharacterTextSplitter(chunk_size=1000, chunk_overlap=200)
embeddings = HuggingFaceEmbeddings(model_name="GanymedeNil/text2vec-large-chinese")

def create_vectors_store_from_text(inputDir, outputPath):
    print(f"Load Data & Split Text from {inputDir}.")
    text_loader_kwargs = {'autodetect_encoding': True}
    loader = DirectoryLoader(inputDir, glob="*.txt", loader_cls=TextLoader, loader_kwargs=text_loader_kwargs, show_progress=True, silent_errors=True, use_multithreading=True)
    documents = loader.load_and_split(text_splitter)

    print(f"Creating Vectors for {outputPath}.")
    outputDir = os.path.dirname(outputPath)
    if not os.path.exists(outputDir):
        os.makedirs(outputDir)
    vectorstore = FAISS.from_documents(documents, embeddings)
    with open(outputPath, "wb") as f:
        pickle.dump(vectorstore, f)

def create_vectors_store_from_pdf(inputDir, outputPath):
    print(f"Load Data & Split Text from {inputDir}.")
    text_loader_kwargs = {}
    loader = DirectoryLoader(inputDir, glob="*.pdf", loader_cls=PyPDFLoader, loader_kwargs=text_loader_kwargs, show_progress=True, silent_errors=True)
    documents = loader.load_and_split(text_splitter)

    print(f"Creating Vectors for {outputPath}.")
    outputDir = os.path.dirname(outputPath)
    if not os.path.exists(outputDir):
        os.makedirs(outputDir)
    vectorstore = FAISS.from_documents(documents, embeddings)
    with open(outputPath, "wb") as f:
        pickle.dump(vectorstore, f)

def create_vectors_store_from_generic(inputDir, glob, outputPath):
    print(f"Load Data & Split Text from {inputDir}/{glob}.")
    text_loader_kwargs = {}
    loader = DirectoryLoader(inputDir, glob=glob, loader_kwargs=text_loader_kwargs, show_progress=True, silent_errors=True)
    documents = loader.load_and_split(text_splitter)

    print(f"Creating Vectors for {outputPath}.")
    outputDir = os.path.dirname(outputPath)
    if not os.path.exists(outputDir):
        os.makedirs(outputDir)
    vectorstore = FAISS.from_documents(documents, embeddings)
    with open(outputPath, "wb") as f:
        pickle.dump(vectorstore, f)

def create_vectors_store_from_web(url, outputPath):
    print(f"Load Data & Split Text from {url}.")
    loader = WebBaseLoader(
        web_paths=(url,),
        bs_kwargs={}
    )
    documents = loader.load_and_split(text_splitter)

    print(f"Creating Vectors for {outputPath}.")
    outputDir = os.path.dirname(outputPath)
    if not os.path.exists(outputDir):
        os.makedirs(outputDir)
    vectorstore = FAISS.from_documents(documents, embeddings)
    with open(outputPath, "wb") as f:
        pickle.dump(vectorstore, f)

if __name__ == '__main__':
    create_vectors_store_from_text("./input/金庸武侠小说全集", "./output/金庸武侠小说全集.pkl")
    create_vectors_store_from_generic("D:\Projects\hugo-blog\content\posts", "*.md", "./output/个人博客.pkl")
    create_vectors_store_from_pdf("./input/文学作品", "./output/文学作品.pkl")
    create_vectors_store_from_web("https://learn.microsoft.com/zh-cn/aspnet/core/tutorials/min-web-api?view=aspnetcore-8.0&tabs=visual-studio", "./output/MSDN.pkl")