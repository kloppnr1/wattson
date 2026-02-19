import { createBrowserRouter, RouterProvider } from 'react-router-dom';
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

const router = createBrowserRouter([
  {
    element: <AppLayout />,
    children: [
      { path: '/', element: <DashboardPage /> },
      { path: '/customers', element: <CustomersPage /> },
      { path: '/customers/:id', element: <CustomerDetailPage /> },
      { path: '/settlements', element: <SettlementsPage /> },
      { path: '/settlements/:id', element: <SettlementDetailPage /> },
      { path: '/prices', element: <PricesPage /> },
      { path: '/processes', element: <ProcesserPage /> },
      { path: '/admin', element: <AdminPage /> },
      { path: '/simulation', element: <SimulationPage /> },
      { path: '/messages', element: <InboxPage /> },
      { path: '/metering-points', element: <MeteringPointsPage /> },
      { path: '/metering-points/:id', element: <MeteringPointDetailPage /> },
      { path: '/supplies', element: <SuppliesPage /> },
      { path: '/outbox', element: <OutboxPage /> },
    ],
  },
]);

export default function App() {
  return (
    <ConfigProvider locale={daDK} theme={wattsOnTheme}>
      <RouterProvider router={router} />
    </ConfigProvider>
  );
}
