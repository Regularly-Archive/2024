<template>
  <div class="flex flex-col h-screen p-5 bg-gray-100">
    <!-- 标题 -->
    <h1 class="text-3xl text-green-600 font-semibold text-center mb-4 drop-shadow-md">
      Code Runner
    </h1>

    <!-- 工具栏 -->
    <div class="flex items-center mb-4 gap-3">
      <label for="language-select" class="font-medium text-gray-700">选择语言:</label>
      <select v-model="selectedLanguage" id="language-select"
        class="border border-gray-300 rounded-lg p-2 shadow-sm focus:ring-1 focus:ring-blue-400 focus:outline-none">
        <option v-for="lang in languageOptions" :key="lang.value" :value="lang.value">
          {{ lang.label }}
        </option>
      </select>

      <button @click="executeCode" :disabled="isLoading"
        class="bg-blue-500 text-white rounded-lg p-2 flex items-center gap-2 shadow hover:bg-blue-600 active:scale-95 transition-transform duration-150">
        <span v-if="isLoading" class="flex items-center gap-2">
          <svg class="animate-spin h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none"
            viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v2a6 6 0 100 12v2a8 8 0 01-8-8z" />
          </svg>
          运行中...
        </span>
        <span v-else>运行代码</span>
      </button>

      <div v-if="executionTime" class="ml-4 text-gray-600">
        本次运行耗时: {{ executionTime }} s
      </div>
    </div>

    <!-- 左右布局 -->
    <div class="flex flex-1 gap-5">
      <!-- 左侧: 代码 + 依赖 -->
      <div class="flex flex-col flex-1 border rounded-lg shadow-sm overflow-hidden bg-white">
        <!-- 代码输入区 -->
        <div class="flex-1 overflow-auto p-3 bg-gray-50 rounded-t-lg">
          <Codemirror v-model:value="codeContent" :options="editorOptions" ref="cmRef"
            class="h-full w-full rounded-lg" />
        </div>

        <!-- 可拖拽分隔条 -->
        <div class="h-2 cursor-row-resize bg-gray-300 hover:bg-gray-400 my-1" @mousedown.prevent="startDrag"></div>

        <!-- 依赖输入区 -->
        <div class="overflow-auto p-3 border-t border-gray-200 bg-gray-100 md:block rounded-b-lg"
          :style="{ height: dependencyHeight + 'px' }">
          <label class="block mb-1 font-medium text-gray-700">依赖项管理：</label>
          <textarea v-model="dependencies" class="w-full h-full border border-gray-300 rounded-lg p-2 resize-none"
            placeholder="每行一个依赖"></textarea>
        </div>
      </div>

      <!-- 右侧输出区 -->
      <div class="flex-1 rounded-lg p-2 overflow-hidden flex flex-col" :class="{
        'border border-gray-300 bg-gray-50': !isNotebook && !showHtml,
        'border border-purple-400 bg-purple-50': showHtml,
        'border border-green-400 bg-green-50': showJupyter
      }">
        <!-- 普通文本输出 -->
        <pre v-if="!isNotebook && !showHtml" class="flex-1 bg-gray-900 text-white p-4 overflow-auto rounded">
{{ executionOutput }}</pre>

        <!-- HTML 输出 -->
        <div v-if="showHtml" class="flex-1 overflow-auto p-2" v-html="executionOutput"></div>

        <!-- Jupyter Notebook 输出 -->
        <RenderJupyterNotebook v-if="showJupyter" class="flex-1 overflow-auto" :notebook="notebook" ref="jupyter" />

        <!-- 文件列表 -->
        <div class="mt-4 border-t pt-2">
          <h3 class="text-lg font-semibold mb-2">文件列表</h3>
          <ul>
            <li v-for="file in files" :key="file.name" class="flex items-center justify-between">
              <span>{{ file.name }}</span>
              <a :href="file.url" download class="text-blue-500 hover:underline">下载</a>
            </li>
          </ul>
        </div>
      </div>
    </div>
  </div>
</template>


<script>
import "codemirror/mode/javascript/javascript.js";
import Codemirror from "codemirror-editor-vue3";
import RenderJupyterNotebook from "render-jupyter-notebook-vue";

