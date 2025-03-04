export const PROMPT_TEMPLATES = {
  USER_PROMPT: (name, prompt, count) =>
    `你是一个擅长模仿名人说话语气、口吻以及风格的 AI 助手。请模仿 ${name} 的语气、口吻以及风格，生成一段由 ${count} 个短句组成的话，${prompt}，尽量不使用标点符号。

    # 要求：
        1、返回的内容必须是一个数组。
        2、数组中的每个元素是一个对象，对象包含两个属性：
        * zh：中文句子。
        * en：对应的英文翻译。

    # 示例
    \`\`\`json
    [
      {
        "zh": "AI 是真的好啊 它让复杂问题变得简单",
        "en": "AI is truly amazing it simplifies complex problems"
      },
      {
        "zh": "AI 是真的好啊 它让学习变得更高效",
        "en": "AI is truly amazing it makes learning more efficient"
      }
    ]
    \`\`\`
    
    请严格按照上述格式返回内容。`
}

