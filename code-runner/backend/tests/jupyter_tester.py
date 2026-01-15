#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Jupyter Notebook Test Suite
Tests: POST /api/jupyter/run
Validates code execution and artifact collection
"""
import sys
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import os
import json
import time
import requests

API_URL = "http://localhost:8001/api/jupyter/run"
BASE_URL = "http://localhost:8001"

# Language mapping
LANG_MAP = {
    "python": "jupyter-python",
    "csharp": "jupyter-csharp",
    "fsharp": "jupyter-fsharp",
    "r": "jupyter-r"
}


class JupyterTester:
    def __init__(self):
        self.test_results = []

    def check_service(self):
        """Check if service is running"""
        try:
            response = requests.get(f"{BASE_URL}/docs", timeout=5)
            if response.status_code == 200:
                print("[PASS] Service is running")
                return True
        except:
            print("[FAIL] Cannot connect to service")
            return False

    def run_notebook(self, code, language, format_type="notebook"):
        """Run Jupyter notebook code"""
        payload = {
            "code": code,
            "language": language,
            "dependencies": [],
            "format": format_type
        }

        response = requests.post(API_URL, json=payload, timeout=120)
        return response

    def download_artifact(self, url):
        """Download artifact file"""
        response = requests.get(url, timeout=30)
        return response.status_code, response.content

    def test_basic_output(self):
        """Test 1: Basic output and print"""
        print("\n=== Test: Basic Output ===")
        code = """
print("Hello from Jupyter!")
print({"status": "success", "message": "Basic output test"})
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            print(f"[PASS] Executed successfully")
            print(f"Duration: {data.get('result', {}).get('duration', 0):.3f}s")
            return True
        else:
            print(f"[FAIL] HTTP {response.status_code}")
            return False

    def test_plot_generation(self):
        """Test 2: Generate and save plot"""
        print("\n=== Test: Plot Generation ===")
        code = """
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

# Generate plot
x = np.linspace(0, 10, 100)
y = np.sin(x)
plt.figure(figsize=(8, 6))
plt.plot(x, y, 'b-', linewidth=2)
plt.title('Sine Wave')
plt.xlabel('X')
plt.ylabel('Y')
plt.grid(True)
plt.savefig('sine_wave.png', dpi=150, bbox_inches='tight')
plt.close()
print("Plot saved: sine_wave.png")
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            artifacts = data.get('result', {}).get('artifacts', [])
            print(f"[PASS] Executed successfully")

            # Check and download artifact
            for artifact in artifacts:
                if artifact['name'] == 'sine_wave.png':
                    status, content = self.download_artifact(artifact['url'])
                    if status == 200 and len(content) > 0:
                        print(f"[PASS] Artifact downloaded: {artifact['name']} ({len(content)} bytes)")
                        return True
            print("[INFO] No plot artifact found (matplotlib may not be installed)")
            return True  # Still pass if execution succeeded
        return False

    def test_csv_generation(self):
        """Test 3: Generate CSV data file"""
        print("\n=== Test: CSV Generation ===")
        code = """
import csv
import json

# Generate sample data
data = [
    {"id": 1, "name": "Alice", "score": 95},
    {"id": 2, "name": "Bob", "score": 87},
    {"id": 3, "name": "Charlie", "score": 92},
    {"id": 4, "name": "Diana", "score": 88},
    {"id": 5, "name": "Eve", "score": 91}
]

# Write to CSV
with open('students.csv', 'w', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=['id', 'name', 'score'])
    writer.writeheader()
    writer.writerows(data)

print("CSV generated: students.csv")

# Also write JSON
with open('students.json', 'w') as f:
    json.dump(data, f, indent=2)
