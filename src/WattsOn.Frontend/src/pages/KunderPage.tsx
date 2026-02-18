import { useEffect, useState } from 'react';
import { Card, Table, Typography, Space, Row, Col, Statistic, Input, Spin } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { Kunde } from '../api/client';
import { getKunder } from '../api/client';

const { Text } = Typography;

export default function KunderPage() {
  const [kunder, setKunder] = useState<Kunde[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    getKunder()
      .then(res => setKunder(res.data))
      .finally(() => setLoading(false));
  }, []);

  const filtered = kunder.filter(k =>
    !search || k.name.toLowerCase().includes(search.toLowerCase()) ||
    k.email?.toLowerCase().includes(search.toLowerCase()) ||
    k.cpr?.includes(search) || k.cvr?.includes(search)
  );

  const privat = kunder.filter(k => k.isPrivate).length;
  const erhverv = kunder.filter(k => k.isCompany).length;

  const columns = [
    {
      title: 'KUNDE',
      key: 'name',
      render: (_: any, record: Kunde) => (
        <Text style={{ fontWeight: 500 }}>{record.name}</Text>
      ),
    },
    {
      title: 'TYPE',
      key: 'type',
      width: 100,
      render: (_: any, record: Kunde) => (
        <span className={`pill-badge ${record.isPrivate ? 'blue' : 'green'}`}>
          {record.isPrivate ? 'privat' : 'erhverv'}
        </span>
      ),
    },
    {
      title: 'IDENTIFIKATION',
      key: 'identifier',
      render: (_: any, record: Kunde) => (
        <span className="mono">{record.cpr || record.cvr || '—'}</span>
      ),
    },
    {
      title: 'KONTAKT',
      key: 'contact',
      render: (_: any, record: Kunde) => (
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
        <h2>Kunder</h2>
        <div className="page-subtitle">Administrer dine elkunder</div>
      </div>

      {/* Stats — 4 in a row */}
      <Row gutter={16}>
        {[
          { title: 'Total Kunder', value: kunder.length },
          { title: 'Privat', value: privat },
          { title: 'Erhverv', value: erhverv },
          { title: 'Aktive', value: kunder.length },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={s.title}
                value={s.value}
                valueStyle={{ fontSize: 36, fontWeight: 700, color: '#1a202c' }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Filter + table */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Input
            placeholder="Søg efter kunde, email, CPR/CVR..."
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
          pagination={kunder.length > 20 ? { pageSize: 20 } : false}
          onRow={record => ({
            onClick: () => navigate(`/kunder/${record.id}`),
            style: { cursor: 'pointer' },
          })}
        />
      </Card>
    </Space>
  );
}
