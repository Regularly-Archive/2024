import { SYSTEM_PROMPTS, SAMPLE_JSON_OUTPUT } from '../constants/prompts'

export const generateQuote = async (name, prompt, count) => {
  try {
    const token = ''
    
    const response = await fetch('https://api.deepseek.com/chat/completions', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        model: 'deepseek-chat',
        messages: [
          {
            role: 'system', 
            content: SYSTEM_PROMPTS.QUOTE_GENERATION(SAMPLE_JSON_OUTPUT)
          },
          {
            role: 'user',
            content: `请模仿${name}的语气、口吻以及风格，根据以下要求，生成由 ${count} 个短句组成的一段话。\n\n要求：${prompt}`
          }
        ],
        temperature: 0.75,
        response_format: {
            type: 'json_object'
        }
      })
    })

    if (!response.ok) {
      const errorData = await response.json()
      throw new Error(errorData.error.message || '生成失败')
    }

    const data = await response.json()
    const quote = data.choices[0].message.content.trim().replaceAll('\n','')
    return JSON.parse(quote)
  } catch (error) {
    console.error('生成名言失败:', error)
    throw error
  }
}
