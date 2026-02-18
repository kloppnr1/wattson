import { Layout, Menu } from 'antd';
import {
  DashboardOutlined,
  UserOutlined,
  CalculatorOutlined,
  SyncOutlined,
  InboxOutlined,
  ExperimentOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import WattsOnIcon from './WattsOnIcon';

const { Sider, Content } = Layout;

const menuItems = [
  { key: '/', icon: <DashboardOutlined />, label: 'Dashboard' },
  { type: 'group' as const, label: 'CUSTOMERS', children: [
    { key: '/customers', icon: <UserOutlined />, label: 'Customers' },
  ]},
  { type: 'group' as const, label: 'BILLING', children: [
    { key: '/settlements', icon: <CalculatorOutlined />, label: 'Settlements' },
  ]},
  { type: 'group' as const, label: 'DATAHUB', children: [
    { key: '/processes', icon: <SyncOutlined />, label: 'Processes' },
    { key: '/messages', icon: <InboxOutlined />, label: 'Messages' },
  ]},
  { type: 'group' as const, label: 'DEVELOPMENT', children: [
    { key: '/simulation', icon: <ExperimentOutlined />, label: 'Simulator' },
  ]},
];

export default function AppLayout() {
  const navigate = useNavigate();
  const location = useLocation();

  const allItems = menuItems.flatMap(item =>
    'children' in item ? item.children || [] : [item]
  );

  const selectedKey = allItems.find(item =>
    'key' in item && (
      location.pathname === item.key ||
      (item.key !== '/' && location.pathname.startsWith(item.key as string))
    )
  )?.key as string || '/';

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        theme="dark"
        width={220}
        style={{
          background: 'linear-gradient(180deg, #3d5a6e 0%, #4a6b80 50%, #5a7d92 100%)',
          position: 'fixed',
          left: 0,
          top: 0,
          bottom: 0,
          zIndex: 10,
          overflow: 'hidden',
        }}
      >
        <div className="sidebar-brand">
          <div className="sidebar-brand-row">
            <WattsOnIcon size={24} />
            <h4>WattsOn</h4>
          </div>
          <div className="subtitle">Energy Platform</div>
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[selectedKey]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
          style={{
            background: 'transparent',
            borderRight: 'none',
            marginTop: 8,
          }}
        />

        <div className="sidebar-footer">
          <div className="env-badge">
            <span className="env-dot" />
            <span>Development</span>
          </div>
          <div className="lang-toggle">
            <span className="lang-option">EN</span>
            <span className="lang-option active">DA</span>
          </div>
          <div className="version">WattsOn v0.1</div>
        </div>
      </Sider>
      <Layout style={{ marginLeft: 220, background: '#eef1f5' }}>
        <Content className="main-content">
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}
