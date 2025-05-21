from fastapi import FastAPI, HTTPException, Body
from fastapi.middleware.cors import CORSMiddleware
import time
from config import LANGUAGE_CONFIG
from utils import code_to_ipynb, code_to_file, prepare_code_dir, create_container, install_dependencies, run_code_in_container, read_output, cleanup_container, remove_ansi_sequences
from models import RunCodeRequest, RunJupyterCellRequest, RunCodeResponse

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.post("/api/code/run", response_model=RunCodeResponse)
async def run_code(request: RunCodeRequest = Body(...)):
    start_time = time.time()
    config = LANGUAGE_CONFIG.get(request.language)
    if not config:
        raise HTTPException(status_code=400, detail=f"Unsupported language: {request.language}")
    
    extension = config['extension']
    env = config['env']
    user = 'sandbox' if env != 'jupyter' else 'jovyan'
    temp_dir = prepare_code_dir(request.code, extension, env, code_to_file, code_to_ipynb, language=request.language, dependencies=request.dependencies)
    container = None
    try:
        container = create_container(config, temp_dir, user, '')
        install_dependencies(container, request.language, request.dependencies, user, config)
        exec_result = run_code_in_container(container, config['commandRedirect'], user)
        output = exec_result.output.decode('utf-8')
        output = remove_ansi_sequences(output)
        output = read_output(temp_dir, output)
        output = remove_ansi_sequences(output)
        duration = time.time() - start_time
        return RunCodeResponse(output=output, contentType='text/plain', duration=duration, language=request.language)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        cleanup_container(container, temp_dir)

@app.post("/api/jupyter/run", response_model=RunCodeResponse)
async def run_jupyter(request: RunJupyterCellRequest = Body(...)):
    start_time = time.time()
    config = LANGUAGE_CONFIG.get(f'jupyter-{request.language}')
    if not config:
        raise HTTPException(status_code=400, detail=f"Unsupported language: {request.language}")
    
    extension = config['extension']
    env = config['env']
    user = 'jovyan'
    temp_dir = prepare_code_dir(request.code, extension, env, code_to_file, code_to_ipynb, language=request.language, dependencies=request.dependencies)
    container = None
    try:
        container = create_container(config, temp_dir, user, request.format)
        install_dependencies(container, request.language, request.dependencies, user, config)
        exec_result = run_code_in_container(container, config['commandRedirect'], user)
        output = exec_result.output.decode('utf-8')
        output = remove_ansi_sequences(output)
        output = read_output(temp_dir, output)
        output = remove_ansi_sequences(output)
        duration = time.time() - start_time
        return RunCodeResponse(output=output, contentType=f'text/{request.format}', duration=duration, language=request.language)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        cleanup_container(container, temp_dir)


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8001)
