from models import ProjectInfo
from config import PROJECT_INDICATORS, EXTENSION_INDICATORS
import os


class ProjectDetector:

    def detect_project_info(self, project_dir: str) -> ProjectInfo:
        files_in_project = [
            os.path.relpath(os.path.join(root, f), project_dir)
            for root, _, files in os.walk(project_dir)
            for f in files
        ]

        detected_type = None
        
        # 根据项目特征文件检测项目类型
        for indicator, config in PROJECT_INDICATORS.items():
            if indicator in files_in_project:
                detected_type = config.copy()
                break

        # 根据文件扩展名检测项目类型
        if not detected_type or detected_type.get('language') == 'csharp':
            for file in files_in_project:
                _, ext = os.path.splitext(file)
                filePath = os.path.join(project_dir, file)
                if ext in EXTENSION_INDICATORS:
                    match_rule = EXTENSION_INDICATORS[ext].get('match_rule') if 'match_rule' in EXTENSION_INDICATORS[ext] else lambda filePath: True
                    if match_rule(filePath):
                        detected_type = EXTENSION_INDICATORS[ext].copy() if not detected_type else detected_type | EXTENSION_INDICATORS[ext].copy()
                        break

        # 入口文件检测
        candidates = []
        if detected_type and not detected_type.get('entry_points'):
            detected_type['entry_points'] = []
            # 根据语言查找典型入口
            lang = detected_type.get('language')
            if lang == 'go':
                candidates = ['main.go']
            elif lang == 'csharp':
                candidates = ['Program.cs', 'Main.cs']
            elif lang == 'cpp':
                candidates = ['main.cpp', 'main.c']
            elif lang == 'rust':
                candidates = ['src/main.rs', 'main.rs']
            elif lang == 'java':
                candidates = ['Main.java']
            else:
                candidates = ['main', 'index', 'app']

        for file in files_in_project:
            basename = os.path.basename(file).lower()
            if any(cand.lower() in basename for cand in candidates):
                detected_type['entry_points'].append(file)

        if not 'entry_points' in detected_type or not detected_type['entry_points']:
           if len(files_in_project) == 1:
               detected_type['entry_points'] = [files_in_project[0]]
           else:
               raise ValueError(
                   f"Unable to detect entry point for the project, language: {detected_type.get('language', 'N/A')}, project_form: {detected_type.get('project_form', 'N/A')}, desription: {detected_type.get('description', 'N/A')}   ")

        return ProjectInfo(
            project_dir=project_dir,
            language=detected_type.get('language', 'unknown'),
            files=files_in_project,
            entry_point=detected_type.get('entry_points')[0] if detected_type.get('entry_points') else None,
            dependencies=detected_type.get('dependency_files', []),
            project_form=detected_type.get('project_form'),
            description=detected_type.get('description')
        )