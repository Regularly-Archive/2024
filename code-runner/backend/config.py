LANGUAGE_CONFIG = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
        'command': 'python code.py',
        'extension': 'py'
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
        'command': 'python code.py',
        'extension': 'py'
    },
    'javascript': {
        'env': 'javascript',
        'image': 'code_runner/nodejs',
        'command': 'node code.js',
        'extension': 'js'
    },
    'typescript': {
        'env': 'typescript',
        'image': 'code_runner/nodejs',
        'command': './node_modules/typescript/bin/tsc code.ts && node code.js',
        'extension': 'ts'
    },
    'csharp': {
        'env' : 'dotnet',
        'image': 'code_runner/dotnet',
        'command': 'dotnet script code.csx  > ./output.txt',
        'extension': 'csx'
    },
    'csharp-mono': {
        'env' :'mono',
        'image': 'code_runner/mono',
        'command': "sh -c 'mcs -out:code code.cs && mono code'",
        'extension': 'cs'
    },
    'cpp': {
        'env': 'cpp',
        'image': 'code_runner/cpp',
        'command': "sh -c 'g++ code.cpp -o code && ./code'",
        'extension': 'cpp'
    },
    'go': {
        'env': 'go',
        'image': 'code_runner/go',
        'command': 'go run code.go',
        'extension': 'go'
    },
    'java': {
        'env': 'java',
        'image': 'code_runner/java',
        'command': "sh -c 'javac code.java && java code'",
        'extension': 'java'
    },
    'jupyter-csharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.html --kernel .net-csharp",
        'extension': 'ipynb'
    },
    'jupyter-fsharp': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.html --kernel .net-fsharp",
        'extension': 'ipynb'
    },
    'jupyter-python3': {
        'env': 'jupyter',
        'image': 'code_runner/jupyterlab',
        'command': "python /nbconvert/convert.py /home/jovyan/code.ipynb /home/jovyan/output.html --kernel python3",
        'extension': 'ipynb'
    },
}
