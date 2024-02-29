from langchain_openai import ChatOpenAI
from langchain.chains import ConversationalRetrievalChain, LLMChain, RetrievalQA, RetrievalQAWithSourcesChain, ConversationalRetrievalChain
from langchain.prompts.prompt import PromptTemplate
from langchain_core.prompts import ChatPromptTemplate
from langchain.vectorstores.base import VectorStoreRetriever
from langchain.memory import ConversationBufferMemory
from langchain.callbacks.streaming_stdout import StreamingStdOutCallbackHandler
from rich.console import Console
from rich.prompt import Prompt
import os, pickle, glob

os.environ["OPENAI_API_KEY"] = ""
OUTPUT_DIR = '.\output'


PROMPT_TEMPLATE = """
You are a helpful AI bot. Your name is {name}.
Please answer the question only based on the following context:

{context}

If the question is about your identity or role or name, answer '{name}' directly, without the need to refer to the context
If the context is not enough to support the generation of an answer, Please return "I'm sorry, I can't anser your question." immediately.
You have an opportunity to refine the existing answer (only if needed) with current context.
You must always answer the question in Chinese. 
"""

def load_retriever(filePath):
    with open(filePath, "rb") as f:
        vectorstore = pickle.load(f)
        retriever = VectorStoreRetriever(vectorstore=vectorstore)
        return retriever
    
def load_vector_store(filePath):
    with open(filePath, "rb") as f:
        vectorstore = pickle.load(f)
        return vectorstore

# 检索型
def get_basic_qa_chain(baseUrl='', apiKey='', storeFilePath=''):
    llm = ChatOpenAI(
        model_name="gpt-3.5-turbo", 
        temperature=0, 
        openai_api_base=baseUrl, 
        openai_api_key=apiKey,
        streaming=True
    )
    retriever = load_retriever(storeFilePath)
    chain = RetrievalQA.from_chain_type(
        llm=llm, 
        chain_type="stuff",
        retriever=retriever, 
        return_source_documents=True,
    )
    return chain

# 对话型 
def get_conversational_retrieval_chain(baseUrl='', apiKey='', storeFilePath='', streamingHandler=None):
    llm = ChatOpenAI(
        model_name="gpt-3.5-turbo", 
        temperature=0.75, 
        openai_api_base=baseUrl, 
        openai_api_key=apiKey,
        streaming=True,
        callbacks=[streamingHandler] if streamingHandler != None else []
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
        return_generated_question=True,
    )
    return chain

def load_store_files(dir):
    return list(glob.glob(os.path.join(dir, "*.pkl")))

# 与知识库交谈
def chat_with_knowledge_base(question, baseUrl='', apiKey='', storeFilePath='', streamingHandler=None):
    chain = get_conversational_retrieval_chain(baseUrl, apiKey, storeFilePath, streamingHandler)
    prompt = ChatPromptTemplate.from_messages([
        ("system", PROMPT_TEMPLATE),
        ("human", "{question}"),
    ])

    vector_store = load_vector_store('./output/金庸武侠小说全集.pkl')
    documents = vector_store.similarity_search(question, k=3)
    context = '\n\n'.join([document.page_content for document in documents])
    return chain.invoke(prompt.format(question=question, name="ChatGPT", context=context))

if __name__ == "__main__":
    console = Console()
    console.print("[bold red]---------------------------------")
    choices = load_store_files(OUTPUT_DIR)
    choice = Prompt.ask("[bold] Chat with your own knowledge! Please select a vectors storage file [bold]", default=choices[0], show_default=True, choices=choices)
    chain = get_conversational_retrieval_chain("http://localhost:8080/v1/", 'sk-1234567890', choice)
    console.print("[bold red]---------------------------------")

    prompt = ChatPromptTemplate.from_messages([
        ("system", PROMPT_TEMPLATE),
        ("human", "{question}"),
    ])
    vector_store = load_vector_store(choice)
    
    while True:
        default_question = "请根据你掌握的知识，介绍一下《追风筝的人》这本书的主要内容"
        question = Prompt.ask("[blue]Question[blue]", default=default_question, show_default=False)
        
        documents = vector_store.similarity_search(question, k=3)
        context = '\n\n'.join([document.page_content for document in documents])
        result = chain.invoke(prompt.format(question=question, name="ChatGPT", context=context))
        console.print("[green]Answer: [/green]" + result['answer'] + "\r\n" + "[green]Reference: [/green]")

        for i in range(len(result['source_documents'])):
            document = result['source_documents'][i]
            if 'page' in document.metadata.keys():
                console.print(f"{i+1}. {os.path.abspath(document.metadata['source'])} - {document.metadata['page']}")
            else:
                console.print(f"{i+1}. {os.path.abspath(document.metadata['source'])}")