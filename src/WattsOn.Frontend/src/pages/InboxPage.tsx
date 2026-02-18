import { useEffect, useState } from 'react';
import { Card, Table, Typography, Space, Row, Col, Statistic, Spin, Select, Segmented } from 'antd';
import type { InboxMessage } from '../api/client';
import { getInbox } from '../api/client';

const { Text } = Typography;

const formatDateTime = (d: string) => new Date(d).toLocaleString('da-DK');

export default function InboxPage() {
  const [data, setData] = useState<InboxMessage[]>([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<string>('inbound');

  useEffect(() => {
    setLoading(true);
    getInbox()
      .then(res => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const processed = data.filter(m => m.isProcessed).length;
  const unresolved = data.filter(m => m.processingError).length;

  const columns = [
    {
      title: 'BESKEDTYPE',
      key: 'type',
      render: (_: any, record: InboxMessage) => (
        <Text style={{ fontWeight: 500 }}>{record.documentType}</Text>
      ),
    },
    {
      title: 'STATUS',
      key: 'status',
      width: 130,
      render: (_: any, record: InboxMessage) => {
        if (record.isProcessed) return <span className="pill-badge green">processed</span>;
        if (record.processingError) return <span className="pill-badge red">error</span>;
        return <span className="pill-badge blue">pending</span>;
      },
    },
    {
      title: 'FORRETNINGSPROCES',
      dataIndex: 'businessProcess',
      key: 'businessProcess',
      render: (v: string | null) => v || <Text style={{ color: '#9ca3af' }}>—</Text>,
    },
    {
      title: 'AFSENDER',
      dataIndex: 'senderGln',
      key: 'senderGln',
      render: (gln: string) => <span className="mono">{gln}</span>,
    },
    {
      title: 'MODTAGET',
      dataIndex: 'receivedAt',
      key: 'receivedAt',
      render: (d: string) => <Text style={{ color: '#6b7280' }}>{formatDateTime(d)}</Text>,
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Beskeder</h2>
        <div className="page-subtitle">Communication with DataHub</div>
      </div>

      {/* Stats */}
      <Row gutter={16}>
        {[
          { title: 'Total Messages', value: data.length },
          { title: 'Inbound', value: data.length },
          { title: 'Processed', value: processed, color: '#10b981' },
          { title: 'Unresolved Errors', value: unresolved, color: unresolved > 0 ? '#dc2626' : undefined },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={<span style={{ color: s.color }}>{s.title}</span>}
                value={s.value}
                styles={{ content: { fontSize: 36, fontWeight: 700, color: s.color || '#1a202c' } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Tab pills + filter + table */}
      <div>
        <Segmented
          value={tab}
          onChange={v => setTab(v as string)}
          options={[
            { label: 'Inbound Messages', value: 'inbound' },
            { label: 'Outbound Requests', value: 'outbound' },
          ]}
          style={{ marginBottom: 16 }}
        />

        <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
          <div className="filter-bar">
            <Select
              defaultValue="all"
              style={{ width: 180 }}
              options={[
                { value: 'all', label: 'All message types' },
                { value: 'RSM-001', label: 'RSM-001' },
                { value: 'RSM-004', label: 'RSM-004' },
              ]}
            />
            <Select
              defaultValue="all"
              style={{ width: 160 }}
              options={[
                { value: 'all', label: 'All statuses' },
                { value: 'processed', label: 'Processed' },
                { value: 'pending', label: 'Venter' },
                { value: 'error', label: 'Fejl' },
              ]}
            />
          </div>
          <Table
            dataSource={data}
            columns={columns}
            rowKey="id"
            pagination={data.length > 20 ? { pageSize: 20 } : false}
          />
        </Card>
      </div>
    </Space>
  );
}
