import { useEffect, useRef } from 'react'

export default function Preview({ image, lines, textStyle, showSubtitles, showWatermark = true }) {
  const canvasRef = useRef(null)
  const { fontSize, fontFamily, blockHeight } = textStyle

  useEffect(() => {
    if (!image || !canvasRef.current || lines.length === 0) return

    const canvas = canvasRef.current
    const ctx = canvas.getContext('2d')
    const img = new Image()

    img.onload = () => {
      const extraLines = lines.length - 1
      const totalExtraHeight = Math.max(0, extraLines * blockHeight)
      canvas.width = img.width
      canvas.height = img.height + totalExtraHeight

      ctx.drawImage(img, 0, 0)
      ctx.textAlign = 'center'
      ctx.font = `${fontSize}px "${fontFamily}", sans-serif`

      // 绘制第一行文字
      if (lines[0]) {
        const firstLineY = img.height - blockHeight * 0.3
        ctx.strokeStyle = 'black'
        ctx.lineWidth = Math.max(3, fontSize / 10)
        ctx.strokeText(lines[0], img.width / 2, firstLineY)
        ctx.fillStyle = 'white'
        ctx.fillText(lines[0], img.width / 2, firstLineY)
      }

      // 绘制英文字幕
      if (showSubtitles && lines[1]) {
        const subtitleY = img.height + blockHeight * 0.5;
        ctx.fillStyle = 'yellow'; // 设置英文字幕颜色
        ctx.fillText(lines[1], img.width / 2, subtitleY);
      }

      // 从第二行开始，为每行文字创建一个新的区域
      lines.slice(1).forEach((line, index) => {
        const blockY = img.height + (index * blockHeight)
        const sourceY = Math.max(0, img.height - blockHeight)
        ctx.drawImage(
          img,
          0, sourceY,
          img.width, blockHeight,
          0, blockY,
          img.width, blockHeight
        )

        // 绘制当前行文字
        if (line) {
          ctx.strokeStyle = 'black'
          ctx.lineWidth = Math.max(3, fontSize / 10)
          ctx.strokeText(line, img.width / 2, blockY + blockHeight * 0.7)
          ctx.fillStyle = 'white'
          ctx.fillText(line, img.width / 2, blockY + blockHeight * 0.7)
        }
      })

      // 添加水印的逻辑移到条件判断中
      if (showWatermark) {
        // 添加水印
        ctx.save()
        
        // 设置水印样式
        const watermarkText = '胡说 - hushuo.app'
        ctx.font = '24px Arial'
        ctx.fillStyle = 'rgba(255, 255, 255, 0.3)'
        ctx.strokeStyle = 'rgba(0, 0, 0, 0.3)'
        ctx.lineWidth = 1

        // 在整个画布上重复绘制水印
        ctx.translate(canvas.width/2, canvas.height/2)
        ctx.rotate(-Math.PI / 6) // 倾斜 30 度

        const watermarkWidth = ctx.measureText(watermarkText).width + 100
        const watermarkHeight = 100
        const cols = Math.ceil(canvas.width * 1.5 / watermarkWidth)
        const rows = Math.ceil(canvas.height * 1.5 / watermarkHeight)
        const startX = -canvas.width * 0.75
        const startY = -canvas.height * 0.75

        for (let i = 0; i < rows; i++) {
          for (let j = 0; j < cols; j++) {
            const x = startX + j * watermarkWidth
            const y = startY + i * watermarkHeight
            ctx.strokeText(watermarkText, x, y)
            ctx.fillText(watermarkText, x, y)
          }
        }

        ctx.restore()
      }
    }

    img.src = image
  }, [image, lines, textStyle, showSubtitles, showWatermark])

  return (
    <div className="relative w-full">
      <canvas
        ref={canvasRef}
        className="max-w-full h-auto mx-auto border rounded-lg shadow-lg"
      />
    </div>
  )
}
