LANGUAGE_CONFIG = {
    'python2': {
        'env': 'python2',
        'image': 'code_runner/python2',
        'command': 'python code.py',
        'commandRedirect': "sh -c 'python code.py > output.txt'",
        'extension': 'py',
        'install': 'pip install {deps}'
    },
    'python3': {
        'env': 'python3',
        'image': 'code_runner/python3',
        'command': 'python code.py',
        'commandRedirect': "sh -c 'python code.py > output.txt'",
        'extension': 'py',
        'install': 'pip install {deps}'
    },
    'javascript': {
        'env': 'javascript',
        'image': 'code_runner/nodejs',
        'command': 'node code.js',
        'commandRedirect': "sh -c 'node code.js > output.txt'",
        'extension': 'js',
        'install': 'npm install {deps}'
    },
    'typescript': {
        'env': 'typescript',
        'image': 'code_runner/nodejs',
        'command': 'tsc code.ts && node code.js',
        'commandRedirect': "sh -c 'tsc code.ts && node code.js > output.txt'",
        'extension': 'ts',
        'install': 'npm install {deps}'
    },
    'csharp': {
        'env' : 'dotnet',
        'image': 'code_runner/dotnet',
        'command': "dotnet run code.cs",
        'commandRedirect': "sh -c 'dotnet run code.cs > output.txt'",
        'extension': 'csx'
    },
    'csharp-sfa': {
        'env' :'dotnet',
        'image': 'code_runner/dotnet',
        'command': "dotnet run code.cs",
        'commandRedirect': "sh -c 'dotnet run code.cs > output.txt'",
        'extension': 'cs'
    },
    'csharp-mono': {
        'env' :'mono',
        'image': 'code_runner/mono',
        'command': "sh -c 'mcs -out:code -codepage:utf8 code.cs && mono code --encoding=utf8'",
        'commandRedirect': "sh -c 'mcs -out:code -codepage:utf8 code.cs && mono code --encoding=utf8 > output.txt'",
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
        'command': 'sh -c "export JAVA_TOOL_OPTIONS=\'-Dfile.encoding=UTF-8\' \u0026\u0026 jbang code.java"',
        'commandRedirect': "sh -c 'export JAVA_TOOL_OPTIONS=\'-Dfile.encoding=UTF-8\' \u0026\u0026 jbang code.java > output.txt'",
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
        'command': 'lua code.lua',
        'commandRedirect': "sh -c 'lua code.lua > output.txt'",
        'extension': 'lua',
    },
    'bash': 
    {
        'env': 'bash',
        'image': 'code_runner/bash',
        'command': 'bash {entry}',
        'commandRedirect': "sh -c 'bash {entry} > output.txt'",
        'extension': 'sh'
    }
}

# 项目特征文件到语言类型的映射
PROJECT_DETECTORS = {
    'package.json': {
        'language': 'javascript',
        'entry_points': ['index.js', 'app.js', 'server.js', 'main.js'],
        'dependency_files': ['package.json'],
        'install_command': 'npm install',
        'build_command': None,
        'run_command': 'node {entry}'
    },
    'requirements.txt': {
        'language': 'python3',
        'entry_points': ['main.py', 'app.py', '__main__.py', 'run.py'],
        'dependency_files': ['requirements.txt'],
        'install_command': 'pip install -r requirements.txt',
        'build_command': None,
        'run_command': 'python {entry}'
    },
    'main.py': {
        'language': 'python3',
        'entry_points': ['main.py'],
        'install_command': None,
        'build_command': None,
        'run_command': 'python main.py'
    },
    'pom.xml': {
        'language': 'java',
        'entry_points': [],
        'dependency_files': ['pom.xml'],
        'install_command': None,  # Maven自动处理依赖
        'build_command': 'mvn compile',
        'run_command': 'mvn exec:java -Dexec.mainClass={main_class}'
    },
    # C# 项目支持
    '*.csproj': {
        'language': 'csharp',
        'entry_points': ['Program.cs'],
        'project_files': ['*.csproj'],
        'build_command': 'dotnet build',
        'run_command': 'dotnet run'
    },
    '*.sln': {
        'language': 'csharp',
        'entry_points': [],
        'project_files': ['*.sln', '*.csproj'],
        'description': 'C# Solution file detected'
    },
    '*.cs': {
        'language': 'csharp-sfa',
        'entry_points': ['Program.cs'],
        'run_command': 'dotnet run {entry}'  # 
    },
    # C# 脚本文件
    '*.csx': {
        'language': 'csharp',
        'entry_points': ['main.csx'],
        'run_command': 'dotnet script {entry}'  # 使用 dotnet-script 工具
    },
    # Go 项目支持
    'go.mod': {
        'language': 'go',
        'entry_points': ['main.go'],
        'dependency_files': ['go.mod', 'go.sum'],
        'install_command': 'go mod tidy',
        'build_command': None,
        'run_command': 'go run .'  # 运行整个目录
    },
    'main.go': {
        'language': 'go',
        'entry_points': ['main.go'],
        'run_command': 'go run {entry}'
    },
    # C/C++ 项目支持
    'Makefile': {
        'language': 'cpp',
        'entry_points': ['main.cpp', 'main.c'],
        'build_command': 'make',
        'run_command': './main'  # 假设编译输出为 main
    },
    'CMakeLists.txt': {
        'language': 'cpp',
        'entry_points': ['main.cpp', 'main.c'],
        'build_commands': ['mkdir -p build', 'cd build && cmake ..', 'cd build && make'],
        'run_command': './build/main'  # 假设编译输出为 build/main
    },
    'main.cpp': {
        'language': 'cpp',
        'entry_points': ['main.cpp'],
        'compile_command': 'g++ main.cpp -o main -std=c++17',
        'run_command': './main'
    },
    'main.c': {
        'language': 'cpp',  # C文件也用cpp镜像，因为包含gcc/g++
        'entry_points': ['main.c'],
        'compile_command': 'gcc main.c -o main',
        'run_command': './main'
    },
    'main.sh': {
        'language': 'bash',
        'entry_points': ['main.sh'],
        'install_command': None,
        'build_command': None,
        'run_command': 'bash main.sh'
    },
    '*.sh': {
        'language': 'bash',
        'entry_points': ['{filename}'],
        'install_command': None,
        'build_command': None,
        'run_command': 'bash {entry}'
    }
}
