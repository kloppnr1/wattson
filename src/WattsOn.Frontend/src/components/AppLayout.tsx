import { Layout, Menu } from 'antd';
import {
  DashboardOutlined,
  UserOutlined,
  CalculatorOutlined,
  SyncOutlined,
  InboxOutlined,
  ExperimentOutlined,
  SettingOutlined,
  DollarOutlined,
  SendOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import WattsOnIcon from './WattsOnIcon';

const { Sider, Content } = Layout;

const menuItems = [
  { key: '/', icon: <DashboardOutlined />, label: 'Overblik' },
  { type: 'group' as const, label: 'KUNDER', children: [
    { key: '/customers', icon: <UserOutlined />, label: 'Kunder' },
  ]},
  { type: 'group' as const, label: 'AFREGNING', children: [
    { key: '/settlements', icon: <CalculatorOutlined />, label: 'Afregninger' },
    { key: '/prices', icon: <DollarOutlined />, label: 'Priser' },
  ]},
  { type: 'group' as const, label: 'DATAHUB', children: [
    { key: '/processes', icon: <SyncOutlined />, label: 'Processer' },
    { key: '/messages', icon: <InboxOutlined />, label: 'Beskeder' },
    { key: '/outbox', icon: <SendOutlined />, label: 'Udbakke' },
  ]},
  { type: 'group' as const, label: 'SYSTEM', children: [
    { key: '/admin', icon: <SettingOutlined />, label: 'Administration' },
    { key: '/simulation', icon: <ExperimentOutlined />, label: 'Simulator' },
  ]},
];

export default function AppLayout() {
  const navigate = useNavigate();
  const location = useLocation();

  const allItems = menuItems.flatMap(item =>
    'children' in item ? item.children || [] : [item]
  );

  // Map orphaned routes (removed from sidebar) to their logical parent
  const routeParents: Record<string, string> = {
    '/metering-points': '/customers',
    '/supplies': '/customers',
  };
  const effectivePath = Object.entries(routeParents).find(
    ([prefix]) => location.pathname.startsWith(prefix)
  )?.[1] || location.pathname;

  const selectedKey = allItems.find(item =>
    'key' in item && (
      effectivePath === item.key ||
      (item.key !== '/' && effectivePath.startsWith(item.key as string))
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
        }}
      >
        <div className="sidebar-brand" style={{ flexShrink: 0 }}>
          <div className="sidebar-brand-row">
            <WattsOnIcon size={24} />
            <h4>WattsOn</h4>
          </div>
          <div className="subtitle">Energy Platform</div>
        </div>
        <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', overflowX: 'hidden' }}>
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
        </div>
        <div className="sidebar-footer" style={{ flexShrink: 0 }}>
          <div className="env-badge">
            <span className="env-dot" />
            <span>Udvikling</span>
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
