from flask import Flask, Response, request, stream_with_context, jsonify
from query_data import chat_with_knowledge_base, load_vector_store
from streaming_api import ChainStreamHandler, START_FLAG, STOP_FLAG
from initial_data import create_vectors_store_from_generic, create_vectors_store_from_web
from queue import Queue
import os, threading, json
import uuid

OPENAI_BASE_URL = 'http://openai-proxy.yuanpei.me/v1/'
OPENAI_API_KEY = ''

VECTOR_STORE_PATH = '.\output\金庸武侠小说全集.pkl'

app = Flask(__name__)
app.config['UPLOAD_FOLDER'] = 'uploads/'
app.config['ALLOWED_EXTENSIONS'] = set(['txt', 'pdf', 'doc', 'docx', 'md'])
app.config['MAX_CONTENT_LENGTH'] = 16 * 1024 * 1024

def is_allowed_file(filename):
    return '.' in filename and filename.rsplit('.', 1)[1] in app.config['ALLOWED_EXTENSIONS']

def generate_embeddings_for_file(filePath):
    create_vectors_store_from_generic(filePath, "*.*", os.path.join(filePath, 'embeddings.pkl'))

def generate_embeddings_for_web(task_id, url):
    create_vectors_store_from_web(url, os.path.join(app.config['UPLOAD_FOLDER'], task_id, 'embeddings.pkl') )

@app.route('/upload', methods=['POST'])
def upload_file():
    if 'file' not in request.files:
        return jsonify({'error': 'No file part'}), 400
    file = request.files['file']
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400


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

@app.route('/api/knowledges', methods=['POST'])
def add_knowledge_from_file():
    task_id = str(uuid.uuid1())
    uploaded_files = request.files.getlist("file")
    archived_files = []
    errored_files = []
    task_folder = os.path.join(app.config['UPLOAD_FOLDER'], task_id)
    os.makedirs(task_folder, exist_ok=True)
    for file in uploaded_files:
        if file and is_allowed_file(file.filename):
            filename = os.path.join(task_folder, file.filename)
            file.save(filename)
            archived_files.append(filename)
        else:
            errored_files.append(file.filename)
    
    if errored_files:
        return {
            'code': 200,
            'result': 'failure',
            'message': ",".join(errored_files)
        }
    else:
        threading.Thread(target=generate_embeddings_for_file, args=(task_folder,)).start()
        return {
            'code': 200,
            'result': 'success',
            'message': f"The task {task_id} has been created."
        }
    
@app.route('/api/web-knowledges', methods=['POST'])
def add_knowledge_from_web():
    task_id = str(uuid.uuid1())
    url = request.json["url"]
    task_folder = os.path.join(app.config['UPLOAD_FOLDER'], task_id)
    os.makedirs(task_folder, exist_ok=True)
    if url == None or url == '':
        return {
            'code': 200,
            'result': 'failure',
            'message': "The field 'url' is required."
        }
    else:
        threading.Thread(target=generate_embeddings_for_web, args=(task_id, url)).start()
        return {
            'code': 200,
            'result': 'success',
            'message': f"The task {task_id} has been created."
        }

@app.route('/api/tasks', methods=['GET'])
def query_embedding_status():
    task_id = request.args.get("id")
    if task_id == None or task_id == '':
        return {
            'code': 200,
            'result': 'failure',
            'message': "The field 'id' is required."
        }
    else:
        embeddingFile = os.path.join(app.config['UPLOAD_FOLDER'], task_id, "embeddings.pkl")
        jobStatus = 'completed' if os.path.exists(embeddingFile) else 'in progress'
        return {
            'code': 200,
            'result': 'success',
            'message': f"The task {task_id} has been {jobStatus}.",
        }

@app.route('/api/knowledges/<taskId>/test', methods=['GET'])
def query_knowledge(taskId):
    embeddingFile = os.path.join(app.config['UPLOAD_FOLDER'], taskId, "embeddings.pkl")
    topK = request.args.get("topK", default=3, type=int)
    query = request.args.get("query", default="")
    if not os.path.exists(embeddingFile):
        return {
            'code': 200,
            'result': 'failure',
            'message': "The 'taskId' is invalid."
        }
    else:
        store = load_vector_store(embeddingFile)
        result = store.similarity_search_with_relevance_scores(query=query,k=topK)
        output = list(map(lambda x: {'page_content': x[0].page_content, 'metadata':x[0].metadata, 'scope': x[1]}, result))
        return {
            'code': 200,
            'result': 'success',
            'message': output
        }

app.run(host='127.0.0.1', port=8998, debug=False)