import os
import json
import requests

TEST_DIR = "jupyter_tests"
API_URL = "http://localhost:8001/api/jupyter/run"

# 文件名对应语言
LANG_MAP = {
    "python": "jupyter-python",
    "csharp": "jupyter-csharp",
    "fsharp": "jupyter-fsharp",
    "r": "jupyter-r"
}

for filename in os.listdir(TEST_DIR):
    if not filename.endswith(".txt"):
        continue

    file_path = os.path.join(TEST_DIR, filename)
    with open(file_path, "r") as f:
        code = f.read()

    # 根据文件名推断语言
    key = filename.split(".")[0].lower()
    language = LANG_MAP.get(key)
    if not language:
        print(f"Skipping {filename}, unknown language")
        continue

    payload = {
        "code": code,
        "language": language,
        "dependencies": [],
        "format": "notebook"
    }

    try:
        response = requests.post(API_URL, json=payload)
        if response.status_code == 200:
            data = response.json()
            print(f"Run Jupyter notebook from: {filename}...")
            summary = {
                "artifacts": data.get("result", {}).get("artifacts", []),
                "duration": data.get("result", {}).get("duration", 0),
                "runtime": {
                    "environment": data.get("runtime", {}).get("environment", "N/A"),
                    "kernel": data.get("runtime", {}).get("kernel", "N/A"),
                    "version": data.get("runtime", {}).get("version", "N/A"),
                }
            }
            print(json.dumps(summary, ensure_ascii=False, indent=2))
        else:
            print(f"{filename} -> HTTP {response.status_code}, {response.text}")
    except Exception as e:
        print(f"{filename} -> request failed: {e}")
