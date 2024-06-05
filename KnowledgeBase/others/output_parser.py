from langchain.output_parsers import PydanticOutputParser
from langchain_core.pydantic_v1 import BaseModel, Field
from typing import List

class RewriteResult(BaseModel):
    input: str = Field(),
    output: List[str] = Field(),

RewriteResultParser = PydanticOutputParser(pydantic_object=RewriteResult)