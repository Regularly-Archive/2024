<template>
    <div id="app">
        <h1 class="title">Code Runner</h1> 
        <CodeRunner />
    </div>
</template>

<script>
import CodeRunner from './components/CodeRunner.vue';

export default {
    components: {
        CodeRunner,
    },
};
</script>

<style>
body { font-family: Arial, sans-serif; }
.logo { width: 100px; height: auto; } /* Logo 样式 */
.title { color: #42b983; font-size: 2em; text-align: center; margin-top: 10px; } /* 标题样式 */
</style>
