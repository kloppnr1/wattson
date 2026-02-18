import { useEffect, useState } from 'react';
import { Table, Tag, Typography, Space } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Supply } from '../api/client';
import { getSupplies } from '../api/client';

export default function SuppliesPage() {
  const [data, setData] = useState<Supply[]>([]);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    getSupplies()
      .then((res) => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: Supply) => (
        <a onClick={() => navigate(`/metering_points/${record.meteringPointId}`)} style={{ fontFamily: 'monospace' }}>
          {gsrn}
        </a>
      ),
    },
    {
      title: 'Customer',
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
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>
      ),
    },
  ];

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Typography.Title level={3}>Supplies</Typography.Title>
      <Table
        dataSource={data}
        columns={columns}
        rowKey="id"
        loading={loading}
        pagination={{ pageSize: 20 }}
      />
    </Space>
  );
}
