from rank_bm25 import BM25Okapi
import jieba
import re, os

def load_stopwords(file_path: str) -> set: 
    if not os.path.exists(file_path):
        return set()
    else:
        with open(file_path, 'r', encoding='utf-8') as f:
            return set([line.strip() for line in f if line.strip()])
    
def preprocess_text(text: str, stopwords: list[str]) -> list[str]:
    text = re.sub(r"[^\u4e00-\u9fa5]", "", text)
    words = jieba.lcut(text)
    return [word for word in words if word not in stopwords and len(word) > 1]


def compute_scores(query: str, docs: list[str]) -> list[float]:
    stopwords = load_stopwords('stopwords.txt')
    processed_docs = [preprocess_text(doc, stopwords) for doc in docs]
    processed_query = [preprocess_text(query, stopwords)]
    bm25 = BM25Okapi(processed_docs)
    return bm25.get_scores(processed_query)

