import { useEffect, useState } from 'react';
import { Card, Table, Tag, Typography, Space, Switch, Row, Col, Statistic, Spin, Empty } from 'antd';
import { SendOutlined, CheckCircleOutlined, ClockCircleOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { getOutbox } from '../api/client';

const { Text } = Typography;

const formatDateTime = (d: string) => new Date(d).toLocaleString('da-DK');

export default function OutboxPage() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [unsentOnly, setUnsentOnly] = useState(false);

  const loadData = (unsent: boolean) => {
    setLoading(true);
    getOutbox(unsent || undefined)
      .then(res => setData(res.data))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadData(unsentOnly); }, [unsentOnly]);

  const sent = data.filter(m => m.isSent).length;
  const pending = data.filter(m => !m.isSent && !m.sendError).length;
  const errors = data.filter(m => m.sendError).length;

  const columns = [
    {
      title: 'BESKED',
      key: 'message',
      render: (_: any, record: any) => (
        <Space direction="vertical" size={0}>
          <Text strong style={{ fontSize: 13 }}>{record.documentType}</Text>
          <Text type="secondary" style={{ fontSize: 11 }}>
            {record.businessProcess || '—'}
          </Text>
        </Space>
      ),
    },
    {
      title: 'MODTAGER',
      dataIndex: 'receiverGln',
      key: 'receiverGln',
      render: (gln: string) => <Text className="mono">{gln}</Text>,
    },
    {
      title: 'STATUS',
      key: 'status',
      width: 130,
      render: (_: any, record: any) => {
        if (record.isSent)
          return <Tag icon={<CheckCircleOutlined />} color="green">Sendt</Tag>;
        if (record.sendError)
          return <Tag icon={<ExclamationCircleOutlined />} color="red">Fejl ({record.sendAttempts})</Tag>;
        return <Tag icon={<ClockCircleOutlined />} color="blue">Venter</Tag>;
      },
    },
    {
      title: 'OPRETTET',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      render: (d: string) => (
        <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>{formatDateTime(d)}</Text>
      ),
    },
    {
      title: 'SENDT',
      dataIndex: 'sentAt',
      key: 'sentAt',
      width: 160,
      render: (d: string | null) => d
        ? <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>{formatDateTime(d)}</Text>
        : <Text type="secondary">—</Text>,
    },
  ];

  if (loading && data.length === 0) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Outbox</h2>
        <div className="page-subtitle">Beskeder sendt til DataHub</div>
      </div>

      <Row gutter={16}>
        <Col xs={8}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Sendt"
              value={sent}
              prefix={<CheckCircleOutlined style={{ color: '#059669' }} />}
              valueStyle={{ color: '#059669', fontSize: 28 }}
            />
          </Card>
        </Col>
        <Col xs={8}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Venter"
              value={pending}
              prefix={<ClockCircleOutlined style={{ color: '#3b82f6' }} />}
              valueStyle={{ color: '#3b82f6', fontSize: 28 }}
            />
          </Card>
        </Col>
        <Col xs={8}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Fejl"
              value={errors}
              prefix={<ExclamationCircleOutlined style={{ color: errors > 0 ? '#e11d48' : '#99afc2' }} />}
              valueStyle={{ color: errors > 0 ? '#e11d48' : '#99afc2', fontSize: 28 }}
            />
          </Card>
        </Col>
      </Row>

      <Card style={{ borderRadius: 12, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div style={{ padding: '16px 20px', borderBottom: '1px solid #e4e9ee', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Text type="secondary" style={{ fontSize: 13 }}>{data.length} beskeder</Text>
          <Space>
            <Text type="secondary" style={{ fontSize: 12 }}>Kun uafsendte</Text>
            <Switch size="small" checked={unsentOnly} onChange={setUnsentOnly} />
          </Space>
        </div>
        {data.length > 0 ? (
          <Table
            dataSource={data}
            columns={columns}
            rowKey="id"
            loading={loading}
            pagination={data.length > 20 ? { pageSize: 20 } : false}
            style={{ borderRadius: 0 }}
          />
        ) : (
          <Empty description="Ingen beskeder" style={{ padding: 40 }} image={Empty.PRESENTED_IMAGE_SIMPLE} />
        )}
      </Card>
    </Space>
  );
}
