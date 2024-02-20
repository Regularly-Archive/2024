from langchain.text_splitter import CharacterTextSplitter
from langchain_community.document_loaders import DirectoryLoader, TextLoader, PyPDFLoader
from langchain_community.vectorstores.faiss import FAISS
from langchain_community.embeddings import HuggingFaceEmbeddings
import os, pickle

text_splitter = CharacterTextSplitter(separator = "\n\n", chunk_size = 600, chunk_overlap = 100, length_function = len)
embeddings = HuggingFaceEmbeddings()

def create_vectors_store_from_text(inputDir, outputPath):
    print(f"Load Data & Split Text from {inputDir}.")
    text_loader_kwargs = {'autodetect_encoding': True}
    loader = DirectoryLoader(inputDir, glob="*.txt", loader_cls=TextLoader, loader_kwargs=text_loader_kwargs, show_progress=True, silent_errors=True)
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

# create_vectors_store_from_text("./input/金庸武侠小说全集", "./output/金庸武侠小说全集.pkl")
create_vectors_store_from_generic("D:\Projects\hugo-blog\content\posts", "*.md", "./output/个人博客.pkl")
# create_vectors_store_from_generic("./input/文学作品", "*.md", "./output/文学作品.pkl")

# create_vectors_store_from_generic("./input/文学作品", "*.pdf", "./output/文学作品.pkl")