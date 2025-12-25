#!/usr/bin/env python3

import requests
import zipfile
import tempfile
import os
import traceback


def create_simple_test():
    """Create a simple Python zip test"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'simple.zip')

    try:
        # 创建简单的Python文件
        with open(os.path.join(temp_dir, 'main.py'), 'w') as f:
            f.write('print("Hello World!")')

        # 创建zip文件
        with zipfile.ZipFile(zip_path, 'w') as zf:
            zf.write(os.path.join(temp_dir, 'main.py'), 'main.py')

        return zip_path
    except Exception as e:
        print(f"Error creating zip: {e}")
        return None


def test_archive():
    """Test archive upload with error catching"""
    zip_path = create_simple_test()
    if not zip_path:
        return

    try:
        with open(zip_path, 'rb') as f:
            files = {'archive_file': ('simple.zip', f, 'application/zip')}
            data = {'language': 'python3', 'entry_point': 'main.py'}

            response = requests.post(
                'http://localhost:8004/api/project/run-archive',
                files=files,
                data=data,
                timeout=30
            )

        print(f"Status: {response.status_code}")
        if response.status_code == 200:
            print("Success!")
            print(f"Response: {response.json()}")
        else:
            print(f"Error: {response.text}")
    except requests.exceptions.Timeout:
        print("Request timed out")
    except requests.exceptions.ConnectionError:
        print("Cannot connect to server - make sure it's running")
    except Exception as e:
        print(f"Error during request: {e}")
        traceback.print_exc()
    finally:
        # Clean up
        try:
            os.unlink(zip_path)
            os.unlink(zip_path.replace('.zip', 'main.py'))
        except:
            pass


if __name__ == "__main__":
    print("Testing archive upload...")
    print("Make sure server.py is running")
    print()

    test_archive()