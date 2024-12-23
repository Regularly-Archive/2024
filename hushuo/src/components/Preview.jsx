import { useEffect, useRef, useState } from 'react'
import { FiArrowLeft, FiArrowRight } from 'react-icons/fi'; // 引入箭头图标

export default function Preview({ image, lines, textStyle, showSubtitles, showWatermark = true }) {
  const canvasRef = useRef(null)
  const [currentIndex, setCurrentIndex] = useState(0); // 当前图片索引
  const { fontSize, fontFamily, blockHeight, firstLineHeightOffset, isBackgroundDarkened, subtitleYFactor, englishSubtitleColor } = textStyle
  const englishFontSize = fontSize * 0.75; 
  
  const splitLines = () => {
    const chunkSize = lines.length / textStyle.splitCount;
    const chunks = [];
    for (let i = 0; i < lines.length; i += chunkSize) {
      chunks.push(lines.slice(i, i + chunkSize));
    }
    return chunks;
  };

  const currentLines = splitLines()[currentIndex] || [];

  useEffect(() => {
    if (!image || !canvasRef.current || currentLines.length === 0) return

    const canvas = canvasRef.current
    const ctx = canvas.getContext('2d')
    const img = new Image()

    img.onload = () => {
      const extraLines = currentLines.length - 1
      const totalExtraHeight = Math.max(0, extraLines * blockHeight)
      canvas.width = img.width
      canvas.height = img.height + totalExtraHeight - firstLineHeightOffset

      ctx.drawImage(img, 0, 0)
      ctx.textAlign = 'center'
      ctx.font = `${fontSize}px "${fontFamily}", sans-serif`

      // 绘制第一行文字
      if (currentLines[0]) {
        const firstLineY = img.height - blockHeight * 0.5 - firstLineHeightOffset
        
        // 如果需要背景加深效果
        if (isBackgroundDarkened) {
          ctx.fillStyle = 'rgba(0, 0, 0, 0.5)'; // 黑色半透明背景
          ctx.fillRect(0, img.height - blockHeight - firstLineHeightOffset, img.width, blockHeight);
        }

        ctx.strokeStyle = 'black'
        ctx.lineWidth = Math.max(3, fontSize / 10)
        ctx.strokeText(currentLines[0].zh, img.width / 2, firstLineY)
        ctx.fillStyle = 'white'
        ctx.fillText(currentLines[0].zh, img.width / 2, firstLineY)

        // 绘制英文字幕
        if (showSubtitles && currentLines[0].en) {
          ctx.font = `${englishFontSize}px "${fontFamily}", sans-serif`;
          const subtitleY = firstLineY + blockHeight * subtitleYFactor;
          ctx.strokeStyle = 'black';
          ctx.strokeText(currentLines[0].en, img.width / 2, subtitleY);
          ctx.fillStyle = englishSubtitleColor;
          ctx.fillText(currentLines[0].en, img.width / 2, subtitleY);
        }
      }

      // 从第二行开始，为每行文字创建一个新的区域
      currentLines.slice(1).forEach((line, index) => {
        ctx.font = `${fontSize}px "${fontFamily}", sans-serif`
        const blockY = img.height + (index * blockHeight) - firstLineHeightOffset
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
          // 如果需要背景加深效果
          if (isBackgroundDarkened) {
            ctx.fillStyle = 'rgba(0, 0, 0, 0.5)'; // 黑色半透明背景
            ctx.fillRect(0, blockY, img.width, blockHeight);
          }

          ctx.strokeStyle = 'black'
          ctx.lineWidth = Math.max(3, fontSize / 10)
          const lineTextY = blockY + blockHeight * 0.5
          ctx.strokeText(line.zh, img.width / 2, blockY + blockHeight * 0.5)
          ctx.fillStyle = 'white'
          ctx.fillText(line.zh, img.width / 2, blockY + blockHeight * 0.5)

          // 绘制英文字幕
          if (showSubtitles && line.en) {
            ctx.font = `${englishFontSize}px "${fontFamily}", sans-serif`; 
            const subtitleY = lineTextY + blockHeight * subtitleYFactor;
            ctx.strokeStyle = 'black';
            ctx.strokeText(line.en, img.width / 2, subtitleY);
            ctx.fillStyle = englishSubtitleColor;
            ctx.fillText(line.en, img.width / 2, subtitleY);
          }
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
  }, [image, currentLines, textStyle, showSubtitles, showWatermark])
  
  const formatTimestamp = () => {
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0'); // 月份从0开始
    const day = String(now.getDate()).padStart(2, '0');
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    const seconds = String(now.getSeconds()).padStart(2, '0');
    const milliseconds = String(now.getMilliseconds()).padStart(3, '0');
    return `${year}${month}${day}${hours}${minutes}${seconds}${milliseconds}`;
  };

  const handleDownloadCurrent = () => {
    const canvas = canvasRef.current;
    if (canvas) {
      const timestamp = formatTimestamp(); // 获取格式化的时间戳
      const link = document.createElement('a');
      const fileName = textStyle.splitCount > 1 
        ? `胡说_${timestamp}_${currentIndex + 1}.png`
        : `胡说_${timestamp}.png`
      link.download = fileName; 
      link.href = canvas.toDataURL('image/png');
      link.click();
    }
  };

  const handleDownloadAll = async () => {
    const originalIndex = currentIndex; // 记录当前索引
    const canvas = canvasRef.current;

    if (canvas && textStyle.splitCount > 1) {
      const timestamp = formatTimestamp()
      for (let i = 0; i < splitLines().length; i++) {
        setCurrentIndex(i); // 切换到下一个索引
        await new Promise(resolve => setTimeout(resolve, 100)); // 等待渲染完成
        const link = document.createElement('a');
        const fileName = `胡说_${timestamp}_${i + 1}.png`
        link.download = fileName
        link.href = canvas.toDataURL('image/png');
        link.click();
      }
    }

    setCurrentIndex(originalIndex); // 重置索引
  };

  return (
    <div className="relative w-full">
      <div className="flex justify-between items-center mb-2">
        {textStyle.splitCount > 1 && (
          <>
            <button 
              className='bg-blue-600 text-white rounded-lg hover:bg-blue-700'
              onClick={() => setCurrentIndex((prev) => Math.max(prev - 1, 0))}>
              <FiArrowLeft /> {/* 使用左箭头图标 */}
            </button>
            <span className="mx-4">
              {currentIndex + 1} / {splitLines().length} {/* 显示当前索引和总数 */}
            </span>
            <button 
              className='bg-blue-600 text-white rounded-lg hover:bg-blue-700'
              onClick={() => setCurrentIndex((prev) => Math.min(prev + 1, splitLines().length - 1))}>
              <FiArrowRight /> {/* 使用右箭头图标 */}
            </button>
          </>
        )}
      </div>
      <canvas
        ref={canvasRef}
        className="max-w-full h-auto mx-auto border rounded-lg shadow-lg"
      />
      <button onClick={handleDownloadCurrent} className="mt-4 w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors duration-200 font-medium shadow-sm">
        下载当前图片
      </button>
      {textStyle.splitCount > 1 && (
        <button onClick={handleDownloadAll} className="mt-4 w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors duration-200 font-medium shadow-sm">
          下载全部图片
        </button>
      )}
    </div>
  )
}
