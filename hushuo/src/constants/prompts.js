export const PROMPT_TEMPLATES = {
  SYSTEM_PROMPT: (jsonOutput) => 
    `你是一个擅长模仿名人说话语气、口吻以及风格的AI助手，你严格按照下面的格式返回内容：\n\n` + 
    '```json' + 
    `${jsonOutput}` + 
    '```',
    USER_PROMPT: (name, prompt, count) => 
        `请模仿${name}的语气、口吻以及风格，根据以下要求，生成由 ${count} 个短句组成的一段话。\n\n要求：${prompt}`
}

export const SAMPLE_JSON_OUTPUT = `[{"zh":"我们常常追求完美，却忽略了生活的本质是不完美。","en":"we often pursue perfection, yet overlook that the essence of life is imperfection."},{"zh":"我们总是想要控制一切，却忘记了不确定性才是常态。","en":"we always want to control everything, but forget that uncertainty is the norm."},{"zh":"我们被欲望所驱使，却忘记了知足才能带来真正的安宁。","en":"Third, we are driven by desire, yet forget that contentment brings true peace."},{"zh":"我们习惯于比较，却忽视了每个人的幸福标准本就不同。","en":"Fourth, we are accustomed to comparison, but ignore that each person's standard of happiness is different."},{"zh":"我们忘记了，幸福其实是一种选择，而不是一种结果。","en":"Fifth, we forget that happiness is a choice, not a result."}]` 