import { useEffect, useState } from 'react'
import { FiTrash2 } from 'react-icons/fi'

export default function TextEditor({ onTextChange, value, showEnglishSubtitles }) {
  const [lines, setLines] = useState(value || [{ zh: '', en: '' }, { zh: '', en: '' }, { zh: '', en: '' }, { zh: '', en: '' }, { zh: '', en: '' }])

  useEffect(() => {
    if (value && JSON.stringify(value) !== JSON.stringify(lines)) {
      setLines(value)
    }
  }, [value])

  const handleChange = (index, newValue, lang) => {
    const newLines = [...lines]
    newLines[index][lang] = newValue
    setLines(newLines)
    onTextChange(newLines)
  }

  const addLine = () => {
    const newLines = [...lines, { zh: '', en: '' }]
    setLines(newLines)
    onTextChange(newLines)
  }

  const removeLine = (index) => {
    if (lines.length > 1) {
      const newLines = lines.filter((_, i) => i !== index)
      setLines(newLines)
      onTextChange(newLines)
    }
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex-1 overflow-y-auto space-y-2">
        {lines.map((line, index) => (
          <div key={index} className="flex gap-2 items-center">
            <input
              type="text"
              value={line.zh}
              onChange={(e) => handleChange(index, e.target.value, 'zh')}
              className="flex-1 px-3 py-2 text-base border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              placeholder={`第 ${index + 1} 行中文文字`}
            />
            <input
              type="text"
              value={line.en}
              onChange={(e) => handleChange(index, e.target.value, 'en')}
              className="flex-1 px-3 py-2 text-base border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              placeholder={`第 ${index + 1} 行英文文字`}
              style={{ display: showEnglishSubtitles ? 'block' : 'none' }}
            />
            <button
              onClick={() => removeLine(index)}
              className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
              disabled={lines.length === 1}
              title="删除"
            >
              <FiTrash2 className="w-5 h-5" />
            </button>
          </div>
        ))}
      </div>
      <button
        onClick={addLine}
        className="mt-4 w-full px-4 py-2 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors duration-200 border border-blue-200"
      >
        添加一行
      </button>
    </div>
  )
}
