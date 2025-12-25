import nbformat
import re
import docker
import os
import shutil
import zipfile
import tarfile
import tempfile
from typing import List, Tuple, Optional

client = docker.from_env()

def code_to_ipynb(code_string, notebook_name='output_notebook.ipynb', language=None, dependencies=None):
    """
    生成 Jupyter Notebook 文件，并在首单元注入依赖安装代码。
    自动插入屏蔽 Python 警告的代码（在依赖安装后）。
    """
    nb = nbformat.v4.new_notebook()
    cells = []

    if dependencies and language in ["jupyter-csharp", "jupyter-python", "jupyter-r"]:
        if language == "jupyter-csharp":
            dep_lines = [f'#r "nuget: {dep}"' for dep in dependencies]
            dep_code = '\n'.join(dep_lines)
            cells.append(nbformat.v4.new_code_cell(dep_code))
        elif language == "jupyter-python":
            dep_lines = [f'!pip install {dep}' for dep in dependencies]
            dep_code = '\n'.join(dep_lines)
            cells.append(nbformat.v4.new_code_cell(dep_code))
        elif language == "jupyter-r":
            dep_lines = [f'install.packages("{dep}")' for dep in dependencies]
            dep_code = '\n'.join(dep_lines)
            cells.append(nbformat.v4.new_code_cell(dep_code))

    cells.append(nbformat.v4.new_code_cell(code_string))
    nb['cells'] = cells
    with open(notebook_name, 'wt', encoding='utf-8') as f:
        nbformat.write(nb, f)


def code_to_file(code_string, file_path, language=None, dependencies=None):
    """
    生成代码文件，并按语言类型在文件头注入依赖声明。
    """
    if language == 'csharp' and dependencies:
        nuget_lines = [f'#:package {dep}' for dep in dependencies]
        code_string = '\n'.join(nuget_lines) + '\n' + code_string
    if language == 'csharp-sfa' and dependencies:
        nuget_lines = [f'#r "nuget: {dep}"' for dep in dependencies]
        code_string = '\n'.join(nuget_lines) + '\n' + code_string
    if language == 'java' and dependencies:
        deps_line = '//DEPS ' + ','.join(dependencies)
        code_string = deps_line + '\n' + code_string
    with open(file_path, 'wt', encoding='utf-8') as f:
        f.write(code_string)


def remove_ansi_sequences(input_string):
    """
    移除终端 ANSI 控制字符，清理输出。
    """
    ansi_escape = re.compile(r'\x1b\[([0-?]*[ -/]*[@-~])')
    cleaned = ansi_escape.sub('', input_string).replace('\x1b=','')
    cleaned = re.sub(r'An issue was encountered verifying workloads.*?dotnet workload update.*?(\n|$)', '', cleaned, flags=re.DOTALL)
    return cleaned


def prepare_code_dir(code: str, extension: str, env: str, code_to_file, code_to_ipynb, language=None, dependencies=None) -> str:
    """
    创建临时代码目录，并写入代码或 notebook 文件。
    """
    temp_dir = f"./runner_{os.urandom(8).hex()}"
    os.makedirs(temp_dir, exist_ok=True)
    code_path = os.path.join(temp_dir, f'code.{extension}')
    if env != 'jupyter':
        code_to_file(code, code_path, language=language, dependencies=dependencies)
    else:
        code_to_ipynb(code, code_path, language=language, dependencies=dependencies)
    return temp_dir

def prepare_project_dir(files):
    """
    创建临时项目目录，并写入多个代码文件。
    """
    temp_dir = f"./runner_{os.urandom(8).hex()}"
    os.makedirs(temp_dir, exist_ok=True)
    for file in files:
        file_path = os.path.join(temp_dir, file.path)
        os.makedirs(os.path.dirname(file_path), exist_ok=True)
        with open(file_path, 'wt', encoding='utf-8') as f:
            f.write(file.content)
    return temp_dir


def create_container(config, temp_dir, user, format):
    """
    创建并启动代码运行所需的 Docker 容器。
    """
    return client.containers.run(
        image=config['image'],
        command="sleep infinity",
        volumes={os.path.abspath(temp_dir): {'bind': f'/home/{user}', 'mode': 'rw'}},
        tty=True,
        detach=True,
        environment={
            'LANG': 'en_US.UTF-8',
            'LC_ALL': 'en_US.UTF-8',
            'NBCONVERT_OUTPUT_FORMAT': format
        }
    )


