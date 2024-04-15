from models import CompletionRequest, ChatCompletionRequest
from transformers import pipeline
from utils import Text_Generation_Model_Cache_Folder as model_cache_folder
import os
from utils import timer, createLogger

logger = createLogger(__name__)

@timer(logger=logger)
def get_chat_completion(request: ChatCompletionRequest) -> str:
    cache_dir = os.path.join(model_cache_folder, request.model) 
    generator = pipeline('text-generation', model=request.model, model_kwargs={
        cache_dir: cache_dir
    })
    prompt = ','.join(map(lambda x: f'{x.role}: {x.content}', request.messages))
    return generator(prompt)

@timer(logger=logger)
def get_text_completion(request: CompletionRequest) -> str:
        cache_dir = os.path.join(model_cache_folder, request.model)
        generator = pipeline('text-generation', model=request.model, model_kwargs={cache_dir: cache_dir})
        response = generator(request.prompt, max_new_tokens=request.max_tokens, do_sample=True, temperature=request.temperature, top_k=request.top_k, top_p=request.top_p)
        return response[0]['generated_text']