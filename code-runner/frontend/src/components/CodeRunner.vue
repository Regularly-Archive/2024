<template>
    <div class="flex flex-col h-screen p-5">
        <h1 class="text-3xl text-green-600 text-center mb-4">Code Runner</h1>
        <div class="flex items-center mb-4">
            <label for="language-select" class="mr-2">选择语言:</label>
            <select v-model="selectedLanguage" id="language-select" class="border rounded p-2 mr-2">
                <option v-for="lang in languageOptions" :key="lang.value" :value="lang.value">{{ lang.label }}</option>
            </select>
            <button @click="executeCode" class="bg-blue-500 text-white rounded p-2 hover:bg-blue-600 flex items-center" :disabled="isLoading">
                <span v-if="isLoading" class="flex items-center">
                    <svg class="animate-spin h-5 w-5 mr-2" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v2a6 6 0 100 12v2a8 8 0 01-8-8z"></path>
                    </svg>
                    运行中...
                </span>
                <span v-else>运行代码</span>
            </button>
            <div v-if="executionTime" class="ml-4 text-gray-600">本次运行耗时: {{ executionTime }} s</div>
        </div>
        <div class="flex flex-1">
            <div class="flex-1 border rounded p-2 mr-5">
                <Codemirror
                    v-model:value="codeContent"
                    :options="editorOptions"
                    ref="cmRef"
                    height="100%"
                    width="100%"
                    @change="handleChange"
                    @input="handleInput"
                    @ready="handleReady"
                ></Codemirror>
            </div>
            <div class="flex-1 border rounded p-2 bg-gray-50">
                <pre class="h-full bg-black text-white" v-if="selectedLanguage.indexOf('jupyter') == -1">{{ executionOutput }}</pre>
                <div class="h-full" v-html="executionOutput" v-else></div>
            </div>
        </div>
    </div>
</template>

<script>
import "codemirror/mode/javascript/javascript.js";
import Codemirror from "codemirror-editor-vue3"; 

export default {
    components: {
        Codemirror
    },
    data() {
        return {
            selectedLanguage: 'python3',
            codeContent: '',
            executionOutput: '',
            languageOptions: this.getLanguageOptions(),
            isLoading: false,
            editorOptions: {
                mode: this.selectedLanguage,
                lineNumbers: true,
                lineWrapping: true,
            },
        };
    },
    watch: {
        selectedLanguage(newLang) {
            this.updateCodeContent(newLang);
        },
    },
    mounted() {
        this.updateCodeContent(this.selectedLanguage);
    },
    computed: {
        formattedCode() {
            return `<pre><code class="${this.selectedLanguage}">${this.codeContent}</code></pre>`;
        },
    },
    methods: {
        getLanguageOptions() {
            return [ 
                { value: 'python2', label: 'Python2', code: '# -*- coding: utf-8 -*-\nprint("Hello, World!")' },
                { value: 'python3', label: 'Python3', code: 'print("Hello, World!")' },
                { value: 'cpp', label: 'C++', code: '#include <iostream>\n\nusing namespace std;\nint main() {\n    cout << "Hello, World!";\n    return 0;\n}' },
                { value: 'java', label: 'Java', code: 'public class code {\n    public static void main(String[] args) {\n        System.out.println("Hello, World!");\n    }\n}' },
                { value: 'go', label: 'Go', code: 'package main\nimport "fmt"\nfunc main() {\n    fmt.Println("Hello, World!")\n}' },
                { value: 'csharp', label: 'C#/.NET', code: 'Console.WriteLine("Hello, World!");' },
                { value: 'csharp-mono', label: 'C#/Mono', code: 'using System;\n\nnamespace HelloWorld\n{\n    class Program\n    {\n        static void Main(string[] args)\n        {\n            Console.WriteLine("Hello, World!");\n        }\n    }\n}' },
                { value: 'javascript', label: 'JavaScript', code: 'console.log("Hello, World!");' },
                { value: 'typescript', label: 'TypeScript', code: 'console.log("Hello, World!");' },
                { value: 'jupyter-python3', label: 'Jupyter/Python', code: "from matplotlib import pyplot as plt\nimport numpy as np\n\n# Generate 100 random data points along 3 dimensions\nx, y, scale = np.random.randn(3, 100)\nfig, ax = plt.subplots()\n\n# Map each onto a scatterplot we'll create with Matplotlib\nax.scatter(x=x, y=y, c=scale, s=np.abs(scale)*500)\nax.set(title=\"Some random data, created with JupyterLab!\")\nplt.show()" },
                { value: 'jupyter-csharp', label: 'Jupyter/C#', code: 'Console.WriteLine("Hello, World!");' },
                { value: 'jupyter-fsharp', label: 'Jupyter/F#', code: 'printfn "Hello from F#"' },
                { value: 'jupyter-r', label: 'Jupyter/R', code: 'curve(sin(x), -2 * pi, 2 * pi)' },
                
            ];
        },
        updateCodeContent(newLang) {
            const selectedLang = this.languageOptions.find(lang => lang.value === newLang);
            this.codeContent = selectedLang ? selectedLang.code : '';
            this.editorOptions.mode = newLang;
            this.executionOutput = '';
        },
        executeCode() {
            this.executionOutput = ''; 
            this.executionTime = null;
            this.isLoading = true;

            fetch('http://localhost:8001/api/run', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    code: this.codeContent,
                    language: this.selectedLanguage,
                    notebook: this.selectedLanguage.indexOf('jupyter') !== -1
                }),
            })
            .then(response => response.json())
            .then(data => {
                this.executionOutput = data.output;
                this.executionTime = data.time;
                this.$nextTick(() => {
                    hljs.highlightAll();
                });
            })
            .catch(error => {
                this.executionOutput = `Error: ${error}`;
            })
            .finally(() => {
                this.isLoading = false;
            });
        },
        handleChange() {},
        handleInput() {},
        handleReady() {},
    },
};
</script>