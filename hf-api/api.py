from typing import Annotated
from fastapi import FastAPI, Request, Path
from models import CompletionRequest, ChatCompletionRequest, EmbeddingsRequest, EmbeddingsObjectResponse, EmbeddingsResponse, Usage, CompletionResponse, CompletionResponseChoice, ChatCompletionResponse, ChatCompletionResponseChoice, ChatMessage
from fastapi import FastAPI, HTTPException
from completion import get_chat_completion_async, get_text_completion_async
from embeddings import get_embeddings_async
import os, uvicorn, asyncio, io
from uvicorn.config import LOGGING_CONFIG
from PIL import Image
from image_to_text import convert_image_to_text, translate

os.environ["HF_ENDPOINT"] = "https://hf-mirror.com"
app = FastAPI(title='A OpenAI Compatible API for HuggingFace')

@app.post("/v1/embeddings")
async def text_embeddings(request: EmbeddingsRequest) -> EmbeddingsResponse:
    embeddings, usage = await get_embeddings_async(request)
    if isinstance(request.input, str):
        return EmbeddingsResponse(data=embeddings, model=request.model, usage=usage, object="list")
    
    if isinstance(request.input, list):
        return EmbeddingsResponse(data=embeddings, model=request.model, usage=usage, object="list")
    
    raise HTTPException(
        status_code=400, detail="input needs to be an array of strings or a string"
    )

@app.post("/v1/chat/completions")
async def chat_completions(request: ChatCompletionRequest):
    text, usage = await get_chat_completion_async(request)
    message = ChatMessage(role='assistant', content=text)
    return ChatCompletionResponse(
        model=request.model, 
        choices=[ChatCompletionResponseChoice(message=message, index=0, finish_reason='length')],
        usage=usage
    )

@app.post("/v1/completions")
async def completions(request: CompletionRequest): 
    text, usage = await get_text_completion_async(request)
    return CompletionResponse(
        model=request.model, 
        choices=[CompletionResponseChoice(text=text, index=0, finish_reason='length', logprobs=None)],
        usage=usage
    )

@app.post("/models/{model_name:path}", openapi_extra={
    "requestBody": {
        "content": {
            "image/png": {"schema": {"type": "string", "format": "binary"}},
            "image/jpeg": {"schema": {"type": "string", "format": "binary"}},
        }
    }
})
async def image_to_text(
    request: Request, 
    model_name: Annotated[str, Path(title="The model name from Hugging Face, e.g. Salesforce/blip-image-captioning-base")],
):
    request_body: bytes = await request.body()
    image = Image.open(io.BytesIO(request_body)).convert('RGB')
    generated_text = convert_image_to_text(image, model_name=model_name)
    #generated_text = translate('Chinese', generated_text, model_name="utrobinmv/t5_translate_en_ru_zh_large_1024") 
    return [{"generated_text": generated_text}]

@app.get("/items/{item_id}")
async def read_item(item_id):
    return {"item_id": item_id}
    
if __name__ == '__main__':
    LOGGING_CONFIG["formatters"]["access"]["fmt"] = ("%(asctime)s " + LOGGING_CONFIG["formatters"]["access"]["fmt"])
    uvicorn.run(app='api:app', host="127.0.0.1", port=8003, reload=True)