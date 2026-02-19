import { useEffect, useState } from 'react';
import { Card, Table, Spin, Alert, Space, Typography, Segmented, Row, Col, Statistic, Select, Input, DatePicker, Tag } from 'antd';
import { SwapOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { SettlementDocument } from '../api/client';
import { getSettlementDocuments } from '../api/client';

const { Text } = Typography;

import { formatDate, formatDateTime, formatPeriodEnd } from '../utils/format';

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);

export default function SettlementsPage() {
  const [allDocs, setAllDocs] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<string>('runs');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const navigate = useNavigate();

  useEffect(() => {
    getSettlementDocuments('all')
      .then(res => setAllDocs(res.data))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (error) return <Alert type="error" message="Kunne ikke hente settlements" description={error} />;

  const runs = allDocs.filter(d => d.documentType === 'settlement');
  const corrections = allDocs.filter(d => d.documentType !== 'settlement');
  const filtered = tab === 'runs' ? runs : corrections;
  const statusFiltered = statusFilter === 'all' ? filtered
    : filtered.filter(d => d.status === statusFilter);

  const columns = [
    {
      title: 'STATUS',
      dataIndex: 'status',
      key: 'status',
      width: 130,
      render: (status: string, record: SettlementDocument) => {
        const color = status === 'Calculated' ? 'green'
          : status === 'Invoiced' ? 'blue'
          : status === 'Adjusted' ? 'orange' : 'gray';
        const danishStatus = status === 'Calculated' ? 'beregnet'
          : status === 'Invoiced' ? 'faktureret'
          : status === 'Adjusted' ? 'justeret' : status.toLowerCase();
        return (
          <Space size={6}>
            <span className="status-badge">
              <span className={`status-dot ${color}`} />
              <span className={`status-text ${color}`}>{danishStatus}</span>
            </span>
            {record.documentType === 'creditNote' && (
              <Tag color="green" style={{ fontSize: 10, lineHeight: '16px', padding: '0 4px', margin: 0 }}>
                <SwapOutlined style={{ marginRight: 2 }} />kredit
              </Tag>
            )}
            {record.documentType === 'debitNote' && (
              <Tag color="orange" style={{ fontSize: 10, lineHeight: '16px', padding: '0 4px', margin: 0 }}>
                <SwapOutlined style={{ marginRight: 2 }} />debit
              </Tag>
            )}
          </Space>
        );
      },
    },
    {
      title: 'MÅLEPUNKT',
      dataIndex: ['meteringPoint', 'gsrn'],
      key: 'gsrn',
      render: (gsrn: string) => <span className="gsrn-badge">{gsrn}</span>,
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: any, record: SettlementDocument) => (
        <Text className="tnum">
          {formatDate(record.period.start)} — {record.period.end ? formatPeriodEnd(record.period.end) : '→'}
        </Text>
      ),
    },
    {
      title: 'KUNDE',
      dataIndex: ['buyer', 'name'],
      key: 'buyer',
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
      render: (d: string) => <Text style={{ color: '#6b7280' }}>{formatDateTime(d)}</Text>,
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Afregninger</h2>
        <div className="page-subtitle">Afregningskørsler og korrektioner</div>
      </div>

      {/* Tabs */}
      <Segmented
        value={tab}
        onChange={v => setTab(v as string)}
        options={[
          { label: 'Kørsler', value: 'runs' },
          { label: 'Korrektioner', value: 'corrections' },
        ]}
      />

      {/* Filter bar */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Select
            value={statusFilter}
            onChange={setStatusFilter}
            style={{ flex: 1 }}
            options={[
              { value: 'all', label: 'Alle statusser' },
              { value: 'Calculated', label: 'Beregnet' },
              { value: 'Invoiced', label: 'Faktureret' },
              { value: 'Adjusted', label: 'Justeret' },
            ]}
          />
          <Input
            placeholder="Målepunkt..."
            style={{ flex: 1 }}
          />
          <Input
            placeholder="Netområde..."
            style={{ flex: 1 }}
          />
          <DatePicker placeholder="Fra dato" style={{ flex: 1 }} />
          <DatePicker placeholder="Til dato" style={{ flex: 1 }} />
        </div>
      </Card>

      {/* Stats — 4 cards */}
      <Row gutter={16}>
        {[
          { title: 'Kørsler i alt', value: allDocs.length },
          { title: 'Klar til fakturering', value: allDocs.filter(d => d.status === 'Calculated').length, color: '#10b981' },
          { title: 'Korrektioner', value: corrections.length, color: corrections.length > 0 ? '#dc2626' : undefined },
          { title: 'På denne side', value: statusFiltered.length },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={s.title}
                value={s.value}
                styles={{ content: { fontSize: 36, fontWeight: 700, color: s.color || '#1a202c' } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Table */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <Table
          dataSource={statusFiltered}
          columns={columns}
          rowKey="settlementId"
          pagination={statusFiltered.length > 20 ? { pageSize: 20 } : false}
          onRow={record => ({
            onClick: () => navigate(`/settlements/${record.settlementId}`),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>
    </Space>
  );
}
