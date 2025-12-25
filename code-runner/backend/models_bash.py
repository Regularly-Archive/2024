from pydantic import BaseModel, Field
from typing import Literal, List, Optional

class BashScriptResponse(BaseModel):
    output: str
    duration: float
    exit_code: int