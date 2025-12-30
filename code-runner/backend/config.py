from utils import is_jbang_file


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
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'command': "dotnet run code.cs",
        'commandRedirect': "sh -c 'dotnet run code.cs > output.txt'",
        'extension': 'csx'
    },
    'csharp-sfa': {
        'env': 'dotnet',
        'image': 'code_runner/dotnet',
        'command': "dotnet run code.cs",
        'commandRedirect': "sh -c 'dotnet run code.cs > output.txt'",
        'extension': 'cs'
    },
    'csharp-mono': {
        'env': 'mono',
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
    'bash': {
        'env': 'bash',
        'image': 'code_runner/bash',
        'command': 'bash {entry}',
        'commandRedirect': "sh -c 'bash {entry} > output.txt'",
        'extension': 'sh'
    },
    'rust': {
        'env': 'bash',
        'image': 'code_runner/rust',
    }
}

PROJECT_INDICATORS = {
    'package.json': {
        'language': 'javascript',
        'entry_points': ['index.js', 'app.js', 'server.js', 'main.js'],
        'dependency_files': ['package.json'],
        'project_form': 'nodejs-project',
        'description': 'Node.js Application'
    },
    'requirements.txt': {
        'language': 'python3',
        'entry_points': ['main.py', 'app.py', '__main__.py', 'run.py'],
        'dependency_files': ['requirements.txt'],
        'project_form': 'python-project',
        'description': 'Python Project'
    },
    'main.py': {
        'language': 'python3',
        'entry_points': ['main.py'],
        'run_command': 'python main.py',
        'project_form': 'python-project',
        'description': 'Python Project'
    },
    'pom.xml': {
        'language': 'java',
        'entry_points': [],
        'dependency_files': ['pom.xml'],
        'project_form': 'java-project',
        'description': 'Java Project(Maven)'
    },
    'Program.cs': {
        'language': 'csharp',
        'entry_points': ['Program.cs'],
    },
    'go.mod': {
        'language': 'go',
        'entry_points': ['main.go'],
        'dependency_files': ['go.mod'],
        'project_form': 'go-module',
        'description': 'Go Module-based Application'
    },
    'main.go': {
        'language': 'go',
        'entry_points': ['main.go'],
        'project_form': 'go-sfa',
        'description': 'Go Application'
    },
    'main.cpp': {
        'language': 'cpp',
        'entry_points': ['main.cpp'],
    },
    'main.c': {
        'language': 'cpp',
        'entry_points': ['main.c'],
    },
    'Makefile': {
        'language': 'cpp',
        'entry_points': [],
    },
    'tsconfig.json': {
        'language': 'typescript',
        'entry_points': ['index.ts', 'app.ts', 'server.ts', 'main.ts'],
        'dependency_files': ['package.json', 'tsconfig.json'],
        'project_form': 'typescript-project',
        'description': 'TypeScript Project'
    },
    'Cargo.toml': {
        'language': 'rust',
        'entry_points': ['src/main.rs', 'main.rs'],
        'dependency_files': ['Cargo.toml'],
        'project_form': 'rust-project',
        'description': 'rust-project'
    }
}


EXTENSION_INDICATORS = {
    '.csproj': {
        'language': 'csharp',
        'project_form': 'csharp-project',
        'description': 'Project-based C# Application'
    },
    '.sln': {
        'language': 'csharp',
        'project_form': 'csharp-solution',
        'description': 'Solution-based C# Application'
    },
    '.csx': {
        'language': 'csharp',
        'project_form': 'csharp-script',
        'description': 'Script-based C# Application'
    },
    '.cs': {
        'language': 'csharp',
        'project_form': 'csharp-sfa',
        'description': 'Single File Based C# Application',
        'match_rule': lambda filePath: not filePath.endswith('.csproj') or not filePath.endswith('.sln')
    },
    '.mod': {
        'language': 'go',
        'project_form': 'go-module',
        'description': 'Go Module-based Application'
    },
    '.sh': {
        'language': 'bash',
        'project_form': 'bash-script',
        'description': 'Bash Script'
    },
    '.ts': {
        'language': 'typescript',
        'project_form': 'typescript-project',
        'description': 'TypeScript Application'
    },
    '.js': {
        'language': 'javascript',
        'project_form': 'nodejs-project',
        'description': 'Node.js Application'
    },
    '.py': {
        'language': 'python3',
        'project_form': 'python-project',
        'description': 'Python Project'
    },
    '.c': {
        'language': 'cpp',
        'project_form': 'cpp-sfa',
        'description': 'C Application'
    },
    '.cpp': {
        'language': 'cpp',
        'project_form': 'cpp-sfa',
        'description': 'C++ Application'
    },
    '.java': {
        'language': 'java',
        'project_form': 'jbang-project',
        'description': 'Java Project(JBang Format)',
        'match_rule': lambda filePath: is_jbang_file(filePath)
    },
    '.go': {
        'language': 'go',   
        'project_form': 'go-sfa',
        'description': 'Go Application'
    },
    '.rs': {
        'language': 'rust', 
        'project_form': 'rust-project',
        'description': 'Rust Application'
    }
}
