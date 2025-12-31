LANGUAGE_RUNTIME_MAP = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
    },
    'javascript': {
        'env': 'javascript',
        'image': 'code_runner/nodejs',
    },
    'typescript': {
        'env': 'typescript',
        'image': 'code_runner/nodejs',
    },
    'csharp': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
    },
    'csharp-sfa': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
    },
    'csharp-mono': {
        'env': 'mono',
        'image': 'code_runner/mono',
    },
    'cpp': {
        'env': 'cpp',
        'image': 'code_runner/cpp',
    },
    'go': {
        'env': 'go',
        'image': 'code_runner/go',
    },
    'java': {
        'env': 'java',
        'image': 'code_runner/java',
    },
    'jupyter-csharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel .net-csharp",
        'commandRedirect': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel .net-csharp",
        'extension': 'ipynb'
    },
    'jupyter-fsharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel .net-fsharp",
        'commandRedirect': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel .net-fsharp",
        'extension': 'ipynb'
    },
    'jupyter-python': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel python3",
        'commandRedirect': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel python3",
        'extension': 'ipynb'
    },
    'jupyter-r': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel ir",
        'commandRedirect': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.txt --kernel ir",
        'extension': 'ipynb'
    },
    'lua': {
        'env': 'lua',
        'image': 'code_runner/lua',
    },
    'bash': {
        'env': 'bash',
        'image': 'code_runner/bash',
    },
    'rust': {
        'env': 'bash',
        'image': 'code_runner/rust',
    }
}
