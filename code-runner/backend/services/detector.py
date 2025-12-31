from enum import Enum
from utils import is_jbang_file
from models import ProjectInfo
import os

class ProjectForm(Enum):
    NODEJS = ("nodejs-project", "Node.js Application")
    PYTHON = ("python-project", "Python Project")
    JAVA_MAVEN = ("java-project", "Java Project(Maven Format)")
    JAVA_JBANG = ("jbang-project", "Java Project(JBang Format)")
    GO_MODULE = ("go-module", "Go Module-based Application")
    GO_SCRIPT = ("go-script", "Standalone Go Application")
    GO_SFA = ("go-sfa", "Single File Go Application")
    TYPESCRIPT = ("typescript-project", "TypeScript Project")
    CSHARP_PROJECT = ("csharp-project", "C# Project-based Application")
    CSHARP_SOLUTION = ("csharp-solution", "C# Solution-based Application")
    CSHARP_SCRIPT = ("csharp-script", "C# Script-based Application")
    CSHARP_SFA = ("csharp-sfa", "C# Single File Application")
    BASH_SCRIPT = ("bash-script", "Bash Script")
    CPP_PROJECT = ("cpp-project", "C++ Project")
    CPP_SFA = ("cpp-sfa", "C/C++ Single File Application")
    RUST = ("rust-project", "Rust Application")
    LUA_SCRIPT = ("lua-script", "Lua Script")

    @property
    def key(self):
        return self.value[0]

    @property
    def description(self):
        return self.value[1]

    @classmethod
    def from_key(cls, key_str):
        for item in cls:
            if item.key == key_str:
                return item.description
        return None

DEFAULT_ENTRY_CANDIDATES = {
    'go': ['main.go'],
    'csharp': ["Program.cs", "Main.cs"],
    'python3': ['main.py', 'app.py', '__main__.py', 'run.py'],
    'cpp': ["main.cpp", "main.c"],
    'rust': ["src/main.rs", "main.rs"],
    'java': ["Main.java"],
    'typescript': ['index.ts', 'app.ts', 'server.ts', 'main.ts'],
    'javascript': ['index.js', 'app.js', 'server.js', 'main.js'],
    'lua': ['main.lua']
}


PROJECT_INDICATORS = {
    'package.json': {
        'language': 'javascript',
        'dependency_files': ['package.json'],
        'project_form': ProjectForm.NODEJS.key,
    },
    'requirements.txt': {
        'language': 'python3',
        'dependency_files': ['requirements.txt'],
        'project_form': ProjectForm.PYTHON.key,
    },
    'go.mod': {
        'language': 'go',
        'dependency_files': ['go.mod'],
        'project_form': ProjectForm.GO_MODULE.key,
    },
    'Makefile': {
        'language': 'cpp',
        'dependency_files': [],
        'project_form': ProjectForm.CPP_PROJECT.key,
    },
    'tsconfig.json': {
        'language': 'typescript',
        'dependency_files': ['package.json'],
        'project_form': ProjectForm.TYPESCRIPT.key,
    },
    'Cargo.toml': {
        'language': 'rust',
        'dependency_files': ['Cargo.toml'],
        'project_form': ProjectForm.RUST.key,
    },
    'pom.xml':{
        'language': 'java',
        'dependency_files': ['pom.xml'],
        'project_form': ProjectForm.JAVA_MAVEN.key
    }
}

