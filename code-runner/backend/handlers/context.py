from dataclasses import dataclass
from typing import Optional, Dict
from models import ProjectInfo, RuntimeInfo, ExecutionResult
from config import LANGUAGE_RUNTIME_MAP
from pathlib import Path


@dataclass
class HandlerContext:
    runtime_info: RuntimeInfo
    project_info: ProjectInfo
    execution_result: ExecutionResult = None

    def __init__(self, project_info: ProjectInfo):
        self.project_info = project_info
        langconfig = LANGUAGE_RUNTIME_MAP.get(project_info.language)
        environment = langconfig.get('env')
        self.runtime_info = RuntimeInfo(
            user='sandbox' if environment != 'jupyter' else 'jovyan',
            image_name=langconfig.get('image'),
            container_id='',
            environment=environment
        )

    @property
    def language(self):
        return self.project_info.language

    @property
    def entry_point(self):
        return self.project_info.entry_point

    @property
    def dependencies(self):
        return self.project_info.dependencies

    @property
    def project_form(self):
        return self.project_info.project_form

    @property
    def project_dir(self):
        return self.project_info.project_dir
    
    @property
    def project_id(self):
        return Path(self.project_info.project_dir).name.replace('project_', '')
    
    def set_container_env(self, key, value):
        self.runtime_info.container_envs[key] = value
    
    def get_container_env(self, key):
        if not key in self.runtime_info.container_envs:
            return None
        
        return self.runtime_info.container_envs[key]
    
    @classmethod
    def from_project(cls, project_info: ProjectInfo):
        return cls(project_info=project_info)

