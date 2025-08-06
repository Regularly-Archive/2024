import os
import modelscope
from dotenv import load_dotenv
from FlagEmbedding import FlagAutoReranker

load_dotenv()
model_name = os.environ.get('RERANKER_MODEL_NAME', default='BAAI/bge-reranker-v2-m3')
model_dir = modelscope.snapshot_download(model_name, revision='master')
reranker = FlagAutoReranker.from_finetuned(model_dir, use_fp16=True)

def compute_scores(query: str, docs: list[str]) -> list[float]:
	pairs = list(map(lambda x:[query, x], docs))
	return reranker.compute_score(pairs, normalize=True)

