import { useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Spin, Alert, Space, Table, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { DashboardStats, SettlementDocument } from '../api/client';
import { getDashboard, getSettlementDocuments } from '../api/client';

const { Text } = Typography;

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');

export default function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [recentDocs, setRecentDocs] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    Promise.all([getDashboard(), getSettlementDocuments('all')])
      .then(([statsRes, docsRes]) => {
        setStats(statsRes.data);
        setRecentDocs(docsRes.data.slice(0, 8));
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
        const color = status === 'Beregnet' ? 'green'
          : status === 'Faktureret' ? 'blue'
          : status === 'Justeret' ? 'orange' : 'gray';
        return (
          <span className="status-badge">
            <span className={`status-dot ${color}`} />
            <span className={`status-text ${color}`}>{status.toLowerCase()}</span>
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
        <h2>Dashboard</h2>
        <div className="page-subtitle">Overblik over din elleverandør-drift</div>
      </div>

      {/* 4 stat cards in a row — flat, no icons, just label + number */}
      <Row gutter={16}>
        {[
          { title: 'Aktive Kunder', value: stats.kunder, color: undefined },
          { title: 'Afregninger klar', value: stats.afregninger.beregnede, color: '#10b981' },
          { title: 'Korrektioner', value: stats.afregninger.korrektioner, color: stats.afregninger.korrektioner > 0 ? '#dc2626' : undefined },
          { title: 'Målepunkter', value: stats.målepunkter, color: undefined },
        ].map(card => (
          <Col xs={12} sm={6} key={card.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={card.title}
                value={card.value}
                valueStyle={{
                  fontSize: 36,
                  fontWeight: 700,
                  color: card.color || '#1a202c',
                }}
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
          <span className="view-all" onClick={() => navigate('/afregninger')}>
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
            onClick: () => navigate(`/afregninger/${record.settlementId}`),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>
    </Space>
  );
}
