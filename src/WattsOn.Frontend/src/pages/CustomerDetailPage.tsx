import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography,
  Button, Tabs, Row, Col, Statistic, Divider, Empty,
} from 'antd';
import {
  ArrowLeftOutlined, UserOutlined, ThunderboltOutlined,
  CalculatorOutlined, MailOutlined, PhoneOutlined, HomeOutlined,
} from '@ant-design/icons';
import type { CustomerDetail, SettlementDocument } from '../api/client';
import { getCustomer, getSettlementDocuments } from '../api/client';

const { Text, Title } = Typography;

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { style: 'currency', currency: 'DKK' }).format(amount);
const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');

const statusColors: Record<string, string> = {
  Calculated: 'green', Invoiced: 'blue', Adjusted: 'orange',
};

export default function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [settlements, setSettlements] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!id) return;
    Promise.all([getCustomer(id), getSettlementDocuments('all')])
      .then(([customerRes, docsRes]) => {
        setCustomer(customerRes.data);
        const identifier = customerRes.data.cpr || customerRes.data.cvr;
        setSettlements(docsRes.data.filter(d => d.buyer.identifier === identifier));
      })
      .catch(err => setError(err.response?.status === 404 ? 'Customer ikke fundet' : err.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!customer) return null;

  const activeLev = customer.supplies.filter(l => l.isActive);
  const totalSettled = settlements
    .filter(s => s.documentType === 'settlement')
    .reduce((sum, s) => sum + s.totalInclVat, 0);
  const corrections = settlements.filter(s => s.documentType !== 'settlement');

  // --- Tables ---

  const supplyColumns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: any) => (
        <Text className="mono" style={{ cursor: 'pointer' }}
          onClick={(e) => { e.stopPropagation(); navigate(`/metering_points/${record.meteringPointId}`); }}>
          {gsrn}
        </Text>
      ),
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: any, record: any) => (
        <Text className="tnum" style={{ fontSize: 13 }}>
          {formatDate(record.supplyStart)} — {record.supplyEnd ? formatDate(record.supplyEnd) : '→'}
        </Text>
      ),
    },
    {
      title: 'STATUS',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>
      ),
    },
  ];

  const settlementColumns = [
    {
      title: 'DOKUMENT',
      dataIndex: 'documentId',
      key: 'documentId',
      render: (docId: string, record: SettlementDocument) => (
        <Text className="mono" strong style={{ cursor: 'pointer' }}
          onClick={() => navigate(`/settlements/${record.settlementId}`)}>
          {docId}
        </Text>
      ),
    },
    {
      title: 'TYPE',
      dataIndex: 'documentType',
      key: 'documentType',
      width: 110,
      render: (type: string) => {
        const map: Record<string, { label: string; color: string }> = {
          settlement: { label: 'Settlement', color: '#5d7a91' },
          debitNote: { label: 'Debitnota', color: '#d97706' },
          creditNote: { label: 'Kreditnota', color: '#059669' },
        };
        const cfg = map[type] || { label: type, color: '#5d7a91' };
        return <Tag color={cfg.color} style={{ color: '#fff' }}>{cfg.label}</Tag>;
      },
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: any, record: SettlementDocument) => (
        <Text className="tnum" style={{ fontSize: 12 }}>
          {formatDate(record.period.start)} — {record.period.end ? formatDate(record.period.end) : '→'}
        </Text>
      ),
    },
    {
      title: 'BELØB EXCL.',
      dataIndex: 'totalExclVat',
      key: 'totalExclVat',
      align: 'right' as const,
      render: (v: number) => (
        <Text strong className="tnum" style={{ color: v < 0 ? '#059669' : undefined }}>
          {formatDKK(v)}
        </Text>
      ),
    },
    {
      title: 'INKL. MOMS',
      dataIndex: 'totalInclVat',
      key: 'totalInclVat',
      align: 'right' as const,
      render: (v: number) => (
        <Text className="tnum" style={{ color: v < 0 ? '#059669' : undefined }}>
          {formatDKK(v)}
        </Text>
      ),
    },
    {
      title: 'STATUS',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (s: string) => <Tag color={statusColors[s] || 'default'}>{s}</Tag>,
    },
  ];

  return (
    <Space direction="vertical" size={20} style={{ width: '100%' }}>
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/customers')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        Customers
      </Button>

      {/* Hero card */}
      <Card style={{ borderRadius: 12, overflow: 'hidden' }}>
        <Row gutter={[32, 16]} align="middle">
          <Col flex="auto">
            <Space size={16} align="start">
              <div style={{
                width: 56, height: 56, borderRadius: 14,
                background: 'linear-gradient(135deg, #e4e9ee 0%, #c9d4de 100%)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <UserOutlined style={{ fontSize: 24, color: '#5d7a91' }} />
              </div>
              <div>
                <Title level={3} style={{ margin: 0 }}>{customer.name}</Title>
                <Space size={8} style={{ marginTop: 6 }}>
                  <Tag color={customer.isPrivate ? 'blue' : 'green'}>
                    {customer.isPrivate ? 'Private' : 'Business'}
                  </Tag>
                  <Text type="secondary" className="mono">{customer.cpr || customer.cvr}</Text>
                  {activeLev.length > 0 && (
                    <Tag color="green">{activeLev.length} aktiv supply</Tag>
                  )}
                </Space>
              </div>
            </Space>
          </Col>
          <Col>
            <div style={{ textAlign: 'right' }}>
              <div className="micro-label">Afregnet total</div>
              <div className="amount amount-large" style={{ marginTop: 4 }}>
                {formatDKK(totalSettled)}
              </div>
            </div>
          </Col>
        </Row>
      </Card>

      {/* Quick stats */}
      <Row gutter={16}>
        {[
          { title: 'Supplies', value: customer.supplies.length, icon: <ThunderboltOutlined />, color: '#7c3aed' },
          { title: 'Active', value: activeLev.length, icon: <ThunderboltOutlined />, color: '#059669' },
          { title: 'Settlements', value: settlements.length, icon: <CalculatorOutlined />, color: '#5d7a91' },
          { title: 'Corrections', value: corrections.length, icon: <CalculatorOutlined />, color: corrections.length > 0 ? '#d97706' : '#99afc2' },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card size="small" style={{ borderRadius: 10, textAlign: 'center' }}>
              <Statistic
                title={s.title} value={s.value}
                prefix={<span style={{ color: s.color }}>{s.icon}</span>}
                valueStyle={{ color: s.color, fontSize: 24 }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Tabs */}
      <Card style={{ borderRadius: 12 }}>
        <Tabs
          defaultActiveKey="overview"
          items={[
            {
              key: 'overview',
              label: 'Oversigt',
              children: (
                <Row gutter={[24, 16]}>
                  <Col xs={24} md={12}>
                    <Card size="small" title="Kontaktoplysninger" style={{ borderRadius: 10 }}>
                      <Space direction="vertical" size={10} style={{ width: '100%' }}>
                        {customer.email && (
                          <Space><MailOutlined style={{ color: '#7593a9' }} /><Text>{customer.email}</Text></Space>
                        )}
                        {customer.phone && (
                          <Space><PhoneOutlined style={{ color: '#7593a9' }} /><Text>{customer.phone}</Text></Space>
                        )}
                        {!customer.email && !customer.phone && <Text type="secondary">Ingen kontaktoplysninger</Text>}
                      </Space>
                    </Card>
                  </Col>
                  <Col xs={24} md={12}>
                    <Card size="small" title="Adresse" style={{ borderRadius: 10 }}>
                      {customer.address ? (
                        <Space><HomeOutlined style={{ color: '#7593a9' }} />
                          <Text>
                            {customer.address.streetName} {customer.address.buildingNumber}
                            {customer.address.floor ? `, ${customer.address.floor}.` : ''}
                            {customer.address.suite ? ` ${customer.address.suite}` : ''}
                            <br />{customer.address.postCode} {customer.address.cityName}
                          </Text>
                        </Space>
                      ) : <Text type="secondary">Ingen adresse registreret</Text>}
                    </Card>
                  </Col>
                  <Col xs={24}>
                    <Descriptions size="small" column={{ xs: 1, sm: 2 }} bordered>
                      <Descriptions.Item label="Type">
                        <Tag color={customer.isPrivate ? 'blue' : 'green'}>{customer.isPrivate ? 'Private' : 'Business'}</Tag>
                      </Descriptions.Item>
                      <Descriptions.Item label={customer.isPrivate ? 'CPR' : 'CVR'}>
                        <Text className="mono">{customer.cpr || customer.cvr || '—'}</Text>
                      </Descriptions.Item>
                      <Descriptions.Item label="Created">
                        {new Date(customer.createdAt).toLocaleString('da-DK')}
                      </Descriptions.Item>
                      <Descriptions.Item label="Afregnet total">
                        <Text strong className="tnum">{formatDKK(totalSettled)}</Text>
                      </Descriptions.Item>
                    </Descriptions>
                  </Col>
                </Row>
              ),
            },
            {
              key: 'supplies',
              label: `Supplies (${customer.supplies.length})`,
              children: customer.supplies.length > 0 ? (
                <Table
                  dataSource={customer.supplies}
                  columns={supplyColumns}
                  rowKey="id"
                  pagination={false}
                  size="small"
                />
              ) : (
                <Empty description="Ingen supplies" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
            {
              key: 'settlements',
              label: `Settlements (${settlements.length})`,
              children: settlements.length > 0 ? (
                <Table
                  dataSource={settlements}
                  columns={settlementColumns}
                  rowKey="settlementId"
                  pagination={false}
                  size="small"
                />
              ) : (
                <Empty description="Ingen settlements endnu" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
          ]}
        />
      </Card>
    </Space>
  );
}
