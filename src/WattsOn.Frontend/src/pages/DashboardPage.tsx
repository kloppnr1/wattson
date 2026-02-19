import { useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Spin, Alert, Space, Table, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { DashboardStats, SettlementDocument } from '../api/client';
import { getDashboard, getSettlementDocuments } from '../api/client';

const { Text } = Typography;

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
import { formatDate } from '../utils/format';

export default function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [recentDocs, setRecentDocs] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    Promise.all([
      getDashboard(),
      getSettlementDocuments('all').catch(() => ({ data: [] })),
    ])
      .then(([statsRes, docsRes]) => {
        setStats(statsRes.data);
        setRecentDocs(Array.isArray(docsRes.data) ? docsRes.data.slice(0, 8) : []);
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message="Kunne ikke hente dashboard" description={error} />;
  if (!stats) return null;

  const columns = [
    {
      title: 'DOKUMENT',
      dataIndex: 'documentId',
      key: 'documentId',
      render: (id: string) => <Text style={{ fontWeight: 500 }}>{id}</Text>,
    },
    {
      title: 'KUNDE',
      dataIndex: ['buyer', 'name'],
      key: 'buyer',
    },
    {
      title: 'MÅLEPUNKT',
      dataIndex: ['meteringPoint', 'gsrn'],
      key: 'gsrn',
      render: (gsrn: string) => <span className="gsrn-badge">{gsrn}</span>,
    },
    {
      title: 'STATUS',
      dataIndex: 'status',
      key: 'status',
      render: (status: string) => {
        const color = status === 'Calculated' ? 'green'
          : status === 'Invoiced' ? 'blue'
          : status === 'Adjusted' ? 'orange' : 'gray';
        const danishStatus = status === 'Calculated' ? 'beregnet'
          : status === 'Invoiced' ? 'faktureret'
          : status === 'Adjusted' ? 'justeret' : status.toLowerCase();
        return (
          <span className="status-badge">
            <span className={`status-dot ${color}`} />
            <span className={`status-text ${color}`}>{danishStatus}</span>
          </span>
        );
      },
    },
    {
      title: 'BELØB',
      dataIndex: 'totalExclVat',
      key: 'amount',
      align: 'right' as const,
      render: (v: number) => (
        <Text className="tnum" style={{ fontWeight: 500 }}>{formatDKK(v)} DKK</Text>
      ),
    },
    {
      title: 'BEREGNET',
      dataIndex: 'calculatedAt',
      key: 'calculatedAt',
      render: (d: string) => <Text style={{ color: '#6b7280' }}>{formatDate(d)}</Text>,
    },
  ];

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Overblik</h2>
        <div className="page-subtitle">Oversigt over din elforsyningsvirksomhed</div>
      </div>

      {/* 4 stat cards in a row — flat, no icons, just label + number */}
      <Row gutter={16}>
        {[
          { title: 'Aktive kunder', value: stats.customers, color: undefined },
          { title: 'Klar til fakturering', value: stats.settlements.calculated, color: '#10b981' },
          { title: 'Korrektioner', value: stats.settlements.corrections, color: stats.settlements.corrections > 0 ? '#dc2626' : undefined },
          { title: 'Målepunkter', value: stats.meteringPoints, color: undefined },
        ].map(card => (
          <Col xs={12} sm={6} key={card.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={card.title}
                value={card.value}
                styles={{ content: {
                  fontSize: 36,
                  fontWeight: 700,
                  color: card.color || '#1a202c',
                } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Recent settlements table */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="section-header">
          <div className="section-title">
            <div className="accent-bar" />
            <span>Seneste afregninger</span>
          </div>
          <span className="view-all" onClick={() => navigate('/settlements')}>
            Se alle &gt;
          </span>
        </div>
        <Table
          dataSource={recentDocs}
          columns={columns}
          rowKey="settlementId"
          pagination={false}
          size="small"
          onRow={record => ({
            onClick: () => navigate(`/settlements/${record.settlementId}`),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>
    </Space>
  );
}
