import { useEffect, useState } from 'react';
import {
  Card, Table, Typography, Space, Row, Col, Statistic, 
  Spin, Input, Select, Modal, Steps, Descriptions,
} from 'antd';
import type { BrsProcess } from '../api/client';
import { getProcesser, getProcess } from '../api/client';

const { Text } = Typography;

const statusMap: Record<string, { dot: string; label: string }> = {
  Oprettet: { dot: 'blue', label: 'oprettet' },
  Indsendt: { dot: 'blue', label: 'indsendt' },
  Modtaget: { dot: 'blue', label: 'modtaget' },
  Bekræftet: { dot: 'green', label: 'bekræftet' },
  IgangVærende: { dot: 'blue', label: 'igangværende' },
  Gennemført: { dot: 'green', label: 'gennemført' },
  Afvist: { dot: 'red', label: 'afvist' },
  Annulleret: { dot: 'gray', label: 'annulleret' },
  Fejlet: { dot: 'red', label: 'fejlet' },
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
    if (search && !p.målepunktGsrn?.includes(search)) return false;
    if (statusFilter !== 'all' && p.status !== statusFilter) return false;
    if (typeFilter !== 'all' && p.processType !== typeFilter) return false;
    return true;
  });

  const completed = data.filter(p => p.status === 'Gennemført').length;
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
      dataIndex: 'målepunktGsrn',
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
      render: () => <span style={{ color: '#6b7280', cursor: 'pointer' }}>Vis</span>,
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Processer</h2>
        <div className="page-subtitle">BRS-processer med DataHub</div>
      </div>

      {/* Filters */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Input
            placeholder="Søg efter GSRN..."
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
              { value: 'all', label: 'Alle statusser' },
              { value: 'Gennemført', label: 'Gennemført' },
              { value: 'Afvist', label: 'Afvist' },
              { value: 'Fejlet', label: 'Fejlet' },
            ]}
          />
          <Select
            value={typeFilter}
            onChange={setTypeFilter}
            style={{ flex: 1 }}
            options={[
              { value: 'all', label: 'Alle typer' },
              ...processTypes.map(t => ({ value: t, label: t })),
            ]}
          />
        </div>
      </Card>

      {/* Stats — 4 cards */}
      <Row gutter={16}>
        {[
          { title: 'Total processer', value: data.length },
          { title: 'Gennemført', value: completed, color: '#10b981' },
          { title: 'Igangværende', value: data.filter(p => !['Gennemført', 'Afvist', 'Annulleret', 'Fejlet'].includes(p.status)).length },
          { title: 'Afvist', value: data.filter(p => p.status === 'Afvist').length, color: data.filter(p => p.status === 'Afvist').length > 0 ? '#dc2626' : undefined },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={s.title}
                value={s.value}
                valueStyle={{ fontSize: 36, fontWeight: 700, color: s.color || '#1a202c' }}
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
                {detail.målepunktGsrn ? <span className="gsrn-badge">{detail.målepunktGsrn}</span> : '—'}
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
