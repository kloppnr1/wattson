import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography,
  Button, Tabs, Row, Col, Statistic, Empty, Modal, Form, Input,
  Select, DatePicker, message,
} from 'antd';
import {
  ArrowLeftOutlined, UserOutlined, ThunderboltOutlined,
  CalculatorOutlined, MailOutlined, PhoneOutlined, HomeOutlined,
  EditOutlined, StopOutlined, LogoutOutlined, SwapOutlined, 
  ExclamationCircleOutlined,
} from '@ant-design/icons';
import type { CustomerDetail, SettlementDocument } from '../api/client';
import { 
  getCustomer, getSettlementDocuments, sendCustomerUpdate,
  initiateEndOfSupply, initiateMoveOut, initiateIncorrectSwitch, 
  initiateIncorrectMove,
} from '../api/client';

const { Text, Title } = Typography;

import { formatDate, formatDateTime, formatPeriodEnd, formatDKK } from '../utils/format';

const statusColors: Record<string, string> = {
  Calculated: 'green', Invoiced: 'blue', Adjusted: 'orange',
};

type SupplyModalType = 'endOfSupply' | 'moveOut' | 'incorrectSwitch' | 'incorrectMove' | null;

export default function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [settlements, setSettlements] = useState<SettlementDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updateOpen, setUpdateOpen] = useState(false);
  const [updateLoading, setUpdateLoading] = useState(false);
  const [updateForm] = Form.useForm();
  const [supplyModal, setSupplyModal] = useState<SupplyModalType>(null);
  const [supplySubmitting, setSupplySubmitting] = useState(false);
  const [supplyForm] = Form.useForm();
  const navigate = useNavigate();

  useEffect(() => {
    if (!id) return;
    Promise.all([getCustomer(id), getSettlementDocuments('all')])
      .then(([customerRes, docsRes]) => {
        setCustomer(customerRes.data);
        const identifier = customerRes.data.cpr || customerRes.data.cvr;
        setSettlements(docsRes.data.filter(d => d.buyer.identifier === identifier));
      })
      .catch(err => setError(err.response?.status === 404 ? 'Customer ikke fundet' : err.message))
      .finally(() => setLoading(false));
  }, [id]);

  const openUpdateModal = () => {
    updateForm.setFieldsValue({
      gsrn: activeLev.length > 0 ? activeLev[0].gsrn : undefined,
      customerName: customer?.name,
      cpr: customer?.cpr || undefined,
      cvr: customer?.cvr || undefined,
      email: customer?.email || undefined,
      phone: customer?.phone || undefined,
      streetName: customer?.address?.streetName || undefined,
      buildingNumber: customer?.address?.buildingNumber || undefined,
      postCode: customer?.address?.postCode || undefined,
      cityName: customer?.address?.cityName || undefined,
    });
    setUpdateOpen(true);
  };

  const handleCustomerUpdate = async (values: any) => {
    setUpdateLoading(true);
    try {
      await sendCustomerUpdate({
        gsrn: values.gsrn,
        effectiveDate: values.effectiveDate.toISOString(),
        customerName: values.customerName,
        cpr: values.cpr || undefined,
        cvr: values.cvr || undefined,
        email: values.email || undefined,
        phone: values.phone || undefined,
        address: values.streetName ? {
          streetName: values.streetName,
          buildingNumber: values.buildingNumber || '',
          postCode: values.postCode || '',
          cityName: values.cityName || '',
          floor: null,
          suite: null,
        } : undefined,
      });
      message.success('Kundedata opdatering sendt til DataHub');
      setUpdateOpen(false);
      updateForm.resetFields();
    } catch (err: any) {
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    } finally {
      setUpdateLoading(false);
    }
  };

  const openSupplyModal = (type: SupplyModalType) => {
    supplyForm.resetFields();
    // Pre-fill GSRN from the customer's active supply
    if (activeLev.length > 0) {
      supplyForm.setFieldsValue({ gsrn: activeLev[0].gsrn });
    }
    setSupplyModal(type);
  };

  const handleSupplySubmit = async () => {
    try {
      const values = await supplyForm.validateFields();
      setSupplySubmitting(true);

      switch (supplyModal) {
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
      setSupplyModal(null);
      // Optionally reload data if needed
    } catch (err: any) {
      if (err.errorFields) return; // validation error
      message.error(err.response?.data?.error || 'Der opstod en fejl');
    } finally {
      setSupplySubmitting(false);
    }
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!customer) return null;

  const activeLev = customer.supplies.filter(l => l.isActive);
  const totalSettled = settlements
    .filter(s => s.documentType === 'settlement')
    .reduce((sum, s) => sum + s.totalInclVat, 0);
  const corrections = settlements.filter(s => s.documentType !== 'settlement');

  // --- Tables ---

  const supplyColumns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: any) => (
        <Text className="mono" style={{ cursor: 'pointer' }}
          onClick={(e) => { e.stopPropagation(); navigate(`/metering-points/${record.meteringPointId}`); }}>
          {gsrn}
        </Text>
      ),
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: any, record: any) => (
        <Text className="tnum" style={{ fontSize: 13 }}>
          {formatDate(record.supplyStart)} — {record.supplyEnd ? formatDate(record.supplyEnd) : '→'}
        </Text>
      ),
    },
    {
      title: 'STATUS',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>
      ),
    },
  ];

  const settlementColumns = [
    {
      title: 'DOKUMENT',
      dataIndex: 'documentId',
      key: 'documentId',
      render: (docId: string, record: SettlementDocument) => (
        <Text className="mono" strong style={{ cursor: 'pointer' }}
          onClick={() => navigate(`/settlements/${record.settlementId}`)}>
          {docId}
        </Text>
      ),
    },
    {
      title: 'TYPE',
      dataIndex: 'documentType',
      key: 'documentType',
      width: 110,
      render: (type: string) => {
        const map: Record<string, { label: string; color: string }> = {
          settlement: { label: 'Afregning', color: '#5d7a91' },
          debitNote: { label: 'Debitnota', color: '#d97706' },
          creditNote: { label: 'Kreditnota', color: '#059669' },
        };
        const cfg = map[type] || { label: type, color: '#5d7a91' };
        return <Tag color={cfg.color} style={{ color: '#fff' }}>{cfg.label}</Tag>;
      },
    },
    {
      title: 'PERIODE',
      key: 'period',
      render: (_: any, record: SettlementDocument) => (
        <Text className="tnum" style={{ fontSize: 12 }}>
          {formatDate(record.period.start)} — {record.period.end ? formatPeriodEnd(record.period.end) : '→'}
        </Text>
      ),
    },
    {
      title: 'BELØB EXCL.',
      dataIndex: 'totalExclVat',
      key: 'totalExclVat',
      align: 'right' as const,
      render: (v: number) => (
        <Text strong className="tnum" style={{ color: v < 0 ? '#059669' : undefined }}>
          {formatDKK(v)}
        </Text>
      ),
    },
    {
      title: 'INKL. MOMS',
      dataIndex: 'totalInclVat',
      key: 'totalInclVat',
      align: 'right' as const,
      render: (v: number) => (
        <Text className="tnum" style={{ color: v < 0 ? '#059669' : undefined }}>
          {formatDKK(v)}
        </Text>
      ),
    },
    {
      title: 'STATUS',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (s: string) => {
        const danishStatus = s === 'Calculated' ? 'Beregnet' 
          : s === 'Invoiced' ? 'Faktureret' 
          : s === 'Adjusted' ? 'Justeret' : s;
        return <Tag color={statusColors[s] || 'default'}>{danishStatus}</Tag>;
      },
    },
  ];

  return (
    <Space direction="vertical" size={20} style={{ width: '100%' }}>
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/customers')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        ← Kunder
      </Button>

      {/* Hero card */}
      <Card style={{ borderRadius: 12, overflow: 'hidden' }}>
        <Row gutter={[32, 16]} align="middle">
          <Col flex="auto">
            <Space size={16} align="start">
              <div style={{
                width: 56, height: 56, borderRadius: 14,
                background: 'linear-gradient(135deg, #e4e9ee 0%, #c9d4de 100%)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <UserOutlined style={{ fontSize: 24, color: '#5d7a91' }} />
              </div>
              <div>
                <Title level={3} style={{ margin: 0 }}>{customer.name}</Title>
                <Space size={8} style={{ marginTop: 6 }}>
                  <Tag color={customer.isPrivate ? 'blue' : 'green'}>
                    {customer.isPrivate ? 'Privat' : 'Erhverv'}
                  </Tag>
                  <Text type="secondary" className="mono">{customer.cpr || customer.cvr}</Text>
                  {activeLev.length > 0 && (
                    <Tag color="green">{activeLev.length} aktiv supply</Tag>
                  )}
                </Space>
              </div>
            </Space>
          </Col>
          <Col>
            <Space direction="vertical" align="end" size={8}>
              <div style={{ textAlign: 'right' }}>
                <div className="micro-label">Afregnet total</div>
                <div className="amount amount-large" style={{ marginTop: 4 }}>
                  {formatDKK(totalSettled)}
                </div>
              </div>
              <Button icon={<EditOutlined />} onClick={openUpdateModal}
                disabled={activeLev.length === 0}>
                Opdater kundedata
              </Button>
            </Space>
          </Col>
        </Row>
      </Card>

      {/* Quick stats */}
      <Row gutter={16}>
        {[
          { title: 'Forsyninger', value: customer.supplies.length, icon: <ThunderboltOutlined />, color: '#7c3aed' },
          { title: 'Aktive', value: activeLev.length, icon: <ThunderboltOutlined />, color: '#059669' },
          { title: 'Afregninger', value: settlements.length, icon: <CalculatorOutlined />, color: '#5d7a91' },
          { title: 'Korrektioner', value: corrections.length, icon: <CalculatorOutlined />, color: corrections.length > 0 ? '#d97706' : '#99afc2' },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card size="small" style={{ borderRadius: 10, textAlign: 'center' }}>
              <Statistic
                title={s.title} value={s.value}
                prefix={<span style={{ color: s.color }}>{s.icon}</span>}
                styles={{ content: { color: s.color, fontSize: 24 } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Three info cards in a row */}
      <Row gutter={16}>
        <Col xs={24} md={8}>
          <Card title="Kontaktoplysninger" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={10} style={{ width: '100%' }}>
              {customer.email && (
                <Space><MailOutlined style={{ color: '#7593a9' }} /><Text>{customer.email}</Text></Space>
              )}
              {customer.phone && (
                <Space><PhoneOutlined style={{ color: '#7593a9' }} /><Text>{customer.phone}</Text></Space>
              )}
              {!customer.email && !customer.phone && <Text type="secondary">Ingen kontaktoplysninger</Text>}
              <div style={{ marginTop: 8 }}>
                <div className="micro-label">TYPE</div>
                <Tag color={customer.isPrivate ? 'blue' : 'green'}>
                  {customer.isPrivate ? 'Privat' : 'Erhverv'}
                </Tag>
              </div>
              <div>
                <div className="micro-label">{customer.isPrivate ? 'CPR' : 'CVR'}</div>
                <Text className="mono">{customer.cpr || customer.cvr || '—'}</Text>
              </div>
            </Space>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Adresse" size="small" style={{ borderRadius: 12, height: '100%' }}>
            {customer.address ? (
              <Space><HomeOutlined style={{ color: '#7593a9' }} />
                <Text>
                  {customer.address.streetName} {customer.address.buildingNumber}
                  {customer.address.floor ? `, ${customer.address.floor}.` : ''}
                  {customer.address.suite ? ` ${customer.address.suite}` : ''}
                  <br />{customer.address.postCode} {customer.address.cityName}
                </Text>
              </Space>
            ) : (
              <Text type="secondary">Ingen adresse registreret</Text>
            )}
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Leverandør" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={8}>
              <div>
                <Text strong>{customer.supplierName}</Text>
              </div>
              <div>
                <div className="micro-label">GLN</div>
                <Text type="secondary" className="mono" style={{ fontSize: 12 }}>{customer.supplierGln}</Text>
              </div>
              <div>
                <div className="micro-label">OPRETTET</div>
                <Text className="tnum" style={{ fontSize: 12 }}>
                  {formatDateTime(customer.createdAt)}
                </Text>
              </div>
            </Space>
          </Card>
        </Col>
      </Row>

      {/* Tabs */}
      <Card style={{ borderRadius: 12 }}>
        <Tabs
          defaultActiveKey="supplies"
          items={[
            {
              key: 'supplies',
              label: `Forsyninger (${customer.supplies.length})`,
              children: (
                <Space direction="vertical" size={16} style={{ width: '100%' }}>
                  {activeLev.length > 0 && (
                    <Space wrap>
                      <Button icon={<StopOutlined />} onClick={() => openSupplyModal('endOfSupply')}>
                        Ophør leverance
                      </Button>
                      <Button icon={<LogoutOutlined />} onClick={() => openSupplyModal('moveOut')}>
                        Fraflytning
                      </Button>
                      <Button icon={<SwapOutlined />} onClick={() => openSupplyModal('incorrectSwitch')}>
                        Fejl: Leverandørskift
                      </Button>
                      <Button icon={<ExclamationCircleOutlined />} onClick={() => openSupplyModal('incorrectMove')}>
                        Fejl: Flytning
                      </Button>
                    </Space>
                  )}
                  {customer.supplies.length > 0 ? (
                    <Table
                      dataSource={customer.supplies}
                      columns={supplyColumns}
                      rowKey="id"
                      pagination={false}
                      size="small"
                    />
                  ) : (
                    <Empty description="Ingen forsyninger" image={Empty.PRESENTED_IMAGE_SIMPLE} />
                  )}
                </Space>
              ),
            },
            {
              key: 'settlements',
              label: `Afregninger (${settlements.length})`,
              children: settlements.length > 0 ? (
                <Table
                  dataSource={settlements}
                  columns={settlementColumns}
                  rowKey="settlementId"
                  pagination={false}
                  size="small"
                />
              ) : (
                <Empty description="Ingen afregninger endnu" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
          ]}
        />
      </Card>

      {/* BRS-015: Customer Update Modal */}
      <Modal
        title="Opdater kundedata (BRS-015)"
        open={updateOpen}
        onCancel={() => { setUpdateOpen(false); updateForm.resetFields(); }}
        footer={null}
        width={560}
      >
        <Form form={updateForm} layout="vertical" onFinish={handleCustomerUpdate}>
          <Form.Item name="gsrn" label="Målepunkt (GSRN)" rules={[{ required: true }]}>
            <Select placeholder="Vælg målepunkt">
              {activeLev.map(s => (
                <Select.Option key={s.gsrn} value={s.gsrn}>{s.gsrn}</Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item name="effectiveDate" label="Effektiv dato" rules={[{ required: true }]}>
            <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="customerName" label="Kundenavn" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="cpr" label="CPR">
                <Input placeholder="0101901234" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="cvr" label="CVR">
                <Input placeholder="12345678" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="email" label="Email">
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="phone" label="Telefon">
                <Input />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={16}>
              <Form.Item name="streetName" label="Vejnavn">
                <Input />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="buildingNumber" label="Nr.">
                <Input />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="postCode" label="Postnr.">
                <Input />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item name="cityName" label="By">
                <Input />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item style={{ marginBottom: 0, textAlign: 'right' }}>
            <Space>
              <Button onClick={() => { setUpdateOpen(false); updateForm.resetFields(); }}>Annuller</Button>
              <Button type="primary" htmlType="submit" loading={updateLoading}>Send til DataHub</Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      {/* Supply Action Modals */}
      <Modal
        open={supplyModal !== null}
        title={(() => {
          const modalTitles: Record<string, string> = {
            endOfSupply: 'Ophør leverance (BRS-002)',
            moveOut: 'Fraflytning (BRS-010)',
            incorrectSwitch: 'Fejl: Leverandørskift (BRS-003)',
            incorrectMove: 'Fejl: Flytning (BRS-011)',
          };
          return supplyModal ? modalTitles[supplyModal] : '';
        })()}
        onCancel={() => setSupplyModal(null)}
        onOk={handleSupplySubmit}
        confirmLoading={supplySubmitting}
        okText="Opret"
        cancelText="Annuller"
      >
        <Form form={supplyForm} layout="vertical" style={{ marginTop: 16 }}>
          {/* GSRN — all modals */}
          <Form.Item name="gsrn" label="GSRN" rules={[{ required: true, message: 'GSRN er påkrævet' }]}>
            <Input placeholder="571313..." style={{ fontFamily: 'monospace' }} />
          </Form.Item>

          {/* End of Supply */}
          {supplyModal === 'endOfSupply' && (
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
          {supplyModal === 'moveOut' && (
            <Form.Item name="effectiveDate" label="Effektiv dato" rules={[{ required: true, message: 'Dato er påkrævet' }]}>
              <DatePicker format="YYYY-MM-DD" style={{ width: '100%' }} />
            </Form.Item>
          )}

          {/* Incorrect Switch */}
          {supplyModal === 'incorrectSwitch' && (
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
          {supplyModal === 'incorrectMove' && (
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
