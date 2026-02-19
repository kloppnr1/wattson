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
import AdminPage from './pages/AdminPage';
import PricesPage from './pages/PricesPage';
import MeteringPointsPage from './pages/MeteringPointsPage';
import MeteringPointDetailPage from './pages/MeteringPointDetailPage';
import SuppliesPage from './pages/SuppliesPage';
import OutboxPage from './pages/OutboxPage';

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
            <Route path="/prices" element={<PricesPage />} />
            <Route path="/processes" element={<ProcesserPage />} />
            <Route path="/admin" element={<AdminPage />} />
            <Route path="/simulation" element={<SimulationPage />} />
            <Route path="/messages" element={<InboxPage />} />
            <Route path="/metering-points" element={<MeteringPointsPage />} />
            <Route path="/metering-points/:id" element={<MeteringPointDetailPage />} />
            <Route path="/supplies" element={<SuppliesPage />} />
            <Route path="/outbox" element={<OutboxPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ConfigProvider>
  );
}
