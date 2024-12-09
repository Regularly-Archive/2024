import { useState } from 'react'
import ImageUploader from '../components/ImageUploader'
import TextEditor from '../components/TextEditor'
import Preview from '../components/Preview'
import StyleEditor from '../components/StyleEditor'
import GenerateDialog from '../components/GenerateDialog'

export default function Editor() {
  const [image, setImage] = useState(null)
  const [lines, setLines] = useState(['', '', '', '', ''])
  const [imageHeight, setImageHeight] = useState(0)
  const [textStyle, setTextStyle] = useState({
    fontSize: 32,
    fontFamily: 'Microsoft YaHei',
    blockHeight: 80,
  })
  const [isGenerateDialogOpen, setIsGenerateDialogOpen] = useState(false)

  const handleImageUpload = (imageData) => {
    setImage(imageData)
    const img = new Image()
    img.onload = () => {
      setImageHeight(img.height)
      setTextStyle(prev => ({
        ...prev,
        blockHeight: Math.floor(img.height / 6)
      }))
    }
    img.src = imageData
  }

  const handleTextChange = (newLines) => {
    setLines(newLines)
  }

  const handleStyleChange = (newStyle) => {
    setTextStyle(newStyle)
  }

  const handleGenerate = (generatedLines) => {
    setLines(generatedLines)
  }

  const handleDownload = () => {
    const canvas = document.querySelector('canvas')
    if (canvas) {
      const link = document.createElement('a')
      link.download = '胡说.png'
      link.href = canvas.toDataURL('image/png')
      link.click()
    }
  }

  return (
    <main className="flex-1 px-4 py-6">
      <div className="flex flex-col lg:flex-row gap-8 max-w-[1920px] mx-auto">
        {/* 左侧面板 */}
        <div className="lg:w-1/2 flex flex-col gap-4">
          <section className="bg-white p-6 rounded-xl shadow-sm">
            <h2 className="text-2xl font-semibold mb-4 text-gray-700">1. 上传图片</h2>
            <ImageUploader onImageUpload={handleImageUpload} />
          </section>

          <section className="bg-white p-6 rounded-xl shadow-sm flex-1">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-2xl font-semibold text-gray-700">2. 编辑文字</h2>
              <button
                onClick={() => setIsGenerateDialogOpen(true)}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-md transition-colors duration-200 shadow-sm"
              >
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="w-4 h-4">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 0 0 2.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 0 0-2.456 2.456ZM16.894 20.567 16.5 21.75l-.394-1.183a2.25 2.25 0 0 0-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 0 0 1.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 0 0 1.423 1.423l1.183.394-1.183.394a2.25 2.25 0 0 0-1.423 1.423Z" />
                </svg>
                一键生成
              </button>
            </div>
            <div className="h-[320px]">
              <TextEditor onTextChange={handleTextChange} value={lines} />
            </div>
          </section>

          {image && (
            <section className="bg-white p-6 rounded-xl shadow-sm">
              <h2 className="text-2xl font-semibold mb-4 text-gray-700">3. 调整样式</h2>
              <StyleEditor imageHeight={imageHeight} onStyleChange={handleStyleChange} />
            </section>
          )}
        </div>

        {/* 右侧预览面板 */}
        <div className="lg:w-1/2 lg:self-start bg-white p-6 rounded-xl shadow-sm">
          <h2 className="text-2xl font-semibold mb-4 text-gray-700">4. 预览效果</h2>
          {image ? (
            <>
              <div className="relative">
                <Preview image={image} lines={lines} textStyle={textStyle} />
              </div>
              <button
                onClick={handleDownload}
                className="mt-4 w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors duration-200 font-medium shadow-sm"
              >
                下载图片
              </button>
            </>
          ) : (
            <div className="border-2 border-dashed border-gray-200 rounded-lg p-8 text-center text-gray-500">
              请先上传一张图片
            </div>
          )}
        </div>
      </div>

      <GenerateDialog
        isOpen={isGenerateDialogOpen}
        onClose={() => setIsGenerateDialogOpen(false)}
        onGenerate={handleGenerate}
      />
    </main>
  )
}
