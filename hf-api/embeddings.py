from sentence_transformers import SentenceTransformer
from models import EmbeddingsRequest, EmbeddingsObjectResponse, Usage
from typing import List, Tuple
from utils import Embedding_Model_Cache_Folder as model_cache_folder
import os, gc, asyncio
from utils import timer, createLogger, LRUCache
from concurrent.futures import ThreadPoolExecutor
from transformers import AutoTokenizer

cached_models = LRUCache(5)
cached_tokenizers = LRUCache(5)
logger = createLogger(__name__)

max_workers = int(os.getenv('MAX-WORKERS', '10'))
executor = ThreadPoolExecutor(max_workers)

@timer(logger=logger)
def get_embeddings(request: EmbeddingsRequest) -> Tuple[List[EmbeddingsObjectResponse], Usage]: 
    cache_dir = os.path.join(model_cache_folder)
    model, tokenizer = get_cached_model(model_name=request.model, cache_dir=cache_dir)
    
    total_tokens = 0
    
    if isinstance(request.input, str):
        tokens = tokenizer(request.input, add_special_tokens=True)
        total_tokens = len(tokens['input_ids'])
        vectors = model.encode(request.input)
        embeddings = [EmbeddingsObjectResponse(embedding=vectors, index=0, object="embedding")]
    elif isinstance(request.input, list):
        embeddings = []
        for index, text_input in enumerate(request.input):
            tokens = tokenizer(text_input, add_special_tokens=True)
            total_tokens += len(tokens['input_ids'])
            vectors = model.encode(text_input)
            embeddings.append(EmbeddingsObjectResponse(embedding=vectors, index=index, object="embedding"))
    
    usage = Usage(
        prompt_tokens=total_tokens,
        completion_tokens=0, 
        total_tokens=total_tokens
    )
    
    return embeddings, usage
    
async def get_embeddings_async(request: EmbeddingsRequest) -> Tuple[List[EmbeddingsObjectResponse], Usage]:
    loop = asyncio.get_event_loop()
    embeddings, usage = await loop.run_in_executor(executor, get_embeddings, request)
    return embeddings, usage

def get_cached_model(model_name: str, cache_dir: str):
    if cached_models.hasKey(model_name):
        return cached_models.get(model_name), cached_tokenizers.get(model_name)
    else:
        model = SentenceTransformer(model_name, cache_folder=cache_dir, trust_remote_code=True)
        tokenizer = AutoTokenizer.from_pretrained(model_name)
        cached_models.put(model_name, model)
        cached_tokenizers.put(model_name, tokenizer)
        return model, tokenizer
    
def release_embedding_models():
    gc.collect()

