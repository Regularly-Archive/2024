#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Bash API Comprehensive Test Suite
Tests: POST /api/project/run-bash
Automatically runs all tests in bash_tests directory
"""
import sys
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import requests
import tarfile
import tempfile
import os
import json
from pathlib import Path
import io, zipfile

BASE_URL = "http://localhost:8001"

class BashAPITester:
    def __init__(self):
        self.base_url = BASE_URL
        self.test_results = []
        self.temp_dir = tempfile.mkdtemp()
        self.test_base_path = Path(__file__).parent / "bash_tests"

    def create_archive_from_directory(self, project_dir):
        """Create archive containing all files from directory (with LF line endings)"""
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

                    # Read file and convert CRLF to LF for bash scripts
                    with open(file_path, 'rb') as f:
                        content = f.read()

                    if file.endswith('.sh'):
                        # Convert Windows line endings to Unix
                        content = content.replace(b'\r\n', b'\n')

                    # Write to zip
                    zipf.writestr(arc_path, content)
        return zip_path

    def upload_and_test(self, archive_path, main_script=None, arguments=None):
        """Upload and run bash script"""
        with open(archive_path, 'rb') as f:
            files = {'archive_file': f}
            data = {}
            if main_script:
                data['main_script'] = main_script
            if arguments:
                data['arguments'] = arguments

            return requests.post(f"{self.base_url}/api/project/run-bash", files=files, data=data)

    def run_directory_tests(self, test_dir):
        """Run all test cases in a single test directory"""
        # Read manifest file
        manifest_path = test_dir / ".manifest"
        if not manifest_path.exists():
            print(f"[WARN] Test directory {test_dir.name} missing .manifest file, skipping")
            return

        with open(manifest_path, 'r', encoding='utf-8') as f:
            manifest = json.load(f)

        print(f"\n=== {manifest['description']} ===")

        # Create archive
        archive_path = self.create_archive_from_directory(test_dir)

        # Execute each test case
        for test_case in manifest['tests']:
            print(f"\n[CASE] {test_case['name']}:")

            response = self.upload_and_test(
                archive_path,
                main_script=test_case['mainScript'],
                arguments=test_case['arguments']
            )

            print(f"Status Code: {response.status_code}")
            if test_case['arguments']:
                print(f"Arguments: {test_case['arguments']}")

            if response.status_code == 200:
                result = response.json()
                print("[PASS] Executed successfully")
                print(f"Output:\n{result['output']}")
                if 'duration' in result:
                    print(f"Duration: {result['duration']:.3f}s")

                # Special validation logic
                if test_dir.name == "multi_module_project":
                    if "Bash API Test" in result['output']:
                        print("[PASS] Project info loaded correctly")
                    if "Module 1" in result['output']:
                        print("[PASS] Module 1 executed successfully")
                    if "Module 2" in result['output']:
                        print("[PASS] Module 2 executed successfully")
            else:
                print(f"[FAIL] Error: {response.text}")
                # For error tests, status code 400 is expected
                if test_dir.name == "error_cases" and response.status_code == 400:
                    print("[PASS] Error handled correctly")

            print("-" * 60)

    def run_all_tests(self):
        """Run all tests in all test directories"""
        print("=== Bash API Test Suite ===\n")

        # Check service status
        try:
            health_response = requests.get(f"{self.base_url}/docs")
            if health_response.status_code == 200:
                print("[PASS] Service is running\n")
            else:
                print("[WARN] Service may have issues\n")
        except:
            print("[FAIL] Cannot connect to service. Make sure server is running on :8001")
            return

        # Iterate through test directories
        test_dirs = [d for d in self.test_base_path.iterdir() if d.is_dir()]
        test_dirs.sort(key=lambda x: x.name)

        for test_dir in test_dirs:
            self.run_directory_tests(test_dir)

        # Cleanup
        import shutil
        shutil.rmtree(self.temp_dir)

        print("\n=== All Bash API tests completed ===\n")

if __name__ == "__main__":
    tester = BashAPITester()
    tester.run_all_tests()
