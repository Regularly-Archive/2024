import { Outlet } from 'react-router-dom';
import Footer from './Footer';

const Layout = () => {
  return (
    <div className="min-h-screen flex flex-col bg-transparent">
      <main className="flex-grow bg-transparent">
        <Outlet />
      </main>
      <Footer />
    </div>
  );
};

export default Layout;
