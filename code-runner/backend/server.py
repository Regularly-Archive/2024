from fastapi import FastAPI, HTTPException, Body, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
import time
import os
import tempfile
from typing import Optional, List
from config import LANGUAGE_CONFIG, PROJECT_DETECTORS
from utils import (
    code_to_ipynb, code_to_file, prepare_code_dir, create_container,
    install_dependencies, run_command as run_container_command, read_output, cleanup_container,
    remove_ansi_sequences, prepare_project_dir, prepare_project_from_archive,
    find_entry_point, detect_project_type
)
from models import RunCodeRequest, RunJupyterCellRequest, RunCodeResponse, RunFilesRequest, ProjectArchiveResponse
import traceback

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
    start_time = time.time()
    temp_archive_path = None

    try:
        # 验证上传的文件
        print(f"Upload file: {archive_file.filename}")
        print(f"Language: {language}")
        print(f"Entry point: {entry_point}")
    except Exception as e:
        print(e)
        raise HTTPException(status_code=500, detail=f"Upload error: {str(e)}\nCheck logs for details")

    try:
        # 验证上传的文件
        print(f"Validating archive file...")
        if hasattr(archive_file, 'filename') and archive_file.filename:
            filename = archive_file.filename
            print(f"Filename: {filename}")
        else:
            filename = "unknown"
            print("Using unknown filename")

        print(f"Validation check - filename: {filename}")
        if not filename.endswith(('.zip', '.tar.gz', '.tgz', '.tar.bz2', '.tbz2')):
            print("Unsupported format detected")
            raise HTTPException(
                status_code=400,
                detail="Unsupported archive format. Supported formats: zip, tar.gz, tar.bz2"
            )
        print("Validation passed")
    except Exception as e:
        print(e)
        raise HTTPException(status_code=500, detail=f"Validation error: {str(e)}")

    try:
        # 保存上传的压缩包到临时文件
        print(f"About to read file content...")
        with tempfile.NamedTemporaryFile(delete=False, suffix=os.path.splitext(archive_file.filename)[1]) as tmp_file:
            temp_archive_path = tmp_file.name
            content = await archive_file.read()
            tmp_file.write(content)

        # 解压并检测项目类型
        print("Calling prepare_project_from_archive...")
        project_dir, project_info = prepare_project_from_archive(temp_archive_path)
        print(f"Project dir: {project_dir}")
        print(f"Project info: {project_info}")

        # 如果没有指定语言，使用自动检测的
        if not language:
            language = project_info.get('language')
            if not language:
                # 尝试通过文件扩展名检测
                extensions = {}
                for file in project_info['files']:
                    _, ext = os.path.splitext(file)
                    if ext in ['.py', '.js', '.java', '.ts', '.go']:
                        extensions[ext] = extensions.get(ext, 0) + 1

                if extensions:
                    main_ext = max(extensions, key=extensions.get)
                    ext_to_lang = {'.py': 'python3', '.js': 'javascript', '.java': 'java', '.ts': 'typescript', '.go': 'go'}
                    language = ext_to_lang.get(main_ext)

        if not language:
            raise HTTPException(
                status_code=400,
                detail="Unable to detect project language. Please specify language parameter. "
                       f"Detected files: {project_info['files'][:10]}"
            )

        # 获取语言配置
        config = LANGUAGE_CONFIG.get(language)
        if not config:
            raise HTTPException(
                status_code=400,
                detail=f"Unsupported language: {language}. Supported: {list(LANGUAGE_CONFIG.keys())}"
            )

        # 确定入口点
        detected_entry = None
        if entry_point:
            # 用户指定的入口点
            if not os.path.exists(os.path.join(project_dir, entry_point)):
                raise HTTPException(
                    status_code=400,
                    detail=f"Specified entry point not found: {entry_point}. "
                           f"Available files: {project_info['files'][:10]}"
                )
            detected_entry = entry_point
        else:
            # 自动查找入口点
            print(f"Finding entry point for project...")
            entry_points = project_info.get('entry_points', []) or []
            print(f"Available entry points: {entry_points}")
            detected_entry = find_entry_point(
                project_dir,
                entry_points,
                None
            )

            # 如果没找到，让用户指定
            if not detected_entry:
                raise HTTPException(
                    status_code=400,
                    detail="Unable to detect entry point. Please specify entry_point parameter. "
                           "Typical patterns: main.py, index.js, app.py"
                )
            print(f"Found entry point: {detected_entry}")

        # 确定运行命令
        run_cmd = run_command if run_command else None
        print(f"Initial run command: {run_cmd}")
        if not run_cmd:
            # 使用项目配置或默认命令
            if project_info.get('run_command'):
                run_cmd = project_info['run_command'].replace('{entry}', detected_entry)
            else:
                # 使用语言配置的默认命令
                extension = os.path.splitext(detected_entry)[1]
                if extension == '.py' and language == 'python3':
                    run_cmd = f"python {detected_entry}"
                elif extension == '.js' and language == 'javascript':
                    run_cmd = f"node {detected_entry}"
                else:
                    # 使用配置中的命令模板
                    run_cmd = config.get('command', '').replace('code.' + config['extension'], detected_entry)

        # 设置正确的commandRedirect
        config = config.copy()
        config['commandRedirect'] = f"sh -c '{run_cmd} > output.txt'"

        # 确定用户
        env = config.get('env', 'sandbox')
        user = 'jovyan' if env == 'jupyter' else 'sandbox'

        container = None

        try:
            # 创建容器
            container = create_container(config, project_dir, user, '')

            # 编译步骤（C/C++/部分C#项目需要）
            build_output = ""
            try:
                # 编译单个源文件
                if project_info.get('compile_command'):
                    exec_result = container.exec_run(
                        project_info['compile_command'],
                        user=user,
                        workdir=f"/home/{user}"
                    )
                    if exec_result.exit_code != 0:
                        raise RuntimeError(f"Compilation failed: {exec_result.output.decode('utf-8')}")
                    build_output += exec_result.output.decode('utf-8')

                # 构建项目的
                elif project_info.get('build_command'):
                    exec_result = container.exec_run(
                        project_info['build_command'],
                        user=user,
                        workdir=f"/home/{user}"
                    )
                    if exec_result.exit_code != 0:
                        raise RuntimeError(f"Build failed: {exec_result.output.decode('utf-8')}")
                    build_output += exec_result.output.decode('utf-8')

                # 安装依赖
                if project_info.get('install_command') and not dependencies:
                    exec_result = container.exec_run(
                        project_info['install_command'],
                        user=user,
                        workdir=f"/home/{user}"
                    )
                    if exec_result.exit_code != 0:
                        raise RuntimeError(f"Dependency installation failed: {exec_result.output.decode('utf-8')}")
                    build_output += exec_result.output.decode('utf-8')

            except RuntimeError as e:
                raise HTTPException(status_code=400, detail=str(e))

            # 安装额外依赖
            if dependencies:
                install_dependencies(container, language, dependencies, user, config)

            # 运行项目
            exec_result = run_container_command(container, config['commandRedirect'], user)
            output = exec_result.output.decode('utf-8')
            output = remove_ansi_sequences(output)
            output = read_output(project_dir, output)
            output = remove_ansi_sequences(output)

            duration = time.time() - start_time

            return ProjectArchiveResponse(
                output=output,
                contentType='text/plain',
                duration=duration,
                language=language,
                detected_language=project_info.get('language'),
                detected_entry_point=detected_entry,
                build_output=remove_ansi_sequences(build_output) if build_output else None
            )

        except Exception as e:
            print(e)
            raise HTTPException(status_code=500, detail=str(e))
        finally:
            cleanup_container(container, project_dir)
            # 清理临时压缩包文件
            if temp_archive_path and os.path.exists(temp_archive_path):
                os.unlink(temp_archive_path)

    except HTTPException:
        # 重新抛出HTTP异常
        raise
    except Exception as e:
        # 清理临时压缩包文件
        if temp_archive_path and os.path.exists(temp_archive_path):
            os.unlink(temp_archive_path)
        print(e)
        raise HTTPException(status_code=500, detail=f"Failed to process archive: {str(e)}")

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get('server_port', '8001'))
    uvicorn.run(app, host="0.0.0.0", port=port)