export default {
  components: { Codemirror, RenderJupyterNotebook },
  data() {
    return {
      selectedLanguage: "python3",
      codeContent: "",
      executionOutput: "",
      outputType: "",
      languageOptions: this.getLanguageOptions(),
      isLoading: false,
      notebook: { cells: [] },
      executionTime: null,
      editorOptions: { mode: "python3", lineNumbers: true, lineWrapping: true },
      dependencies: "",
      dependencyHeight: 120, // 默认高度
      dragging: false,
      files: []
    };
  },
  watch: {
    selectedLanguage(newLang) {
      this.updateCodeContent(newLang);
      this.dependencies = "";
    }
  },
  mounted() {
    this.updateCodeContent(this.selectedLanguage);
    window.addEventListener("mousemove", this.onDrag);
    window.addEventListener("mouseup", this.stopDrag);
  },
  beforeUnmount() {
    window.removeEventListener("mousemove", this.onDrag);
    window.removeEventListener("mouseup", this.stopDrag);
  },
  computed: {
    showHtml() {
      return this.selectedLanguage.includes("jupyter") && this.outputType === "text/html";
    },
    showJupyter() {
      return this.selectedLanguage.includes("jupyter") && this.outputType === "text/notebook";
    },
    isNotebook() {
      return this.selectedLanguage.includes("jupyter");
    }
  },
  methods: {
    startDrag() { this.dragging = true; },
    onDrag(e) {
      if (this.dragging) {
        // 限制最小高度 60px，最大 400px
        const newHeight = Math.min(Math.max(window.innerHeight - e.clientY - 30, 60), 400);
        this.dependencyHeight = newHeight;
      }
    },
    stopDrag() { this.dragging = false; },

    getLanguageOptions() {
      return [
        { value: 'python2', label: 'Python2', code: '# -*- coding: utf-8 -*-\nprint("Hello, World!")' },
        { value: 'python3', label: 'Python3', code: 'print("Hello, World!")' },
        { value: 'cpp', label: 'C++', code: '#include <iostream>\n\nusing namespace std;\nint main() {\n    cout << "Hello, World!";\n    return 0;\n}' },
        { value: 'java', label: 'Java', code: 'public class code {\n    public static void main(String[] args) {\n        System.out.println("Hello, World!");\n    }\n}' },
        { value: 'go', label: 'Go', code: 'package main\nimport "fmt"\nfunc main() {\n    fmt.Println("Hello, World!")\n}' },
        { value: 'csharp', label: 'C#/.NET', code: 'Console.WriteLine("Hello, World!");' },
        { value: 'csharp-sfa', label: 'C#/.NET 单文件', code: '#!/usr/bin/dotnet run\nConsole.WriteLine("Hello from a C# script!");' },
        { value: 'csharp-mono', label: 'C#/Mono', code: 'using System;\n\nnamespace HelloWorld\n{\n    class Program\n    {\n        static void Main(string[] args)\n        {\n            Console.WriteLine("Hello, World!");\n        }\n    }\n}' },
        { value: 'javascript', label: 'JavaScript', code: 'console.log("Hello, World!");' },
        { value: 'typescript', label: 'TypeScript', code: 'console.log("Hello, World!");' },
        { value: 'jupyter-python', label: 'Jupyter/Python', code: "from matplotlib import pyplot as plt\nimport numpy as np\n\n# Generate 100 random data points along 3 dimensions\nx, y, scale = np.random.randn(3, 100)\nfig, ax = plt.subplots()\n\n# Map each onto a scatterplot we'll create with Matplotlib\nax.scatter(x=x, y=y, c=scale, s=np.abs(scale)*500)\nax.set(title=\"Some random data, created with JupyterLab!\")\nplt.show()" },
        { value: 'jupyter-csharp', label: 'Jupyter/C#', code: 'Console.WriteLine("Hello, World!");' },
        { value: 'jupyter-fsharp', label: 'Jupyter/F#', code: 'printfn "Hello from F#"' },
        { value: 'jupyter-r', label: 'Jupyter/R', code: 'curve(sin(x), -2 * pi, 2 * pi)' },
      ];
    },

    updateCodeContent(newLang) {
      const lang = this.languageOptions.find(l => l.value === newLang);
      this.codeContent = lang ? lang.code : "";
      this.editorOptions.mode = newLang;
      this.executionOutput = "";
      this.outputType = "";
      this.notebook = { cells: [] };
      this.files = [];
    },

    async executeCode() {
      this.isLoading = true;
      this.reset();

      try {
        const env = this.selectedLanguage.includes("jupyter") ? "jupyter" : "code";
        const response = await fetch(`http://localhost:8001/api/${env}/run`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            code: this.codeContent,
            language: this.selectedLanguage,
            dependencies: this.dependencies.split("\n").map(d => d.trim()).filter(d => d),
            format: "notebook"
          })
        });
        const data = await response.json();
        this.executionOutput = data.result.output;
        this.executionTime = data.result.duration;
        this.outputType = data.result.content_type;
        this.files = data.result.artifacts || [];

        this.$nextTick(() => {
          if (this.showJupyter) {
            this.notebook = JSON.parse(this.executionOutput);
            this.$refs.jupyter.render();
          }
        });
      } catch (error) {
        this.executionOutput = `Error: ${error}`;
      } finally {
        this.isLoading = false;
      }
    },

    reset() {
      this.executionOutput = "";
      this.outputType = "";
      this.executionTime = null;
      this.notebook = { cells: [] };
    }
  }
};
</script>
