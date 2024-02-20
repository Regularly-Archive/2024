from langchain_openai import ChatOpenAI
from langchain.chains import ConversationalRetrievalChain
from langchain.prompts.prompt import PromptTemplate
from langchain.vectorstores.base import VectorStoreRetriever
from langchain.memory import ConversationBufferMemory
from rich.console import Console
from rich.prompt import Prompt
import os, pickle, glob

os.environ["OPENAI_API_KEY"] = ""
OUTPUT_DIR = '.\output'

def load_retriever(filePath):
    with open(filePath, "rb") as f:
        vectorstore = pickle.load(f)
        retriever = VectorStoreRetriever(vectorstore=vectorstore)
        return retriever

def get_basic_qa_chain(baseUrl='', apiKey='', storeFilePath=''):
    llm = ChatOpenAI(
        model_name="gpt-3.5-turbo", 
        temperature=0, 
        openai_api_base=baseUrl, 
        openai_api_key=apiKey
    )
    retriever = load_retriever(storeFilePath)
    memory = ConversationBufferMemory(
        memory_key="chat_history", 
        return_messages=True, 
        input_key="question", 
        output_key="source_documents"
    )
    chain = ConversationalRetrievalChain.from_llm(
        llm=llm, 
        retriever=retriever, 
        memory=memory, 
        return_source_documents=True, 
        return_generated_question=True
    )
    return chain

def get_basic_qa_chain_with_prompt(baseUrl='', apiKey='', storeFilePath=''):
    llm = ChatOpenAI(
        model_name="gpt-3.5-turbo", 
        temperature=0, 
        openai_api_base=baseUrl, 
        openai_api_key=apiKey
    )
    retriever = load_retriever(storeFilePath)
    memory = ConversationBufferMemory(
        memory_key="chat_history", 
        return_messages=True, 
        input_key="question", 
        output_key="source_documents"
    )
    prompt = PromptTemplate.from_template("Please answer my question based on the docs")
    chain = ConversationalRetrievalChain.from_llm(
        llm=llm, 
        retriever=retriever, 
        memory=memory, 
        return_source_documents=True, 
        return_generated_question=True,
        prompt=prompt
    )
    return chain

def load_store_files(dir):
    return list(glob.glob(os.path.join(dir, "*.pkl")))

if __name__ == "__main__":
    console = Console()
    console.print("[bold red]---------------------------------")
    choices = load_store_files(OUTPUT_DIR)
    choice = Prompt.ask("[bold] Chat with your own knowledge! Please select a vectors storage file [bold]", default=choices[0], show_default=True, choices=choices)
    chain = get_basic_qa_chain("http://localhost:8080/v1/", 'sk-1234567', choice)
    console.print("[bold red]---------------------------------")
    
    while True:
        default_question = "请根据你掌握的知识，介绍一下《追风筝的人》这本书的主要内容"
        question = Prompt.ask("[blue]Question[blue]", default=default_question, show_default=False)
        result = chain.invoke(question)
        console.print("[green]Answer: [/green]" + result['answer'] + "\r\n" + "[green]Reference: [/green]")

        for i in range(len(result['source_documents'])):
            document = result['source_documents'][i]
            if 'page' in document.metadata.keys():
                console.print(f"{i+1}. {document.metadata['source']} - {document.metadata['page']} \r\n {document.page_content.strip()}\r\n")
            else:
                console.print(f"{i+1}. {document.metadata['source']} \r\n {document.page_content.strip()}\r\n")