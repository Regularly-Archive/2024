from fastapi import FastAPI
from models import CompletionRequest, ChatCompletionRequest, EmbeddingsRequest, EmbeddingsObjectResponse, EmbeddingsResponse, Usage, CompletionResponse, CompletionResponseChoice, ChatCompletionResponse, ChatCompletionResponseChoice
from fastapi import FastAPI, HTTPException
from sentence_transformers import SentenceTransformer
import os
from completions import chatCompletions, textCompletions

app = FastAPI()
os.environ["HF_ENDPOINT"] = "https://hf-mirror.com/"

@app.post("/v1/embeddings")
async def text_embeddings(item: EmbeddingsRequest) -> EmbeddingsResponse:
    model: SentenceTransformer = SentenceTransformer(item.model)
    if isinstance(item.input, str):
        vectors = model.encode(item.input)
        tokens = len(vectors)
        return EmbeddingsResponse(
            data=[EmbeddingsObjectResponse(embedding=vectors, index=0, object="embedding")],
            model=item.model,
            usage=Usage(prompt_tokens=tokens, total_tokens=tokens),
            object="list",
        )
    if isinstance(item.input, list):
        embeddings = []
        tokens = 0
        for index, text_input in enumerate(item.input):
            if not isinstance(text_input, str):
                raise HTTPException(
                    status_code=400,
                    detail="input needs to be an array of strings or a string",
                )
            vectors = model.encode(text_input)
            tokens += len(vectors)
            embeddings.append(
                EmbeddingsObjectResponse(embedding=vectors, index=index, object="embedding")
            )
        return EmbeddingsResponse(
            data=embeddings,
            model=item.model,
            usage=Usage(prompt_tokens=tokens, total_tokens=tokens),
            object="list",
        )
    raise HTTPException(
        status_code=400, detail="input needs to be an array of strings or a string"
    )

@app.post("/v1/chat/completions")
async def chat_completions(item: ChatCompletionRequest):
    text = chatCompletions(item)
    return ChatCompletionResponse(
        model=item.model,
        choices=[ChatCompletionResponseChoice(text=text, index=0, Usage=None)],
    )

@app.post("/v1/completions")
async def completions(item: CompletionRequest): 
    text = textCompletions(item)
    return CompletionResponse(
        model=item.model,
        choices=[CompletionResponseChoice(text=text, index=0)],
    )
    


if __name__ == '__main__':
    import os,uvicorn
    os.environ["HF_ENDPOINT"] = "https://hf-mirror.com/"
    uvicorn.run(app='api:app', host="127.0.0.1", port=8003, reload=True)