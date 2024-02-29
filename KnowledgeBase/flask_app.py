from flask import Flask, Response, request, stream_with_context, jsonify
from query_data import chat_with_knowledge_base
from streaming_api import ChainStreamHandler, START_FLAG, STOP_FLAG
from queue import Queue
import threading

OPENAI_BASE_URL = 'http://openai-proxy.yuanpei.me/v1/'
OPENAI_API_KEY = ''

VECTOR_STORE_PATH = '.\output\金庸武侠小说全集.pkl'


app = Flask(__name__)

@app.route('/api/chat', methods=['POST'])
def chat():
    input_text = request.json['message']
    result = chat_with_knowledge_base(input_text, OPENAI_BASE_URL, OPENAI_API_KEY, VECTOR_STORE_PATH)
    output = {'answer': result['answer']}
    return jsonify(output)

@app.route('/api/chat/streaming', methods=['POST'])
def chat_with_stream():
    q = Queue()
    handler = ChainStreamHandler(q)

    def generate(q): 
        while (True):
            result = q.get()
            if result == START_FLAG:
                continue
            if result == STOP_FLAG:
                break
            yield result
            

    input_text = request.json['message']
    threading.Thread(
       target=chat_with_knowledge_base, 
       args=(input_text, OPENAI_BASE_URL, OPENAI_API_KEY, VECTOR_STORE_PATH, handler)
    ).start()
    return Response(stream_with_context(generate(q)), mimetype="text/event-stream")


app.run(host='127.0.0.1', port=8998, debug=False)