import { useEffect, useState } from 'react';
import {
  Card, Table, Spin, Alert, Space, Typography, Segmented, Row, Col,
  Statistic, Select, Input, DatePicker, Tag, Button, Tooltip, Badge,
} from 'antd';
import {
  SwapOutlined, WarningOutlined, ExclamationCircleOutlined,
  CheckCircleOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { SettlementDocument } from '../api/client';
import { getSettlementDocuments } from '../api/client';
import { formatDate, formatDateTime, formatPeriodEnd } from '../utils/format';
import api from '../api/client';

const { Text } = Typography;

const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);

interface SettlementIssue {
  id: string;
  meteringPointId: string;
  timeSeriesId: string;
  timeSeriesVersion: number;
  periodStart: string;
  periodEnd: string | null;
  issueType: string;
  message: string;
  details: string;
  status: string;
  resolvedAt: string | null;
  createdAt: string;
}

export default function SettlementsPage() {
  const [allDocs, setAllDocs] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<string>('runs');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const navigate = useNavigate();

  // Settlement issues
  const [issues, setIssues] = useState<SettlementIssue[]>([]);
  const [issuesLoading, setIssuesLoading] = useState(false);
  const [dismissingId, setDismissingId] = useState<string | null>(null);

  const fetchIssues = async (status = 'Open') => {
    setIssuesLoading(true);
    try {
      const res = await api.get<SettlementIssue[]>(`/settlement-issues?status=${status}`);
      setIssues(res.data);
    } catch { setIssues([]); }
    finally { setIssuesLoading(false); }
  };

  useEffect(() => {
    Promise.all([
      getSettlementDocuments('all'),
      api.get<SettlementIssue[]>('/settlement-issues?status=Open'),
    ])
      .then(([docsRes, issuesRes]) => {
        setAllDocs(docsRes.data);
        setIssues(issuesRes.data);
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  const handleDismiss = async (id: string) => {
    setDismissingId(id);
    try {
      await api.post(`/settlement-issues/${id}/dismiss`);
      setIssues(prev => prev.filter(i => i.id !== id));
    } catch { /* ignore */ }
    finally { setDismissingId(null); }
  };

  if (error) return <Alert type="error" message="Kunne ikke hente settlements" description={error} />;

  const runs = allDocs.filter(d => d.documentType === 'settlement');
  const corrections = allDocs.filter(d => d.documentType !== 'settlement');
  const filtered = tab === 'runs' ? runs : tab === 'corrections' ? corrections : [];
  const statusFiltered = statusFilter === 'all' ? filtered
    : filtered.filter(d => d.status === statusFilter);

  const openIssueCount = issues.length;

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
      render: (_: unknown, record: SettlementDocument) => (
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

  const issueColumns = [
    {
      title: 'TYPE',
      dataIndex: 'issueType',
      key: 'issueType',
      width: 180,
      render: (type: string) => (
        <Tag
          color={type === 'MissingPriceElements' ? 'red' : 'orange'}
          icon={<ExclamationCircleOutlined />}
        >
          {type === 'MissingPriceElements' ? 'Manglende priser' : 'Prisdækning'}
        </Tag>
      ),
    },
    {
      title: 'MÅLEPUNKT',
      dataIndex: 'meteringPointId',
      key: 'mp',
      ellipsis: true,
      render: (id: string) => (
        <Tooltip title={id}>
          <Text className="mono" style={{ fontSize: 12 }}>{id.slice(0, 8)}…</Text>
        </Tooltip>
      ),
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: unknown, record: SettlementIssue) => (
        <Text className="tnum">
          {formatDate(record.periodStart)} — {record.periodEnd ? formatPeriodEnd(record.periodEnd) : '→'}
        </Text>
      ),
    },
    {
      title: 'BESKED',
      dataIndex: 'message',
      key: 'message',
      ellipsis: { showTitle: true },
    },
    {
      title: 'MANGLER',
      dataIndex: 'details',
      key: 'details',
      render: (details: string) => (
        <Space size={4} wrap>
          {details.split('\n').map((d, i) => (
            <Tag key={i} color="red" style={{ fontSize: 11 }}>{d}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: 'OPDAGET',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 140,
      render: (d: string) => <Text type="secondary">{formatDateTime(d)}</Text>,
    },
    {
      title: '',
      key: 'actions',
      width: 100,
      render: (_: unknown, record: SettlementIssue) => (
        <Button
          size="small"
          icon={<CheckCircleOutlined />}
          loading={dismissingId === record.id}
          onClick={(e) => { e.stopPropagation(); handleDismiss(record.id); }}
        >
          Afvis
        </Button>
      ),
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Afregninger</h2>
        <div className="page-subtitle">Afregningskørsler, korrektioner og blokeringer</div>
      </div>

      {/* Issue alert banner */}
      {openIssueCount > 0 && (
        <Alert
          type="error"
          showIcon
          icon={<WarningOutlined />}
          message={
            <span>
              <strong>{openIssueCount} blokeret afregning{openIssueCount > 1 ? 'er' : ''}</strong>
              {' — '}manglende priselementer forhindrer korrekt beregning
            </span>
          }
          description="Se fanen 'Blokeringer' for detaljer. Afregning kører automatisk når priserne er på plads."
          action={
            <Button
              type="primary"
              danger
              size="small"
              onClick={() => setTab('issues')}
            >
              Vis blokeringer
            </Button>
          }
          style={{ borderRadius: 8 }}
        />
      )}

      {/* Tabs */}
      <Segmented
        value={tab}
        onChange={v => { setTab(v as string); if (v === 'issues') fetchIssues(); }}
        options={[
          { label: 'Kørsler', value: 'runs' },
          { label: 'Korrektioner', value: 'corrections' },
          {
            label: (
              <Badge count={openIssueCount} size="small" offset={[8, -2]}>
                <span style={{ color: openIssueCount > 0 ? '#dc2626' : undefined }}>
                  Blokeringer
                </span>
              </Badge>
            ),
            value: 'issues',
          },
        ]}
      />

      {tab !== 'issues' && (
        <>
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
              <Input placeholder="Målepunkt..." style={{ flex: 1 }} />
              <Input placeholder="Netområde..." style={{ flex: 1 }} />
              <DatePicker placeholder="Fra dato" style={{ flex: 1 }} />
              <DatePicker placeholder="Til dato" style={{ flex: 1 }} />
            </div>
          </Card>

          {/* Stats */}
          <Row gutter={16}>
            {[
              { title: 'Kørsler i alt', value: allDocs.length },
              { title: 'Klar til fakturering', value: allDocs.filter(d => d.status === 'Calculated').length, color: '#10b981' },
              { title: 'Korrektioner', value: corrections.length, color: corrections.length > 0 ? '#dc2626' : undefined },
              { title: 'Blokeringer', value: openIssueCount, color: openIssueCount > 0 ? '#dc2626' : undefined },
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

          {/* Settlements table */}
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
        </>
      )}

      {tab === 'issues' && (
        <Card style={{ borderRadius: 8 }}>
          {issuesLoading ? (
            <Spin size="large" style={{ display: 'block', margin: '40px auto' }} />
          ) : issues.length > 0 ? (
            <Table
              dataSource={issues}
              columns={issueColumns}
              rowKey="id"
              pagination={issues.length > 20 ? { pageSize: 20 } : false}
            />
          ) : (
            <Alert
              type="success"
              showIcon
              icon={<CheckCircleOutlined />}
              message="Ingen blokeringer"
              description="Alle afregninger er beregnet korrekt. Ingen manglende priselementer."
              style={{ borderRadius: 8 }}
            />
          )}
        </Card>
      )}
    </Space>
  );
}
