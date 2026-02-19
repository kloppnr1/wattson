import { useEffect, useState, useCallback } from 'react';
import { Card, Table, Tag, Typography, Space, Switch, Row, Col, Statistic, Spin, Empty, Button, message, Tooltip } from 'antd';
import {
  CheckCircleOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  StopOutlined,
  ReloadOutlined,
  RetweetOutlined,
} from '@ant-design/icons';
import { getOutbox, retryOutboxMessage } from '../api/client';

const { Text, Paragraph } = Typography;

const formatDateTime = (d: string) => new Date(d).toLocaleString('da-DK');

const MAX_RETRIES = 5; // must match DataHubSettings.MaxRetries

type MessageStatus = 'sent' | 'pending' | 'failing' | 'dead';

function getStatus(record: any): MessageStatus {
  if (record.isSent) return 'sent';
  if (record.sendError && record.sendAttempts >= MAX_RETRIES) return 'dead';
  if (record.sendError) return 'failing';
  return 'pending';
}

export default function OutboxPage() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [unsentOnly, setUnsentOnly] = useState(false);

  const loadData = useCallback((unsent: boolean) => {
    setLoading(true);
    getOutbox(unsent || undefined)
      .then(res => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { loadData(unsentOnly); }, [unsentOnly, loadData]);

  // Auto-refresh every 15s
  useEffect(() => {
    const interval = setInterval(() => loadData(unsentOnly), 15000);
    return () => clearInterval(interval);
  }, [unsentOnly, loadData]);

  const handleRetry = async (id: string) => {
    try {
      await retryOutboxMessage(id);
      message.success('Besked sat i kø til gensendelse');
      loadData(unsentOnly);
    } catch {
      message.error('Kunne ikke gensende besked');
    }
  };

  const sent = data.filter(m => getStatus(m) === 'sent').length;
  const pending = data.filter(m => getStatus(m) === 'pending').length;
  const failing = data.filter(m => getStatus(m) === 'failing').length;
  const dead = data.filter(m => getStatus(m) === 'dead').length;

  const statusTag = (record: any) => {
    const status = getStatus(record);
    switch (status) {
      case 'sent':
        return <Tag icon={<CheckCircleOutlined />} color="green">Sendt</Tag>;
      case 'dead':
        return <Tag icon={<StopOutlined />} color="red">Fejlet</Tag>;
      case 'failing':
        return <Tag icon={<ExclamationCircleOutlined />} color="orange">Fejl ({record.sendAttempts}/{MAX_RETRIES})</Tag>;
      default:
        return <Tag icon={<ClockCircleOutlined />} color="blue">Venter</Tag>;
    }
  };

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
      title: 'AFSENDER',
      dataIndex: 'senderGln',
      key: 'senderGln',
      responsive: ['lg' as const],
      render: (gln: string) => <Text className="mono" style={{ fontSize: 12 }}>{gln}</Text>,
    },
    {
      title: 'MODTAGER',
      dataIndex: 'receiverGln',
      key: 'receiverGln',
      render: (gln: string) => <Text className="mono" style={{ fontSize: 12 }}>{gln}</Text>,
    },
    {
      title: 'STATUS',
      key: 'status',
      width: 150,
      render: (_: any, record: any) => statusTag(record),
    },
    {
      title: 'FORSØG',
      dataIndex: 'sendAttempts',
      key: 'sendAttempts',
      width: 80,
      align: 'center' as const,
      render: (attempts: number) => (
        <Text className="tnum" type="secondary">{attempts}</Text>
      ),
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
    {
      title: '',
      key: 'actions',
      width: 50,
      render: (_: any, record: any) => {
        const status = getStatus(record);
        if (status === 'dead' || status === 'failing') {
          return (
            <Tooltip title="Gensend">
              <Button
                type="text"
                size="small"
                icon={<RetweetOutlined />}
                onClick={(e) => { e.stopPropagation(); handleRetry(record.id); }}
              />
            </Tooltip>
          );
        }
        return null;
      },
    },
  ];

  const expandedRow = (record: any) => {
    const status = getStatus(record);
    return (
      <div style={{ padding: '8px 0' }}>
        <Row gutter={24}>
          <Col span={12}>
            <Text strong style={{ fontSize: 12, textTransform: 'uppercase', color: '#8899a6' }}>
              Payload
            </Text>
            <Paragraph
              code
              copyable
              style={{ fontSize: 11, maxHeight: 200, overflow: 'auto', marginTop: 4 }}
            >
              {formatJson(record.payload)}
            </Paragraph>
          </Col>
          <Col span={12}>
            {record.response && (
              <>
                <Text strong style={{ fontSize: 12, textTransform: 'uppercase', color: '#8899a6' }}>
                  Svar
                </Text>
                <Paragraph
                  code
                  style={{ fontSize: 11, maxHeight: 200, overflow: 'auto', marginTop: 4 }}
                >
                  {formatJson(record.response)}
                </Paragraph>
              </>
            )}
            {record.sendError && (
              <>
                <Text strong style={{ fontSize: 12, textTransform: 'uppercase', color: '#e11d48' }}>
                  Fejl
                </Text>
                <Paragraph
                  style={{ fontSize: 12, color: '#e11d48', marginTop: 4 }}
                >
                  {record.sendError}
                </Paragraph>
              </>
            )}
            {record.updatedAt && (
              <div style={{ marginTop: 8 }}>
                <Text type="secondary" style={{ fontSize: 11 }}>
                  Sidst opdateret: {formatDateTime(record.updatedAt)}
                </Text>
              </div>
            )}
            {(status === 'dead' || status === 'failing') && (
              <Button
                type="primary"
                danger={status === 'dead'}
                icon={<RetweetOutlined />}
                size="small"
                style={{ marginTop: 12 }}
                onClick={() => handleRetry(record.id)}
              >
                Gensend besked
              </Button>
            )}
          </Col>
        </Row>
      </div>
    );
  };

  if (loading && data.length === 0) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h2>Outbox</h2>
            <div className="page-subtitle">Beskeder sendt til DataHub</div>
          </div>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => loadData(unsentOnly)}
            loading={loading}
          >
            Opdater
          </Button>
        </div>
      </div>

      <Row gutter={16}>
        <Col xs={12} md={6}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Sendt"
              value={sent}
              prefix={<CheckCircleOutlined style={{ color: '#059669' }} />}
              styles={{ content: { color: '#059669', fontSize: 28 } }}
            />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Venter"
              value={pending}
              prefix={<ClockCircleOutlined style={{ color: '#3b82f6' }} />}
              styles={{ content: { color: '#3b82f6', fontSize: 28 } }}
            />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Fejlende"
              value={failing}
              prefix={<ExclamationCircleOutlined style={{ color: failing > 0 ? '#f59e0b' : '#99afc2' }} />}
              styles={{ content: { color: failing > 0 ? '#f59e0b' : '#99afc2', fontSize: 28 } }}
            />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card style={{ borderRadius: 12 }}>
            <Statistic
              title="Fejlet"
              value={dead}
              prefix={<StopOutlined style={{ color: dead > 0 ? '#e11d48' : '#99afc2' }} />}
              styles={{ content: { color: dead > 0 ? '#e11d48' : '#99afc2', fontSize: 28 } }}
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
            expandable={{
              expandedRowRender: expandedRow,
              expandRowByClick: true,
            }}
            style={{ borderRadius: 0 }}
          />
        ) : (
          <Empty description="Ingen beskeder" style={{ padding: 40 }} image={Empty.PRESENTED_IMAGE_SIMPLE} />
        )}
      </Card>
    </Space>
  );
}

function formatJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
