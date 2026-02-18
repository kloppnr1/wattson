import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import daDK from 'antd/locale/da_DK';
import { wattsOnTheme } from './theme';
import AppLayout from './components/AppLayout';
import DashboardPage from './pages/DashboardPage';
import KunderPage from './pages/KunderPage';
import KundeDetailPage from './pages/KundeDetailPage';
import MålepunkterPage from './pages/MålepunkterPage';
import MålepunktDetailPage from './pages/MålepunktDetailPage';
import LeverancerPage from './pages/LeverancerPage';
import AfregningerPage from './pages/AfregningerPage';
import AfregningDetailPage from './pages/AfregningDetailPage';
import ProcesserPage from './pages/ProcesserPage';
import SimulationPage from './pages/SimulationPage';
import InboxPage from './pages/InboxPage';
import OutboxPage from './pages/OutboxPage';

export default function App() {
  return (
    <ConfigProvider locale={daDK} theme={wattsOnTheme}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/kunder" element={<KunderPage />} />
            <Route path="/kunder/:id" element={<KundeDetailPage />} />
            <Route path="/målepunkter" element={<MålepunkterPage />} />
            <Route path="/målepunkter/:id" element={<MålepunktDetailPage />} />
            <Route path="/leverancer" element={<LeverancerPage />} />
            <Route path="/afregninger" element={<AfregningerPage />} />
            <Route path="/afregninger/:id" element={<AfregningDetailPage />} />
            <Route path="/processer" element={<ProcesserPage />} />
            <Route path="/simulation" element={<SimulationPage />} />
            <Route path="/inbox" element={<InboxPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ConfigProvider>
  );
}
