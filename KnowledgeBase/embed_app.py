import os

from embedchain import App

# 安装依赖
# python -m pip install --upgrade 'embedchain[ollama]'  -i https://mirrors.aliyun.com/pypi/simple  
# python -m pip install ollama

# 配置 Ollama 地址，参见 https://docs.embedchain.ai/components/llms#ollama
os.environ["OLLAMA_HOST"] = "http://127.0.0.1:11434"

# 读取配置，参见
# https://docs.embedchain.ai/components/llms
# https://docs.embedchain.ai/components/embedding-models
# https://docs.embedchain.ai/components/vector-databases/chromadb
# 生成模型：deepseek-r1:7b
# 嵌入模型：all-minilm:latest 
# 向量数据库: chroma

app = App.from_config(config_path="embed_app.yml")

# 添加数据源，参见：https://docs.embedchain.ai/components/data-sources/overview
if not os.path.exists('./db/chroma.sqlite3'):
    # 添加网页
    app.add("https://baike.sogou.com/v61415269.htm?fromTitle=%E5%9F%83%E9%9A%86%C2%B7%E9%A9%AC%E6%96%AF%E5%85%8B")

    # 添加文件
    app.add('./input/Musk/埃隆·马斯克.txt')

    # 添加 Q&A
    app.add(("王子豪是谁", "一个身高180的大帅哥"), data_type="qna_pair")
    app.add(("Elon Musk的职务", "美国摄政王"), data_type="qna_pair")

# 检索
try:
    while True:
        question = input("请输入你的问题:")
        app.query(question)
except KeyboardInterrupt:
    print('程序中止')

