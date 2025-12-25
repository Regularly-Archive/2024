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


def parse_project_info(project_path: str) -> Dict:
    """解析项目信息，确定项目类型和依赖"""
    project_type = "unknown"
    info = {
        'name': os.path.basename(project_path),
        'type': 'unknown',
        'dependencies': [],
        'entry_point': None
    }

    file_list = []
    for root, dirs, files in os.walk(project_path):
        for file in files:
            if not file.startswith('.'):
                file_list.append(os.path.join(root, file))

    # 检查项目类型
    if any(f.endswith('.csproj') for f in file_list):
        info['type'] = 'csharp_project'
        info['entry_point'] = next((f for f in file_list if f.endswith('Program.cs')), None)
    elif any(f.endswith('.csx') for f in file_list):
        info['type'] = 'csharp_script'
        info['entry_point'] = next((f for f in file_list if f.endswith('.csx')), None)
    elif 'pom.xml' in [os.path.basename(f) for f in file_list]:
        info['type'] = 'java_maven'
        info['entry_point'] = next((f for f in file_list if f.endswith('Main.java')), None)
    elif any(f.endswith('.java') for f in file_list):
        # 检测是否是 Jbang 格式的单文件
        jbang_file = next((f for f in file_list if f.endswith('Main.java') and is_jbang_file(f)), None)
        if jbang_file:
            info['type'] = 'java_jbang'
            info['entry_point'] = jbang_file
        else:
            info['type'] = 'java_sfa'
            info['entry_point'] = next((f for f in file_list if f.endswith('Main.java')), None)
    elif 'package.json' in [os.path.basename(f) for f in file_list]:
        info['type'] = 'typescript'
        info['entry_point'] = next((f for f in file_list if f.endswith('index.ts')), None)
    elif any(f.endswith('.py') for f in file_list):
        info['type'] = 'python'
        info['entry_point'] = next((f for f in file_list if f.endswith('main.py')), None)
    elif any(f.endswith('.go') for f in file_list):
        if 'go.mod' in [os.path.basename(f) for f in file_list]:
            info['type'] = 'go_module'
        else:
            info['type'] = 'go_sfa'
        # 查找主入口文件
        if 'go.mod' in [os.path.basename(f) for f in file_list]:
            # 有 go.mod 的项目，通常在根目录有 main.go
            info['entry_point'] = next((f for f in file_list if f.endswith('/main.go')), None)
        else:
            # 单文件项目
            info['entry_point'] = next((f for f in file_list if f.endswith('.go')), None)
    elif any(f.endswith('.cpp') or f.endswith('.c') for f in file_list):
        info['type'] = 'cpp'
        info['entry_point'] = next((f for f in file_list
                                  if f.endswith('main.cpp') or f.endswith('main.c')), None)

    return info


def test_project(project_path: str, run_command: Optional[str] = None, data: Optional[dict] = None) -> Dict:
    """测试单个项目"""
    print(f"\n=== 测试项目: {os.path.basename(project_path)} ===")

    # 解析项目信息
    project_info = parse_project_info(project_path)
    print(f"项目类型: {project_info['type']}")
    print(f"入口文件: {os.path.basename(project_info['entry_point']) if project_info['entry_point'] else 'N/A'}")

    # 创建压缩包
    zip_path = create_project_archive(project_path)

    try:
        # 发送到后端测试
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': (f'{project_info["name"]}.zip', zip_file, 'application/zip')
            }

            request_data = data.copy() if data else {}
            if run_command:
                request_data['run_command'] = run_command

            response = requests.post(url, files=files, data=request_data)

            print(f"状态码: {response.status_code}")

            if response.status_code == 200:
                result = response.json()
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


def scan_and_test_tests_directory():
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
        # 根据项目类型确定特殊参数和运行命令
        run_command = None
        data = {}

        # 先解析项目信息
        project_info = parse_project_info(str(project_dir))
        project_type = project_info.get('type', '')

        if project_type == 'go_module':
            # Go Module 项目: 下载依赖并运行
            run_command = 'go run .'
            data['language'] = 'go'
        elif project_type == 'go_sfa':
            # Go 单文件: 直接运行
            entry_point = os.path.basename(project_info['entry_point'])
            run_command = f'go run {entry_point}'
            data['language'] = 'go'
        elif project_type == 'typescript':
            run_command = 'npm install && npm start'
        elif project_dir.name.startswith('java_pom'):
            run_command = 'mvn compile exec:java'
        elif project_dir.name.startswith('java_sfa'):
            run_command = 'jbang Main.java'
        elif 'java_jbang' in parse_project_info(str(project_dir)).get('type', ''):
            # 使用 jbang 运行
            project_info = parse_project_info(str(project_dir))
            entry_file = os.path.basename(project_info['entry_point'])
            run_command = f'jbang {entry_file}'

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


def main():
    """主函数"""
    print("开始扫描 tests 目录...")
    print("确保后端服务正在运行: python server.py")
    print()

    try:
        results = scan_and_test_tests_directory()

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
    main()