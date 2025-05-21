from pydantic import BaseModel, Field
from typing import Literal

class RunCodeRequest(BaseModel):
    code: str
    language: str
    dependencies: list[str] = []

class RunJupyterCellRequest(BaseModel):
    code: str
    language: str
    dependencies: list[str] = []
    format: Literal['html', 'notebook'] = Field('html', description='The output format for jupyter runner')

class RunCodeResponse(BaseModel):
    output: str
    contentType: str
    duration: float
    language: str
