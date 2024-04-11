from sentence_transformers import SentenceTransformer
from models import EmbeddingsRequest, EmbeddingsObjectResponse
from typing import List
import os

model_cache_folder = './models/embedding/'

def get_embeddings(request: EmbeddingsRequest) -> List[EmbeddingsObjectResponse]: 
    cache_folder = os.path.join(model_cache_folder, request.model)
    model = SentenceTransformer(request.model, cache_folder=cache_folder)
    if isinstance(request.input, str):
        vectors = model.encode(request.input)
        return [EmbeddingsObjectResponse(embedding=vectors, index=0, object="embedding")]
    if isinstance(request.input, list):
        embeddings = []
        for index, text_input in enumerate(request.input):
            vectors = model.encode(text_input)
            embeddings.append(EmbeddingsObjectResponse(embedding=vectors, index=index, object="embedding"))
        return embeddings