import { useEffect, useState } from 'react';
import { Card, Table, Typography, Space, Row, Col, Statistic, Input, Spin } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { Customer } from '../api/client';
import { getCustomers } from '../api/client';

const { Text } = Typography;

export default function CustomersPage() {
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    getCustomers()
      .then(res => setCustomers(res.data))
      .finally(() => setLoading(false));
  }, []);

  const filtered = customers.filter(k =>
    !search || k.name.toLowerCase().includes(search.toLowerCase()) ||
    k.email?.toLowerCase().includes(search.toLowerCase()) ||
    k.cpr?.includes(search) || k.cvr?.includes(search)
  );

  const privateCount = customers.filter(k => k.isPrivate).length;
  const businessCount = customers.filter(k => k.isCompany).length;

  const columns = [
    {
      title: 'KUNDE',
      key: 'name',
      render: (_: any, record: Customer) => (
        <Text style={{ fontWeight: 500 }}>{record.name}</Text>
      ),
    },
    {
      title: 'TYPE',
      key: 'type',
      width: 100,
      render: (_: any, record: Customer) => (
        <span className={`pill-badge ${record.isPrivate ? 'blue' : 'green'}`}>
          {record.isPrivate ? 'private' : 'business'}
        </span>
      ),
    },
    {
      title: 'IDENTIFIKATION',
      key: 'identifier',
      render: (_: any, record: Customer) => (
        <span className="mono">{record.cpr || record.cvr || '—'}</span>
      ),
    },
    {
      title: 'SUPPLIER',
      key: 'supplier',
      render: (_: any, record: Customer) => (
        <Text style={{ color: '#6b7280', fontSize: 13 }}>{record.supplierName || '—'}</Text>
      ),
    },
    {
      title: 'KONTAKT',
      key: 'contact',
      render: (_: any, record: Customer) => (
        <Text style={{ color: '#6b7280' }}>{record.email || '—'}</Text>
      ),
    },
    {
      title: 'OPRETTET',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      render: (d: string) => (
        <Text style={{ color: '#6b7280' }}>{new Date(d).toLocaleDateString('da-DK')}</Text>
      ),
    },
  ];

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Customers</h2>
        <div className="page-subtitle">Administrer dine elcustomers</div>
      </div>

      {/* Stats — 4 in a row */}
      <Row gutter={16}>
        {[
          { title: 'Total Customers', value: customers.length },
          { title: 'Private', value: privateCount },
          { title: 'Business', value: businessCount },
          { title: 'Active', value: customers.length },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={s.title}
                value={s.value}
                styles={{ content: { fontSize: 36, fontWeight: 700, color: '#1a202c' } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Filter + table */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Input
            placeholder="Search by name, email, CPR/CVR..."
            prefix={<SearchOutlined style={{ color: '#9ca3af' }} />}
            value={search}
            onChange={e => setSearch(e.target.value)}
            allowClear
            style={{ maxWidth: 340 }}
          />
        </div>
        <Table
          dataSource={filtered}
          columns={columns}
          rowKey="id"
          pagination={customers.length > 20 ? { pageSize: 20 } : false}
          onRow={record => ({
            onClick: () => navigate(`/customers/${record.id}`),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>
    </Space>
  );
}
