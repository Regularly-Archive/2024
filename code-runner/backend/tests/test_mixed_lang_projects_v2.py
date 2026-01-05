#!/usr/bin/env python3
"""
改进版：测试多语言项目
自动扫描 project_tests 目录中的不同项目结构，生成压缩包并测试
"""
import os
import json
import requests
import zipfile
import tempfile
from pathlib import Path
from typing import Dict, Optional, Callable
import argparse


def create_project_archive(project_dir: str) -> str: 
    """将项目目录打包成 zip 文件""" 
    temp_file = tempfile.NamedTemporaryFile(suffix=".zip", delete=False)
    zip_path = Path(temp_file.name)
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf: 
        for root, dirs, files in os.walk(project_dir): 
            # 排除隐藏文件和目录 
            files = [f for f in files if not f.startswith('.')] 
            dirs[:] = [d for d in dirs if not d.startswith('.')] 
            for file in files: 
                file_path = os.path.join(root, file) 
                arc_path = os.path.relpath(file_path, project_dir) 
                zipf.write(file_path, arc_path) 

    return zip_path


def print_project_info(manifest: dict) -> None:
    """打印项目信息"""
    print(f"项目语言: {manifest.get('language', 'N/A')}")
    print(f"项目形式: {manifest.get('project_form', 'N/A')}")
    print(f"项目描述: {manifest.get('description', 'N/A')}")


def test_project(project_path: Path, run_command: Optional[str] = None, data: Optional[dict] = None) -> Dict:
    """测试单个项目"""
    print(f"\n***** 测试项目: {project_path.name} *****")

    manifest_file = project_path / ".manifest"
    if not manifest_file.exists():
        print(f"缺少 .manifest 文件: {manifest_file}")
        return {'success': False, 'error': '.manifest 文件缺失', 'output': ''}

    with manifest_file.open('r', encoding='utf-8') as f:
        manifest = json.load(f)

    print_project_info(manifest)

    zip_path = create_project_archive(project_path)

    try:
        url = "http://localhost:8001/api/project/run-archive"
        request_data = data.copy() if data else {}
        if run_command:
            request_data['run_command'] = run_command

        with zip_path.open('rb') as zip_file:
            files = {'archive_file': (f"{project_path.name}.zip", zip_file, 'application/zip')}
            response = requests.post(url, files=files, data=request_data, timeout=60)

        if response.ok:
            result = response.json()
            print(f"状态码: {response.status_code}")
            print(f"耗时: {result.get('duration')}")
            print(f"检测到的语言: {result.get('detected_language')}")
            print(f"检测到的入口点: {result.get('detected_entry_point')}")
            if result.get('build_output'):
                print(f"编译输出:\n{result.get('build_output')}")
            print(f"运行输出:\n{result.get('output', '')}")

            return {
                'success': True,
                'detected_language': result.get('detected_language'),
                'output': result.get('output', ''),
                'error': None
            }
        else:
            print(f"错误: {response.text}")
            return {'success': False, 'error': response.text, 'output': ''}

    except requests.RequestException as e:
        print(f"请求错误: {e}")
        return {'success': False, 'error': str(e), 'output': ''}

    finally:
        zip_path.unlink(missing_ok=True)


def scan_and_test_tests_directory(filter_fn: Callable[[str], bool]) -> list[dict]:
    """扫描 project_tests 目录并测试所有项目"""
    tests_dir = Path("project_tests")
    if not tests_dir.exists():
        print(f"目录不存在: {tests_dir}")
        return []

    results = []
    project_dirs = [d for d in tests_dir.iterdir() if d.is_dir()]
    print(f"发现 {len(project_dirs)} 个项目目录\n{'='*60}")

    for project_dir in sorted(project_dirs):
        if not filter_fn(project_dir.name):
            continue

        run_command, data = None, {}
        result = test_project(project_dir, run_command, data)
        results.append({'project': project_dir.name, 'result': result})
        print("=" * 60)

    # 汇总报告
    success_count = sum(1 for r in results if r['result']['success'])
    fail_count = len(results) - success_count

    print("\n=== 测试汇总报告 ===")
    print(f"总计项目数: {len(results)} 成功: {success_count} 失败: {fail_count}")

    for r in results:
        status = "✓ 成功" if r['result']['success'] else "✗ 失败"
        print(f"\n{r['project']}: {status}")
        print(f"  检测到的语言: {r['result'].get('detected_language', 'N/A')}")
        if r['result'].get('error'):
            print(f"  错误信息: {r['result']['error'][:100]}...")

    return results


def main(filter_fn: Callable[[str], bool]):
    print("开始扫描 project_tests 目录...")
    print("确保后端服务正在运行: python server.py\n")
    try:
        results = scan_and_test_tests_directory(filter_fn)
        if results:
            with open('test_results.json', 'w', encoding='utf-8') as f:
                json.dump(results, f, indent=2, ensure_ascii=False)
            print(f"\n测试结果已保存到 test_results.json")
    except KeyboardInterrupt:
        print("\n测试被中断")
    except Exception as e:
        print(f"测试过程中出错: {e}")
        import traceback
        print(traceback.print_exc())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="测试多语言项目")
    parser.add_argument("--project", type=str, help="指定项目名称")
    args = parser.parse_args()

    filter_fn = lambda name: name == args.project if args.project else True
    main(filter_fn)
