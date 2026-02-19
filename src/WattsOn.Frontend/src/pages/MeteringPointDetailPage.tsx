import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography, Button,
  Modal, Form, DatePicker, Select, Input, message,
} from 'antd';
import {
  ArrowLeftOutlined, DatabaseOutlined, ToolOutlined, FireOutlined,
  BarChartOutlined, CalendarOutlined, LinkOutlined,
} from '@ant-design/icons';
import type { MeteringPointDetail } from '../api/client';
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
      title: 'Customer',
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
      render: (d: string) => new Date(d).toLocaleDateString('da-DK'),
    },
    {
      title: 'Til',
      dataIndex: 'supplyEnd',
      key: 'supplyEnd',
      render: (d: string | null) => d ? new Date(d).toLocaleDateString('da-DK') : '→',
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
        `${new Date(record.periodStart).toLocaleDateString('da-DK')} → ${record.periodEnd ? new Date(record.periodEnd).toLocaleDateString('da-DK') : '→'}`,
    },
    { title: 'Opløsning', dataIndex: 'resolution', key: 'resolution' },
    { title: 'Version', dataIndex: 'version', key: 'version' },
    {
      title: 'Latest',
      dataIndex: 'isLatest',
      key: 'isLatest',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Ja' : 'Nej'}</Tag>,
    },
    {
      title: 'Received',
      dataIndex: 'receivedAt',
      key: 'receivedAt',
      render: (d: string) => new Date(d).toLocaleString('da-DK'),
    },
  ];

  const modalTitles: Record<string, string> = {
    service: 'Serviceydelse (BRS-039)',
    heating: 'Elvarme (BRS-041)',
    meteredData: 'Hent måledata (BRS-025)',
    chargeLinks: 'Hent pristilknytninger (BRS-038)',
  };

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/metering-points')}>Back</Button>

      <Card>
        <Typography.Title level={3} style={{ fontFamily: 'monospace' }}>{mp.gsrn}</Typography.Title>
        <Descriptions column={2} bordered size="small">
          <Descriptions.Item label="Type">{mp.type}</Descriptions.Item>
          <Descriptions.Item label="Art">{mp.art}</Descriptions.Item>
          <Descriptions.Item label="Settlementsmetode">{mp.settlementMethod}</Descriptions.Item>
          <Descriptions.Item label="Opløsning">{mp.resolution}</Descriptions.Item>
          <Descriptions.Item label="Tilstand">
            <Tag color={connectionStateColors[mp.connectionState] || 'default'}>{mp.connectionState}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Aktiv forsyning">
            <Tag color={mp.hasActiveSupply ? 'green' : 'default'}>{mp.hasActiveSupply ? 'Ja' : 'Nej'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Netområde">{mp.gridArea}</Descriptions.Item>
          <Descriptions.Item label="Netvirksomhed GLN">{mp.gridCompanyGln}</Descriptions.Item>
          {mp.address && (
            <Descriptions.Item label="Adresse" span={2}>
              {mp.address.streetName} {mp.address.buildingNumber}
              {mp.address.floor ? `, ${mp.address.floor}.` : ''}
              {mp.address.suite ? ` ${mp.address.suite}` : ''}
              , {mp.address.postCode} {mp.address.cityName}
            </Descriptions.Item>
          )}
          <Descriptions.Item label="Created">
            {new Date(mp.createdAt).toLocaleString('da-DK')}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      {/* Actions card */}
      <Card title="Handlinger" style={{ borderRadius: 12 }}>
        <Space wrap>
          <Button icon={<DatabaseOutlined />} onClick={handleRequestMasterData}>
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

      <Card title={`Supplies (${mp.supplies.length})`}>
        <Table dataSource={mp.supplies} columns={supplyColumns} rowKey="id" pagination={false} size="small" />
      </Card>

      <Card title={`TimeSeriesCollection (${mp.time_series.length})`}>
        <Table dataSource={mp.time_series} columns={time_seriesColumns} rowKey="id" pagination={false} size="small" />
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
