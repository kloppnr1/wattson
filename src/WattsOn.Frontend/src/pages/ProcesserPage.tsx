import { useEffect, useState } from 'react';
import {
  Card, Table, Typography, Space, Row, Col, Statistic, 
  Spin, Input, Select, Modal, Steps, Descriptions,
} from 'antd';
import type { BrsProcess } from '../api/client';
import { getProcesser, getProcess } from '../api/client';

const { Text } = Typography;

const statusMap: Record<string, { dot: string; label: string }> = {
  Created: { dot: 'blue', label: 'created' },
  Submitted: { dot: 'blue', label: 'submitted' },
  Received: { dot: 'blue', label: 'received' },
  Confirmed: { dot: 'green', label: 'confirmed' },
  InProgress: { dot: 'blue', label: 'in progress' },
  Completed: { dot: 'green', label: 'completed' },
  Rejected: { dot: 'red', label: 'rejected' },
  Cancelled: { dot: 'gray', label: 'cancelled' },
  Failed: { dot: 'red', label: 'failed' },
};

const formatDateTime = (d: string) => new Date(d).toLocaleString('da-DK');
const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');

export default function ProcesserPage() {
  const [data, setData] = useState<BrsProcess[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [detail, setDetail] = useState<any>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  useEffect(() => {
    getProcesser()
      .then(res => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const filtered = data.filter(p => {
    if (search && !p.meteringPointGsrn?.includes(search)) return false;
    if (statusFilter !== 'all' && p.status !== statusFilter) return false;
    if (typeFilter !== 'all' && p.processType !== typeFilter) return false;
    return true;
  });

  const completed = data.filter(p => p.status === 'Completed').length;
  const processTypes = [...new Set(data.map(p => p.processType))];

  const openDetail = async (record: BrsProcess) => {
    setDetailLoading(true);
    setDetail(null);
    try {
      const res = await getProcess(record.id);
      setDetail(res.data);
    } catch {
      setDetail(record);
    } finally {
      setDetailLoading(false);
    }
  };

  const columns = [
    {
      title: 'PROCES TYPE',
      key: 'processType',
      render: (_: any, record: BrsProcess) => (
        <span className="process-link">{record.processType}</span>
      ),
    },
    {
      title: 'MÅLEPUNKT',
      dataIndex: 'meteringPointGsrn',
      key: 'gsrn',
      render: (gsrn: string | null) => gsrn
        ? <span className="gsrn-badge">{gsrn}</span>
        : <Text style={{ color: '#9ca3af' }}>—</Text>,
    },
    {
      title: 'ROLLE',
      dataIndex: 'role',
      key: 'role',
      width: 100,
      render: (role: string) => (
        <span className={`pill-badge ${role === 'Initiator' ? 'blue' : 'orange'}`}>
          {role.toLowerCase()}
        </span>
      ),
    },
    {
      title: 'STATUS',
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (status: string) => {
        const cfg = statusMap[status] || { dot: 'gray', label: status.toLowerCase() };
        return (
          <span className="status-badge">
            <span className={`status-dot ${cfg.dot}`} />
            <span className={`status-text ${cfg.dot}`}>{cfg.label}</span>
          </span>
        );
      },
    },
    {
      title: 'EFFEKTIV DATO',
      dataIndex: 'effectiveDate',
      key: 'effectiveDate',
      render: (d: string | null) => d
        ? <Text style={{ color: '#6b7280' }}>{formatDate(d)}</Text>
        : <Text style={{ color: '#9ca3af' }}>—</Text>,
    },
    {
      title: 'OPRETTET',
      dataIndex: 'startedAt',
      key: 'startedAt',
      render: (d: string) => <Text style={{ color: '#6b7280' }}>{formatDate(d)}</Text>,
    },
    {
      title: '',
      key: 'action',
      width: 60,
      render: () => <span style={{ color: '#6b7280', cursor: 'pointer' }}>View</span>,
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Processer</h2>
        <div className="page-subtitle">BRS processes with DataHub</div>
      </div>

      {/* Filters */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Input
            placeholder="Search by GSRN..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            allowClear
            style={{ flex: 1 }}
          />
          <Select
            value={statusFilter}
            onChange={setStatusFilter}
            style={{ flex: 1 }}
            options={[
              { value: 'all', label: 'All statuses' },
              { value: 'Completed', label: 'Completed' },
              { value: 'Rejected', label: 'Rejected' },
              { value: 'Failed', label: 'Failed' },
            ]}
          />
          <Select
            value={typeFilter}
            onChange={setTypeFilter}
            style={{ flex: 1 }}
            options={[
              { value: 'all', label: 'All types' },
              ...processTypes.map(t => ({ value: t, label: t })),
            ]}
          />
        </div>
      </Card>

      {/* Stats — 4 cards */}
      <Row gutter={16}>
        {[
          { title: 'Total Processes', value: data.length },
          { title: 'Completed', value: completed, color: '#10b981' },
          { title: 'In Progress', value: data.filter(p => !['Completed', 'Rejected', 'Cancelled', 'Failed'].includes(p.status)).length },
          { title: 'Rejected', value: data.filter(p => p.status === 'Rejected').length, color: data.filter(p => p.status === 'Rejected').length > 0 ? '#dc2626' : undefined },
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
          dataSource={filtered}
          columns={columns}
          rowKey="id"
          pagination={filtered.length > 20 ? { pageSize: 20 } : false}
          onRow={record => ({
            onClick: () => openDetail(record),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>

      {/* Detail modal */}
      <Modal
        open={!!detail || detailLoading}
        onCancel={() => setDetail(null)}
        footer={null}
        width={600}
        title={detail && `${detail.processType} — ${detail.status}`}
      >
        {detailLoading ? (
          <Spin style={{ display: 'block', margin: '40px auto' }} />
        ) : detail ? (
          <Space direction="vertical" size={20} style={{ width: '100%' }}>
            <Descriptions size="small" column={2} bordered>
              <Descriptions.Item label="Transaction ID">
                <span className="mono">{detail.transactionId || '—'}</span>
              </Descriptions.Item>
              <Descriptions.Item label="Rolle">
                <span className={`pill-badge ${detail.role === 'Initiator' ? 'blue' : 'orange'}`}>
                  {detail.role?.toLowerCase()}
                </span>
              </Descriptions.Item>
              <Descriptions.Item label="GSRN">
                {detail.meteringPointGsrn ? <span className="gsrn-badge">{detail.meteringPointGsrn}</span> : '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Effektiv dato">
                {detail.effectiveDate ? formatDate(detail.effectiveDate) : '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Startet">{formatDateTime(detail.startedAt)}</Descriptions.Item>
              <Descriptions.Item label="Afsluttet">
                {detail.completedAt ? formatDateTime(detail.completedAt) : '—'}
              </Descriptions.Item>
            </Descriptions>

            {detail.transitions?.length > 0 && (
              <div>
                <Text strong style={{ fontSize: 14, display: 'block', marginBottom: 12 }}>
                  Procesforløb
                </Text>
                <Steps
                  className="process-timeline"
                  direction="vertical"
                  size="small"
                  current={detail.transitions.length}
                  items={detail.transitions.map((t: any) => ({
                    title: (
                      <Space>
                        <Text strong>{t.toState}</Text>
                        <Text className="tnum" style={{ color: '#9ca3af', fontSize: 11 }}>
                          {new Date(t.transitionedAt).toLocaleTimeString('da-DK')}
                        </Text>
                      </Space>
                    ),
                    description: t.reason && (
                      <Text style={{ color: '#6b7280', fontSize: 13 }}>{t.reason}</Text>
                    ),
                    status: 'finish' as const,
                  }))}
                />
              </div>
            )}
          </Space>
        ) : null}
      </Modal>
    </Space>
  );
}
