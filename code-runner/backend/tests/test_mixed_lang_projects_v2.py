#!/usr/bin/env python3
"""
改进版：测试多语言项目
自动扫描 tests 目录中的不同项目结构，生成压缩包并测试
"""

import os
import zipfile
import tempfile
import json
import requests
from pathlib import Path
from typing import Dict, List, Optional
import argparse


def is_jbang_file(file_path: str) -> bool:
    """检查文件是否是 Jbang 格式的 Java 文件"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            first_line = f.readline().strip()
            if first_line.startswith('///usr/bin/env jbang'):
                return True
        return False
    except Exception:
        return False


def create_project_archive(project_dir: str) -> str:
    """将项目目录打包成 zip 文件"""
    temp_file = tempfile.mktemp(suffix='.zip')
    with zipfile.ZipFile(temp_file, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(project_dir):
            # 排除隐藏文件和目录
            files = [f for f in files if not f.startswith('.')]
            dirs[:] = [d for d in dirs if not d.startswith('.')]

            for file in files:
                file_path = os.path.join(root, file)
                arc_path = os.path.relpath(file_path, project_dir)
                zipf.write(file_path, arc_path)
    return temp_file

def test_project(project_path: str, run_command: Optional[str] = None, data: Optional[dict] = None) -> Dict:
    """测试单个项目"""
    project_name = os.path.basename(project_path)
    print(f"\n***** 测试项目: {project_name} *****")

    # 解析项目信息
    with open(os.path.join(project_path, '.manifest'), 'r', encoding='utf-8') as f:
        manifest = json.load(f)
        print(f"项目语言: {manifest.get('language', 'N/A')}")
        print(f"项目形式: {manifest.get('project_form', 'N/A')}")
        print(f"项目描述: {manifest.get('description', 'N/A')}")

    # 创建压缩包
    zip_path = create_project_archive(project_path)

    try:
        # 发送到后端测试
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': (f'{project_name}.zip', zip_file, 'application/zip')
            }

            request_data = data.copy() if data else {}
            if run_command:
                request_data['run_command'] = run_command

            response = requests.post(url, files=files, data=request_data)

            print(f"状态码: {response.status_code}")

            if response.status_code == 200:
                result = response.json()
                print(result)
                print(f"耗时: {result.get('duration')}")
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                if result.get('build_output'):
                    print(f"编译输出: {result.get('build_output')}")
                print(f"运行输出:")
                print(result.get('output', ''))

                return {
                    'success': True,
                    'detected_language': result.get('detected_language'),
                    'output': result.get('output', ''),
                    'error': None
                }
            else:
                print(f"错误: {response.text}")
                return {
                    'success': False,
                    'error': response.text,
                    'output': ''
                }

    except Exception as e:
        print(f"错误: {str(e)}")
        return {
            'success': False,
            'error': str(e),
            'output': ''
        }
    finally:
        # 清理临时文件
        if os.path.exists(zip_path):
            os.unlink(zip_path)


def scan_and_test_tests_directory(filter):
    """扫描 tests 目录并测试所有项目"""
    tests_dir = Path("project_tests")

    if not tests_dir.exists():
        print(f"project_tests 目录不存在: {tests_dir}")
        return

    results = []
    project_dirs = [d for d in tests_dir.iterdir() if d.is_dir()]

    print(f"发现 {len(project_dirs)} 个项目目录")
    print("=" * 60)

    for project_dir in sorted(project_dirs):
        project_name = os.path.basename(project_dir)
        if not filter(project_name):
            continue

        # 根据项目类型确定特殊参数和运行命令
        run_command = None
        data = {}

        result = test_project(str(project_dir), run_command, data)
        results.append({
            'project': project_dir.name,
            'result': result
        })
        print("=" * 60)

    # 打印汇总报告
    print("\n=== 测试汇总报告 ===")
    print(f"总计项目数: {len(results)}")
    print(f"成功: {sum(1 for r in results if r['result']['success'])}")
    print(f"失败: {sum(1 for r in results if not r['result']['success'])}")

    for result in results:
        project_name = result['project']
        status = "✓ 成功" if result['result']['success'] else "✗ 失败"
        detected_lang = result['result'].get('detected_language', 'N/A')
        print(f"\n{project_name}: {status}")
        print(f"  检测到的语言: {detected_lang}")
        if result['result']['error']:
            print(f"  错误信息: {result['result']['error'][:100]}...")

    return results


def main(filter):
    """主函数"""
    print("开始扫描 tests 目录...")
    print("确保后端服务正在运行: python server.py")
    print()

    try:
        results = scan_and_test_tests_directory(filter)

        # 保存结果到文件
        if results:
            with open('test_results.json', 'w', encoding='utf-8') as f:
                json.dump(results, f, indent=2, ensure_ascii=False)
            print(f"\n测试结果已保存到 test_results.json")

    except KeyboardInterrupt:
        print("\n测试被中断")
    except Exception as e:
        print(f"测试过程中出错: {e}")


if __name__ == "__main__":

    parser = argparse.ArgumentParser(description="A script to test codes from different languages")
    parser.add_argument("--project", type=str, help="项目名称")
    args = parser.parse_args()

    filter = lambda x:  args.project == x if args.project else True
    main(filter)