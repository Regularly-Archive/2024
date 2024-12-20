import Footer from '../components/Footer';

export default function About() {
  return (
    <main className="flex-1 px-4 py-6 bg-gray-50">
      {/* 标题区域 */}
      <div className="mx-auto max-w-2xl lg:text-center mb-12">
        <div className="mx-auto max-w-2xl lg:text-center mb-12 flex justify-center">
          <img src="/images/about_logo.png" className="p-2 w-64" />
        </div>
        <h2 className="text-base font-semibold leading-7 text-blue-600">关于胡说</h2>
        <p className="mt-2 text-3xl font-bold tracking-tight text-gray-900 sm:text-4xl">
          让图片说话的艺术
        </p>
        <p className="mt-6 text-lg leading-8 text-gray-600">
        胡说是一个简单而强大的图片文字编辑工具，专注于为用户提供最佳的文字叠加体验
        </p>
      </div>

      {/* 特点列表 */}
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <dl className="grid max-w-xl grid-cols-1 gap-x-8 gap-y-8 lg:max-w-none lg:grid-cols-3">
          <div className="bg-white p-6 rounded-xl shadow-sm">
            <dt className="text-lg font-semibold leading-7 text-gray-900">
              <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-blue-600">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor" className="w-6 h-6 text-white">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 0 0 2.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 0 0-2.456 2.456ZM16.894 20.567 16.5 21.75l-.394-1.183a2.25 2.25 0 0 0-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 0 0 1.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 0 0 1.423 1.423l1.183.394-1.183.394a2.25 2.25 0 0 0-1.423 1.423Z" />
                </svg>
              </div>
              AI 生成
            </dt>
            <dd className="mt-4 text-base leading-7 text-gray-600">
              内置智能文案生成功能，一键生成有趣的文字内容，让创作更加轻松。
            </dd>
          </div>

          <div className="bg-white p-6 rounded-xl shadow-sm">
            <dt className="text-lg font-semibold leading-7 text-gray-900">
              <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-blue-600">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor" className="w-6 h-6 text-white">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16.862 4.487l1.687-1.688a1.875 1.875 0 112.652 2.652L10.582 16.07a4.5 4.5 0 01-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 011.13-1.897l8.932-8.931zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0115.75 21H5.25A2.25 2.25 0 013 18.75V8.25A2.25 2.25 0 015.25 6H10" />
                </svg>
              </div>
              简单易用
            </dt>
            <dd className="mt-4 text-base leading-7 text-gray-600">
              直观的界面设计，简单的操作流程，让你能够快速上手，专注于创意的表达。
            </dd>
          </div>

          <div className="bg-white p-6 rounded-xl shadow-sm">
            <dt className="text-lg font-semibold leading-7 text-gray-900">
              <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-blue-600">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor" className="w-6 h-6 text-white">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9.53 16.122a3 3 0 00-5.78 1.128 2.25 2.25 0 01-2.4 2.245 4.5 4.5 0 008.4-2.245c0-.399-.078-.78-.22-1.128zm0 0a15.998 15.998 0 003.388-1.62m-5.043-.025a15.994 15.994 0 011.622-3.395m3.42 3.42a15.995 15.995 0 004.764-4.648l3.876-5.814a1.151 1.151 0 00-1.597-1.597L14.146 6.32a15.996 15.996 0 00-4.649 4.763m3.42 3.42a6.776 6.776 0 00-3.42-3.42" />
                </svg>
              </div>
              样式丰富
            </dt>
            <dd className="mt-4 text-base leading-7 text-gray-600">
              提供丰富的样式选项，包括字体、大小、间距等，让你的作品更具个性。
            </dd>
          </div>
        </dl>
      </div>
    </main>
  )
}
