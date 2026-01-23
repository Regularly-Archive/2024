#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Project Archive Test Suite
Tests: POST /api/project/run-archive
Automatically scans and tests multi-language projects
"""
import sys
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import os
import json
import requests
import zipfile
import tempfile
from pathlib import Path
from typing import Dict, Optional, Callable
import argparse

API_URL = "http://localhost:8001/api/project/run-archive"
BASE_URL = "http://localhost:8001"


class ProjectTester:
    def __init__(self):
        self.results = []

    def check_service(self) -> bool:
        """Check if service is running"""
        try:
            response = requests.get(f"{BASE_URL}/docs", timeout=5)
            if response.status_code == 200:
                print("[PASS] Service is running")
                return True
        except:
            print("[FAIL] Cannot connect to service")
            return False

    def create_project_archive(self, project_dir: Path) -> Path:
        """Create zip archive from project directory (with LF line endings)"""
        temp_file = tempfile.NamedTemporaryFile(suffix=".zip", delete=False)
        zip_path = Path(temp_file.name)

        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for root, dirs, files in os.walk(project_dir):
                # Exclude hidden files and directories
                files = [f for f in files if not f.startswith('.')]
                dirs[:] = [d for d in dirs if not d.startswith('.')]

                for file in files:
                    file_path = os.path.join(root, file)
                    arc_path = os.path.relpath(file_path, project_dir)

                    # Read and convert line endings for text files
                    with open(file_path, 'rb') as f:
                        content = f.read()

                    if file.endswith(('.py', '.js', '.ts', '.java', '.cs', '.go',
                                      '.rs', '.cpp', '.c', '.h', '.sh', '.json',
                                      '.xml', '.yaml', '.yml', '.txt', '.md')):
                        # Convert CRLF to LF
                        content = content.replace(b'\r\n', b'\n')

                    zipf.writestr(arc_path, content)

        return zip_path

    def download_artifact(self, url: str) -> tuple:
        """Download artifact file"""
        try:
            response = requests.get(url, timeout=30)
            return response.status_code, response.content
        except:
            return 0, b''

    def test_project(self, project_path: Path, run_command: Optional[str] = None) -> Dict:
        """Test a single project"""
        print(f"\n[TEST] {project_path.name}")

        manifest_file = project_path / ".manifest"
        if not manifest_file.exists():
            print(f"[FAIL] Missing .manifest file")
            return {'success': False, 'error': 'Missing .manifest', 'output': ''}

        with manifest_file.open('r', encoding='utf-8') as f:
            manifest = json.load(f)

        print(f"  Language: {manifest.get('language', 'N/A')}")
        print(f"  Form: {manifest.get('project_form', 'N/A')}")
        print(f"  Description: {manifest.get('description', 'N/A')}")

        # Create archive
        zip_path = self.create_project_archive(project_path)

        try:
            request_data = {}
            if run_command:
                request_data['run_command'] = run_command

            with zip_path.open('rb') as zip_file:
                files = {'archive_file': (f"{project_path.name}.zip", zip_file, 'application/zip')}
                response = requests.post(API_URL, files=files, data=request_data, timeout=120)

            if response.ok:
                res = response.json()
                result = res.get('result', {})
                runtime = res.get('runtime', {})

                print(f"  Status: HTTP {response.status_code}")
                print(f"  Duration: {result.get('duration', 0):.3f}s")
                print(f"  Detected Language: {runtime.get('language', 'N/A')}")
                print(f"  Entry Point: {runtime.get('entry_point', 'N/A')}")

                output = result.get('output', '')
                artifacts = result.get('artifacts', [])

                # Print output (truncated if too long)
                if output:
                    output_preview = output[:500] + '...' if len(output) > 500 else output
                    print(f"  Output:\n{output_preview}")

                # Print artifacts
                if artifacts:
                    print(f"  Artifacts ({len(artifacts)}):")
                    for artifact in artifacts:
                        print(f"    - {artifact['name']} ({artifact['size']} bytes)")

                return {
                    'success': True,
                    'detected_language': runtime.get('language'),
                    'entry_point': runtime.get('entry_point'),
                    'output': output,
                    'artifacts': artifacts,
                    'duration': result.get('duration', 0),
                    'error': None
                }
            else:
                print(f"  Error: HTTP {response.status_code}")
                return {'success': False, 'error': response.text[:200], 'output': ''}

        except requests.RequestException as e:
            print(f"  Request Error: {e}")
            return {'success': False, 'error': str(e), 'output': ''}
        finally:
            zip_path.unlink(missing_ok=True)

    def run_all_tests(self, filter_fn: Callable[[str], bool] = None) -> list:
        """Run all project tests"""
        print("=" * 60)
        print("Project Archive Test Suite")
        print("=" * 60)

        if not self.check_service():
            return []

        tests_dir = Path(__file__).parent / "project_tests"
        if not tests_dir.exists():
            print(f"[FAIL] Directory not found: {tests_dir}")
            return []

        project_dirs = [d for d in tests_dir.iterdir() if d.is_dir()]
        print(f"\nFound {len(project_dirs)} project(s)\n{'-' * 60}")

        filter_fn = filter_fn or (lambda name: True)
        self.results = []

        for project_dir in sorted(project_dirs):
            if not filter_fn(project_dir.name):
                continue

            result = self.test_project(project_dir)
            self.results.append({'project': project_dir.name, 'result': result})
            print("-" * 60)

        # Summary
        passed = sum(1 for r in self.results if r['result']['success'])
        failed = len(self.results) - passed

        print(f"\n{'=' * 60}")
        print(f"Results: {passed} passed, {failed} failed out of {len(self.results)} total")
        print("=" * 60)

        # Detailed summary
        for r in self.results:
            status = "[PASS]" if r['result']['success'] else "[FAIL]"
            print(f"{status} {r['project']}")
            if r['result'].get('detected_language'):
                print(f"       Language: {r['result']['detected_language']}")
            if r['result'].get('error'):
                error = r['result']['error'][:100]
                print(f"       Error: {error}")

        return self.results

    def save_results(self, filename: str = "test_results.json"):
        """Save test results to JSON"""
        with open(filename, 'w', encoding='utf-8') as f:
            json.dump(self.results, f, indent=2, ensure_ascii=False)
        print(f"\nResults saved to {filename}")


def main():
    parser = argparse.ArgumentParser(description="Test multi-language projects")
    parser.add_argument("--project", type=str, help="Test specific project only")
    parser.add_argument("--save", type=str, default="test_results.json",
                        help="Save results to file (default: test_results.json)")
    args = parser.parse_args()

    filter_fn = lambda name: name == args.project if args.project else lambda _: True

    tester = ProjectTester()
    results = tester.run_all_tests(filter_fn)

    if results:
        tester.save_results(args.save)


if __name__ == "__main__":
    main()
