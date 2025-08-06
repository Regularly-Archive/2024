from rank_bm25 import BM25Okapi
import jieba
import re, os

def _load_stopwords(file_path: str) -> set[str]: 
    if not os.path.exists(file_path):
        return set()
    else:
        with open(file_path, 'r', encoding='utf-8') as f:
            return set([line.strip() for line in f if line.strip()])

def _tokenize(text: str, stopwords: set[str]) -> list[str]:
    tokens = jieba.lcut(text)
    tokens = [t for t in tokens if t.strip() and t not in stopwords]
    return tokens

def compute_scores(query: str, docs: list[str]) -> list[float]:
    stopwords = _load_stopwords('stopwords.txt')
    tokenized_docs = [_tokenize(doc, stopwords) for doc in docs]
    tokenized_query = _tokenize(query, stopwords)
    bm25 = BM25Okapi(tokenized_docs)
    return bm25.get_scores(tokenized_query).tolist()