import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import daDK from 'antd/locale/da_DK';
import { wattsOnTheme } from './theme';
import AppLayout from './components/AppLayout';
import DashboardPage from './pages/DashboardPage';
import CustomersPage from './pages/CustomersPage';
import CustomerDetailPage from './pages/CustomerDetailPage';
import SettlementsPage from './pages/SettlementsPage';
import SettlementDetailPage from './pages/SettlementDetailPage';
import ProcesserPage from './pages/ProcesserPage';
import SimulationPage from './pages/SimulationPage';
import InboxPage from './pages/InboxPage';

export default function App() {
  return (
    <ConfigProvider locale={daDK} theme={wattsOnTheme}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/customers" element={<CustomersPage />} />
            <Route path="/customers/:id" element={<CustomerDetailPage />} />
            <Route path="/settlements" element={<SettlementsPage />} />
            <Route path="/settlements/:id" element={<SettlementDetailPage />} />
            <Route path="/processes" element={<ProcesserPage />} />
            <Route path="/simulation" element={<SimulationPage />} />
            <Route path="/messages" element={<InboxPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ConfigProvider>
  );
}
