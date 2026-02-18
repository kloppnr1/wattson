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
import type { KundeDetail, SettlementDocument } from '../api/client';
import { getKunde, getSettlementDocuments } from '../api/client';

const { Text, Title } = Typography;

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { style: 'currency', currency: 'DKK' }).format(amount);
const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');

const statusColors: Record<string, string> = {
  Beregnet: 'green', Faktureret: 'blue', Justeret: 'orange',
};

export default function KundeDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [kunde, setKunde] = useState<KundeDetail | null>(null);
  const [settlements, setSettlements] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!id) return;
    Promise.all([getKunde(id), getSettlementDocuments('all')])
      .then(([kundeRes, docsRes]) => {
        setKunde(kundeRes.data);
        const identifier = kundeRes.data.cpr || kundeRes.data.cvr;
        setSettlements(docsRes.data.filter(d => d.buyer.identifier === identifier));
      })
      .catch(err => setError(err.response?.status === 404 ? 'Kunde ikke fundet' : err.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!kunde) return null;

  const activeLev = kunde.leverancer.filter(l => l.isActive);
  const totalSettled = settlements
    .filter(s => s.documentType === 'settlement')
    .reduce((sum, s) => sum + s.totalInclVat, 0);
  const corrections = settlements.filter(s => s.documentType !== 'settlement');

  // --- Tables ---

  const leveranceColumns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: any) => (
        <Text className="mono" style={{ cursor: 'pointer' }}
          onClick={(e) => { e.stopPropagation(); navigate(`/målepunkter/${record.målepunktId}`); }}>
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
          onClick={() => navigate(`/afregninger/${record.settlementId}`)}>
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
          settlement: { label: 'Afregning', color: '#5d7a91' },
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
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/kunder')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        Kunder
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
                <Title level={3} style={{ margin: 0 }}>{kunde.name}</Title>
                <Space size={8} style={{ marginTop: 6 }}>
                  <Tag color={kunde.isPrivate ? 'blue' : 'green'}>
                    {kunde.isPrivate ? 'Privat' : 'Erhverv'}
                  </Tag>
                  <Text type="secondary" className="mono">{kunde.cpr || kunde.cvr}</Text>
                  {activeLev.length > 0 && (
                    <Tag color="green">{activeLev.length} aktiv leverance</Tag>
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
          { title: 'Leverancer', value: kunde.leverancer.length, icon: <ThunderboltOutlined />, color: '#7c3aed' },
          { title: 'Aktive', value: activeLev.length, icon: <ThunderboltOutlined />, color: '#059669' },
          { title: 'Afregninger', value: settlements.length, icon: <CalculatorOutlined />, color: '#5d7a91' },
          { title: 'Korrektioner', value: corrections.length, icon: <CalculatorOutlined />, color: corrections.length > 0 ? '#d97706' : '#99afc2' },
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
                        {kunde.email && (
                          <Space><MailOutlined style={{ color: '#7593a9' }} /><Text>{kunde.email}</Text></Space>
                        )}
                        {kunde.phone && (
                          <Space><PhoneOutlined style={{ color: '#7593a9' }} /><Text>{kunde.phone}</Text></Space>
                        )}
                        {!kunde.email && !kunde.phone && <Text type="secondary">Ingen kontaktoplysninger</Text>}
                      </Space>
                    </Card>
                  </Col>
                  <Col xs={24} md={12}>
                    <Card size="small" title="Adresse" style={{ borderRadius: 10 }}>
                      {kunde.address ? (
                        <Space><HomeOutlined style={{ color: '#7593a9' }} />
                          <Text>
                            {kunde.address.streetName} {kunde.address.buildingNumber}
                            {kunde.address.floor ? `, ${kunde.address.floor}.` : ''}
                            {kunde.address.suite ? ` ${kunde.address.suite}` : ''}
                            <br />{kunde.address.postCode} {kunde.address.cityName}
                          </Text>
                        </Space>
                      ) : <Text type="secondary">Ingen adresse registreret</Text>}
                    </Card>
                  </Col>
                  <Col xs={24}>
                    <Descriptions size="small" column={{ xs: 1, sm: 2 }} bordered>
                      <Descriptions.Item label="Type">
                        <Tag color={kunde.isPrivate ? 'blue' : 'green'}>{kunde.isPrivate ? 'Privat' : 'Erhverv'}</Tag>
                      </Descriptions.Item>
                      <Descriptions.Item label={kunde.isPrivate ? 'CPR' : 'CVR'}>
                        <Text className="mono">{kunde.cpr || kunde.cvr || '—'}</Text>
                      </Descriptions.Item>
                      <Descriptions.Item label="Oprettet">
                        {new Date(kunde.createdAt).toLocaleString('da-DK')}
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
              key: 'leverancer',
              label: `Leverancer (${kunde.leverancer.length})`,
              children: kunde.leverancer.length > 0 ? (
                <Table
                  dataSource={kunde.leverancer}
                  columns={leveranceColumns}
                  rowKey="id"
                  pagination={false}
                  size="small"
                />
              ) : (
                <Empty description="Ingen leverancer" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
            {
              key: 'afregninger',
              label: `Afregninger (${settlements.length})`,
              children: settlements.length > 0 ? (
                <Table
                  dataSource={settlements}
                  columns={settlementColumns}
                  rowKey="settlementId"
                  pagination={false}
                  size="small"
                />
              ) : (
                <Empty description="Ingen afregninger endnu" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
          ]}
        />
      </Card>
    </Space>
  );
}
