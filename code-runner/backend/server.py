from fastapi import FastAPI, HTTPException, UploadFile, File
from fastapi.responses import JSONResponse
import docker
from pydantic import BaseModel
import os
import shutil
import uuid
import time
from config import LANGUAGE_CONFIG
from fastapi.middleware.cors import CORSMiddleware
from utils import code_to_ipynb, code_to_file, remove_ansi_sequences

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
client = docker.from_env()


class RunCodeRequest(BaseModel):
    code: str
    language: str
    notebook: bool = False


@app.post("/api/run")
async def run_code(request: RunCodeRequest):
    start_time = time.time()
    container_name = f"./runner_{uuid.uuid4().hex}"
    config = LANGUAGE_CONFIG.get(request.language)

    if not config:
        raise HTTPException(
            status_code=400, detail=f"Unsupported language: {request.language}")

    extension = config['extension']
    os.makedirs(container_name, exist_ok=True)
    if config['env'] != 'jupyter':
        code_to_file(request.code, os.path.join(container_name, f'code.{extension}'))
    else:
        code_to_ipynb(request.code, os.path.join(container_name, f'code.{extension}'))

    try:
        user = 'sandbox' if config['env'] != 'jupyter' else 'jovyan'
        container = client.containers.run(
            image=config['image'],
            command=config['command'],
            volumes={os.path.abspath(container_name): {
                'bind': f'/home/{user}', 'mode': 'rw'}},
            tty=True,
            detach=True
        )
        container.wait()

        output = container.logs().decode('utf-8')
        output = remove_ansi_sequences(output)

        if config['env'] == 'jupyter':
            converted_html = os.path.join(container_name, 'output.html')
            if not os.path.exists(converted_html):
                output = 'An error occurs when executing code.ipynb file.'
            else:
                with open(os.path.join(container_name, 'output.html'), 'r') as f:
                    output = f.read()

            return JSONResponse(content={'type': 'text/html', 'time': time.time() - start_time, "output": output})

        return JSONResponse(content={"output": output, 'type': 'text/plain', 'time': time.time() - start_time, "output": output})
    except Exception as e:
         raise HTTPException(status_code=500, detail=str(e))
    finally:
        container.stop()
        container.remove(force=True)
        shutil.rmtree(container_name)

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8001)
