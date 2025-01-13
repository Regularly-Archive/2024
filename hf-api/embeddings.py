from sentence_transformers import SentenceTransformer
from models import EmbeddingsRequest, EmbeddingsObjectResponse
from typing import List
from utils import Embedding_Model_Cache_Folder as model_cache_folder
import os, gc, asyncio
from utils import timer, createLogger, LRUCache
from concurrent.futures import ThreadPoolExecutor, ProcessPoolExecutor


cached_models = LRUCache(5)
logger = createLogger(__name__)

max_workers = int(os.getenv('MAX-WORKERS', '10'))
executor = ThreadPoolExecutor(max_workers)

@timer(logger=logger)
def get_embeddings(request: EmbeddingsRequest) -> List[EmbeddingsObjectResponse]: 
    cache_dir = os.path.join(model_cache_folder)
    model = get_cached_model(model_name=request.model, cache_dir=cache_dir)
    if isinstance(request.input, str):
        vectors = model.encode(request.input)
        return [EmbeddingsObjectResponse(embedding=vectors, index=0, object="embedding")]
    if isinstance(request.input, list):
        embeddings = []
        for index, text_input in enumerate(request.input):
            vectors = model.encode(text_input)
            embeddings.append(EmbeddingsObjectResponse(embedding=vectors, index=index, object="embedding"))
        return embeddings
    
async def get_embeddings_async(request: EmbeddingsRequest) -> List[EmbeddingsObjectResponse]:
    loop = asyncio.get_event_loop()
    embeddings = await loop.run_in_executor(executor, get_embeddings, request)
    return embeddings

def get_cached_model(model_name: str, cache_dir: str):
    if cached_models.hasKey(model_name):
        return cached_models.get(model_name)
    else:
        model = SentenceTransformer(model_name, cache_folder=cache_dir, trust_remote_code=True)
        cached_models.put(model_name, model)
        return model
    
def release_embedding_models():
    gc.collect()

