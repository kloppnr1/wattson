import { useEffect, useState } from 'react';
import { Card, Table, Button, Modal, Form, Input, Switch, Tag, Space, Statistic, Row, Col, message, Popconfirm } from 'antd';
import { PlusOutlined, ThunderboltOutlined, BankOutlined, KeyOutlined } from '@ant-design/icons';
import type { SupplierIdentity } from '../api/client';
import { getSupplierIdentities, createSupplierIdentity } from '../api/client';
import axios from 'axios';

const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK', {
  year: 'numeric', month: 'short', day: 'numeric'
});

export default function AdminPage() {
  const [identities, setIdentities] = useState<SupplierIdentity[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);

  const load = () => {
    setLoading(true);
    getSupplierIdentities()
      .then(res => setIdentities(res.data))
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);

  const handleCreate = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);
      await createSupplierIdentity({
        gln: values.gln,
        name: values.name,
        cvr: values.cvr || undefined,
        isActive: values.isActive ?? false,
      });
      message.success('Supplier identity created');
      setModalOpen(false);
      form.resetFields();
      load();
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return; // validation
      message.error('Failed to create identity');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleActive = async (id: string, currentlyActive: boolean) => {
    try {
      const api = axios.create({ baseURL: '/api' });
      await api.patch(`/supplier-identities/${id}`, { isActive: !currentlyActive });
      message.success(currentlyActive ? 'Marked as legacy' : 'Activated');
      load();
    } catch {
      message.error('Failed to update');
    }
  };

  const active = identities.filter(i => i.isActive);
  const legacy = identities.filter(i => !i.isActive);

  const columns = [
    {
      title: 'NAME',
      dataIndex: 'name',
      key: 'name',
      render: (name: string, record: SupplierIdentity) => (
        <Space>
          <BankOutlined style={{ color: record.isActive ? '#3d5a6e' : '#999' }} />
          <span style={{ fontWeight: 500 }}>{name}</span>
        </Space>
      ),
    },
    {
      title: 'GLN',
      dataIndex: 'gln',
      key: 'gln',
      render: (gln: string) => <span className="gsrn-badge">{gln}</span>,
    },
    {
      title: 'CVR',
      dataIndex: 'cvr',
      key: 'cvr',
      render: (cvr: string | null) => cvr || <span style={{ color: '#ccc' }}>—</span>,
    },
    {
      title: 'STATUS',
      dataIndex: 'isActive',
      key: 'status',
      render: (isActive: boolean) => isActive
        ? <Tag color="green">Active</Tag>
        : <Tag color="default">Legacy</Tag>,
    },
    {
      title: 'CREATED',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => formatDate(d),
    },
    {
      title: '',
      key: 'actions',
      render: (_: unknown, record: SupplierIdentity) => (
        <Popconfirm
          title={record.isActive
            ? 'Mark as legacy? This GLN will only process corrections.'
            : 'Activate this identity? It will be used for new supplies.'}
          onConfirm={() => handleToggleActive(record.id, record.isActive)}
          okText="Yes"
          cancelText="No"
        >
          <Button size="small" type="link">
            {record.isActive ? 'Mark Legacy' : 'Activate'}
          </Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <div>
      <div className="section-header">
        <h2>Administration</h2>
      </div>

      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={6}>
          <Card className="stat-card">
            <Statistic title="TOTAL IDENTITIES" value={identities.length} prefix={<KeyOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card className="stat-card">
            <Statistic title="ACTIVE" value={active.length}
              valueStyle={{ color: '#52c41a' }} prefix={<ThunderboltOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card className="stat-card">
            <Statistic title="LEGACY" value={legacy.length}
              valueStyle={{ color: '#999' }} prefix={<BankOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card className="stat-card">
            <Statistic title="GLNs" value={identities.map(i => i.gln).filter((v, i, a) => a.indexOf(v) === i).length} />
          </Card>
        </Col>
      </Row>

      <Card
        title="Supplier Identities"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            Add Identity
          </Button>
        }
        style={{ borderRadius: 8 }}
      >
        <p style={{ color: '#666', marginBottom: 16 }}>
          GLN identities that WattsOn operates as. Active identities trade new supplies.
          Legacy identities (from acquired competitors) only process correction settlements.
        </p>
        <Table
          dataSource={identities}
          columns={columns}
          rowKey="id"
          loading={loading}
          pagination={false}
          size="middle"
        />
      </Card>

      <Modal
        title="Add Supplier Identity"
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        onOk={handleCreate}
        confirmLoading={submitting}
        okText="Create"
      >
        <Form form={form} layout="vertical" initialValues={{ isActive: false }}>
          <Form.Item
            name="gln"
            label="GLN Number"
            rules={[
              { required: true, message: 'GLN is required' },
              { len: 13, message: 'GLN must be exactly 13 digits' },
            ]}
          >
            <Input placeholder="5790001330552" maxLength={13} />
          </Form.Item>
          <Form.Item
            name="name"
            label="Company Name"
            rules={[{ required: true, message: 'Name is required' }]}
          >
            <Input placeholder="Acquired Energy A/S" />
          </Form.Item>
          <Form.Item name="cvr" label="CVR Number">
            <Input placeholder="12345678" maxLength={8} />
          </Form.Item>
          <Form.Item name="isActive" label="Status" valuePropName="checked">
            <Switch
              checkedChildren="Active"
              unCheckedChildren="Legacy"
            />
          </Form.Item>
          <div style={{ color: '#888', fontSize: 12, marginTop: -12 }}>
            Legacy = acquired competitor, only processes corrections for historical periods.
          </div>
        </Form>
      </Modal>
    </div>
  );
}
