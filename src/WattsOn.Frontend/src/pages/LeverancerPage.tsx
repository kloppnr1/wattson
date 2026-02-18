import { useEffect, useState } from 'react';
import { Table, Tag, Typography, Space } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Leverance } from '../api/client';
import { getLeverancer } from '../api/client';

export default function LeverancerPage() {
  const [data, setData] = useState<Leverance[]>([]);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    getLeverancer()
      .then((res) => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: Leverance) => (
        <a onClick={() => navigate(`/målepunkter/${record.målepunktId}`)} style={{ fontFamily: 'monospace' }}>
          {gsrn}
        </a>
      ),
    },
    {
      title: 'Kunde',
      dataIndex: 'kundeNavn',
      key: 'kundeNavn',
      render: (name: string, record: Leverance) => (
        <a onClick={() => navigate(`/kunder/${record.kundeId}`)}>{name}</a>
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
      <Typography.Title level={3}>Leverancer</Typography.Title>
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
