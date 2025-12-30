from fastapi import FastAPI, HTTPException, Body, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
import time
import os
import tempfile
from typing import Optional, List
from config import LANGUAGE_CONFIG
from utils import (
    code_to_ipynb, code_to_file, prepare_code_dir, create_container,
    install_dependencies, run_command as run_container_command, read_output, cleanup_container,
    remove_ansi_sequences, prepare_project_dir, prepare_project_from_archive,
    find_entry_point, detect_project_type, extract_archive
)
from models import RunCodeRequest, RunJupyterCellRequest, RunCodeResponse, RunFilesRequest, ProjectArchiveResponse
from models_bash import BashScriptResponse
import traceback
import shutil
from handlers.context import HandlerContext
from handlers.resolver import HandlerResolver
from services.runner import RunnerService
from services.detector import ProjectDetector

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

detector = ProjectDetector()

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
        exec_result = run_container_command(container, config['commandRedirect'], user)
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
        exec_result = run_container_command(container, config['commandRedirect'], user)
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

@app.post("/api/files/run", response_model=RunCodeResponse)
async def run_files(request: RunFilesRequest = Body(...)):
    start_time = time.time()
    config = LANGUAGE_CONFIG.get(request.language)
    if not config:
        raise HTTPException(status_code=400, detail=f"Unsupported language: {request.language}")
    
    extension = config['extension']
    env = config['env']
    user = 'sandbox' if env != 'jupyter' else 'jovyan'
    temp_dir = prepare_project_dir(request.files)
    container = None
    try:
        container = create_container(config, temp_dir, user, '')
        install_dependencies(container, request.language, request.dependencies, user, config)
        exec_result = run_container_command(container, config['commandRedirect'], user)
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


