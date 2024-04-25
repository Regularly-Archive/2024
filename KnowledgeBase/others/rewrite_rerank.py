from langchain_community.vectorstores import Chroma
from langchain_openai import ChatOpenAI
from langchain_community.embeddings import HuggingFaceEmbeddings
from langchain.prompts.prompt import PromptTemplate
from langchain.chains import LLMChain
from rich.console import Console
from rich.prompt import Prompt
from langchain.text_splitter import RecursiveCharacterTextSplitter
from langchain_community.document_loaders import DirectoryLoader
import modelscope
from FlagEmbedding import FlagReranker
import os, json, logging

os.environ["HF_ENDPOINT"] = "https://hf-mirror.com/"
OPENAI_API_BASE = 'https://api.moonshot.cn/v1'
OPENAI_API_KEY = ''

CHROMA_PERSIST_DIRECTORY = './output/chroma'

reranker_model_dir = modelscope.snapshot_download('Xorbits/bge-reranker-base', revision='master')
reranker = FlagReranker(reranker_model_dir, use_fp16=True)

embedding_model_dir= modelscope.snapshot_download('Xorbits/bge-base-zh-v1.5', revision='master')
embedding_function = HuggingFaceEmbeddings(model_name=embedding_model_dir)

chroma = Chroma(persist_directory=CHROMA_PERSIST_DIRECTORY, embedding_function=embedding_function)

# 对问题进行重写的提示词
rewrite_prompt_template = '''
    你是一个帮助用户完成信息检索的智能助理，你的职责是将用户输入的问题，转化为若干个相似的问题，从而帮助用户检索到更多有用的信息。此外，你还需要遵守下列约定：
    1、生成的问题必须与原问题存在一定的相关性，至少>=50%
    2、生成的问题必须与原问题相似或相近，不得改变用户原有的意图
    3、生成的问题以 JSON 格式返回，示例如下：
    ```
    {{
        "input": "《越女剑》这部小说主要讲了什么样的一个故事",
        "output": ["《越女剑》这部小说主要情节是什么","《越女剑》这部小说的故事梗概是什么"]
    }}
    ```
    4、每次最多产生 5 个相似的问题

    现在，我的问题是：{question}
    '''

# 生成最终答案的提示词
generate_prompt_template = '''
    Role:
    You are a helpful AI bot. Your name is {{name}}.

    Act:
    Please answer the question only based on the following context:
    
    {context}

    Rules:
    1. If the question is about your identity or role or name, answer '{name}' directly, without the need to refer to the context.
    2. If the context is not enough to support the generation of an answer, Please return "{empty_answer}" immediately.
    3. You have an opportunity to refine the existing answer (only if needed) with current context.
    4. You must always answer the question in Chinese. 
    5. Please don't include words like "according to the text" or "according to the context" in your answers.

    Your Question is: {question}
    '''

llm = ChatOpenAI(
    model_name="moonshot-v1-8k", 
    temperature=0.75, 
    openai_api_base=OPENAI_API_BASE, 
    openai_api_key=OPENAI_API_KEY,
    streaming=False,
)

rewrite_chain = LLMChain(
    llm=llm,
    prompt=PromptTemplate(template=rewrite_prompt_template, input_variables=["question"]),
)

qa_chain = LLMChain(
    llm=llm,
    prompt=PromptTemplate(template=generate_prompt_template, input_variables=["question", "name","context", "empty_answer"]),
)

# 生成向量
def init_embeddings(inputDir):
    text_splitter = RecursiveCharacterTextSplitter(chunk_size=500, chunk_overlap=150, length_function=len, is_separator_regex=False)
    text_loader_kwargs = {}
    loader = DirectoryLoader(inputDir, glob="*.txt", loader_kwargs=text_loader_kwargs, show_progress=True, silent_errors=True, use_multithreading=True)
    documents = loader.load_and_split(text_splitter)
    Chroma.from_documents(documents, embedding_function, persist_directory=CHROMA_PERSIST_DIRECTORY)

# 重新排序
def rerank(question, documents):
    pairs = [[question, document.page_content] for document in documents]
    scores = reranker.compute_score(pairs, normalize=True)
    documents_with_scores = [{'document':documents[idx], 'score': score} for idx, score in enumerate(scores)]
    return documents_with_scores

# 问题改写
def rewrite(question):
    try:
        result = rewrite_chain.invoke({'question': question})
        text = result['text'].replace('```json','').replace('```','').replace('\n','').replace(' ','')
        return json.loads(text)
    except:
        return {'input': question, 'output':[]}

# 向量检索
def retrieve(questions):
    documents = []
    for question in questions:
        search_result =  chroma.similarity_search(question, k=20)
        if len(search_result) > 0:
            documents.extend(search_result)
        return documents

# 生成答案
def generate(question, context):
    result = qa_chain.invoke({
        'name': 'Kimi',
        'empty_answer': '抱歉，我无法回答你的问题！',
        'question': question,
        'context': context
    })

    return result['text']
    


# init_embeddings("./input")

if __name__ == "__main__":
   console = Console()
   logging.basicConfig()
   logging.getLogger(__name__).setLevel(logging.INFO)
   while (True):
        question = Prompt.ask("[blue]Question[blue]", default="西安的天气怎么样?")

        # 对用户输入的问题进行改写
        rewrite_result = rewrite(question)
        rewrite_questions = rewrite_result['output']
        
        # 合并问题 & 检索文档
        questions = [question]
        if len(rewrite_questions) > 0:
            questions.extend(rewrite_questions)
        console.print(f"[blue][Rewrite][blue] -> \n{', '.join(questions)}")
        documents = retrieve(questions)
        
        # 对文档重新排序,取前十条文本作为上下文
        documents = rerank(question, documents)
        documents = list(sorted(documents, key=lambda x:x['score'], reverse=True))
        documents = documents[:10]
        reranked = list(map(lambda x:{'text':x['document'].page_content, 'scope':x['score']}, documents))
        console.print(f"[blue][Rerank][blue] -> \n{json.dumps(reranked, ensure_ascii=False)}")
        documents = list(map(lambda x:x['document'], documents))
        contents = list(map(lambda x:x.page_content, documents))
        context = '\n'.join(contents)
        
        # 生成答案
        answer = generate(question, context)
        console.print("[green]Answer: [/green]" + answer + "\r\n")