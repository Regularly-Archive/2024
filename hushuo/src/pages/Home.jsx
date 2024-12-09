import { Link } from 'react-router-dom'
import { ArrowRight } from 'lucide-react'
import Footer from '../components/Footer';

export default function Home() {
  return (
    <div className="relative isolate overflow-hidden">
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:flex lg:px-8 lg:py-40">
        <div className="mx-auto max-w-2xl flex-shrink-0 lg:mx-0 lg:max-w-xl lg:pt-8">
          <div className="mt-24 sm:mt-32 lg:mt-16">
            <a href="#" className="inline-flex space-x-6">
              <span className="rounded-full bg-blue-500/10 px-3 py-1 text-sm font-semibold leading-6 text-blue-500 ring-1 ring-inset ring-blue-500/20">
                灵感来源
              </span>
            </a>
          </div>
          <blockquote className="mt-6 border-l-4 border-blue-500 pl-4 font-bold text-gray-600">
            "我实在没有说过这样的话。"
            <footer className="mt-2 text-sm text-gray-500">
              — 鲁迅
            </footer>
          </blockquote>
          <h1 className="mt-10 text-3xl font-bold tracking-tight text-gray-900 sm:text-6xl">
            胡说
          </h1>
          <h1 className="text-3xl font-bold tracking-tight text-gray-900 sm:text-6xl">
          一本正经地胡说八道 
          </h1>
          <p className="mt-6 text-lg leading-8 text-gray-600">
            一个专门用来制作"名人名言"的工具，让你轻松创作出令人捧腹的内容。无论是恶搞还是娱乐，让你的创意自由发挥。
          </p>
          <div className="mt-10 flex items-center gap-x-6">
            <Link
              to="/editor"
              className="rounded-md bg-blue-600 px-3.5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
            >
              开始胡说
              <ArrowRight className="ml-2 -mr-1 inline-block h-4 w-4" />
            </Link>
            <Link to="/about" className="text-sm font-semibold leading-6 text-gray-900">
              了解更多 <span aria-hidden="true">→</span>
            </Link>
          </div>
        </div>
        <div className="mx-auto mt-16 flex max-w-2xl sm:mt-24 lg:ml-10 lg:mr-0 lg:mt-0 lg:max-w-none xl:ml-32">
          <div className="max-w-3xl flex-none sm:max-w-5xl lg:max-w-none">
            <img
              src="/images/193a9a422f139.png"
              alt="App screenshot"
              width={2432}
              height={1442}
              className="w-[76rem] rounded-md bg-white/5 shadow-2xl ring-1 ring-white/10"
            />
          </div>
        </div>
      </div>
      <Footer />
    </div>
  )
}
