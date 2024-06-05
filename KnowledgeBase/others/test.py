import modelscope
from FlagEmbedding import FlagReranker

reranker_model_dir = modelscope.snapshot_download('Xorbits/bge-reranker-base', revision='master')
reranker = FlagReranker(reranker_model_dir, use_fp16=True)

score = reranker.compute_score(['夏天到了', '四五月的夏天热到爆炸'], normalize=True)
print("The score of ['夏天到了', '四五月的夏天热到爆炸'] is " + str(score))

# 计算多对文本间的相关性评分
scores = reranker.compute_score([
        ['你好', 'How are you'], 
        ['你好', 'Hello']
    ],
    normalize=True
)

print("The score of  ['你好', 'How are you'] is " + str(scores[0]))
print("The score of  ['你好', 'Hello'] is " + str(scores[1]))