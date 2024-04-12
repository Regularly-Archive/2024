from models import CompletionRequest, ChatCompletionRequest
from transformers import pipeline, AutoModel, AutoTokenizer
from utils import Text_Generation_Model_Cache_Folder as model_cache_folder
import os, torch

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

def get_chat_completion(request: ChatCompletionRequest) -> str:
    cache_dir = os.path.join(model_cache_folder, request.model)
    model = AutoModel.from_pretrained(request.model, cache_dir=cache_dir)
    
    generator = pipeline('text-generation', model=model)
    prompt = ','.join(map(lambda x: f'{x.role}: {x.content}', request.messages))
    return generator(prompt)

def get_text_completion(request: CompletionRequest) -> str:
        cache_dir = os.path.join(model_cache_folder, request.model)
        generator = pipeline('text-generation', model=request.model)
        response = generator(request.prompt)
        return response[0]['generated_text']