import { useEffect, useState } from 'react';
import { Card, Table, Button, Modal, Form, Input, Switch, Tag, Space, Statistic, Row, Col, message, Popconfirm, InputNumber } from 'antd';
import { PlusOutlined, BankOutlined, DeleteOutlined, UndoOutlined, EditOutlined, ThunderboltOutlined, CloudDownloadOutlined } from '@ant-design/icons';
import { formatDate, formatDateTime } from '../utils/format';

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

export default function AdminPage() {
  const [identities, setIdentities] = useState<SupplierIdentity[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editModal, setEditModal] = useState<SupplierIdentity | null>(null);
  const [form] = Form.useForm();
  const [editForm] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);
  const [showArchived, setShowArchived] = useState(false);
  const [adminStats, setAdminStats] = useState<any>(null);
  const [fetchingSpot, setFetchingSpot] = useState(false);
  const [fetchDays, setFetchDays] = useState(7);

  const loadStats = () => {
    apiFetch('/admin/stats').then(setAdminStats).catch(() => {});
  };

  const handleFetchSpot = async () => {
    setFetchingSpot(true);
    try {
      const result = await apiFetch(`/admin/spot-price-fetch?days=${fetchDays}`, { method: 'POST' });
      if (result.success) {
        message.success(`Hentet ${result.recordsReceived} priser, ${result.inserted} nye indsat`);
        loadStats();
      } else {
        message.error(result.error || 'Fejl ved hentning');
      }
    } catch (err: any) {
      message.error(err?.message || 'Kunne ikke hente spotpriser');
    } finally {
      setFetchingSpot(false);
    }
  };

  const load = () => {
    setLoading(true);
    apiFetch(`/supplier-identities${showArchived ? '?includeArchived=true' : ''}`)
      .then(data => setIdentities(data))
      .catch(() => {})
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); loadStats(); }, [showArchived]);

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
      message.success('Leverandøridentitet oprettet');
      setModalOpen(false);
      form.resetFields();
      load();
    } catch (err: any) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error(err?.message || 'Kunne ikke oprette identitet');
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
      message.success('Identitet opdateret');
      setEditModal(null);
      load();
    } catch (err: any) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error(err?.message || 'Kunne ikke opdatere');
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
      message.success('Identitet arkiveret');
      load();
    } catch {
      message.error('Kunne ikke arkivere');
    }
  };

  const handleUnarchive = async (id: string) => {
    try {
      await apiFetch(`/supplier-identities/${id}/unarchive`, { method: 'POST' });
      message.success('Identitet gendannet');
      load();
    } catch {
      message.error('Kunne ikke gendanne');
    }
  };

  const active = identities.filter(i => i.isActive && !i.isArchived);
  const legacy = identities.filter(i => !i.isActive && !i.isArchived);

  const columns = [
    {
      title: 'NAVN',
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
        if (record.isArchived) return <Tag color="red">Arkiveret</Tag>;
        return record.isActive ? <Tag color="green">Aktiv</Tag> : <Tag color="default">Arv</Tag>;
      },
    },
    {
      title: 'OPRETTET',
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
              title="Gendan denne identitet fra arkiv?"
              onConfirm={() => handleUnarchive(record.id)}
              okText="Gendan"
              cancelText="Annuller"
            >
              <Button size="small" type="link" icon={<UndoOutlined />}>Gendan</Button>
            </Popconfirm>
          );
        }
        return (
          <Space size={4}>
            <Button size="small" type="link" icon={<EditOutlined />} onClick={() => openEdit(record)}>
              Rediger
            </Button>
            <Popconfirm
              title="Arkiver denne identitet?"
              description="Ingen flere korrektioner forventet for målepunkter på dette GLN."
              onConfirm={() => handleArchive(record.id)}
              okText="Arkiver"
              okButtonProps={{ danger: true }}
              cancelText="Annuller"
            >
              <Button size="small" type="link" danger icon={<DeleteOutlined />}>Arkiver</Button>
            </Popconfirm>
          </Space>
        );
      },
    },
  ];

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Administration</h2>
        <div className="page-subtitle">Systemindstillinger og dataimport</div>
      </div>

      {/* ==================== PRISDATA ==================== */}
      <Card title={<Space><ThunderboltOutlined /> Prisdata</Space>} style={{ borderRadius: 8 }}>
        <Row gutter={16}>
          <Col span={6}>
            <Statistic
              title="SPOTPRISER"
              value={adminStats?.spotPrices?.count ?? '—'}
              styles={{ content: { color: '#3d5a6e' } }}
            />
            <div style={{ fontSize: 12, color: '#888', marginTop: 4 }}>
              {adminStats?.spotPrices?.earliest
                ? `${formatDateTime(adminStats.spotPrices.earliest)} — ${formatDateTime(adminStats.spotPrices.latest)}`
                : 'Ingen data'}
            </div>
          </Col>
          <Col span={6}>
            <Statistic
              title="LEVERANDØRMARGINER"
              value={adminStats?.supplierMargins?.count ?? '—'}
              styles={{ content: { color: '#3d5a6e' } }}
            />
          </Col>
          <Col span={6}>
            <Statistic
              title="DATAHUB PRISER"
              value={adminStats?.datahubCharges?.count ?? '—'}
              suffix={adminStats?.datahubCharges?.links != null ? <span style={{ fontSize: 14, color: '#888' }}>({adminStats.datahubCharges.links} links)</span> : undefined}
              styles={{ content: { color: '#3d5a6e' } }}
            />
          </Col>
          <Col span={6} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <InputNumber
              min={1}
              max={90}
              value={fetchDays}
              onChange={(v) => setFetchDays(v ?? 7)}
              addonAfter="dage"
              style={{ width: 120 }}
            />
            <Button
              type="primary"
              icon={<CloudDownloadOutlined />}
              loading={fetchingSpot}
              onClick={handleFetchSpot}
            >
              Hent spotpriser
            </Button>
          </Col>
        </Row>
      </Card>

      {/* ==================== LEVERANDØRIDENTITETER ==================== */}
      <div style={{ marginTop: 8 }}>
        <h3 style={{ margin: '0 0 4px 0' }}>Leverandøridentiteter</h3>
        <div style={{ color: '#6b7280', fontSize: 13, marginBottom: 16 }}>GLN identiteter som WattsOn opererer som</div>
      </div>

      <Row gutter={12}>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="I ALT"
              value={identities.length} 
              styles={{ content: { color: '#3d5a6e' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="AKTIVE"
              value={active.length}
              styles={{ content: { color: '#10b981' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="ARV"
              value={legacy.length}
              styles={{ content: { color: '#6b7280' } }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card style={{ borderRadius: 8 }}>
            <Statistic 
              title="UNIKKE GLN'ER"
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
              checkedChildren="Vis arkiverede"
              unCheckedChildren="Skjul arkiverede"
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
              Tilføj identitet
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
        title="Tilføj leverandøridentitet"
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        onOk={handleCreate}
        confirmLoading={submitting}
        okText="Opret"
      >
        <Form form={form} layout="vertical" initialValues={{ isActive: false }}>
          <Form.Item
            name="gln"
            label="GLN nummer"
            rules={[
              { required: true, message: 'GLN er påkrævet' },
              { len: 13, message: 'GLN skal være præcis 13 cifre' },
            ]}
          >
            <Input placeholder="5790001330552" maxLength={13} />
          </Form.Item>
          <Form.Item
            name="name"
            label="Firmanavn"
            rules={[{ required: true, message: 'Navn er påkrævet' }]}
          >
            <Input placeholder="Acquired Energy A/S" />
          </Form.Item>
          <Form.Item name="cvr" label="CVR nummer">
            <Input placeholder="12345678" maxLength={8} />
          </Form.Item>
          <Form.Item name="isActive" label="Status" valuePropName="checked">
            <Switch checkedChildren="Aktiv" unCheckedChildren="Arv" />
          </Form.Item>
          <div style={{ color: '#888', fontSize: 12, marginTop: -12 }}>
            Arv = opkøbt konkurrent, behandler kun korrektioner for historiske perioder.
          </div>
        </Form>
      </Modal>

      {/* Edit modal */}
      <Modal
        title={editModal ? `Rediger — ${editModal.gln}` : 'Rediger'}
        open={!!editModal}
        onCancel={() => setEditModal(null)}
        onOk={handleEdit}
        confirmLoading={submitting}
        okText="Gem"
      >
        <Form form={editForm} layout="vertical">
          <Form.Item label="GLN nummer">
            <Input value={editModal?.gln} disabled />
          </Form.Item>
          <Form.Item
            name="name"
            label="Firmanavn"
            rules={[{ required: true, message: 'Navn er påkrævet' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item 
            name="cvr" 
            label="CVR nummer"
            rules={[
              { len: 8, message: 'CVR skal være præcis 8 cifre' }
            ]}
          >
            <Input placeholder="12345678" maxLength={8} />
          </Form.Item>
          <Form.Item name="isActive" label="Status" valuePropName="checked">
            <Switch checkedChildren="Aktiv" unCheckedChildren="Arv" />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