def install_dependencies(container, language, dependencies, user, config=None):
    """
    按语言类型自动安装依赖。
    """
    if language in ["javascript", "typescript"] and dependencies and config and 'install' in config:
        container.exec_run("npm init -y", user=user, workdir=f"/home/{user}")
        install_cmd = config['install'].format(deps=' '.join(dependencies))
        exec_result = container.exec_run(install_cmd, user=user, workdir=f"/home/{user}")
        if exec_result.exit_code != 0:
            raise RuntimeError(f"Unable to install dependencies with '{install_cmd }': {exec_result.output.decode('utf-8')}")
    elif language == "java" and dependencies:
        # jbang 运行时无需额外安装依赖，//DEPS 已在 code_to_file 注入
        pass
    elif dependencies and config and 'install' in config:
        install_cmd = config['install'].format(deps=' '.join(dependencies))
        exec_result = container.exec_run(install_cmd, user=user, workdir=f"/home/{user}")
        if exec_result.exit_code != 0:
            raise RuntimeError(f"Unable to install dependencies with '{install_cmd }': {exec_result.output.decode('utf-8')}")


def run_command(container, command, user):
    """
    在容器内执行代码运行命令。
    """
    print("execute command in container:", command)
    exec_result = container.exec_run(command, user=user, workdir=f"/home/{user}")
    return exec_result


def read_output(temp_dir, fallback_output):
    """
    读取代码执行输出结果。
    """
    redirected_output = os.path.join(temp_dir, 'output.txt')
    if not os.path.exists(redirected_output):
        return 'An error occurs when executing code.'
    with open(redirected_output, 'rt', encoding='utf-8') as f:
        content = f.read()
        return content if content else fallback_output


def cleanup_container(container, temp_dir):
    """
    停止并移除容器，清理临时目录。
    """
    if container:
        container.stop()
        container.remove(force=True)
    shutil.rmtree(temp_dir)


def extract_archive(archive_path: str, extract_to: str) -> List[str]:
    """
    解压压缩包到指定目录，支持的格式：zip, tar.gz, tar.bz2
    返回解压的文件列表
    """
    extracted_files = []

    try:
        if archive_path.endswith('.zip'):
            with zipfile.ZipFile(archive_path, 'r') as zip_ref:
                zip_ref.extractall(extract_to)
                extracted_files = zip_ref.namelist()

        elif archive_path.endswith(('.tar.gz', '.tgz')):
            with tarfile.open(archive_path, 'r:gz') as tar_ref:
                tar_ref.extractall(extract_to)
                extracted_files = tar_ref.getnames()

        elif archive_path.endswith(('.tar.bz2', '.tbz2')):
            with tarfile.open(archive_path, 'r:bz2') as tar_ref:
                tar_ref.extractall(extract_to)
                extracted_files = tar_ref.getnames()

        else:
            raise ValueError(f"Unsupported archive format: {archive_path}")

    except (zipfile.BadZipFile, tarfile.TarError) as e:
        raise ValueError(f"Invalid archive file: {str(e)}")

    return extracted_files


