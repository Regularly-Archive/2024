import nbformat
import re
import docker
import os
import shutil


def code_to_ipynb(code_string, notebook_name='output_notebook.ipynb', language=None, dependencies=None):
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
    if language == 'csharp' and dependencies:
        nuget_lines = [f'#r "nuget: {dep}"' for dep in dependencies]
        code_string = '\n'.join(nuget_lines) + '\n' + code_string
    if language == 'java' and dependencies:
        deps_line = '//DEPS ' + ','.join(dependencies)
        code_string = deps_line + '\n' + code_string
    with open(file_path, 'wt', encoding='utf-8') as f:
        f.write(code_string)


def remove_ansi_sequences(input_string):
    ansi_escape = re.compile(r'\x1b\[([0-?]*[ -/]*[@-~])')
    cleaned = ansi_escape.sub('', input_string).replace('\x1b=','')
    # 屏蔽 dotnet workload 提示
    cleaned = re.sub(r'An issue was encountered verifying workloads.*?dotnet workload update.*?(\n|$)', '', cleaned, flags=re.DOTALL)
    return cleaned



client = docker.from_env()

def prepare_code_dir(code: str, extension: str, env: str, code_to_file, code_to_ipynb, language=None, dependencies=None) -> str:
    temp_dir = f"./runner_{{os.urandom(8).hex()}}"
    os.makedirs(temp_dir, exist_ok=True)
    code_path = os.path.join(temp_dir, f'code.{extension}')
    if env != 'jupyter':
        code_to_file(code, code_path, language=language, dependencies=dependencies)
    else:
        code_to_ipynb(code, code_path, language=language, dependencies=dependencies)
    return temp_dir

def create_container(config, temp_dir, user, format):
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

def run_code_in_container(container, command, user):
    exec_result = container.exec_run(command, user=user, workdir=f"/home/{user}")
    return exec_result

def read_output(temp_dir, fallback_output):
    redirected_output = os.path.join(temp_dir, 'output.txt')
    if not os.path.exists(redirected_output):
        return 'An error occurs when executing code.'
    with open(redirected_output, 'rt', encoding='utf-8') as f:
        content = f.read()
        return content if content else fallback_output

def cleanup_container(container, temp_dir):
    if container:
        container.stop()
        container.remove(force=True)
    shutil.rmtree(temp_dir)
