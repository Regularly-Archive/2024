#!/usr/bin/env python3
import random
import json
from datetime import datetime
import sys
import os

# 添加第三方依赖路径（模拟项目依赖）
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'lib'))

def main():
    # 生成随机数
    a = random.randint(1, 100)
    b = random.randint(1, 100)
    sum_result = a + b

    # 创建结果字典
    result = {
        "A": a,
        "B": b,
        "Sum": sum_result,
        "Timestamp": datetime.now().isoformat(),
        "Language": "Python Project"
    }

    # 使用标准库的 json 模块序列化
    json_output = json.dumps(result, indent=2, ensure_ascii=False)
    print(json_output)

if __name__ == "__main__":
    main()