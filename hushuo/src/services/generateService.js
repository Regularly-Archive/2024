import { PROMPT_TEMPLATES, SAMPLE_JSON_OUTPUT } from '../constants/prompts'

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
            content: PROMPT_TEMPLATES.SYSTEM_PROMPT(SAMPLE_JSON_OUTPUT)
          },
          {
            role: 'user',
            content: PROMPT_TEMPLATES.USER_PROMPT(name, prompt, count)
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
