LANGUAGE_RUNTIME_MAP = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
        'extension': '.py'
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
        'extension': '.py'
    },
    'javascript': {
        'env': 'javascript',
        'image': 'code_runner/nodejs',
        'extension': '.js'
    },
    'typescript': {
        'env': 'typescript',
        'image': 'code_runner/nodejs',
        'extension': '.ts'
    },
    'csharp': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'extension': '.cs'
    },
    'csharp-sfa': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'extension': '.cs'
    },
    'csharp-mono': {
        'env': 'mono',
        'image': 'code_runner/mono',
        'extension': '.cs'
    },
    'cpp': {
        'env': 'cpp',
        'image': 'code_runner/cpp',
        'extension': '.cpp'
    },
    'go': {
        'env': 'go',
        'image': 'code_runner/go',
        'extension': '.go'
    },
    'java': {
        'env': 'java',
        'image': 'code_runner/java',
        'extension': '.java'
    },
    'jupyter-csharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': '.net-csharp',
        'extension': '.ipynb'
    },
    'jupyter-fsharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': ".net-fsharp",
        'extension': '.ipynb'
    },
    'jupyter-python': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': "python3",
        'extension': '.ipynb'
    },
    'jupyter-r': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': "ir",
        'extension': '.ipynb'
    },
    'lua': {
        'env': 'lua',
        'image': 'code_runner/lua',
        'extension': '.lua'
    },
    'bash': {
        'env': 'bash',
        'image': 'code_runner/bash',
        'extension': '.sh'
    },
    'rust': {
        'env': 'rust',
        'image': 'code_runner/rust',
        'extension': '.rs'
    }
}
