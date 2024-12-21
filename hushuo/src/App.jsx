import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import Layout from './components/layout/Layout'
import Navbar from './components/Navbar'
import Home from './pages/Home'
import Editor from './pages/Editor'
import About from './pages/About'
import './App.css'
import './index.css'

function App() {
  return (
    <Router>
      <div className="min-h-screen bg-gray-50 flex flex-col">
        <Navbar />
        <Routes>
          <Route path="/" element={<Layout />}>
            {/* 在这里添加子路由 */}
            <Route index element={<Home />} />
            <Route path="editor" element={<Editor />} />
            <Route path="about" element={<About />} />
          </Route>
        </Routes>
      </div>
    </Router>
  )
}

export default App