def detect_project_type(project_dir: str) -> dict:
    """
    检测项目类型，返回语言信息和默认配置
    """
    # 导入PROJECT_DETECTORS配置
    from config import PROJECT_DETECTORS

    # 项目特征检测规则（简化版，保持核心功能）
    project_indicators = {
        'package.json': {
            'language': 'javascript',
            'entry_points': ['index.js', 'app.js', 'server.js', 'main.js'],
            'dependency_files': ['package.json'],
            'install_command': 'npm install',
            'run_command': 'node {entry}'
        },
        'requirements.txt': {
            'language': 'python3',
            'entry_points': ['main.py', 'app.py', '__main__.py', 'run.py'],
            'dependency_files': ['requirements.txt'],
            'install_command': 'pip install -r requirements.txt',
            'run_command': 'python {entry}'
        },
        'main.py': {
            'language': 'python3',
            'entry_points': ['main.py'],
            'run_command': 'python main.py'
        },
        'pom.xml': {
            'language': 'java',
            'entry_points': [],  # Java需要构建后运行
            'dependency_files': ['pom.xml'],
            'build_command': 'mvn compile',
            'run_command': 'mvn exec:java -Dexec.mainClass={main_class}'
        },
        # C# 支持
        'Program.cs': {
            'language': 'csharp',
            'entry_points': ['Program.cs'],
            'run_command': 'dotnet script {entry}'  # 或者 dotnet run
        },
        # Go 支持
        'go.mod': {
            'language': 'go',
            'entry_points': ['main.go'],
            'dependency_files': ['go.mod'],
            'install_command': 'go mod tidy',
            'run_command': 'go run .'
        },
        'main.go': {
            'language': 'go',
            'entry_points': ['main.go'],
            'run_command': 'go run {entry}'
        },
        # C++ 支持
        'main.cpp': {
            'language': 'cpp',
            'entry_points': ['main.cpp'],
            'compile_command': 'g++ main.cpp -o main -std=c++17',
            'run_command': './main'
        },
        'main.c': {
            'language': 'cpp',  # 使用cpp镜像运行C程序
            'entry_points': ['main.c'],
            'compile_command': 'gcc main.c -o main',
            'run_command': './main'
        },
        # Makefile支持
        'Makefile': {
            'language': 'cpp',
            'entry_points': [],
            'build_command': 'make',
            'run_command': './main'  # 典型输出
        }
    }

    detected_type = None
    files_in_project = []

    # 获取项目中的所有文件
    for root, dirs, files in os.walk(project_dir):
        for file in files:
            files_in_project.append(os.path.relpath(os.path.join(root, file), project_dir))

    # 检测项目类型 - 优先顺序
    # 1. 查找具体的配置文件
    for indicator, config in project_indicators.items():
        if indicator in files_in_project:
            detected_type = config.copy()
            break
    print("Detected project type:", detected_type)
    if not detected_type or detected_type.get('language') == 'csharp':
        # 2. 查找基于扩展名的项目文件
        extension_indicators = {
            '.csproj': {
                'language': 'csharp',
                'run_command': 'dotnet run'
            },
            '.sln': {
                'language': 'csharp',
                'description': 'C# Solution'
            },
            '.csx': {
                'language': 'csharp',
                'run_command': 'dotnet script {entry}'
            },
            '.cs': {
                'language': 'csharp',
                'run_command': 'dotnet run {entry}'
            }
        }

        for file in files_in_project:
            _, ext = os.path.splitext(file)
            if ext in extension_indicators:
                detected_type = extension_indicators[ext].copy()
                break

    if not detected_type:
        # 3. 基于文件扩展名作语言检测
        extensions = {}
        for file in files_in_project:
            _, ext = os.path.splitext(file)
            if ext in ['.py', '.js', '.java', '.ts', '.go', '.rs', '.cpp', '.c', '.cs', '.csx', '.sh']:
                extensions[ext] = extensions.get(ext, 0) + 1

        # 取最多的扩展类型
        if extensions:
            main_ext = max(extensions, key=extensions.get)
            ext_to_lang = {
                '.py': 'python3',
                '.js': 'javascript',
                '.ts': 'typescript',
                '.java': 'java',
                '.go': 'go',
                '.rs': 'rust',
                '.cpp': 'cpp',
                '.c': 'cpp',  # C用cpp镜像
                '.cs': 'csharp',
                '.csx': 'csharp',
                '.sh': 'bash'
            }
            if main_ext in ext_to_lang:
                detected_type = {
                    'language': ext_to_lang[main_ext],
                    'run_command': None  # 需要具体分析
                }

    # 如果没有明确入口点，尝试查找
    if detected_type and not detected_type.get('entry_points'):
        detected_type['entry_points'] = []
        # 根据语言查找典型入口
        lang = detected_type.get('language')
        if lang == 'go':
            candidates = ['main.go']
        elif lang == 'csharp':
            candidates = ['Program.cs', 'Main.cs']
        elif lang == 'cpp':
            candidates = ['main.cpp', 'main.c']
        elif lang == 'rust':
            candidates = ['src/main.rs', 'main.rs']
        else:
            candidates = ['main', 'index', 'app']

        for file in files_in_project:
            basename = os.path.basename(file).lower()
            if any(cand.lower() in basename for cand in candidates):
                detected_type['entry_points'].append(file)

    return {
        'language': detected_type.get('language') if detected_type else None,
        'entry_points': detected_type.get('entry_points', []) if detected_type else [],
        'dependency_files': detected_type.get('dependency_files', []) if detected_type else [],
        'install_command': detected_type.get('install_command') if detected_type else None,
        'build_command': detected_type.get('build_command') if detected_type else None,
        'compile_command': detected_type.get('compile_command') if detected_type else None,
        'run_command': detected_type.get('run_command') if detected_type else None,
        'files': files_in_project
    }


def prepare_project_from_archive(archive_path: str) -> Tuple[str, dict]:
    """
    从压缩包准备项目目录，返回临时目录路径和项目信息
    """
    # 创建临时目录
    temp_dir = tempfile.mkdtemp(prefix='project_')

    try:
        # 解压压缩包
        extract_archive(archive_path, temp_dir)

        # 检测项目类型
        project_info = detect_project_type(temp_dir)

        return temp_dir, project_info

    except Exception as e:
        # 出错时清理临时目录
        if os.path.exists(temp_dir):
            shutil.rmtree(temp_dir)
        raise e


def find_entry_point(project_dir: str, entry_points: List[str], preferred: Optional[str] = None) -> Optional[str]:
    """
    在项目目录中查找入口文件
    """
    if preferred and os.path.exists(os.path.join(project_dir, preferred)):
        return preferred

    for entry in entry_points:
        if os.path.exists(os.path.join(project_dir, entry)):
            return entry

    # 如果没找到预设的入口点，查找单个可执行文件
    candidates = []
    for root, dirs, files in os.walk(project_dir):
        for file in files:
            if file.endswith(('.py', '.js', '.java', '.go', '.rs')):
                candidates.append(os.path.relpath(os.path.join(root, file), project_dir))

    if len(candidates) == 1:
        return candidates[0]

    return None
