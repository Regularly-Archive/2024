from sentence_transformers import SentenceTransformer
from models import EmbeddingsRequest, EmbeddingsObjectResponse
from typing import List
from utils import Embedding_Model_Cache_Folder as model_cache_folder
import os, asyncio
from utils import timer, createLogger
from concurrent.futures import ThreadPoolExecutor

logger = createLogger(__name__)
executor = ThreadPoolExecutor()

@timer(logger=logger)
def get_embeddings(request: EmbeddingsRequest) -> List[EmbeddingsObjectResponse]: 
    cache_folder = os.path.join(model_cache_folder)
    model = SentenceTransformer(request.model, cache_folder=cache_folder, trust_remote_code=True)
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

