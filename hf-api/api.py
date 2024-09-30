from fastapi import FastAPI
from models import CompletionRequest, ChatCompletionRequest, EmbeddingsRequest, EmbeddingsObjectResponse, EmbeddingsResponse, Usage, CompletionResponse, CompletionResponseChoice, ChatCompletionResponse, ChatCompletionResponseChoice, ChatMessage
from fastapi import FastAPI, HTTPException
from completion import get_chat_completion_async, get_text_completion_async
from embeddings import get_embeddings_async
import os, uvicorn, asyncio
from uvicorn.config import LOGGING_CONFIG

os.environ["HF_ENDPOINT"] = "https://hf-mirror.com"
app = FastAPI(title='A OpenAI Compatible API for HuggingFace')

@app.post("/v1/embeddings")
async def text_embeddings(request: EmbeddingsRequest) -> EmbeddingsResponse:
    embeddings = await get_embeddings_async(request)
    if isinstance(request.input, str):
        return EmbeddingsResponse(data=embeddings, model=request.model, usage=Usage(), object="list")
    
    if isinstance(request.input, list):
        return EmbeddingsResponse(data=embeddings,model=request.model,usage=Usage(), object="list")
    
    raise HTTPException(
        status_code=400, detail="input needs to be an array of strings or a string"
    )

@app.post("/v1/chat/completions")
async def chat_completions(request: ChatCompletionRequest):
    text = await get_chat_completion_async(request)
    message = ChatMessage(role='assistant', content=text)
    return ChatCompletionResponse(
        model=request.model, 
        choices=[ChatCompletionResponseChoice(message=message, index=0, finish_reason='length')],
        usage=Usage()
    )

@app.post("/v1/completions")
async def completions(request: CompletionRequest): 
    text = await get_text_completion_async(request)
    return CompletionResponse(
        model=request.model, 
        choices=[CompletionResponseChoice(text=text, index=0, finish_reason='length', logprobs=None)],
        usage=Usage()
    )
    
if __name__ == '__main__':
    LOGGING_CONFIG["formatters"]["access"]["fmt"] = ("%(asctime)s " + LOGGING_CONFIG["formatters"]["access"]["fmt"])
    uvicorn.run(app='api:app', host="127.0.0.1", port=8003, reload=True)