import { useEffect, useState } from 'react';
import {
  Card, Table, Typography, Space, Row, Col, Statistic,
  Spin, Input, Select, Modal, Steps, Descriptions, Button,
  Dropdown, Form, DatePicker, message,
} from 'antd';
import { PlusOutlined, DownOutlined } from '@ant-design/icons';
import type { BrsProcess } from '../api/client';
import {
  getProcesser, getProcess, requestAggregatedData,
  requestWholesaleSettlement, requestPrices,
} from '../api/client';

const { Text } = Typography;

const statusMap: Record<string, { dot: string; label: string }> = {
  Created: { dot: 'blue', label: 'oprettet' },
  Submitted: { dot: 'blue', label: 'indsendt' },
  Received: { dot: 'blue', label: 'modtaget' },
  Confirmed: { dot: 'green', label: 'bekræftet' },
  InProgress: { dot: 'blue', label: 'i gang' },
  Completed: { dot: 'green', label: 'afsluttet' },
  Rejected: { dot: 'red', label: 'afvist' },
  Cancelled: { dot: 'gray', label: 'annulleret' },
  Failed: { dot: 'red', label: 'fejlet' },
};

import { formatDate, formatDateTime } from '../utils/format';

type RequestModal = 'aggregated' | 'wholesale' | 'prices' | null;

export default function ProcesserPage() {
  const [data, setData] = useState<BrsProcess[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [detail, setDetail] = useState<any>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [activeModal, setActiveModal] = useState<RequestModal>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();

  const loadData = () => {
    setLoading(true);
    getProcesser()
      .then(res => setData(res.data))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadData(); }, []);

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

  const openModal = (type: RequestModal) => {
    form.resetFields();
    setActiveModal(type);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      switch (activeModal) {
        case 'aggregated':
          await requestAggregatedData({
            gridArea: values.gridArea,
            startDate: values.startDate.format('YYYY-MM-DD'),
            endDate: values.endDate.format('YYYY-MM-DD'),
            meteringPointType: values.meteringPointType || undefined,
            processType: values.processType || undefined,
          });
          break;
        case 'wholesale':
          await requestWholesaleSettlement({
            gridArea: values.gridArea,
            startDate: values.startDate.format('YYYY-MM-DD'),
            endDate: values.endDate.format('YYYY-MM-DD'),
            energySupplierGln: values.energySupplierGln || undefined,
          });
          break;
        case 'prices':
          await requestPrices({
            startDate: values.startDate.format('YYYY-MM-DD'),
            endDate: values.endDate ? values.endDate.format('YYYY-MM-DD') : undefined,
            priceOwnerGln: values.priceOwnerGln || undefined,
            requestType: values.requestType || undefined,
          });
          break;
      }

      message.success('Process oprettet');
      setActiveModal(null);
      loadData();
    } catch (err: any) {
      if (err.errorFields) return;
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    } finally {
      setSubmitting(false);
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
      render: () => <span style={{ color: '#6b7280', cursor: 'pointer' }}>Se</span>,
    },
  ];

  const modalTitles: Record<string, string> = {
    aggregated: 'Hent aggregerede data (BRS-023)',
    wholesale: 'Hent engrosafregning (BRS-027)',
    prices: 'Hent priser (BRS-034)',
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h2>Processer</h2>
            <div className="page-subtitle">BRS processer med DataHub</div>
          </div>
          <Dropdown
            menu={{
              items: [
                { key: 'aggregated', label: 'Hent aggregerede data (BRS-023)' },
                { key: 'wholesale', label: 'Hent engrosafregning (BRS-027)' },
                { key: 'prices', label: 'Hent priser (BRS-034)' },
              ],
              onClick: ({ key }) => openModal(key as RequestModal),
            }}
          >
            <Button type="primary" icon={<PlusOutlined />}>
              Ny anmodning <DownOutlined />
            </Button>
          </Dropdown>
        </div>
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
              { value: 'Completed', label: 'Afsluttet' },
              { value: 'Rejected', label: 'Afvist' },
              { value: 'Failed', label: 'Fejlet' },
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
          { title: 'Processer i alt', value: data.length },
          { title: 'Afsluttet', value: completed, color: '#10b981' },
          { title: 'I gang', value: data.filter(p => !['Completed', 'Rejected', 'Cancelled', 'Failed'].includes(p.status)).length },
          { title: 'Afvist', value: data.filter(p => p.status === 'Rejected').length, color: data.filter(p => p.status === 'Rejected').length > 0 ? '#dc2626' : undefined },
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

      {/* Process Detail modal */}
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
              <Descriptions.Item label="Transaktions ID">
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
                          {formatDateTime(t.transitionedAt)}
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

      {/* Request modals */}
      <Modal
        open={activeModal !== null}
        title={activeModal ? modalTitles[activeModal] : ''}
        onCancel={() => setActiveModal(null)}
        onOk={handleSubmit}
        confirmLoading={submitting}
        okText="Opret"
        cancelText="Annuller"
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          {/* Aggregated Data (BRS-023) */}
          {activeModal === 'aggregated' && (
            <>
              <Form.Item name="gridArea" label="Netområde" rules={[{ required: true, message: 'Netområde er påkrævet' }]}>
                <Input placeholder="f.eks. DK1" />
              </Form.Item>
              <Form.Item name="startDate" label="Startdato" rules={[{ required: true, message: 'Startdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="endDate" label="Slutdato" rules={[{ required: true, message: 'Slutdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="meteringPointType" label="Målepunkttype">
                <Select allowClear placeholder="Valgfrit" options={[
                  { value: 'E17', label: 'E17 — Forbrug' },
                  { value: 'E18', label: 'E18 — Produktion' },
                ]} />
              </Form.Item>
              <Form.Item name="processType" label="Processtype">
                <Select allowClear placeholder="Valgfrit" options={[
                  { value: 'D04', label: 'D04' },
                  { value: 'D05', label: 'D05' },
                  { value: 'D32', label: 'D32' },
                ]} />
              </Form.Item>
            </>
          )}

          {/* Wholesale Settlement (BRS-027) */}
          {activeModal === 'wholesale' && (
            <>
              <Form.Item name="gridArea" label="Netområde" rules={[{ required: true, message: 'Netområde er påkrævet' }]}>
                <Input placeholder="f.eks. DK1" />
              </Form.Item>
              <Form.Item name="startDate" label="Startdato" rules={[{ required: true, message: 'Startdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="endDate" label="Slutdato" rules={[{ required: true, message: 'Slutdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="energySupplierGln" label="Elleverandør GLN">
                <Input placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}

          {/* Prices (BRS-034) */}
          {activeModal === 'prices' && (
            <>
              <Form.Item name="startDate" label="Startdato" rules={[{ required: true, message: 'Startdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="endDate" label="Slutdato">
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} placeholder="Valgfrit" />
              </Form.Item>
              <Form.Item name="priceOwnerGln" label="Prisejer GLN">
                <Input placeholder="Valgfrit" />
              </Form.Item>
              <Form.Item name="requestType" label="Anmodningstype">
                <Select allowClear placeholder="Valgfrit" options={[
                  { value: 'E0G', label: 'E0G' },
                  { value: 'D48', label: 'D48' },
                ]} />
              </Form.Item>
            </>
          )}
        </Form>
      </Modal>
    </Space>
  );
}
