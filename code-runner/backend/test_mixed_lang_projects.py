#!/usr/bin/env python3
"""
测试多种语言的压缩包项目
"""

import requests
import zipfile
import tempfile
import os
import json


def test_go_project():
    """测试 Go 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'go_project.zip')

    try:
        go_mod = '''module example_project

go 1.19
'''

        main_go = '''package main

import "fmt"

func main() {
    fmt.Println("Hello from Go project!")
    fmt.Println("Project type: Go module")
    fmt.Println("Detected entry: main.go")
}
'''

        # 创建文件
        with open(os.path.join(temp_dir, 'go.mod'), 'w') as f:
            f.write(go_mod)

        with open(os.path.join(temp_dir, 'main.go'), 'w') as f:
            f.write(main_go)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'go.mod'), 'go.mod')
            zipf.write(os.path.join(temp_dir, 'main.go'), 'main.go')

        print("=== 测试 Go 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('go_project.zip', zip_file, 'application/zip')
            }
            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['go.mod', 'main.go', 'go_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_cpp_project():
    """测试 C++ 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'cpp_project.zip')

    try:
        main_cpp = '''#include <iostream>
#include <string>

int main() {
    std::cout << "Hello from C++ project!" << std::endl;
    std::cout << "Detected language: C++" << std::endl;

    std::string name = "World";
    std::cout << "Hello, " << name << "!" << std::endl;

    return 0;
}
'''

        # 创建项目文件
        with open(os.path.join(temp_dir, 'main.cpp'), 'w') as f:
            f.write(main_cpp)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'main.cpp'), 'main.cpp')

        print("=== 测试 C++ 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('cpp_project.zip', zip_file, 'application/zip')
            }
            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"编译输出: {result.get('build_output')}")
                print(f"运行输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['main.cpp', 'cpp_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_csharp_project():
    """测试 C# 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'csharp_project.zip')

    try:
        program_cs = '''using System;

namespace TestProject
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello from C# project!");
            Console.WriteLine("Project type: C#");
            Console.WriteLine("Entry point: Program.cs");

            // 简单的计算示例
            int a = 5;
            int b = 3;
            int sum = a + b;
            Console.WriteLine($"Calculation: {a} + {b} = {sum}");
        }
    }
}
'''

        # 创建文件
        with open(os.path.join(temp_dir, 'Program.cs'), 'w') as f:
            f.write(program_cs)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'Program.cs'), 'Program.cs')

        print("=== 测试 C# 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        # 先测试自动检测
        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('csharp_project.zip', zip_file, 'application/zip')
            }

            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['Program.cs', 'csharp_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_multifile_cpp_project():
    """测试多文件 C++ 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'mulifile_cpp.zip')

    try:
        main_cpp = '''#include "utils.h"

int main() {
    sayHello("C++ project with header");
    printNumbers(5);

    return 0;
}
'''

        utils_h = '''#include <iostream>
#include <string>

void sayHello(const std::string& name) {
    std::cout << "Hello, " << name << "!" << std::endl;
}

void printNumbers(int n) {
    for(int i = 0; i < n; i++) {
        std::cout << i << " ";
    }
    std::cout << std::endl;
}
'''

        # 创建项目文件
        with open(os.path.join(temp_dir, 'main.cpp'), 'w') as f:
            f.write(main_cpp)

        with open(os.path.join(temp_dir, 'utils.h'), 'w') as f:
            f.write(utils_h)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'main.cpp'), 'main.cpp')
            zipf.write(os.path.join(temp_dir, 'utils.h'), 'utils.h')

        print("=== 测试多文件 C++ 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('mulifile_cpp.zip', zip_file, 'application/zip')
            }
            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"编译输出: {result.get('build_output')}")
                print(f"运行输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['main.cpp', 'utils.h', 'mulifile_cpp.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_javascript_project():
    """测试 JavaScript 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'js_project.zip')

    try:
        package_json = '''{
  "name": "test-js-project",
  "version": "1.0.0",
  "description": "A simple JavaScript test project",
  "main": "index.js",
  "scripts": {
    "start": "node index.js"
  }
}
'''

        index_js = '''console.log("Hello from JavaScript project!");
console.log("Project type: Node.js");
console.log("Entry point: index.js");

// 简单的计算示例
const a = 10;
const b = 20;
const sum = a + b;
console.log(`Calculation: ${a} + ${b} = ${sum}`);

// 数组操作
const fruits = ['apple', 'banana', 'orange'];
console.log('Fruits list:');
fruits.forEach((fruit, index) => {
    console.log(`${index + 1}. ${fruit}`);
});
'''

        # 创建文件
        with open(os.path.join(temp_dir, 'package.json'), 'w') as f:
            f.write(package_json)

        with open(os.path.join(temp_dir, 'index.js'), 'w') as f:
            f.write(index_js)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'package.json'), 'package.json')
            zipf.write(os.path.join(temp_dir, 'index.js'), 'index.js')

        print("=== 测试 JavaScript 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('js_project.zip', zip_file, 'application/zip')
            }
            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['package.json', 'index.js', 'js_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


def test_typescript_project():
    """测试 TypeScript 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'ts_project.zip')

    try:
        package_json = '''{
  "name": "test-ts-project",
  "version": "1.0.0",
  "description": "A simple TypeScript test project",
  "main": "dist/index.js",
  "scripts": {
    "build": "tsc",
    "start": "npm run build && node dist/index.js"
  },
  "devDependencies": {
    "typescript": "^4.0.0"
  }
}
'''

        tsconfig_json = '''{
  "compilerOptions": {
    "target": "ES2018",
    "module": "commonjs",
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules"]
}
'''

        index_ts = '''console.log("Hello from TypeScript project!");
console.log("Project type: TypeScript");
console.log("Entry point: src/index.ts");

interface Person {
    name: string;
    age: number;
}

const user: Person = {
    name: "Alice",
    age: 25
};

console.log(`User info: ${user.name}, ${user.age} years old`);

// 类型安全的计算
function add(a: number, b: number): number {
    return a + b;
}

const result = add(15, 25);
console.log(`Calculation: 15 + 25 = ${result}`);
'''

        # 创建文件
        os.makedirs(os.path.join(temp_dir, 'src'))

        with open(os.path.join(temp_dir, 'package.json'), 'w') as f:
            f.write(package_json)

        with open(os.path.join(temp_dir, 'tsconfig.json'), 'w') as f:
            f.write(tsconfig_json)

        with open(os.path.join(temp_dir, 'src', 'index.ts'), 'w') as f:
            f.write(index_ts)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'package.json'), 'package.json')
            zipf.write(os.path.join(temp_dir, 'tsconfig.json'), 'tsconfig.json')
            zipf.write(os.path.join(temp_dir, 'src', 'index.ts'), 'src/index.ts')

        print("=== 测试 TypeScript 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        # 由于 TypeScript 需要编译，我们直接提供运行命令
        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('ts_project.zip', zip_file, 'application/zip')
            }
            data = {
                'language': 'typescript',
                'run_command': 'npm start'
            }
            response = requests.post(url, files=files, data=data)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"编译输出: {result.get('build_output')}")
                print(f"运行输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['package.json', 'tsconfig.json', 'ts_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        src_path = os.path.join(temp_dir, 'src')
        if os.path.exists(src_path):
            os.rmdir(src_path)
        os.rmdir(temp_dir)


def test_java_project():
    """测试 Java 项目"""
    temp_dir = tempfile.mkdtemp()
    zip_path = os.path.join(temp_dir, 'java_project.zip')

    try:
        main_java = '''public class Main {
    public static void main(String[] args) {
        System.out.println("Hello from Java project!");
        System.out.println("Project type: Java");
        System.out.println("Entry point: Main.java");

        int a = 100;
        int b = 200;
        int sum = a + b;
        System.out.println("Calculation: " + a + " + " + b + " = " + sum);

        String[] fruits = {"apple", "banana", "orange"};
        System.out.println("Fruits list:");
        for (int i = 0; i < fruits.length; i++) {
            System.out.println((i + 1) + ". " + fruits[i]);
        }

        System.out.println("Using Java 8+ features:");
        java.util.Arrays.stream(fruits)
            .map(String::toUpperCase)
            .forEach(System.out::println);
    }
}
'''

        # 创建文件
        with open(os.path.join(temp_dir, 'Main.java'), 'w') as f:
            f.write(main_java)

        # 创建ZIP
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            zipf.write(os.path.join(temp_dir, 'Main.java'), 'Main.java')

        print("=== 测试 Java 项目 ===")
        url = "http://localhost:8001/api/project/run-archive"

        with open(zip_path, 'rb') as zip_file:
            files = {
                'archive_file': ('java_project.zip', zip_file, 'application/zip')
            }
            response = requests.post(url, files=files)
            print(f"状态码: {response.status_code}")
            if response.status_code == 200:
                result = response.json()
                print(f"检测到的语言: {result.get('detected_language')}")
                print(f"检测到的入口点: {result.get('detected_entry_point')}")
                print(f"输出:\n{result.get('output')}")
            else:
                print(f"错误: {response.text}")

    finally:
        for file in ['Main.java', 'java_project.zip']:
            path = os.path.join(temp_dir, file)
            if os.path.exists(path):
                os.unlink(path)
        os.rmdir(temp_dir)


if __name__ == "__main__":
    print("开始测试多语言项目...")
    print("确保后端服务正在运行: python server.py")
    print()

    try:
        #test_go_project()
        print("\n" + "="*50 + "\n")
        #test_cpp_project()
        print("\n" + "="*50 + "\n")
        test_csharp_project()
        print("\n" + "="*50 + "\n")
        #test_javascript_project()
        print("\n" + "="*50 + "\n")
        #test_typescript_project()
        print("\n" + "="*50 + "\n")
        test_java_project()
        print("\n" + "="*50 + "\n")
        test_multifile_cpp_project()
    except Exception as e:
        print(f"测试过程中出错: {e}")

    print("\n多语言测试完成!")

    # 打印当前支持的语言配置
    print("\n当前支持的语言配置检查:")
    from config import LANGUAGE_CONFIG
    print(f"支持的语言: {list(LANGUAGE_CONFIG.keys())}")