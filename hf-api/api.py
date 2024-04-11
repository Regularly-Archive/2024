from fastapi import FastAPI
from models import CompletionRequest, ChatCompletionRequest, EmbeddingsRequest, EmbeddingsObjectResponse, EmbeddingsResponse, Usage, CompletionResponse, CompletionResponseChoice, ChatCompletionResponse, ChatCompletionResponseChoice
from fastapi import FastAPI, HTTPException
from completion import get_chat_completion, get_text_completion
from embeddings import get_embeddings
import os, uvicorn

app = FastAPI()
os.environ["HF_ENDPOINT"] = "https://hf-mirror.com"

@app.post("/v1/embeddings")
async def text_embeddings(request: EmbeddingsRequest) -> EmbeddingsResponse:
    embeddings = get_embeddings(request)
    if isinstance(request.input, str):
        return EmbeddingsResponse(data=embeddings, model=request.model, usage=Usage(), object="list")
    
    if isinstance(request.input, list):
        return EmbeddingsResponse(data=embeddings,model=request.model,usage=Usage(), object="list")
    
    raise HTTPException(
        status_code=400, detail="input needs to be an array of strings or a string"
    )

@app.post("/v1/chat/completions")
async def chat_completions(request: ChatCompletionRequest):
    text = get_chat_completion(request)
    return ChatCompletionResponse(model=request.model,choices=[ChatCompletionResponseChoice(text=text, index=0, Usage=None)])

@app.post("/v1/completions")
async def completions(request: CompletionRequest): 
    text = get_text_completion(request)
    return CompletionResponse(model=request.model, choices=[CompletionResponseChoice(text=text, index=0)])
    
if __name__ == '__main__':
    uvicorn.run(app='api:app', host="127.0.0.1", port=8003, reload=True)