EXTENSION_INDICATORS = {
    '.csproj': {
        'language': 'csharp',
        'project_form': ProjectForm.CSHARP_PROJECT.key,
        'priority': 100,
    },
    '.sln': {
        'language': 'csharp',
        'project_form': ProjectForm.CSHARP_SOLUTION.key,
        'priority': 90,
    },
    '.csx': {
        'language': 'csharp',
        'project_form': ProjectForm.CSHARP_SCRIPT.key,
        'priority': 80,
    },
    '.cs': {
        'language': 'csharp',
        'project_form': ProjectForm.CSHARP_SFA.key,
        'priority': 50,
        'match_rule': lambda filePath: not filePath.endswith('.csproj') and not filePath.endswith('.sln'),
    },
    '.mod': {
        'language': 'go',
        'project_form': ProjectForm.GO_MODULE.key,
        'priority': 100
    },
    '.sh': {
        'language': 'bash',
        'project_form': ProjectForm.BASH_SCRIPT.key,
        'priority': 50
    },
    '.ts': {
        'language': 'typescript',
        'project_form': ProjectForm.TYPESCRIPT.key,
        'priority': 50
    },
    '.js': {
        'language': 'javascript',
        'project_form': ProjectForm.NODEJS.key,
        'priority': 50
    },
    '.py': {
        'language': 'python3',
        'project_form': ProjectForm.PYTHON.key,
        'priority': 50
    },
    '.c': {
        'language': 'cpp',
        'project_form': ProjectForm.CPP_SFA.key,
        'priority': 50
    },
    '.cpp': {
        'language': 'cpp',
        'project_form': ProjectForm.CPP_SFA.key,
        'priority': 50
    },
    '.java': {
        'language': 'java',
        'project_form': ProjectForm.JAVA_JBANG.key,
        'priority': 50,
        'match_rule': lambda filePath: is_jbang_file(filePath)
    },
    '.go': {
        'language': 'go',
        'project_form': ProjectForm.GO_SFA.key,
        'priority': 50
    },
    '.rs': {
        'language': 'rust',
        'project_form': ProjectForm.RUST.key,
        'priority': 50
    },
    '.lua': {
        'language': 'lua',
        'project_form': ProjectForm.LUA_SCRIPT.key,
        'priority': 50
    }
}

class ProjectDetector:

    def detect_project_info(self, project_dir: str, entry_point: str = None) -> ProjectInfo:
        files_in_project = [
            os.path.relpath(os.path.join(root, f), project_dir)
            for root, _, files in os.walk(project_dir)
            for f in files
        ]

        detected_type = None

        # 根据项目特征文件检测项目类型
        for indicator, config in PROJECT_INDICATORS.items():
            if indicator in files_in_project:
                detected_type = {
                    'language': config['language'],
                    'project_form': config['project_form'],
                    'description': ProjectForm.from_key(config['project_form']),
                    'dependency_files': config.get('dependency_files', []),
                    'entry_points': [entry_point] if entry_point else []
                }
                break

        # 根据文件扩展名检测项目类型
        matches = []
        for file in files_in_project:
            _, ext = os.path.splitext(file)
            filePath = os.path.join(project_dir, file)
            if ext in EXTENSION_INDICATORS:
                indicator = EXTENSION_INDICATORS[ext]
                match_rule = indicator.get('match_rule', lambda _: True)
                if match_rule(filePath):
                    matches.append(indicator)

        # 按优先级排序
        if matches:
            matches.sort(key=lambda x: x['priority'], reverse=True)
            chosen = matches[0]
            language = chosen['language']
            project_form = chosen['project_form']
            description = ProjectForm.from_key(project_form)
            if detected_type:
                detected_type.update({
                    'language': language,
                    'project_form': project_form,
                    'description': description
                })
            else:
                detected_type = {
                    'language': language,
                    'project_form': project_form,
                    'description': description,
                    'dependency_files': [],
                    'entry_points': [entry_point] if entry_point else []
                }

        if detected_type and not detected_type['entry_points']:
            detected_type['entry_points'] = self._find_entry_points(detected_type['language'], files_in_project)
            if not detected_type['entry_points']:
                detected_type['entry_points'] = [files_in_project[0]] if len(files_in_project) == 1 else []
                if not detected_type['entry_points']:
                    raise ValueError(
                        f"Unable to detect entry point for project (language={detected_type.get('language')}, "
                        f"project_form={detected_type.get('project_form')})"
                    )

        return ProjectInfo(
            project_dir=project_dir,
            language=detected_type.get('language', 'unknown'),
            files=files_in_project,
            entry_point=detected_type['entry_points'][0] if detected_type.get('entry_points') else None,
            dependencies=detected_type.get('dependency_files', []),
            project_form=detected_type.get('project_form'),
            description=detected_type.get('description')
        )


    def _find_entry_points(self, language: str, files: list[str]):
        candidates = DEFAULT_ENTRY_CANDIDATES.get(language, [])
        return [f for f in files if any(f.lower().endswith(c.lower()) for c in candidates)]