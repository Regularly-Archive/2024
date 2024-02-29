import gradio as gr
from query_data import chat_with_knowledge_base

OPENAI_BASE_URL = 'http://openai-proxy.yuanpei.me/v1/'
OPENAI_API_KEY = ''

VECTOR_STORE_PATH = '.\output\金庸武侠小说全集.pkl'

def chatbot(input_text):
    return chat_with_knowledge_base(input_text, OPENAI_BASE_URL, OPENAI_API_KEY, VECTOR_STORE_PATH)['answer']

app = gr.Interface(
    fn=chatbot,
    inputs=gr.inputs.Textbox(lines=7, label="请输入，您想从知识库中获取什么？"),
    outputs="text",
    title="AI 本地知识库 ChatBot")
app.launch(share=True)
