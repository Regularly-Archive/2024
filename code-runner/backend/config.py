LANGUAGE_CONFIG = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
        'command': 'python code.py',
        'commandRedirect': "sh -c 'python code.py > output.txt'",
        'extension': 'py'
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
        'command': 'python code.py',
        'commandRedirect': "sh -c 'python code.py > output.txt'",
        'extension': 'py'
    },
    'javascript': {
        'env': 'javascript',
        'image': 'code_runner/nodejs',
        'command': 'node code.js',
        'commandRedirect': "sh -c 'node code.js > output.txt'",
        'extension': 'js'
    },
    'typescript': {
        'env': 'typescript',
        'image': 'code_runner/nodejs',
        'command': 'tsc code.ts && node code.js',
        'commandRedirect': "sh -c 'tsc code.ts && node code.js > output.txt'",
        'extension': 'ts'
    },
    'csharp': {
        'env' : 'dotnet',
        'image': 'code_runner/dotnet',
        'command': 'dotnet script code.csx',
        'commandRedirect': "sh -c 'dotnet script code.csx > output.txt'",
        'extension': 'csx'
    },
    'csharp-mono': {
        'env' :'mono',
        'image': 'code_runner/mono',
        'command': "sh -c 'mcs -out:code -codepage:utf8 code.cs && mono code --encoding=utf8'",
        'commandRedirect': "sh -c 'mcs -out:code -codepage:utf8 code.cs && mono code --encoding=utf8> output.txt'",
        'extension': 'cs'
    },
    'cpp': {
        'env': 'cpp',
        'image': 'code_runner/cpp',
        'command': "sh -c 'g++ code.cpp -o code && ./code'",
        'commandRedirect': "sh -c 'g++ code.cpp -o code && ./code > output.txt'",
        'extension': 'cpp'
    },
    'go': {
        'env': 'go',
        'image': 'code_runner/go',
        'command': 'go run code.go',
        'commandRedirect': "sh -c 'go run code.go > output.txt'",
        'extension': 'go'
    },
    'java': {
        'env': 'java',
        'image': 'code_runner/java',
        'command': "sh -c 'javac -encoding utf-8 code.java && java code'",
        'commandRedirect': "sh -c 'javac -encoding utf-8 code.java && java code > output.txt'",
        'extension': 'java'
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
    'jupyter-python3': {
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
}
