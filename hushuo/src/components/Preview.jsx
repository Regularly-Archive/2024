import { useEffect, useRef } from 'react'

export default function Preview({ image, lines, textStyle }) {
  const canvasRef = useRef(null)
  const { fontSize, fontFamily, blockHeight } = textStyle

  useEffect(() => {
    if (!image || !canvasRef.current || lines.length === 0) return

    const canvas = canvasRef.current
    const ctx = canvas.getContext('2d')
    const img = new Image()

    img.onload = () => {
      // 计算新的画布尺寸
      const extraLines = lines.length - 1 // 减去第一行，因为第一行直接绘制在原图上
      const totalExtraHeight = Math.max(0, extraLines * blockHeight) // 额外需要的总高度
      
      // 设置画布大小为原图高度加上额外文字区域的总高度
      canvas.width = img.width
      canvas.height = img.height + totalExtraHeight

      // 首先绘制原始图片
      ctx.drawImage(img, 0, 0)

      // 设置文字样式
      ctx.textAlign = 'center'
      ctx.font = `${fontSize}px "${fontFamily}", sans-serif`

      // 绘制第一行文字（在原图上）
      if (lines[0]) {
        const firstLineY = img.height - blockHeight * 0.3
        // 绘制文字描边
        ctx.strokeStyle = 'black'
        ctx.lineWidth = Math.max(3, fontSize / 10)
        ctx.strokeText(lines[0], img.width / 2, firstLineY)
        // 绘制文字
        ctx.fillStyle = 'white'
        ctx.fillText(lines[0], img.width / 2, firstLineY)
      }

      // 从第二行开始，为每行文字创建一个新的区域
      lines.slice(1).forEach((line, index) => {
        // 计算当前文字块的位置
        const blockY = img.height + (index * blockHeight)
        
        // 从原图底部截取一部分并绘制到新位置
        const sourceY = Math.max(0, img.height - blockHeight)
        ctx.drawImage(
          img,
          0, sourceY, // 源图片的裁剪起始位置
          img.width, blockHeight, // 源图片的裁剪尺寸
          0, blockY, // 目标位置
          img.width, blockHeight // 目标尺寸
        )

        // 绘制文字
        const textY = blockY + (blockHeight * 0.6)
        
        // 绘制文字描边
        ctx.strokeStyle = 'black'
        ctx.lineWidth = Math.max(3, fontSize / 10)
        ctx.strokeText(line, img.width / 2, textY)
        // 绘制文字
        ctx.fillStyle = 'white'
        ctx.fillText(line, img.width / 2, textY)
      })

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

    img.src = image
  }, [image, lines, fontSize, fontFamily, blockHeight])

  return (
    <div className="relative w-full">
      <canvas
        ref={canvasRef}
        className="max-w-full h-auto mx-auto border rounded-lg shadow-lg"
      />
    </div>
  )
}
