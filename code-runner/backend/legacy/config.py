LANGUAGE_RUNTIME_MAP = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
        'extension': '.py',
        'version': '2.7.18'
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
        'extension': '.py',
        'version': '3.9.18'
    },
    'javascript': {
        'env': 'node',
        'image': 'code_runner/nodejs',
        'extension': '.js',
        'version': '18'
    },
    'typescript': {
        'env': 'node',
        'image': 'code_runner/nodejs',
        'extension': '.ts',
        'version': '18'
    },
    'csharp': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'extension': '.cs',
        'version': '10.0-preview'
    },
    'csharp-sfa': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'extension': '.cs',
        'version': '10.0-preview'
    },
    'csharp-mono': {
        'env': 'mono',
        'image': 'code_runner/mono',
        'extension': '.cs',
        'version': 'latest'
    },
    'cpp': {
        'env': 'gcc',
        'image': 'code_runner/cpp',
        'extension': '.cpp',
        'version':'11.3'
    },
    'go': {
        'env': 'golang',
        'image': 'code_runner/go',
        'extension': '.go',
        'version':'latest'
    },
    'java': {
        'env': 'openjdk',
        'image': 'code_runner/java',
        'extension': '.java',
        'version':'20'
    },
    'jupyter-csharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': '.net-csharp',
        'extension': '.ipynb',
        'version': 'latest'
    },
    'jupyter-fsharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': ".net-fsharp",
        'extension': '.ipynb',
        'version': 'latest'
    },
    'jupyter-python': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': "python3",
        'extension': '.ipynb',
        'version': 'latest'
    },
    'jupyter-r': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'kernel': "ir",
        'extension': '.ipynb',
        'version': 'latest'
    },
    'lua': {
        'env': 'lua',
        'image': 'code_runner/lua',
        'extension': '.lua',
        'version': ''
    },
    'bash': {
        'env': 'alpine',
        'image': 'code_runner/bash',
        'extension': '.sh',
        'version': '3.20'
    },
    'rust': {
        'env': 'rust',
        'image': 'code_runner/rust',
        'extension': '.rs',
        'version':'1.81'
    }
}


ALLOWED_ARTIFACT_PATTERNS = [
    # Spreadsheet / Data
    "*.csv",
    "*.xlsx",
    "*.xls",

    # Documents
    "*.pdf",
    "*.docx",
    "*.md",
    "*.markdown",
    "*.html",
    "*.htm",
    "*.txt",

    # Images
    "*.png",
    "*.jpg",
    "*.jpeg",
    "*.gif",
    "*.svg",
    "*.webp",
]

IGNORED_DIRS = {
    # logs
    "log", "logs",

    # Python
    "__pycache__", ".pytest_cache", ".mypy_cache",
    ".venv", "venv", ".conda", ".ipython",

    # Node / JS
    "node_modules", ".npm", ".yarn", ".pnpm",

    # Java / JVM
    ".gradle", "target", ".m2",

    # General
    ".git", ".svn", ".hg",
    ".cache", ".tmp", "tmp"
}

