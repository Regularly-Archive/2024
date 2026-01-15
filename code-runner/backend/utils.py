import nbformat
import os
import zipfile
import tarfile
import tempfile
from typing import List
from config import LANGUAGE_RUNTIME_MAP
import uuid

def code_to_ipynb(code_string, notebook_name='output_notebook.ipynb', language=None, dependencies=None):
    """
    Generate Jupyter Notebook file with dependency installation code injected in the first cell.
    Automatically inserts code to suppress Python warnings (after dependency installation).
    """
    nb = nbformat.v4.new_notebook()
    cells = []
    
    dependencies = [dep for dep in dependencies if dep and dep.strip()]
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
    Generate code file with dependency declarations injected at the file header based on language type.
    """
    dependencies = [dep for dep in dependencies if dep and dep.strip()]
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

async def prepare_project_dir_from_archive(archive_file) -> str:
    """
    Extract project files from archive to a temporary directory.
    """
    allowed_extensions = ('.zip', '.tar.gz', '.tgz', '.tar.bz2', '.tbz2')
    filename = archive_file.filename

    if not filename.endswith(allowed_extensions):
        raise ValueError(f"Unsupported archive format. Supported formats: {', '.join(allowed_extensions)}")

    suffix = next((ext for ext in allowed_extensions if filename.endswith(ext)), None)

    temp_archive_path = None
    try:
        project_dir = tempfile.mkdtemp(prefix='project_')

        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp_file:
            temp_archive_path = tmp_file.name
            content = await archive_file.read()
            tmp_file.write(content)

        extract_archive(temp_archive_path, project_dir)
        return project_dir

    finally:
        if temp_archive_path and os.path.exists(temp_archive_path):
            os.remove(temp_archive_path)

def prepare_project_dir_from_code(code: str, language: str, dependencies: List[str]) -> str:
    language_config = LANGUAGE_RUNTIME_MAP.get(language)

    if not language_config:
        raise ValueError(f"Unsupported language: {language}")
    
    project_dir = tempfile.mkdtemp(prefix='project_')
    code_extension = language_config.get('extension')
    code_env = language_config.get('env')
    code_file = os.path.join(project_dir, f'code_{uuid.uuid4().hex}{code_extension}')
    if code_env != 'jupyter':
        code_to_file(code, code_file, language=language, dependencies=dependencies)
    else:
        code_to_ipynb(code, code_file, language=language, dependencies=dependencies)
    return project_dir

def read_output(temp_dir, output_file, fallback_output):
    """
    Read code execution output result.
    """
    redirected_output = os.path.join(temp_dir, output_file)
    if not os.path.exists(redirected_output):
        return 'An error occurs when executing code.'
    with open(redirected_output, 'rt', encoding='utf-8') as f:
        content = f.read()
        return content if content else fallback_output

def extract_archive(archive_path: str, extract_to: str) -> List[str]:
    """
    Extract archive to specified directory. Supported formats: zip, tar.gz, .taz, tar.bz2, .tbz2
    Returns the list of extracted files.
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

def is_jbang_file(file_path: str) -> bool:
    """Check if the file is in JBang format for Java."""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            first_line = f.readline().strip()
            if first_line.startswith('///usr/bin/env jbang'):
                return True
        return False
    except Exception:
        return False
