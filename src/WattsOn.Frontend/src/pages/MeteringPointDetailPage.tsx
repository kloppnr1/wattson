import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography, Button,
  Modal, Form, DatePicker, Select, Input, message, Row, Col, Tabs, Empty,
} from 'antd';
import {
  ArrowLeftOutlined, DatabaseOutlined, ToolOutlined, FireOutlined,
  BarChartOutlined, CalendarOutlined, LinkOutlined, ThunderboltOutlined,
  HomeOutlined,
} from '@ant-design/icons';
import type { MeteringPointDetail } from '../api/client';
import { formatDate, formatDateTime } from '../utils/format';

const { Text, Title } = Typography;
import {
  getMeteringPoint, requestMasterData, createServiceRequest,
  toggleElectricalHeating, requestMeteredData, requestYearlySum,
  requestChargeLinks,
} from '../api/client';

type ModalType = 'service' | 'heating' | 'meteredData' | 'chargeLinks' | null;

export default function MeteringPointDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [mp, setMp] = useState<MeteringPointDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeModal, setActiveModal] = useState<ModalType>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();
  const navigate = useNavigate();

  useEffect(() => {
    if (!id) return;
    getMeteringPoint(id)
      .then((res) => setMp(res.data))
      .catch((err) => setError(err.response?.status === 404 ? 'MeteringPoint ikke fundet' : err.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!mp) return null;

  const gsrn = mp.gsrn;

  const connectionStateColors: Record<string, string> = {
    Tilsluttet: 'green', Afbrudt: 'red', Ny: 'blue', Nedlagt: 'default',
  };

  const handleRequestMasterData = async () => {
    try {
      await requestMasterData({ gsrn });
      message.success('Stamdata anmodet');
    } catch (err: any) {
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    }
  };

  const handleRequestYearlySum = async () => {
    try {
      await requestYearlySum({ gsrn });
      message.success('Årssum anmodet');
    } catch (err: any) {
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    }
  };

  const openModal = (type: ModalType) => {
    form.resetFields();
    setActiveModal(type);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      switch (activeModal) {
        case 'service':
          await createServiceRequest({
            gsrn,
            serviceType: values.serviceType,
            requestedDate: values.requestedDate.format('YYYY-MM-DD'),
            reason: values.reason || undefined,
          });
          break;
        case 'heating':
          await toggleElectricalHeating({
            gsrn,
            action: values.action,
            effectiveDate: values.effectiveDate.format('YYYY-MM-DD'),
          });
          break;
        case 'meteredData':
          await requestMeteredData({
            gsrn,
            startDate: values.startDate.format('YYYY-MM-DD'),
            endDate: values.endDate.format('YYYY-MM-DD'),
          });
          break;
        case 'chargeLinks':
          await requestChargeLinks({
            gsrn,
            startDate: values.startDate.format('YYYY-MM-DD'),
            endDate: values.endDate ? values.endDate.format('YYYY-MM-DD') : undefined,
          });
          break;
      }

      message.success('Process oprettet');
      setActiveModal(null);
    } catch (err: any) {
      if (err.errorFields) return;
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    } finally {
      setSubmitting(false);
    }
  };

  const supplyColumns = [
    {
      title: 'Kunde',
      dataIndex: 'customerName',
      key: 'customerName',
      render: (name: string, record: any) => (
        <a onClick={() => navigate(`/customers/${record.customerId}`)}>{name}</a>
      ),
    },
    {
      title: 'Fra',
      dataIndex: 'supplyStart',
      key: 'supplyStart',
      render: (d: string) => formatDate(d),
    },
    {
      title: 'Til',
      dataIndex: 'supplyEnd',
      key: 'supplyEnd',
      render: (d: string | null) => d ? formatDate(d) : '→',
    },
    {
      title: 'Status',
      dataIndex: 'isActive',
      key: 'isActive',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>,
    },
  ];

  const time_seriesColumns = [
    {
      title: 'Periode',
      key: 'period',
      render: (_: any, record: any) =>
        (() => {
          const start = formatDate(record.periodStart);
          if (!record.periodEnd) return `${start} → →`;
          return `${start} → ${formatDate(record.periodEnd)}`;
        })(),
    },
    { title: 'Opløsning', dataIndex: 'resolution', key: 'resolution' },
    { title: 'Version', dataIndex: 'version', key: 'version' },
    {
      title: 'Seneste',
      dataIndex: 'isLatest',
      key: 'isLatest',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Ja' : 'Nej'}</Tag>,
    },
    {
      title: 'Modtaget',
      dataIndex: 'receivedAt',
      key: 'receivedAt',
      render: (d: string) => formatDateTime(d),
    },
  ];

  const modalTitles: Record<string, string> = {
    service: 'Serviceydelse (BRS-039)',
    heating: 'Elvarme (BRS-041)',
    meteredData: 'Hent måledata (BRS-025)',
    chargeLinks: 'Hent pristilknytninger (BRS-038)',
  };

  const activeSupplies = mp.supplies.filter(s => s.isActive);

  return (
    <Space direction="vertical" size={20} style={{ width: '100%' }}>
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/metering-points')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        Målepunkter
      </Button>

      {/* Hero card */}
      <Card style={{ borderRadius: 12 }}>
        <Row gutter={24} align="middle">
          <Col flex="auto">
            <Space size={12} align="center">
              <div style={{
                width: 48, height: 48, borderRadius: 14,
                background: 'linear-gradient(135deg, #7c3aed20, #7c3aed10)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <ThunderboltOutlined style={{ fontSize: 20, color: '#7c3aed' }} />
              </div>
              <div>
                <Title level={3} style={{ margin: 0 }} className="mono">{mp.gsrn}</Title>
                <Space size={8} style={{ marginTop: 4 }}>
                  <Tag color={connectionStateColors[mp.connectionState] || 'default'}>{mp.connectionState}</Tag>
                  <Tag color={mp.hasActiveSupply ? 'green' : 'default'}>
                    {mp.hasActiveSupply ? 'Aktiv forsyning' : 'Ingen forsyning'}
                  </Tag>
                  <Text type="secondary">{mp.gridArea}</Text>
                </Space>
              </div>
            </Space>
          </Col>
          <Col>
            <div style={{ textAlign: 'right' }}>
              <div className="micro-label">Aktive forsyninger</div>
              <div className="amount-large" style={{
                marginTop: 4,
                color: activeSupplies.length > 0 ? '#059669' : '#99afc2',
              }}>
                {activeSupplies.length}
              </div>
            </div>
          </Col>
        </Row>
      </Card>

      {/* Three info cards in a row */}
      <Row gutter={16}>
        <Col xs={24} md={8}>
          <Card title="Teknisk info" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Descriptions size="small" column={1} colon={false}>
              <Descriptions.Item label={<span className="micro-label">TYPE</span>}>{mp.type}</Descriptions.Item>
              <Descriptions.Item label={<span className="micro-label">ART</span>}>{mp.art}</Descriptions.Item>
              <Descriptions.Item label={<span className="micro-label">OPLØSNING</span>}>{mp.resolution}</Descriptions.Item>
              <Descriptions.Item label={<span className="micro-label">AFREGNINGSMETODE</span>}>{mp.settlementMethod}</Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Lokation" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={4}>
              {mp.address ? (
                <Space>
                  <HomeOutlined style={{ color: '#7593a9' }} />
                  <div>
                    <Text>{mp.address.streetName} {mp.address.buildingNumber}</Text>
                    {mp.address.floor && <Text>, {mp.address.floor}.</Text>}
                    {mp.address.suite && <Text> {mp.address.suite}</Text>}
                    <br />
                    <Text>{mp.address.postCode} {mp.address.cityName}</Text>
                  </div>
                </Space>
              ) : (
                <Text type="secondary">Ingen adresse registreret</Text>
              )}
              <div style={{ marginTop: 8 }}>
                <div className="micro-label">NETOMRÅDE</div>
                <Text>{mp.gridArea}</Text>
              </div>
              <div>
                <div className="micro-label">NETVIRKSOMHED GLN</div>
                <Text className="mono">{mp.gridCompanyGln}</Text>
              </div>
            </Space>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Status" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={8}>
              <div>
                <div className="micro-label">TILSLUTNINGSSTATUS</div>
                <Tag color={connectionStateColors[mp.connectionState] || 'default'}>{mp.connectionState}</Tag>
              </div>
              <div>
                <div className="micro-label">AKTIV FORSYNING</div>
                <Tag color={mp.hasActiveSupply ? 'green' : 'default'}>
                  {mp.hasActiveSupply ? 'Ja' : 'Nej'}
                </Tag>
              </div>
              <div>
                <div className="micro-label">OPRETTET</div>
                <Text className="tnum" style={{ fontSize: 12 }}>
                  {formatDateTime(mp.createdAt)}
                </Text>
              </div>
            </Space>
          </Card>
        </Col>
      </Row>

      {/* Actions card */}
      <Card title="Handlinger" style={{ borderRadius: 12 }}>
        <Space wrap>
          <Button type="primary" icon={<DatabaseOutlined />} onClick={handleRequestMasterData}>
            Hent stamdata
          </Button>
          <Button icon={<ToolOutlined />} onClick={() => openModal('service')}>
            Serviceydelse
          </Button>
          <Button icon={<FireOutlined />} onClick={() => openModal('heating')}>
            Elvarme
          </Button>
          <Button icon={<BarChartOutlined />} onClick={() => openModal('meteredData')}>
            Hent måledata
          </Button>
          <Button icon={<CalendarOutlined />} onClick={handleRequestYearlySum}>
            Hent årssum
          </Button>
          <Button icon={<LinkOutlined />} onClick={() => openModal('chargeLinks')}>
            Hent pristilknytninger
          </Button>
        </Space>
      </Card>

      {/* Tabs for Supplies and TimeSeries */}
      <Card style={{ borderRadius: 12 }}>
        <Tabs
          defaultActiveKey="supplies"
          items={[
            {
              key: 'supplies',
              label: `Forsyninger (${mp.supplies.length})`,
              children: mp.supplies.length > 0 ? (
                <Table 
                  dataSource={mp.supplies} 
                  columns={supplyColumns} 
                  rowKey="id" 
                  pagination={false} 
                  size="small" 
                />
              ) : (
                <Empty description="Ingen forsyninger" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
            {
              key: 'timeseries',
              label: `Tidsserier (${mp.time_series.length})`,
              children: mp.time_series.length > 0 ? (
                <Table 
                  dataSource={mp.time_series} 
                  columns={time_seriesColumns} 
                  rowKey="id" 
                  pagination={false} 
                  size="small" 
                />
              ) : (
                <Empty description="Ingen tidsserier" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
          ]}
        />
      </Card>

      {/* Modals */}
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
          {/* Service Request */}
          {activeModal === 'service' && (
            <>
              <Form.Item name="serviceType" label="Servicetype" rules={[{ required: true, message: 'Vælg servicetype' }]}>
                <Select placeholder="Vælg type" options={[
                  { value: 'Disconnect', label: 'Afbrydelse' },
                  { value: 'Reconnect', label: 'Gentilslutning' },
                  { value: 'MeterInvestigation', label: 'Målerundersøgelse' },
                ]} />
              </Form.Item>
              <Form.Item name="requestedDate" label="Ønsket dato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="reason" label="Årsag">
                <Input placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}

          {/* Electrical Heating */}
          {activeModal === 'heating' && (
            <>
              <Form.Item name="action" label="Handling" rules={[{ required: true, message: 'Vælg handling' }]}>
                <Select placeholder="Vælg handling" options={[
                  { value: 'Add', label: 'Tilføj' },
                  { value: 'Remove', label: 'Fjern' },
                ]} />
              </Form.Item>
              <Form.Item name="effectiveDate" label="Effektiv dato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
            </>
          )}

          {/* Metered Data */}
          {activeModal === 'meteredData' && (
            <>
              <Form.Item name="startDate" label="Startdato" rules={[{ required: true, message: 'Startdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="endDate" label="Slutdato" rules={[{ required: true, message: 'Slutdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
            </>
          )}

          {/* Charge Links */}
          {activeModal === 'chargeLinks' && (
            <>
              <Form.Item name="startDate" label="Startdato" rules={[{ required: true, message: 'Startdato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="endDate" label="Slutdato">
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}
        </Form>
      </Modal>
    </Space>
  );
}