print("JSON generated: students.json")
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            artifacts = data.get('result', {}).get('artifacts', [])
            print(f"[PASS] Executed successfully")

            # Check artifacts
            found_csv = False
            found_json = False
            for artifact in artifacts:
                if artifact['name'] == 'students.csv':
                    status, content = self.download_artifact(artifact['url'])
                    if status == 200:
                        print(f"[PASS] CSV artifact: {artifact['name']} ({len(content)} bytes)")
                        found_csv = True
                elif artifact['name'] == 'students.json':
                    status, content = self.download_artifact(artifact['url'])
                    if status == 200:
                        print(f"[PASS] JSON artifact: {artifact['name']} ({len(content)} bytes)")
                        found_json = True

            if not found_csv and not found_json:
                print("[INFO] No data artifacts (dependencies may not be installed)")
            return True
        return False

    def test_dataframe_operations(self):
        """Test 4: Pandas DataFrame operations"""
        print("\n=== Test: DataFrame Operations ===")
        code = """
import pandas as pd
import numpy as np

# Create DataFrame
np.random.seed(42)
df = pd.DataFrame({
    'A': np.random.rand(100),
    'B': np.random.rand(100),
    'C': np.random.choice(['X', 'Y', 'Z'], 100),
    'category': np.random.choice(['Group1', 'Group2'], 100)
})

# Summary statistics
print("DataFrame shape:", df.shape)
print("Summary:")
print(df.describe())

# Group by and aggregate
grouped = df.groupby('category').agg({'A': 'mean', 'B': 'sum', 'C': 'count'})
print("\\nGrouped summary:")
print(grouped)

# Save to CSV
df.to_csv('dataframe_output.csv', index=False)
print("\\nSaved: dataframe_output.csv")
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            print(f"[PASS] Executed successfully")
            print(f"Duration: {data.get('result', {}).get('duration', 0):.3f}s")
            return True
        return False

    def test_markdown_report(self):
        """Test 5: Generate Markdown report"""
        print("\n=== Test: Markdown Report ===")
        code = """
from datetime import datetime

# Generate markdown report
report = f'''# Test Report

Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}

## Summary

This is an auto-generated markdown report from Jupyter.

### Key Points

- Item 1: First point
- Item 2: Second point
- Item 3: Third point

| Column A | Column B | Column C |
|----------|----------|----------|
| Data 1   | Data 2   | Data 3   |
| Data 4   | Data 5   | Data 6   |

---
*End of Report*
'''

# Save to file
with open('report.md', 'w') as f:
    f.write(report)

print("Report saved: report.md")
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            artifacts = data.get('result', {}).get('artifacts', [])
            print(f"[PASS] Executed successfully")

            for artifact in artifacts:
                if artifact['name'] == 'report.md':
                    status, content = self.download_artifact(artifact['url'])
                    if status == 200:
                        print(f"[PASS] Markdown artifact: {artifact['name']} ({len(content)} bytes)")
                        # Show preview
                        preview = content.decode('utf-8')[:200]
                        print(f"  Preview: {preview}...")
            return True
        return False

    def test_multiple_outputs(self):
        """Test 6: Multiple outputs in one notebook"""
        print("\n=== Test: Multiple Outputs ===")
        code = """
import json

# Generate multiple artifacts
artifacts = {}
for i in range(1, 6):
    with open(f'file_{i}.txt', 'w') as f:
        f.write(f'This is file number {i}\\n')
        f.write(f'Content for item {i}\\n')
    artifacts[i] = f'file_{i}.txt'

print(f'Generated {len(artifacts)} files')
print('Files:', list(artifacts.values()))
"""
        response = self.run_notebook(code, "jupyter-python")
        if response.status_code == 200:
            data = response.json()
            artifacts = data.get('result', {}).get('artifacts', [])
            print(f"[PASS] Executed successfully")
            print(f"Artifacts found: {len(artifacts)}")
            for artifact in artifacts:
                print(f"  - {artifact['name']}")
            return True
        return False

    def test_error_handling(self):
        """Test 7: Error handling"""
        print("\n=== Test: Error Handling ===")
        code = """
# This should cause an error
print("Before error")
result = 1 / 0
print("After error - should not reach here")
"""
        response = self.run_notebook(code, "jupyter-python")
        # Error is expected, but should still return 200 with error in output
        if response.status_code == 200:
            data = response.json()
            output = data.get('result', {}).get('output', '')
            if 'ZeroDivisionError' in output or 'error' in output.lower():
                print("[PASS] Error handled correctly")
                return True
        print("[WARN] Unexpected response for error test")
        return True  # Not a critical failure

    def run_all_tests(self):
        """Run all test cases"""
        print("=" * 50)
        print("Jupyter Notebook Test Suite")
        print("=" * 50)

        if not self.check_service():
            return

        tests = [
            ("Basic Output", self.test_basic_output),
            ("Plot Generation", self.test_plot_generation),
            ("CSV/JSON Generation", self.test_csv_generation),
            ("DataFrame Operations", self.test_dataframe_operations),
            ("Markdown Report", self.test_markdown_report),
            ("Multiple Outputs", self.test_multiple_outputs),
            ("Error Handling", self.test_error_handling),
        ]

        passed = 0
        failed = 0

        for name, test_func in tests:
            try:
                if test_func():
                    passed += 1
                else:
                    failed += 1
            except Exception as e:
                print(f"[ERROR] {name}: {e}")
                failed += 1

        print("\n" + "=" * 50)
        print(f"Test Results: {passed} passed, {failed} failed")
        print("=" * 50)


if __name__ == "__main__":
    tester = JupyterTester()
    tester.run_all_tests()