@app.post("/api/project/run-archive", response_model=ProjectArchiveResponse)
async def run_project_archive(
    archive_file: UploadFile = File(...),
    language: Optional[str] = Form(None),
    entry_point: Optional[str] = Form(None),
    build_command: Optional[str] = Form(None),
    run_command: Optional[str] = Form(None),
    dependencies: Optional[List[str]] = Form(None)
):
    """
    运行压缩包中的项目
    """
    temp_archive_path = None
    try:

        filename = archive_file.filename
        if not filename.endswith(('.zip', '.tar.gz', '.tgz', '.tar.bz2', '.tbz2')):
            raise HTTPException(
                status_code=400,
                detail="Unsupported archive format. Supported formats: zip, tar.gz, tar.bz2"
            )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Validation error: {str(e)}")

    try:
        # 保存上传的压缩包到临时文件
        with tempfile.NamedTemporaryFile(delete=False, suffix=os.path.splitext(archive_file.filename)[1]) as tmp_file:
            temp_archive_path = tmp_file.name
            content = await archive_file.read()
            tmp_file.write(content)

        project_dir = tempfile.mkdtemp(prefix='project_')
        extract_archive(temp_archive_path, project_dir)

        ctx = HandlerContext(
            project_info=detector.detect_project_info(project_dir),
        )

        resolver = HandlerResolver()
        handler = resolver.resolve(ctx)
        runnerService = RunnerService()
        output = runnerService.run(handler)
        print(ctx.execution_result)
        return RunCodeResponse(
            output=output, 
            contentType='text/plain', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
    except Exception as e:
        import traceback
        print(traceback.print_exc())
        print(f"Error running project archive: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to run project archive: {str(e)}")

@app.post("/api/bash/run", response_model=BashScriptResponse)
async def run_bash_script(
    archive_file: UploadFile = File(...),
    main_script: str = Form(None),
    arguments: str = Form(None)
):
    """
    运行bash脚本（支持多文件引用）
    """
    from models_bash import BashScriptResponse

    start_time = time.time()
    temp_archive_path = None
    container = None
    run_cmd = None

    try:
        print(f"Bash script request: file={archive_file.filename}, main_script={main_script}, arguments={arguments}")

        # 保存上传的压缩包
        temp_dir = tempfile.mkdtemp()
        temp_archive_path = os.path.join(temp_dir, archive_file.filename)

        with open(temp_archive_path, 'wb') as f:
            content = await archive_file.read()
            f.write(content)

        # 解压压缩包
        script_dir = os.path.join(temp_dir, 'bash_workdir')
        os.makedirs(script_dir, exist_ok=True)

        if archive_file.filename.endswith('.zip'):
            shutil.unpack_archive(temp_archive_path, script_dir, 'zip')
        else:
            # 支持 tar.gz
            shutil.unpack_archive(temp_archive_path, script_dir, 'tar')

        # 设置正确的权限
        if os.name != "nt":
            uid = os.getuid()
            gid = os.getgid()
            os.chown(script_dir, uid, gid)
            os.chmod(script_dir, 0o755)

        # 确定主脚本
        if main_script:
            main_script_path = os.path.join(script_dir, main_script.lstrip('/'))
            if not os.path.exists(main_script_path):
                raise HTTPException(
                    status_code=400,
                    detail=f"Specified script '{main_script}' not found in archive"
                )
            main_script = main_script.lstrip('/')  # Remove leading slash if any
        else:
            # 查找主脚本（main.sh > run.sh > start.sh > 第一个.sh）
            sh_files = []
            for root, dirs, files in os.walk(script_dir):
                for file in files:
                    if file.endswith('.sh'):
                        rel_path = os.path.relpath(os.path.join(root, file), script_dir)
                        sh_files.append(rel_path)

            if not sh_files:
                raise HTTPException(status_code=400, detail="No bash scripts found in archive")

            # 优先级：main.sh > run.sh > start.sh > 第一个.sh
            main_script_candidates = ['main.sh', 'run.sh', 'start.sh']
            main_script = None
            for candidate in main_script_candidates:
                for sh_file in sh_files:
                    if sh_file == candidate:
                        main_script = candidate
                        break
                if main_script:
                    break

            if not main_script:
                main_script = sh_files[0]  # 取第一个找到的sh文件

        # 确保主脚本可执行
        main_script_path = os.path.join(script_dir, main_script)
        os.chmod(main_script_path, 0o755)

        # 构建运行命令
        run_cmd = f'bash {main_script}'
        if arguments:
            run_cmd += f' {arguments}'

        print(f"Running bash command: {run_cmd}")

        # 创建容器
        config = LANGUAGE_CONFIG.get('bash', {})
        container = create_container(config, script_dir,'sandbox', '')

        # 设置正确的权限
        exec_result = run_container_command(
            container,
            f"sh -c '{run_cmd} > output.txt 2>&1'",
            user='sandbox'
        )

        # 读取输出
        output = exec_result.output.decode('utf-8') if exec_result.output else ""

        # 如果 output.txt 有内容，优先使用它
        output = read_output(script_dir, output)

        # 清理输出
        output = remove_ansi_sequences(output)

        duration = time.time() - start_time

        return BashScriptResponse(
            output=output,
            duration=duration,
            exit_code=exec_result.exit_code
        )

    except HTTPException:
        raise
    except Exception as e:
        print(f"Bash script error: {e}")
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=f"Failed to run bash script: {str(e)}")
    finally:
        # 清理
        if container:
            try:
                container.kill()
                container.remove()
            except:
                pass

        # 清理临时文件
        if temp_archive_path and os.path.exists(temp_archive_path):
            try:
                os.unlink(temp_archive_path)
            except:
                pass

        if temp_dir and os.path.exists(temp_dir):
            try:
                shutil.rmtree(temp_dir)
            except:
                pass

@app.post("/api/python/run", response_model=RunCodeResponse)
async def run_code(request: RunCodeRequest = Body(...)):
    start_time = time.time()
    project_dir = f"./runner_{os.urandom(8).hex()}"
    os.makedirs(project_dir, exist_ok=True)
    with open(os.path.join(project_dir, 'test.py'), 'w', encoding='utf-8') as f:
        f.write(request.code)
    
    project_info = detector.detect_project_info(project_dir)
    runtime_info =  {
        'docker_image': LANGUAGE_CONFIG.get('python3').get('image')
    }
    ctx = HandlerContext(
        runtime_info=runtime_info,
        project_info=project_info,
        user='sandbox'
    )


    resolver = HandlerResolver()
    handler = resolver.resolve(ctx)
    runnerService = RunnerService()
    output = runnerService.run(handler)
    duration = time.time() - start_time
    
    return RunCodeResponse(output=output, contentType='text/plain', duration=duration, language=ctx.language)

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get('server_port', '8001'))
    uvicorn.run(app, host="0.0.0.0", port=port)
