import { useEffect, useState, useCallback } from 'react';
import {
  Table, Tag, Space, Card, Row, Col, Statistic, Button,
  Modal, Form, Input, DatePicker, Select, message,
} from 'antd';
import {
  StopOutlined, LogoutOutlined, SwapOutlined, ExclamationCircleOutlined,
  LinkOutlined, CheckCircleOutlined, ClockCircleOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { Supply } from '../api/client';
import { formatDate } from '../utils/format';
import {
  getSupplies, initiateEndOfSupply, initiateMoveOut,
  initiateIncorrectSwitch, initiateIncorrectMove,
} from '../api/client';

// Typography available if needed

type ModalType = 'endOfSupply' | 'moveOut' | 'incorrectSwitch' | 'incorrectMove' | null;

export default function SuppliesPage() {
  const [data, setData] = useState<Supply[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeModal, setActiveModal] = useState<ModalType>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();
  const navigate = useNavigate();

  const loadData = useCallback(() => {
    setLoading(true);
    getSupplies()
      .then((res) => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { loadData(); }, [loadData]);

  const active = data.filter(s => s.isActive).length;
  const ended = data.filter(s => !s.isActive).length;
  const thirtyDaysAgo = new Date();
  thirtyDaysAgo.setDate(thirtyDaysAgo.getDate() - 30);
  const recent = data.filter(s => new Date(s.createdAt) >= thirtyDaysAgo).length;

  const openModal = (type: ModalType) => {
    form.resetFields();
    setActiveModal(type);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      switch (activeModal) {
        case 'endOfSupply':
          await initiateEndOfSupply({
            gsrn: values.gsrn,
            desiredEndDate: values.desiredEndDate.format('YYYY-MM-DD'),
            reason: values.reason || undefined,
          });
          break;
        case 'moveOut':
          await initiateMoveOut({
            gsrn: values.gsrn,
            effectiveDate: values.effectiveDate.format('YYYY-MM-DD'),
          });
          break;
        case 'incorrectSwitch':
          await initiateIncorrectSwitch({
            gsrn: values.gsrn,
            switchDate: values.switchDate.format('YYYY-MM-DD'),
            reason: values.reason || undefined,
          });
          break;
        case 'incorrectMove':
          await initiateIncorrectMove({
            gsrn: values.gsrn,
            moveDate: values.moveDate.format('YYYY-MM-DD'),
            moveType: values.moveType,
            reason: values.reason || undefined,
          });
          break;
      }

      message.success('Process oprettet');
      setActiveModal(null);
      loadData();
    } catch (err: any) {
      if (err.errorFields) return; // validation error
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    } finally {
      setSubmitting(false);
    }
  };

  const columns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: Supply) => (
        <a onClick={() => navigate(`/metering-points/${record.meteringPointId}`)} style={{ fontFamily: 'monospace' }}>
          {gsrn}
        </a>
      ),
    },
    {
      title: 'Kunde',
      dataIndex: 'customerName',
      key: 'customerName',
      render: (name: string, record: Supply) => (
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
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>
      ),
    },
  ];

  const modalTitles: Record<string, string> = {
    endOfSupply: 'Ophør leverance (BRS-002)',
    moveOut: 'Fraflytning (BRS-010)',
    incorrectSwitch: 'Fejl: Leverandørskift (BRS-003)',
    incorrectMove: 'Fejl: Flytning (BRS-011)',
  };

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h2>Leverancer</h2>
            <div className="page-subtitle">Forsyningsaftaler og handlinger</div>
          </div>
          <Space>
            <Button icon={<StopOutlined />} onClick={() => openModal('endOfSupply')}>
              Ophør leverance
            </Button>
            <Button icon={<LogoutOutlined />} onClick={() => openModal('moveOut')}>
              Fraflytning
            </Button>
            <Button icon={<SwapOutlined />} onClick={() => openModal('incorrectSwitch')}>
              Fejl: Leverandørskift
            </Button>
            <Button icon={<ExclamationCircleOutlined />} onClick={() => openModal('incorrectMove')}>
              Fejl: Flytning
            </Button>
          </Space>
        </div>
      </div>

      {/* Stats */}
      <Row gutter={16}>
        {[
          { title: 'Total', value: data.length, icon: <LinkOutlined style={{ color: '#5d7a91' }} />, color: '#5d7a91' },
          { title: 'Aktive', value: active, icon: <CheckCircleOutlined style={{ color: '#059669' }} />, color: '#059669' },
          { title: 'Afsluttede', value: ended, icon: <StopOutlined style={{ color: '#99afc2' }} />, color: '#99afc2' },
          { title: 'Seneste 30 dage', value: recent, icon: <ClockCircleOutlined style={{ color: '#3b82f6' }} />, color: '#3b82f6' },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 12 }}>
              <Statistic
                title={s.title}
                value={s.value}
                prefix={s.icon}
                styles={{ content: { color: s.color, fontSize: 28 } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Table */}
      <Card style={{ borderRadius: 12, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <Table
          dataSource={data}
          columns={columns}
          rowKey="id"
          loading={loading}
          pagination={data.length > 20 ? { pageSize: 20 } : false}
          onRow={record => ({
            onClick: () => navigate(`/metering-points/${record.meteringPointId}`),
            style: { cursor: 'pointer' },
          })}
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
          {/* GSRN — all modals */}
          <Form.Item name="gsrn" label="GSRN" rules={[{ required: true, message: 'GSRN er påkrævet' }]}>
            <Input placeholder="571313..." style={{ fontFamily: 'monospace' }} />
          </Form.Item>

          {/* End of Supply */}
          {activeModal === 'endOfSupply' && (
            <>
              <Form.Item name="desiredEndDate" label="Ønsket slutdato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="reason" label="Årsag">
                <Input placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}

          {/* Move-Out */}
          {activeModal === 'moveOut' && (
            <Form.Item name="effectiveDate" label="Effektiv dato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
              <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
            </Form.Item>
          )}

          {/* Incorrect Switch */}
          {activeModal === 'incorrectSwitch' && (
            <>
              <Form.Item name="switchDate" label="Skiftedato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="reason" label="Årsag">
                <Input placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}

          {/* Incorrect Move */}
          {activeModal === 'incorrectMove' && (
            <>
              <Form.Item name="moveDate" label="Flytningsdato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
                <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="moveType" label="Flytningstype" rules={[{ required: true, message: 'Type er påkrævet' }]}>
                <Select placeholder="Vælg type" options={[
                  { value: 'MoveIn', label: 'Tilflytning' },
                  { value: 'MoveOut', label: 'Fraflytning' },
                ]} />
              </Form.Item>
              <Form.Item name="reason" label="Årsag">
                <Input placeholder="Valgfrit" />
              </Form.Item>
            </>
          )}
        </Form>
      </Modal>
    </Space>
  );
}
