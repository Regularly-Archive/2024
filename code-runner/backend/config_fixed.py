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
        'command': 'sh -c "npx tsc code.ts 2>>tsc.log && node code.js || (cat tsc.log)"',
        'commandRedirect': "sh -c 'npx tsc code.ts 2>>tsc.log && node code.js > output.txt || (echo \"TypeScript error:\" && cat tsc.log)'",
        'extension': 'ts',
        'install': 'npm install {deps}'
    },
    'csharp': {
        'env' : 'dotnet',
        'image': 'code_runner/dotnet',
        'command': 'dotnet script code.csx',
        'commandRedirect': "sh -c 'dotnet script code.csx > output.txt'",
        'extension': 'csx'
    },
    'csharp-sfa': {
        'env' :'dotnet',
        'image': 'code_runner/dotnet',
        'command': "sh -c 'dotnet script code.cs 2>/dev/null || (cat > myapp.csproj <<EOF\n<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\nEOF\n&& mv code.cs Program.cs && dotnet run)'",
        'commandRedirect': "sh -c 'dotnet script code.cs 2>/dev/null > output.txt || (cat >myapp.csproj <<EOF\n<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\nEOF\n&& mv code.cs Program.cs && dotnet run > output.txt)'",
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
        'command': 'sh -c "export JAVA_TOOL_OPTIONS=\'-Dfile.encoding=UTF-8\' && jbang code.java"',
        'commandRedirect': "sh -c 'export JAVA_TOOL_OPTIONS=\'-Dfile.encoding=UTF-8\' && jbang code.java > output.txt'",
        'extension': 'java'
    },
}