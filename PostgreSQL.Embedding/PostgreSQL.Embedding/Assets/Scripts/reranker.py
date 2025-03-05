import os
import modelscope
from dotenv import load_dotenv
from FlagEmbedding import FlagAutoReranker

load_dotenv()
model_name = os.environ.get('RERANKER_MODEL_NAME', default='BAAI/bge-reranker-v2-m3')
model_dir = modelscope.snapshot_download(model_name, revision='master')
reranker = FlagAutoReranker.from_finetuned(model_dir, use_fp16=True)

def compute_score(pairs: list[str]) -> float:
	scores = reranker.compute_score(pairs, normalize=True)
	return scores[0]

def compute_scores(pairs: list[list[str]]) -> list[float]:
	return reranker.compute_score(pairs, normalize=True)

