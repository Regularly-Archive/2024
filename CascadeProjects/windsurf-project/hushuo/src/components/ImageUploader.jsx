import { useCallback } from 'react'
import { useDropzone } from 'react-dropzone'
import { Upload } from 'lucide-react'

export default function ImageUploader({ onImageUpload }) {
  const onDrop = useCallback((acceptedFiles) => {
    const file = acceptedFiles[0]
    if (file) {
      const reader = new FileReader()
      reader.onload = (e) => {
        onImageUpload(e.target.result)
      }
      reader.readAsDataURL(file)
    }
  }, [onImageUpload])

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: {
      'image/*': ['.jpeg', '.jpg', '.png', '.gif']
    },
    maxFiles: 1
  })

  return (
    <div
      {...getRootProps()}
      className={`border-2 border-dashed rounded-lg p-4 text-center cursor-pointer transition-colors
        ${isDragActive ? 'border-blue-500 bg-blue-50' : 'border-gray-300 hover:border-gray-400'}`}
    >
      <input {...getInputProps()} />
      <div className="flex items-center justify-center gap-3">
        <Upload className="h-8 w-8 text-gray-400" />
        <div className="text-left">
          <p className="text-sm text-gray-500">
            {isDragActive ? '放开以上传图片' : '拖拽图片到这里，或点击上传'}
          </p>
          <p className="text-xs text-gray-400">
            支持 JPG, PNG, GIF 格式
          </p>
        </div>
      </div>
    </div>
  )
}
