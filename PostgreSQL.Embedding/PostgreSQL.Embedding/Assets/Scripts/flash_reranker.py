import os
from dotenv import load_dotenv
from flashrank import Ranker, RerankRequest

load_dotenv()
os.environ["HF_ENDPOINT"] = "https://hf-mirror.com"
model_name = os.environ.get('RERANKER_MODEL_NAME', default='ms-marco-MiniLM-L-12-v2')
cache_folder = os.environ.get('MODEL_CACHE_DIR', default='\cached_models')
ranker = Ranker(model_name=model_name, cache_dir=cache_folder)

def compute_scores(query: str, docs: list[str]) -> list[float]:
    passages = _to_passages(docs)
    rerankrequest = RerankRequest(query=query, passages=passages)
    results = ranker.rerank(rerankrequest)
    return list(map(lambda x:x['score'], results))

def _to_passages(docs: list[str]) -> list[dict]:
    return [
        {
            "id": idx + 1,
            "text": text,
            "meta": {"additional": f"info{idx + 1}"}
        }
        for idx, text in enumerate(text_list)
    ]