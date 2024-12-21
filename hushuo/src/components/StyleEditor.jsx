import { useEffect, useState } from 'react'

const FONTS = [
  { value: 'Microsoft YaHei', label: '微软雅黑' },
  { value: 'SimSun', label: '宋体' },
  { value: 'KaiTi', label: '楷体' },
  { value: 'SimHei', label: '黑体' },
  { value: 'STXihei', label: '华文细黑' },
]

export default function StyleEditor({ imageHeight, onStyleChange, showEnglishSubtitles}) {
  const [fontSize, setFontSize] = useState(32)
  const [selectedFont, setSelectedFont] = useState(FONTS[0].value)
  const [blockHeight, setBlockHeight] = useState(
    showEnglishSubtitles ? Math.floor(imageHeight / 8) : Math.floor(imageHeight / 16)
  )

  const [isBackgroundDarkened, setIsBackgroundDarkened] = useState(false);
  
  // 设置第一行文本高度的默认值和最小值
  const minFirstLineHeightOffset = 0;
  const maxFirstLineHeightOffset = imageHeight / 2;
  const [firstLineHeightOffset, setFirstLineHeightOffset] = useState(0);

  // 新增的状态：中英文字幕间距
  const [subtitleYFactor, setSubtitleYFactor] = useState(0.35); // 默认值

  const minHeight = Math.max(32, Math.floor(imageHeight / 16))
  const maxHeight = Math.floor(imageHeight / 8)

  useEffect(() => {
    onStyleChange({
      fontSize,
      fontFamily: selectedFont,
      blockHeight,
      firstLineHeightOffset,
      isBackgroundDarkened,
      subtitleYFactor, // 传递新的状态
    })
  }, [fontSize, selectedFont, blockHeight, firstLineHeightOffset, isBackgroundDarkened, subtitleYFactor])

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium text-gray-700">
            字体选择
          </label>
          <span className="text-sm text-gray-500">
            当前字体：{FONTS.find(f => f.value === selectedFont)?.label}
          </span>
        </div>
        <select
          value={selectedFont}
          onChange={(e) => setSelectedFont(e.target.value)}
          className="w-full"
        >
          {FONTS.map((font) => (
            <option key={font.value} value={font.value}>
              {font.label}
            </option>
          ))}
        </select>
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium text-gray-700">
            字号大小
          </label>
          <span className="text-sm text-gray-500">
            {fontSize}px
          </span>
        </div>
        <input
          type="range"
          min="20"
          max="60"
          value={fontSize}
          onChange={(e) => setFontSize(Number(e.target.value))}
          className="w-full"
        />
        <div className="flex justify-between text-xs text-gray-500">
          <span>20px</span>
          <span>60px</span>
        </div>
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium text-gray-700">
            文字区域高度
          </label>
          <span className="text-sm text-gray-500">
            {blockHeight}px
          </span>
        </div>
        <input
          type="range"
          min={minHeight}
          max={maxHeight}
          value={blockHeight}
          onChange={(e) => setBlockHeight(Number(e.target.value))}
          className="w-full"
        />
        <div className="flex justify-between text-xs text-gray-500">
          <span>{minHeight}px</span>
          <span>{maxHeight}px</span>
        </div>
      </div>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium text-gray-700">
            首行文本偏移量
          </label>
          <span className="text-sm text-gray-500">
            -{firstLineHeightOffset}px
          </span>
        </div>
        <input
          type="range"
          min={minFirstLineHeightOffset}
          max={maxFirstLineHeightOffset}
          value={firstLineHeightOffset}
          onChange={(e) => setFirstLineHeightOffset(Number(e.target.value))}
          className="w-full"
        />
        <div className="flex justify-between text-xs text-gray-500">
          <span>{minFirstLineHeightOffset}px</span>
          <span>{maxFirstLineHeightOffset}px</span>
        </div>
      </div>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium text-gray-700">
            中英文字幕间距
          </label>
          <span className="text-sm text-gray-500">
            {subtitleYFactor}
          </span>
        </div>
        <input
          type="range"
          min={0.3}
          max={0.5}
          step={0.01} // 允许小数步进
          value={subtitleYFactor}
          onChange={(e) => setSubtitleYFactor(Number(e.target.value))}
          className="w-full"
        />
        <div className="flex justify-between text-xs text-gray-500">
          <span>0.3</span>
          <span>0.5</span>
        </div>
      </div>
      <div className="flex items-center mt-4">
        <input
          type="checkbox"
          id="isBackgroundDarkened"
          checked={isBackgroundDarkened}
          onChange={() => setIsBackgroundDarkened(!isBackgroundDarkened)}
          className="mr-2"
        />
        <label htmlFor="isBackgroundDarkened" className="text-gray-700">字幕背景增强</label>
      </div>
    </div>
  )
}
