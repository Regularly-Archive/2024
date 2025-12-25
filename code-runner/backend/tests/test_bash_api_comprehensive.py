#!/usr/bin/env python3
"""
Bash API 综合测试文件 - 自动化版本
测试新API: POST /api/bash/run
通过遍历 bash_tests 目录自动执行测试
"""

import requests
import tarfile
import tempfile
import os
import json
from pathlib import Path
import io

BASE_URL = "http://localhost:8001"

class BashAPITester:
    def __init__(self):
        self.base_url = BASE_URL
        self.test_results = []
        self.temp_dir = tempfile.mkdtemp()
        self.test_base_path = Path(__file__).parent / "bash_tests"

    def create_archive_from_directory(self, test_dir, arcname):
        """从目录创建包含所有文件的压缩包"""
        archive_path = os.path.join(self.temp_dir, arcname)

        with tarfile.open(archive_path, "w:gz") as tar:
            # 遍历目录中的所有文件
            for root, dirs, files in os.walk(test_dir):
                for file in files:
                    # 跳过 manifest 文件
                    if file == ".manifest":
                        continue
                    file_path = os.path.join(root, file)
                    arc_path = os.path.relpath(file_path, test_dir)

                    # 对于bash脚本文件，确保使用Unix行尾格式
                    if file.endswith('.sh'):
                        # 读取文件内容并进行行尾转换
                        with open(file_path, 'rb') as f:
                            content = f.read()

                        # 转换行尾符 (CRLF -> LF)
                        content = content.replace(b'\r\n', b'\n')

                        # 创建 TarInfo 对象
                        tarinfo = tarfile.TarInfo(name=arc_path)
                        tarinfo.size = len(content)
                        tarinfo.mode = 0o755  # 确保有执行权限

                        # 使用 BytesIO 将内容添加到压缩包
                        content_io = io.BytesIO(content)
                        tar.addfile(tarinfo, content_io)
                    else:
                        # 非bash脚本文件直接添加
                        tar.add(file_path, arcname=arc_path)

        return archive_path

    def upload_and_test(self, archive_path, main_script=None, arguments=None):
        """上传并运行bash脚本"""
        with open(archive_path, 'rb') as f:
            files = {'archive_file': f}
            data = {}
            if main_script:
                data['main_script'] = main_script
            if arguments:
                data['arguments'] = arguments

            return requests.post(f"{self.base_url}/api/bash/run", files=files, data=data)

    def run_directory_tests(self, test_dir):
        """运行单个测试目录中的所有测试用例"""
        # 读取 manifest 文件
        manifest_path = test_dir / ".manifest"
        if not manifest_path.exists():
            print(f"⚠️  测试目录 {test_dir.name} 缺少 .manifest 文件，跳过")
            return

        with open(manifest_path, 'r', encoding='utf-8') as f:
            manifest = json.load(f)

        print(f"\n=== {manifest['description']} ===")

        # 创建压缩包
        archive_path = self.create_archive_from_directory(test_dir, f"{test_dir.name}.tar.gz")

        # 执行每个测试用例
        for test_case in manifest['tests']:
            print(f"\n📋 {test_case['name']}:")

            response = self.upload_and_test(
                archive_path,
                main_script=test_case['mainScript'],
                arguments=test_case['arguments']
            )

            print(f"状态码: {response.status_code}")
            if test_case['arguments']:
                print(f"参数: {test_case['arguments']}")

            if response.status_code == 200:
                result = response.json()
                print("✅成功执行")
                print(f"输出:\n{result['output']}")
                if 'duration' in result:
                    print(f"执行时间: {result['duration']:.3f}s")

                # 特殊检查逻辑
                if test_dir.name == "multi_module_project":
                    if "Bash API Test" in result['output']:
                        print("✅项目信息正确加载")
                    if "模块1" in result['output']:
                        print("✅模块1执行成功")
                    if "模块2" in result['output']:
                        print("✅模块2执行成功")
            else:
                print(f"❌失败: {response.text}")
                # 对于错误测试，状态码为400是预期行为
                if test_dir.name == "error_cases" and response.status_code == 400:
                    print("✅正确处理错误")

            print("-" * 60)

    def run_all_tests(self):
        """运行所有测试目录中的测试"""
        print("🚀 开始综合bash API测试...\n")

        # 检查服务状态
        try:
            health_response = requests.get(f"{self.base_url}/docs")
            if health_response.status_code == 200:
                print("✅服务运行正常\n")
            else:
                print("⚠️  服务异常\n")
        except:
            print("❌无法连接到服务，请确保服务运行在 :8001")
            return

        # 遍历测试目录
        test_dirs = [d for d in self.test_base_path.iterdir() if d.is_dir()]
        test_dirs.sort(key=lambda x: x.name)

        for test_dir in test_dirs:
            self.run_directory_tests(test_dir)

        # 清理
        import shutil
        shutil.rmtree(self.temp_dir)

        print("\n✅ 所有bash API测试完成！\n")

if __name__ == "__main__":
    tester = BashAPITester()
    tester.run_all_tests()