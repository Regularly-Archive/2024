#!/usr/bin/env python3
"""
测试压缩包上传API的脚本
"""

import requests
import zipfile
import tempfile
import os


def create_test_zip():
    """创建一个包含Python项目的测试压缩包"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'test_project.zip')

    # 项目文件内容
    main_py = '''
def main():
    print("Hello from test archibe project!")
    print("Project type: Python3")
    print("Entry point: main.py")

if __name__ == "__main__":
    main()
'''

    utils_py = '''
def greet(name):
    return f"Hello, {name}!"
'''

    requirements_txt = '''
requests==2.28.1
'''

    # 写入文件
    with open(os.path.join(temp_dir, 'main.py'), 'w') as f:
        f.write(main_py)

    with open(os.path.join(temp_dir, 'utils.py'), 'w') as f:
        f.write(utils_py)

    with open(os.path.join(temp_dir, 'requirements.txt'), 'w') as f:
        f.write(requirements_txt)

    # 创建ZIP文件
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
        zipf.write(os.path.join(temp_dir, 'main.py'), 'main.py')
        zipf.write(os.path.join(temp_dir, 'utils.py'), 'utils.py')
        zipf.write(os.path.join(temp_dir, 'requirements.txt'), 'requirements.txt')

    return zip_path, temp_dir


def test_python_project():
    """测试Python项目压缩包"""
    zip_path, temp_dir = create_test_zip()

    try:
        url = "http://localhost:8001/api/project/run-archive"

        files = {
            'archive_file': ('test_project.zip', open(zip_path, 'rb'), 'application/zip')
        }

        # 测试1: 指定语言
        print("=== 测试1: 指定语言为python3 ===")
        data = {
            'language': 'python3',
            'entry_point': 'main.py'
        }

        response = requests.post(url, files=files, data=data)
        print(f"状态码: {response.status_code}")
        print(f"响应: {response.json()}")
        print()

        # 测试2: 自动检测
        print("=== 测试2: 自动检测语言 ===")
        files['archive_file'] = ('test_project.zip', open(zip_path, 'rb'), 'application/zip')
        data2 = {}

        response = requests.post(url, files=files, data=data2)
        print(f"状态码: {response.status_code}")
        if response.status_code == 200:
            result = response.json()
            print(f"检测到的语言: {result.get('detected_language')}")
            print(f"检测到的入口点: {result.get('detected_entry_point')}")
            print(f"输出: {result.get('output')}")
        else:
            print(f"错误: {response.text}")

    finally:
        # 清理临时文件
        for file in ['main.py', 'utils.py', 'requirements.txt', 'test_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_javascript_project():
    """测试JavaScript项目压缩包"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'js_project.zip')

    try:
        # 创建JavaScript项目文件
        package_json = '''
{
  "name": "test-js-project",
  "version": "1.0.0",
  "description": "Test JS project",
  "main": "index.js"
}
'''

        index_js = '''
console.log("Hello from JavaScript project!");
console.log("Project type: Node.js");
console.log("Entry point: index.js");
'''

        # 写入文件
        with open(os.path.join(temp_dir, 'package.json'), 'w') as f:
            f.write(package_json)

        with open(os.path.join(temp_dir, 'index.js'), 'w') as f:
            f.write(index_js)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'package.json'), 'package.json')
            zipf.write(os.path.join(temp_dir, 'index.js'), 'index.js')

        print("=== 测试3: JavaScript项目自动检测 ===")
        url = "http://localhost:8001/api/project/run-archive"

        files = {
            'archive_file': ('js_project.zip', open(zip_path, 'rb'), 'application/zip')
        }

        response = requests.post(url, files=files)
        print(f"状态码: {response.status_code}")
        if response.status_code == 200:
            result = response.json()
            print(f"检测到的语言: {result.get('detected_language')}")
            print(f"检测到的入口点: {result.get('detected_entry_point')}")
            print(f"输出: {result.get('output')}")
        else:
            print(f"错误: {response.text}")

    finally:
        # 清理
        for file in ['package.json', 'index.js', 'js_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


if __name__ == "__main__":
    print("开始测试压缩包项目运行功能...")
    print("确保后端服务正在运行: python server.py")
    print()

    test_python_project()
    print()

    test_javascript_project()

    print("\n测试完成!")