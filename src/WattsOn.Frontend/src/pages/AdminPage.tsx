import { useEffect, useState } from 'react';
import { Card, Table, Button, Modal, Form, Input, Switch, Tag, Space, Statistic, Row, Col, message, Popconfirm } from 'antd';
import { PlusOutlined, ThunderboltOutlined, BankOutlined, KeyOutlined, DeleteOutlined, UndoOutlined, EditOutlined } from '@ant-design/icons';

export interface SupplierIdentity {
  id: string; gln: string; name: string; cvr: string | null; isActive: boolean; isArchived: boolean; createdAt: string;
}

const apiFetch = async (path: string, opts?: RequestInit) => {
  const res = await fetch(`/api${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...opts,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `HTTP ${res.status}`);
  }
  return res.json();
};

const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK', {
  year: 'numeric', month: 'short', day: 'numeric'
});

export default function AdminPage() {
  const [identities, setIdentities] = useState<SupplierIdentity[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editModal, setEditModal] = useState<SupplierIdentity | null>(null);
  const [form] = Form.useForm();
  const [editForm] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);
  const [showArchived, setShowArchived] = useState(false);

  const load = () => {
    setLoading(true);
    apiFetch(`/supplier-identities${showArchived ? '?includeArchived=true' : ''}`)
      .then(data => setIdentities(data))
      .catch(() => {})
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [showArchived]);

  const handleCreate = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);
      await apiFetch('/supplier-identities', {
        method: 'POST',
        body: JSON.stringify({
          gln: values.gln,
          name: values.name,
          cvr: values.cvr || undefined,
          isActive: values.isActive ?? false,
        }),
      });
      message.success('Supplier identity created');
      setModalOpen(false);
      form.resetFields();
      load();
    } catch (err: any) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error(err?.message || 'Failed to create identity');
    } finally {
      setSubmitting(false);
    }
  };

  const handleEdit = async () => {
    try {
      const values = await editForm.validateFields();
      setSubmitting(true);
      await apiFetch(`/supplier-identities/${editModal!.id}`, {
        method: 'PATCH',
        body: JSON.stringify({
          name: values.name,
          cvr: values.cvr || null,
          isActive: values.isActive,
        }),
      });
      message.success('Identity updated');
      setEditModal(null);
      load();
    } catch (err: any) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error(err?.message || 'Failed to update');
    } finally {
      setSubmitting(false);
    }
  };

  const openEdit = (record: SupplierIdentity) => {
    setEditModal(record);
    editForm.setFieldsValue({ 
      name: record.name, 
      cvr: record.cvr || '', 
      isActive: record.isActive 
    });
  };

  const handleArchive = async (id: string) => {
    try {
      await apiFetch(`/supplier-identities/${id}/archive`, { method: 'POST' });
      message.success('Identity archived');
      load();
    } catch {
      message.error('Failed to archive');
    }
  };

  const handleUnarchive = async (id: string) => {
    try {
      await apiFetch(`/supplier-identities/${id}/unarchive`, { method: 'POST' });
      message.success('Identity restored');
      load();
    } catch {
      message.error('Failed to restore');
    }
  };

  const active = identities.filter(i => i.isActive && !i.isArchived);
  const legacy = identities.filter(i => !i.isActive && !i.isArchived);

  const columns = [
    {
      title: 'NAME',
      dataIndex: 'name',
      key: 'name',
      width: '30%',
      render: (name: string, record: SupplierIdentity) => (
        <Space>
          <BankOutlined style={{ color: record.isArchived ? '#ccc' : record.isActive ? '#3d5a6e' : '#999' }} />
          <span style={{ fontWeight: 500, color: record.isArchived ? '#999' : undefined }}>{name}</span>
        </Space>
      ),
    },
    {
      title: 'GLN',
      dataIndex: 'gln',
      key: 'gln',
      width: 160,
      render: (gln: string) => <span className="mono" style={{ fontSize: 13 }}>{gln}</span>,
    },
    {
      title: 'CVR',
      dataIndex: 'cvr',
      key: 'cvr',
      width: 110,
      render: (cvr: string | null) => cvr
        ? <span className="mono" style={{ fontSize: 13 }}>{cvr}</span>
        : <span style={{ color: '#ccc' }}>—</span>,
    },
    {
      title: 'STATUS',
      key: 'status',
      width: 110,
      render: (_: unknown, record: SupplierIdentity) => {
        if (record.isArchived) return <Tag color="red">Archived</Tag>;
        return record.isActive ? <Tag color="green">Active</Tag> : <Tag color="default">Legacy</Tag>;
      },
    },
    {
      title: 'CREATED',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 130,
      render: (d: string) => <span style={{ color: '#6b7280' }}>{formatDate(d)}</span>,
    },
    {
      title: '',
      key: 'actions',
      width: 200,
      align: 'right' as const,
      render: (_: unknown, record: SupplierIdentity) => {
        if (record.isArchived) {
          return (
            <Popconfirm
              title="Restore this identity from archive?"
              onConfirm={() => handleUnarchive(record.id)}
              okText="Restore"
              cancelText="Cancel"
            >
              <Button size="small" type="link" icon={<UndoOutlined />}>Restore</Button>
            </Popconfirm>
          );
        }
        return (
          <Space size={4}>
            <Button size="small" type="link" icon={<EditOutlined />} onClick={() => openEdit(record)}>
              Edit
            </Button>
            <Popconfirm
              title="Archive this identity?"
              description="No more corrections expected for any metering points on this GLN."
              onConfirm={() => handleArchive(record.id)}
              okText="Archive"
              okButtonProps={{ danger: true }}
              cancelText="Cancel"
            >
              <Button size="small" type="link" danger icon={<DeleteOutlined />}>Archive</Button>
            </Popconfirm>
          </Space>
        );
      },
    },
  ];

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Supplier Identities</h2>
        <div className="page-subtitle">GLN identities that WattsOn operates as</div>
      </div>

      <Row gutter={12}>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="TOTAL"
              value={identities.length} 
              styles={{ content: { color: '#3d5a6e' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="ACTIVE"
              value={active.length}
              styles={{ content: { color: '#10b981' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="LEGACY"
              value={legacy.length}
              styles={{ content: { color: '#6b7280' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="UNIQUE GLNs"
              value={identities.map(i => i.gln).filter((v, i, a) => a.indexOf(v) === i).length}
              styles={{ content: { color: '#3d5a6e' } }}
            />
          </Card>
        </Col>
      </Row>

      <Card
        style={{ borderRadius: 8 }}
        styles={{ body: { padding: 0 } }}
        extra={
          <Space>
            <Switch
              checked={showArchived}
              onChange={setShowArchived}
              checkedChildren="Show archived"
              unCheckedChildren="Hide archived"
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
              Add Identity
            </Button>
          </Space>
        }
      >
        <Table
          dataSource={identities}
          columns={columns}
          rowKey="id"
          loading={loading}
          pagination={false}
          size="middle"
          tableLayout="fixed"
        />
      </Card>

      {/* Create modal */}
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
            <Switch checkedChildren="Active" unCheckedChildren="Legacy" />
          </Form.Item>
          <div style={{ color: '#888', fontSize: 12, marginTop: -12 }}>
            Legacy = acquired competitor, only processes corrections for historical periods.
          </div>
        </Form>
      </Modal>

      {/* Edit modal */}
      <Modal
        title={editModal ? `Edit — ${editModal.gln}` : 'Edit'}
        open={!!editModal}
        onCancel={() => setEditModal(null)}
        onOk={handleEdit}
        confirmLoading={submitting}
        okText="Save"
      >
        <Form form={editForm} layout="vertical">
          <Form.Item label="GLN Number">
            <Input value={editModal?.gln} disabled />
          </Form.Item>
          <Form.Item
            name="name"
            label="Company Name"
            rules={[{ required: true, message: 'Name is required' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item 
            name="cvr" 
            label="CVR Number"
            rules={[
              { len: 8, message: 'CVR must be exactly 8 digits' }
            ]}
          >
            <Input placeholder="12345678" maxLength={8} />
          </Form.Item>
          <Form.Item name="isActive" label="Status" valuePropName="checked">
            <Switch checkedChildren="Active" unCheckedChildren="Legacy" />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
